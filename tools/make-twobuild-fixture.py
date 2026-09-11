"""Builds a fixture with the SAME faulting offset recorded under two different game builds.

A game patch moves every address, so one offset before and after an update is two unrelated instructions. That is
the whole reason signatures are build-keyed, and it is the one path real crash data here cannot exercise - this
machine has only ever crashed on 3.0.80.51928. So: take a real dump, copy it, and rewrite the faulting module's
VS_FIXEDFILEINFO to an older build.
"""
import io, os, re, shutil, struct, sys, datetime

ROOT = sys.argv[1]
SRC = os.path.join(os.environ["LOCALAPPDATA"], "REDEngine", "ReportQueue")
OLD_BUILD = (3 << 16 | 0, 79 << 16 | 40000)   # 3.0.79.40000, a plausible previous patch

fx = os.path.join(ROOT, "twobuild")
if os.path.isdir(fx):
    shutil.rmtree(fx)
game = os.path.join(fx, "game")
os.makedirs(os.path.join(game, "bin", "x64"), exist_ok=True)
io.open(os.path.join(game, "bin", "x64", "Cyberpunk2077.exe"), "w").write("stub\n")
queue = os.path.join(fx, "LocalAppData", "REDEngine", "ReportQueue")
os.makedirs(queue, exist_ok=True)
os.makedirs(os.path.join(fx, "AppData", "CrashDoctor"), exist_ok=True)
os.makedirs(os.path.join(fx, "LocalAppData", "CD Projekt Red", "Cyberpunk 2077"), exist_ok=True)


def patch_build(path, ms, ls):
    """Rewrite the file version of whichever module contains the faulting address."""
    d = bytearray(io.open(path, "rb").read())
    assert d[:4] == b"MDMP"
    nstreams, dirrva = struct.unpack_from("<II", d, 8)
    streams = {}
    for i in range(nstreams):
        t, sz, rva = struct.unpack_from("<III", d, dirrva + i * 12)
        streams[t] = (sz, rva)
    _, er = streams[6]
    addr = struct.unpack_from("<Q", d, er + 8 + 16)[0]
    _, mr = streams[4]
    n = struct.unpack_from("<I", d, mr)[0]
    off = mr + 4
    for i in range(n):
        base, size = struct.unpack_from("<QI", d, off)
        if base <= addr < base + size:
            struct.pack_into("<II", d, off + 32, ms, ls)   # dwFileVersionMS / LS
            io.open(path, "wb").write(bytes(d))
            return True
        off += 108
    return False


# two real crashes at the same offset: keep one as-is, rewrite the other to the older build
same = []
for name in sorted(os.listdir(SRC)):
    p = os.path.join(SRC, name, "Cyberpunk2077.dmp")
    if not os.path.exists(p):
        continue
    d = io.open(p, "rb").read(1 << 20)
    if d[:4] != b"MDMP":
        continue
    same.append(name)

pick = [n for n in same if "20260906-16" in n or "20260906-17" in n][:2]
assert len(pick) == 2, "need two crash folders to copy, got %r" % pick

for i, name in enumerate(pick):
    dst = os.path.join(queue, name)
    shutil.copytree(os.path.join(SRC, name), dst)
    if i == 1:
        ok = patch_build(os.path.join(dst, "Cyberpunk2077.dmp"), *OLD_BUILD)
        print("rewrote build on", name, "->", ok)

# the reporter log is how crash times are found when no session log survives
lines = []
for name in pick:
    m = re.match(r"Cyberpunk2077-(\d{8})-(\d{6})-", name)
    t = datetime.datetime.strptime(m.group(1) + m.group(2), "%Y%m%d%H%M%S")
    lines.append("%s|INFO|Redbug.Reporter.CP2077.Preparation.ReportCreator|Matching crash data directory: '%s'."
                 % (t.strftime("%m/%d/%Y %H:%M:%S -07:00"), name))
io.open(os.path.join(fx, "LocalAppData", "REDEngine", "CrashReporter.log"), "w").write("\n".join(lines) + "\n")
print("fixture:", fx)
print("copied:", pick)

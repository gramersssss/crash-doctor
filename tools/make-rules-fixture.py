"""Builds an install that deliberately trips every rule in the catalogue.

A rule that has never been seen to fire is not a rule, it is a hope. Real crash data cannot produce these on
demand - the whole point of them is that they are the things that go wrong on *other* people's machines - so the
conditions are manufactured here instead.

Run: python rulesfx.py <outdir>
"""
import io, os, re, shutil, struct, sys, datetime

ROOT = sys.argv[1]
SRC = os.path.join(os.environ["LOCALAPPDATA"], "REDEngine", "ReportQueue")

fx = os.path.join(ROOT, "broken")
if os.path.isdir(fx):
    shutil.rmtree(fx)
game = os.path.join(fx, "game")
os.makedirs(os.path.join(game, "bin", "x64"), exist_ok=True)
os.makedirs(os.path.join(game, "archive", "pc", "mod"), exist_ok=True)
queue = os.path.join(fx, "LocalAppData", "REDEngine", "ReportQueue")
os.makedirs(queue, exist_ok=True)
os.makedirs(os.path.join(fx, "AppData", "CrashDoctor"), exist_ok=True)
os.makedirs(os.path.join(fx, "LocalAppData", "CD Projekt Red", "Cyberpunk 2077"), exist_ok=True)

io.open(os.path.join(game, "bin", "x64", "Cyberpunk2077.exe"), "w").write("stub\n")

# rule 1: an upscaler enabler that overwrote the game's own DLLs
for f in ("dlss-enabler.dll", "fakenvapi.ini"):
    io.open(os.path.join(game, "bin", "x64", f), "w").write("x")

# rule 2: archives that downloaded as nothing
io.open(os.path.join(game, "archive", "pc", "mod", "halfdownloaded.archive"), "wb").write(b"")
io.open(os.path.join(game, "archive", "pc", "mod", "alsobroken.archive"), "wb").write(b"\x00" * 40)
io.open(os.path.join(game, "archive", "pc", "mod", "fine.archive"), "wb").write(b"\x00" * 500000)

# rule 3: saves inside a synced folder - the scan is run with OneDrive pointed here
od = os.path.join(fx, "OneDrive", "Saved Games", "CD Projekt Red", "Cyberpunk 2077")
os.makedirs(od, exist_ok=True)
io.open(os.path.join(od, "ManualSave-1", "x"), "w") if False else None
os.makedirs(os.path.join(od, "ManualSave-1"), exist_ok=True)

# rule 4: a capture hook injected into the game at the moment of the crash
def rename_module(path, new_name):
    """Overwrite one module's name in place. Shorter or equal length only, so nothing shifts."""
    d = bytearray(io.open(path, "rb").read())
    assert d[:4] == b"MDMP"
    nstreams, dirrva = struct.unpack_from("<II", d, 8)
    streams = {}
    for i in range(nstreams):
        t, sz, rva = struct.unpack_from("<III", d, dirrva + i * 12)
        streams[t] = (sz, rva)
    _, mr = streams[4]
    n = struct.unpack_from("<I", d, mr)[0]
    off = mr + 4
    want = new_name.encode("utf-16le")
    for i in range(n):
        name_rva = struct.unpack_from("<I", d, off + 20)[0]
        ln = struct.unpack_from("<I", d, name_rva)[0]
        cur = d[name_rva + 4: name_rva + 4 + ln].decode("utf-16le", "replace")
        # pick a long, obviously irrelevant system module so nothing else in the report changes meaning
        # needs a name at least as long as the replacement, and one whose meaning nothing else depends on
        if ln >= len(want) and cur.lower().endswith("physx3characterkinematic_x64.dll"):
            struct.pack_into("<I", d, name_rva, len(want))
            d[name_rva + 4: name_rva + 4 + len(want)] = want
            io.open(path, "wb").write(bytes(d))
            return cur
        off += 108
    return None


picked = None
for name in sorted(os.listdir(SRC)):
    p = os.path.join(SRC, name, "Cyberpunk2077.dmp")
    if not os.path.exists(p) or io.open(p, "rb").read(4) != b"MDMP":
        continue
    dst = os.path.join(queue, name)
    shutil.copytree(os.path.join(SRC, name), dst)
    was = rename_module(os.path.join(dst, "Cyberpunk2077.dmp"), "RTSSHooks64.dll")
    if was:
        picked = (name, was)
        m = re.match(r"Cyberpunk2077-(\d{8})-(\d{6})-", name)
        t = datetime.datetime.strptime(m.group(1) + m.group(2), "%Y%m%d%H%M%S")
        io.open(os.path.join(fx, "LocalAppData", "REDEngine", "CrashReporter.log"), "w").write(
            "%s|INFO|Redbug.Reporter.CP2077.Preparation.ReportCreator|Matching crash data directory: '%s'.\n"
            % (t.strftime("%m/%d/%Y %H:%M:%S -07:00"), name))
        break
    shutil.rmtree(dst)

print("fixture:", fx)
print("renamed module:", picked)

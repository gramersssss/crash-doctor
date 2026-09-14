"""Builds an install that deliberately trips every rule in the catalogue.

A rule that has never been seen to fire is not a rule, it is a hope. Real crash data cannot produce these on
demand - the whole point of them is that they are the things that go wrong on *other* people's machines - so the
conditions are manufactured here instead.

Run: python tools/make-rules-fixture.py <outdir>

It prints the exact variables to set before scanning. One of them, CRASHDOCTOR_TEST_SYSTEM, stands in for the card
size and the driver date: two rules read the machine rather than a file, and no fixture can fake hardware.

Every rule in src/Engine/Rules.cs should appear in out.json's health list, and none of them should appear when the
same build is run against the `healthy` fixture or a real install.
"""
import io, json, os, re, shutil, struct, sys, datetime

ROOT = sys.argv[1]
SRC = os.path.join(os.environ["LOCALAPPDATA"], "REDEngine", "ReportQueue")
NOW = datetime.datetime.now()

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

# BrokenUpscalerOverride: an upscaler enabler that overwrote the game's own DLLs
for f in ("dlss-enabler.dll", "fakenvapi.ini"):
    io.open(os.path.join(game, "bin", "x64", f), "w").write("x")

# EmptyOrTruncatedArchives: archives that downloaded as nothing
io.open(os.path.join(game, "archive", "pc", "mod", "halfdownloaded.archive"), "wb").write(b"")
io.open(os.path.join(game, "archive", "pc", "mod", "alsobroken.archive"), "wb").write(b"\x00" * 40)
io.open(os.path.join(game, "archive", "pc", "mod", "fine.archive"), "wb").write(b"\x00" * 500000)

# SavesInCloudStorage: saves inside a synced folder - the scan is run with OneDrive pointed here
od = os.path.join(fx, "OneDrive", "Saved Games", "CD Projekt Red", "Cyberpunk 2077")
os.makedirs(os.path.join(od, "ManualSave-1"), exist_ok=True)


# ---------------------------------------------------------------- minidump surgery
def streams(d):
    """{stream type: (size, rva)} from a minidump already read into a bytearray."""
    assert d[:4] == b"MDMP"
    nstreams, dirrva = struct.unpack_from("<II", d, 8)
    out = {}
    for i in range(nstreams):
        t, sz, rva = struct.unpack_from("<III", d, dirrva + i * 12)
        out[t] = (sz, rva)
    return out


def rename_module(path, new_name):
    """Overwrite one module's name in place. Shorter or equal length only, so nothing shifts."""
    d = bytearray(io.open(path, "rb").read())
    _, mr = streams(d)[4]
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


def set_exception_code(path, code):
    """MINIDUMP_EXCEPTION_STREAM: thread id, alignment, then the exception code. Four bytes, in place."""
    d = bytearray(io.open(path, "rb").read())
    _, rva = streams(d)[6]
    was = struct.unpack_from("<I", d, rva + 8)[0]
    struct.pack_into("<I", d, rva + 8, code)
    io.open(path, "wb").write(bytes(d))
    return was


def telemetry_file(crash_dir):
    attch = os.path.join(crash_dir, "attch")
    if not os.path.isdir(attch):
        return None
    txts = [os.path.join(attch, f) for f in os.listdir(attch) if f.lower().endswith(".txt")]
    return max(txts, key=os.path.getsize) if txts else None


def telemetry_value(crash_dir, key):
    f = telemetry_file(crash_dir)
    if not f:
        return None
    m = re.search(r'"' + re.escape(key) + r'@\d+#TID=\d+"\s*:\s*"([^"]*)"', io.open(f, encoding="utf-8", errors="replace").read())
    return m.group(1) if m else None


def set_engine_oom(crash_dir):
    """Flip the engine's own out-of-memory flag in the telemetry attachment."""
    f = telemetry_file(crash_dir)
    if not f:
        return False
    t = io.open(f, encoding="utf-8", errors="replace").read()
    t2 = re.sub(r'("Engine/OOM@\d+#TID=\d+"\s*:\s*)"[^"]*"', r'\1"true"', t, count=1)
    if t2 == t:
        t2 = t.replace("{", '{"Engine/OOM@4#TID=0":"true",', 1)
    io.open(f, "w", encoding="utf-8").write(t2)
    return t2 != t


# Three real crash reports, each doctored for one rule. Copied rather than generated because a minidump is not a
# thing worth writing by hand, and because the rest of the report staying real is what makes the fixture honest.
have = []
for name in sorted(os.listdir(SRC)):
    src_dir = os.path.join(SRC, name)
    p = os.path.join(src_dir, "Cyberpunk2077.dmp")
    if not os.path.isdir(src_dir) or not os.path.exists(p) or io.open(p, "rb").read(4) != b"MDMP":
        continue
    have.append(name)

# For the out-of-memory rule, pick the report where the card was emptiest. The rule has two branches - "the card
# was full too, so this is the video-memory story" and "the card was nowhere near full, so something else ran out"
# - and the second is the one worth seeing, because it is the one that says something new.
def card_use(name):
    used = telemetry_value(os.path.join(SRC, name), "Gpu/Device/UsedMemoryMB")
    total = telemetry_value(os.path.join(SRC, name), "Gpu/Device/TotalMemoryMB")
    try:
        return int(used) / int(total)
    except (TypeError, ValueError, ZeroDivisionError):
        return None


emptiest = sorted(((card_use(n), n) for n in have), key=lambda x: (x[0] is None, x[0]))
oom_pick = emptiest[0][1] if emptiest and emptiest[0][0] is not None and emptiest[0][0] < 0.95 else None
rest = [n for n in have if n != oom_pick]
picks = {"hook": rest[0] if len(rest) > 0 else None,
         "stack": rest[1] if len(rest) > 1 else None,
         "oom": oom_pick}

log_lines = []
done = {}
for what, name in picks.items():
    if not name:
        continue
    dst = os.path.join(queue, name)
    shutil.copytree(os.path.join(SRC, name), dst)
    dmp = os.path.join(dst, "Cyberpunk2077.dmp")
    if what == "hook":
        done[what] = "renamed %s -> RTSSHooks64.dll" % rename_module(dmp, "RTSSHooks64.dll")
    elif what == "stack":
        done[what] = "exception 0x%08X -> 0xC00000FD" % set_exception_code(dmp, 0xC00000FD)
    elif what == "oom":
        done[what] = "Engine/OOM -> true (card at %s of %s MB)" % (
            telemetry_value(dst, "Gpu/Device/UsedMemoryMB"), telemetry_value(dst, "Gpu/Device/TotalMemoryMB")) \
            if set_engine_oom(dst) else "FAILED to set Engine/OOM"
    m = re.match(r"Cyberpunk2077-(\d{8})-(\d{6})-", name)
    t = datetime.datetime.strptime(m.group(1) + m.group(2), "%Y%m%d%H%M%S")
    log_lines.append("%s|INFO|Redbug.Reporter.CP2077.Preparation.ReportCreator|Matching crash data directory: '%s'.\n"
                     % (t.strftime("%m/%d/%Y %H:%M:%S -07:00"), name))

io.open(os.path.join(fx, "LocalAppData", "REDEngine", "CrashReporter.log"), "w").write("".join(log_lines))


# ---------------------------------------------------------------- CrashesOnlyAtStartup
# Three sessions that each die about a minute in. The rule wants crashes whose length is actually known, so each
# log carries RED4ext's own crash block rather than just stopping - a log that simply ends is a session of unknown
# length, which the rule deliberately ignores.
PLUGINS = [("ArchiveXL", "1.27.2"), ("Codeware", "1.20.3"), ("TweakXL", "1.11.4")]
for day, secs in ((3, 71), (2, 48), (1, 96)):
    start = (NOW - datetime.timedelta(days=day)).replace(microsecond=0)
    t = lambda d, ms=0: (start + datetime.timedelta(seconds=d)).strftime("%Y-%m-%d %H:%M:%S") + ".%03d" % ms
    L = ["[%s] [info    ] [  4120] [RED4ext] RED4ext (v1.30.0) is initializing..." % t(0, 101),
         "[%s] [info    ] [  4120] [RED4ext] Product version: 2.31" % t(0, 102),
         "[%s] [info    ] [  4120] [RED4ext] File version: 3.0.80.51928" % t(0, 103),
         "[%s] [info    ] [  4120] [RED4ext] Loading plugins..." % t(1, 400)]
    for i, (name, ver) in enumerate(PLUGINS):
        L.append("[%s] [info    ] [  4120] [RED4ext] %s (version: %s, author(s): someone) has been loaded" % (t(2 + i, 0), name, ver))
    # NativePluginsRefusingToLoad rides along on the same logs: RED4ext names the plugin it would not load and the
    # runtime it wanted, every launch.
    L += ["[%s] [warning ] [  4120] [RED4ext] Trainer Bridge (version: 1.0.2) is incompatible with the current patch "
          "(requested runtime 3.0.80.33925, game is 3.0.80.51928)." % t(4, 500),
          "[%s] [info    ] [  4120] [RED4ext] %d plugin(s) loaded" % (t(6, 0), len(PLUGINS)),
          "[%s] [info    ] [  4120] [RED4ext] RED4ext has been started" % t(6, 200),
          "[%s] [error   ] [  4120] [RED4ext] Crash report" % t(secs, 0),
          "[%s] [error   ] [  4120] [RED4ext] File: scriptable.cpp" % t(secs, 1),
          "[%s] [error   ] [  4120] [RED4ext] Message: Unhandled exception while loading scripts" % t(secs, 2)]
    os.makedirs(os.path.join(game, "red4ext", "logs"), exist_ok=True)
    io.open(os.path.join(game, "red4ext", "logs", "red4ext-%s.log" % start.strftime("%Y-%m-%d-%H-%M-%S")),
            "w", encoding="utf-8").write("\n".join(L) + "\n")

    # ArchiveXlStartupProblems: a world sector patch that cannot be applied, logged in the first three minutes.
    A = ["[%s] [8800] [info] ArchiveXL 1.27.2 initialized." % t(3, 0),
         '[%s] [8800] [error] [WorldStreaming] jigjig.xl: Sector "jig_jig_unleashed.streamingsector" '
         "doesn't exist." % t(20, 0),
         '[%s] [8800] [error] [EntityAnimation] Animation "base\\animations\\ui\\photomode.anims" '
         "doesn't exist. Skipped." % t(22, 0)]
    os.makedirs(os.path.join(game, "red4ext", "plugins", "ArchiveXL"), exist_ok=True)
    io.open(os.path.join(game, "red4ext", "plugins", "ArchiveXL", "ArchiveXL-%s.log" % start.strftime("%Y-%m-%d-%H-%M-%S")),
            "w", encoding="utf-8").write("\n".join(A) + "\n")

# ScriptErrorSpam: one CET mod erroring far more than the ten-per-session threshold, after the newest session began.
# ---------------------------------------------------------------- VideoMemoryWhilePlaying
# A recording Crash Doctor's own window would have made during the newest of those sessions (VramMonitor.cs), in the
# CSV shape the recorder writes: card-wide use climbing from 60 % to 97 % of an 8006 MB card over two and a half
# minutes. The rule wants at least two minutes of readings, and a peak at 95 % or above is what makes it speak up.
vstart = (NOW - datetime.timedelta(days=1)).replace(microsecond=0)
vdir = os.path.join(fx, "AppData", "CrashDoctor", "vram-logs")
os.makedirs(vdir, exist_ok=True)
V = ["# Crash Doctor 0.6.0 video memory log", "# started %s" % vstart.strftime("%Y-%m-%d %H:%M:%S"),
     "# adapter Fixture Graphics 8 GB", "# total_mb 8006", "# interval_s 5", "# game_pid 4120", "time,card_mb,game_mb,shared_mb"]
for i in range(30):
    pct = 60 + (97 - 60) * i / 29.0
    card = int(8006 * pct / 100)
    V.append("%s,%d,%d,%d" % ((vstart + datetime.timedelta(seconds=5 * i)).strftime("%Y-%m-%d %H:%M:%S"), card, card - 900, 150))
io.open(os.path.join(vdir, "vram-%s.csv" % vstart.strftime("%Y-%m-%d_%H-%M-%S")), "w", encoding="utf-8").write("\r\n".join(V) + "\r\n")

newest = (NOW - datetime.timedelta(days=1)).replace(microsecond=0)
cet_mod = os.path.join(game, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "TriggerBridge")
os.makedirs(cet_mod, exist_ok=True)
io.open(os.path.join(cet_mod, "init.lua"), "w", encoding="utf-8").write("return {}\n")
io.open(os.path.join(cet_mod, "TriggerBridge.log"), "w", encoding="utf-8").write("".join(
    "[%s] [4120] init.lua:144: attempt to call global 'CloseClientProcess' (a nil value)\n"
    % (newest + datetime.timedelta(seconds=30 + i * 12)).strftime("%Y-%m-%d %H:%M:%S") for i in range(21)))

# DuplicatedScripts: the same script, byte for byte, bundled by two different mods.
SHARED = "// shared compatibility fix\n@replaceMethod(VehicleComponent) func Fix() -> Void {}\n"
for mod in ("ComputerAnywhere", "IdleAnywhere"):
    os.makedirs(os.path.join(game, "r6", "scripts", mod), exist_ok=True)
    io.open(os.path.join(game, "r6", "scripts", mod, "liftcheck.reds"), "w", encoding="utf-8").write(SHARED)


# ---------------------------------------------------------------- DeployedFilesAreMissing
# Vortex's record of what it put in the game folder, where some of it is no longer there - what verifying the game
# files through Steam leaves behind. Two files exist, three do not.
#
# All three missing files belong to one mod on purpose. That is the branch of the rule that can name the mod, so it
# is the branch that sets Mod and Flag and puts a chip on that row of the mods table - strictly more code than the
# "across N mods" wording, which differs from it only in a string.
present = [("archive\\pc\\mod\\fine.archive", "Nicer Hair-4821-2-1-1773080000"),
           ("bin\\x64\\dlss-enabler.dll", "GTX and RTX package-9001-3-0-2-1773081000")]
absent = [("archive\\pc\\mod\\betterloot.archive", "Better Loot-4102-1-9-1773082000"),
          ("archive\\pc\\mod\\betterloot.archive.xl", "Better Loot-4102-1-9-1773082000"),
          ("r6\\scripts\\BetterLoot\\BetterLoot.reds", "Better Loot-4102-1-9-1773082000")]
io.open(os.path.join(game, "vortex.deployment.json"), "w", encoding="utf-8").write(json.dumps({
    "instance": "fixture",
    "stagingPath": os.path.join(fx, "staging"),
    "files": [{"relPath": rel, "source": src, "target": "", "time": 1773082675000}
              for rel, src in present + absent],
}, indent=2))

# ---------------------------------------------------------------- RedscriptProblems
# One script that does not compile, and one method two mods are both replacing.
os.makedirs(os.path.join(game, "r6", "logs"), exist_ok=True)
REDSCRIPT_LOG = [
    "[INFO] Compiling 61 modules",
    r"[WARN] At r6\scripts\BetterLoot\loot.reds:44:1:",
    "  @replaceMethod(LootManager) overwrites a previous annotation on ProcessLoot",
    r"[ERROR] At r6\scripts\WeaponWheel\wheel.reds:12:3:",
    "  expected a value of type Int32, found String",
]
io.open(os.path.join(game, "r6", "logs", "redscript_rCURRENT.log"), "w", encoding="utf-8").write(
    "\n".join(REDSCRIPT_LOG) + "\n")


# ---------------------------------------------------------------- SettingsOverTheVramBudget
# Frame generation and ray tracing both on, plus path tracing. Whether this can fire depends on the card in the
# machine running the scan, which no fixture can fake - hence CRASHDOCTOR_TEST_SYSTEM, printed below.
io.open(os.path.join(fx, "LocalAppData", "CD Projekt Red", "Cyberpunk 2077", "UserSettings.json"),
        "w", encoding="utf-8").write(json.dumps({"var": [
    {"name": "FrameGeneration", "value": "On"},
    {"name": "RayTracing", "value": "true"},
    {"name": "RayTracedLighting", "value": "Ultra"},
    {"name": "RayTracedPathTracing", "value": "true"},
    {"name": "TextureQuality", "value": "High"},
    {"name": "CrowdDensity", "value": "High"},
    {"name": "Resolution", "value": "2560x1440"},
]}, indent="	"))

print("fixture:", fx)
for what, msg in done.items():
    print("  %-6s %s" % (what, msg))
print("  startup crash sessions: 3")
print("  vortex manifest: %d files, %d of them missing" % (len(present) + len(absent), len(absent)))
print("  redscript: 1 error, 1 annotation overwrite")
print("  red4ext: 1 incompatible plugin; archivexl: 2 startup errors")
print("  cet: 21 identical errors from one mod; scripts: 1 file bundled twice")
print("  video memory recording: 1 session, 30 readings climbing to 97%")
print()
print("Scan it with all three variables set - the third stands in for hardware, which a fixture cannot fake:")
print("  set CRASHDOCTOR_TEST_ROOT=%s" % fx)
print("  set OneDrive=%s" % os.path.join(fx, "OneDrive"))
print("  set CRASHDOCTOR_TEST_SYSTEM=vramgb=8;ramgb=16;driverdate=2023-04-01")
print("  CrashDoctor.exe --json out.json --game %s" % game)

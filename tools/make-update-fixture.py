"""Builds an install the morning after a game update (patch 2.5 is due 22 Oct 2026).

    python tools/make-update-fixture.py <outdir>
    set CRASHDOCTOR_TEST_ROOT=<outdir>\\updated
    CrashDoctor.exe --json out.json --game <outdir>\\updated\\game

Three sessions on game 2.31 that quit normally; then the exe is rewritten (its time is set to yesterday evening);
then one launch on the new build, which crashes at start-up. In that launch RED4ext reports the new build, refuses
ArchiveXL and TweakXL ("incompatible with the current patch") and loads Codeware; Cyber Engine Tweaks logs that it
does not support the game version. redscript is not installed, so it is not mentioned.

The build numbers here (2.5 / 3.0.81.10000) are made up for the fixture. Crash Doctor itself has no table of
versions: it reads them from the exe and the logs (GameUpdate.cs), so this tests the reasoning, not a guess.

Expected: a high Health item "Your game updated to 2.5, and your frameworks do not all support it yet" listing what
each log said; ArchiveXL, TweakXL and Cyber Engine Tweaks "outdated" in Requirements; the crash's verdict mentions
the update. Scanning a second time must say the same (the change is then remembered in config.json).
"""
import io, os, sys, shutil, datetime

ROOT = sys.argv[1] if len(sys.argv) > 1 else "."
fx = os.path.join(ROOT, "updated")
if os.path.isdir(fx):
    shutil.rmtree(fx)
game = os.path.join(fx, "game")


def W(path, text=""):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    io.open(path, "w", encoding="utf-8").write(text)


exe = os.path.join(game, "bin", "x64", "Cyberpunk2077.exe")
W(exe, "not a real executable\n")
W(os.path.join(game, "bin", "x64", "plugins", "cyber_engine_tweaks.asi"), "asi")
W(os.path.join(game, "red4ext", "RED4ext.dll"), "dll")
for p in ("ArchiveXL", "TweakXL", "Codeware"):
    W(os.path.join(game, "red4ext", "plugins", p, p + ".dll"), "dll")
W(os.path.join(game, "archive", "pc", "mod", "nicerhair.xl"), "resource:\n  patch: {}\n")
W(os.path.join(game, "r6", "tweaks", "loot.yaml"), "Items.X:\n  $base: Items.Y\n")
os.makedirs(os.path.join(fx, "AppData", "CrashDoctor"), exist_ok=True)
os.makedirs(os.path.join(fx, "LocalAppData", "REDEngine", "ReportQueue"), exist_ok=True)

NOW = datetime.datetime.now().replace(microsecond=0)
OLD = [("Product version: 2.31", "File version: 3.0.80.51928")]


def red4ext_log(start, product, filever, plugins, refused, end_line):
    t = lambda d, ms=0: (start + datetime.timedelta(seconds=d)).strftime("%Y-%m-%d %H:%M:%S") + ".%03d" % ms
    L = ["[%s] [info    ] [  4120] [RED4ext] RED4ext (v1.30.0) is initializing..." % t(0, 101),
         "[%s] [info    ] [  4120] [RED4ext] Product version: %s" % (t(0, 102), product),
         "[%s] [info    ] [  4120] [RED4ext] File version: %s" % (t(0, 103), filever),
         "[%s] [info    ] [  4120] [RED4ext] Loading plugins..." % t(1, 400)]
    for i, (name, ver) in enumerate(plugins):
        L.append("[%s] [info    ] [  4120] [RED4ext] %s (version: %s, author(s): someone) has been loaded" % (t(2 + i, 0), name, ver))
    for i, (name, ver) in enumerate(refused):
        L.append("[%s] [warning ] [  4120] [RED4ext] %s (version: %s) is incompatible with the current patch. The requested runtime of the plugin is 3.0.80.51928" % (t(4 + i, 0), name, ver))
    L.append("[%s] [info    ] [  4120] [RED4ext] RED4ext has been started" % t(6, 200))
    L += end_line(t)
    W(os.path.join(game, "red4ext", "logs", "red4ext-%s.log" % start.strftime("%Y-%m-%d-%H-%M-%S")), "\n".join(L) + "\n")


ALL = [("ArchiveXL", "1.27.4"), ("TweakXL", "1.11.4"), ("Codeware", "1.20.3")]
for day, minutes in ((4, 80), (3, 125), (2, 60)):
    start = NOW - datetime.timedelta(days=day, hours=3)
    end = start + datetime.timedelta(minutes=minutes)
    red4ext_log(start, "2.31", "3.0.80.51928", ALL, [],
                lambda t, end=end: ["[%s] [info    ] [  4120] [RED4ext] RED4ext has been shut down" % (end.strftime("%Y-%m-%d %H:%M:%S") + ".880")])

# the update: the exe is rewritten yesterday evening
upd = NOW - datetime.timedelta(days=1, hours=4)
os.utime(exe, (upd.timestamp(), upd.timestamp()))

# one launch on the new build, ending in a crash at start-up
start = NOW - datetime.timedelta(days=1, hours=2)
red4ext_log(start, "2.5", "3.0.81.10000", [("Codeware", "1.20.3")], [("ArchiveXL", "1.27.4"), ("TweakXL", "1.11.4")],
            lambda t: ["[%s] [error   ] [  4120] [RED4ext] Crash report" % t(41, 0),
                       "[%s] [error   ] [  4120] [RED4ext] File: scriptable.cpp" % t(41, 1),
                       "[%s] [error   ] [  4120] [RED4ext] Message: Unhandled exception" % t(41, 2)])

before = NOW - datetime.timedelta(days=2, hours=3)
st = lambda dt: dt.strftime("%Y-%m-%d %H:%M:%S")
W(os.path.join(game, "bin", "x64", "plugins", "cyber_engine_tweaks", "cyber_engine_tweaks.log"),
  "[%s UTC-07:00] [info] [Options] [9548] Cyber Engine Tweaks is starting...\n" % st(before)
  + "[%s UTC-07:00] [info] [Options] [9548] CET version v1.37.1 [HEAD]\n" % st(before)
  + "[%s UTC-07:00] [info] [Options] [9548] Game version 3.0.80.51928\n" % st(before)
  + "[%s UTC-07:00] [info] [Options] [9548] Cyber Engine Tweaks is starting...\n" % st(start)
  + "[%s UTC-07:00] [info] [Options] [9548] CET version v1.37.1 [HEAD]\n" % st(start)
  + "[%s UTC-07:00] [info] [Options] [9548] Game version 3.0.81.10000\n" % st(start)
  + "[%s UTC-07:00] [error] [Options] [9548] Unsupported game version! This version of CET supports 3.0.80.51928 only.\n" % st(start))
# the frameworks were installed long before any of these sessions (Crash Doctor 0.8.0 treats a framework file newer
# than the last launch as "updated since the game last ran")
old = (NOW - datetime.timedelta(days=10)).timestamp()
for rel in (os.path.join("red4ext", "RED4ext.dll"), os.path.join("bin", "x64", "plugins", "cyber_engine_tweaks.asi")) + tuple(os.path.join("red4ext", "plugins", p, p + ".dll") for p in ("ArchiveXL", "TweakXL", "Codeware")):
    os.utime(os.path.join(game, rel), (old, old))
print("built updated  ->", fx)

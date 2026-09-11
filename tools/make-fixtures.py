"""Builds synthetic Cyberpunk installs so Crash Doctor can be scanned in isolation.

Two scenarios:
  fresh   - game installed, never launched, no mods, no logs, no settings
  healthy - 12 mods, three sessions all quit normally, nothing wrong anywhere

Run:  python fixture.py <outdir>
Then: set CRASHDOCTOR_TEST_ROOT=<outdir>\<scenario> and scan <outdir>\<scenario>\game
"""
import io, json, os, sys, shutil, datetime

ROOT = sys.argv[1] if len(sys.argv) > 1 else "."


def W(path, text=""):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    io.open(path, "w", encoding="utf-8").write(text)


def D(path):
    os.makedirs(path, exist_ok=True)


def base(name):
    """Skeleton every scenario needs: the exe is what the game locator looks for."""
    s = os.path.join(ROOT, name)
    if os.path.isdir(s):
        shutil.rmtree(s)
    game = os.path.join(s, "game")
    D(os.path.join(game, "bin", "x64"))
    # not a real PE - deliberately, so the "exe has no version info" path gets exercised too
    W(os.path.join(game, "bin", "x64", "Cyberpunk2077.exe"), "not a real executable\n")
    D(os.path.join(s, "LocalAppData", "REDEngine", "ReportQueue"))
    D(os.path.join(s, "AppData", "CrashDoctor"))
    return s, game


# ---------------------------------------------------------------- fresh
fresh, game = base("fresh")
# Nothing else at all: no red4ext, no CET, no archive folder, no settings, no crash reports.
print("built fresh    ->", fresh)


# ---------------------------------------------------------------- healthy
healthy, game = base("healthy")

PLUGINS = [("ArchiveXL", "1.27.2"), ("Codeware", "1.20.3"), ("TweakXL", "1.11.4"), ("input_loader", "0.1.1")]
MODS_ARCHIVE = ["betterloot.archive", "nicerhair.archive", "vehiclepack.archive", "hd_textures.archive",
                "weaponsound.archive", "nightcitysigns.archive"]
MODS_XL = ["betterloot.archive.xl", "nicerhair.xl"]
MODS_CET = ["BetterLootMarkers", "SimpleFlashlight", "FreeFly"]
MODS_REDS = ["BetterLoot", "WeaponWheel", "NoIntro"]

for a in MODS_ARCHIVE:
    W(os.path.join(game, "archive", "pc", "mod", a), "x" * 4096)
for x in MODS_XL:
    W(os.path.join(game, "archive", "pc", "mod", x), "resource:\n  patch: {}\n")
for m in MODS_CET:
    W(os.path.join(game, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", m, "init.lua"),
      "return { onInit = function() end }\n")
for m in MODS_REDS:
    W(os.path.join(game, "r6", "scripts", m, m + ".reds"), "// " + m + "\n")
for name, _ in PLUGINS:
    W(os.path.join(game, "red4ext", "plugins", name, name + ".dll"), "dll")

# three sessions, every one of them quit through the menu
SESSIONS = [
    (datetime.datetime(2026, 9, 9, 19, 12, 4), 94),
    (datetime.datetime(2026, 9, 10, 20, 40, 51), 137),
    (datetime.datetime(2026, 9, 11, 18, 3, 17), 62),
]
for start, minutes in SESSIONS:
    end = start + datetime.timedelta(minutes=minutes)
    t = lambda d, ms=0: (start + datetime.timedelta(seconds=d)).strftime("%Y-%m-%d %H:%M:%S") + ".%03d" % ms
    L = [
        "[%s] [info    ] [  4120] [RED4ext] RED4ext (v1.30.0) is initializing..." % t(0, 101),
        "[%s] [info    ] [  4120] [RED4ext] Product version: 2.31" % t(0, 102),
        "[%s] [info    ] [  4120] [RED4ext] File version: 3.0.80.51928" % t(0, 103),
        "[%s] [info    ] [  4120] [RED4ext] Loading plugins..." % t(1, 400),
    ]
    for i, (name, ver) in enumerate(PLUGINS):
        L.append("[%s] [info    ] [  4120] [RED4ext] %s (version: %s, author(s): someone) has been loaded"
                 % (t(2 + i, 10 * i), name, ver))
    L += [
        "[%s] [info    ] [  4120] [RED4ext] %d plugin(s) loaded" % (t(8, 1), len(PLUGINS)),
        "[%s] [info    ] [  4120] [RED4ext] RED4ext has been started" % t(8, 200),
        "[%s] [info    ] [  4120] [RED4ext] scc invoked successfully, 31047 source refs were registered" % t(14, 0),
        "[%s] [info    ] [  4120] [RED4ext] RED4ext has been shut down" % (end.strftime("%Y-%m-%d %H:%M:%S") + ".880"),
    ]
    W(os.path.join(game, "red4ext", "logs", "red4ext-%s.log" % start.strftime("%Y-%m-%d-%H-%M-%S")), "\n".join(L) + "\n")

    # ArchiveXL: ordinary streaming activity, no errors
    A = ['[%s] [4120] [info] ArchiveXL 1.27.2 initialized.' % t(3, 0)]
    for k in range(24):
        A.append('[%s] [8800] [info] [WorldStreaming] Patching sector "base\\worlds\\03_night_city\\_compiled\\default\\exterior_-%d_%d_0_1.streamingsector"...'
                 % (t(60 + k * 40, 0), 4 + k % 6, 7 + k % 5))
        A.append('[%s] [8800] [info] [WorldStreaming] Applying changes from "betterloot.archive.xl"...' % t(60 + k * 40, 1))
        A.append('[%s] [8800] [info] [WorldStreaming] All patches have been applied to "base\\worlds\\03_night_city\\_compiled\\default\\exterior_-%d_%d_0_1.streamingsector".'
                 % (t(60 + k * 40, 2), 4 + k % 6, 7 + k % 5))
    W(os.path.join(game, "red4ext", "plugins", "ArchiveXL",
                   "ArchiveXL-%s.log" % start.strftime("%Y-%m-%d-%H-%M-%S")), "\n".join(A) + "\n")

last = SESSIONS[-1][0]
W(os.path.join(game, "bin", "x64", "plugins", "cyber_engine_tweaks", "cyber_engine_tweaks.log"),
  "[%s] [info] CET version v1.37.1 [HEAD]\n[%s] [info] Game version 2.31\n"
  % (last.strftime("%Y-%m-%d %H:%M:%S"), last.strftime("%Y-%m-%d %H:%M:%S")))
W(os.path.join(game, "bin", "x64", "plugins", "cyber_engine_tweaks", "scripting.log"),
  "".join("[%s UTC-07:00] [4120] Mod %s loaded! ('...')\n" % (last.strftime("%Y-%m-%d %H:%M:%S"), m) for m in MODS_CET))
W(os.path.join(game, "r6", "logs", "redscript_rCURRENT.log"),
  "[INFO] Compiling 3 modules\n[INFO] Output successfully saved\n")

# settings: nothing alarming - no frame gen, ray tracing off, textures High on a card that can take it
groups = {"var": [
    {"name": "FrameGeneration", "value": "Off"}, {"name": "RayTracing", "value": "false"},
    {"name": "RayTracedLighting", "value": "Off"}, {"name": "RayTracedPathTracing", "value": "false"},
    {"name": "TextureQuality", "value": "High"}, {"name": "CrowdDensity", "value": "Medium"},
    {"name": "Resolution", "value": "2560x1440"}, {"name": "ResolutionScaling", "value": "DLSS"},
    {"name": "DLSS", "value": "Quality"}, {"name": "ReflexMode", "value": "Enabled"},
    {"name": "WindowMode", "value": "Fullscreen"}, {"name": "VSync", "value": "UI-Settings-Video-QualitySetting-Off"},
]}
W(os.path.join(healthy, "LocalAppData", "CD Projekt Red", "Cyberpunk 2077", "UserSettings.json"),
  json.dumps(groups, indent="\t"))
# an empty crash reporter log: the reporter has run but never had anything to report
W(os.path.join(healthy, "LocalAppData", "REDEngine", "CrashReporter.log"),
  "09/11/2026 18:03:20 -07:00|INFO|Redbug.Reporter.CP2077.App|Crash Reporter launched.\n")
print("built healthy  ->", healthy)
print("\nmods in healthy:", len(MODS_ARCHIVE) + len(MODS_XL) + len(MODS_CET) + len(MODS_REDS) + len(PLUGINS), "artifacts")

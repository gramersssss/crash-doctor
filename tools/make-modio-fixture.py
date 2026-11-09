"""Builds an install with mods from the game's own mod menu (patch 2.5, run by mod.io).

    python tools/make-modio-fixture.py C:\\temp\\fixtures
    set CRASHDOCTOR_TEST_ROOT=C:\\temp\\fixtures\\modio
    CrashDoctor.exe --json out.json --game C:\\temp\\fixtures\\modio\\game

The layout is the mod.io SDK v2's, as the game's exe names it ("state.json", "PathOnDisk", "{}/mods/{}") and as an
empty install on Gary's PC shows it (8 Nov 2026: C:\\Users\\Public\\mod.io\\5784\\metadata\\state.json = {"Mods":[]},
LocalAppData\\mod.io\\globalsettings.json = {"RootLocalStoragePath":"C:/Users/Public/mod.io/"}, and
Saved Games\\CD Projekt Red\\Cyberpunk 2077\\EnabledMods.json with an empty "enabled_mods"). No install with mods was
available, so the entries below are a best guess at the SDK's keys and the reader must not depend on them:

- 6272372 "Neon Rain Retexture" 1.2.0: an archive folder, compiled scripts and a json tweak; listed as enabled
- 6338259 "Quiet Streets" 0.9: archive only; NOT in enabled_mods, so it must read "disabled"
- 6406157: a folder with no state.json entry (the name falls back to "mod.io 6406157")
- 6999999: a state.json entry whose folder is gone (must not be listed)
- the root comes from globalsettings.json (forward slashes), as the game writes it
- LocalAppData\\mod.io\\5784\\someone\\user.json holds a fake sign-in token that must never appear in any output
- one RED4ext session that ended normally two days ago, so the mod.io installs (their folders were created now) show
  under "what changed since your last clean session"

Expected: 3 extra rows in the mods list marked mod.io (2 enabled, 1 disabled), types "archive + compiled scripts +
tweaks" style for the first, the chip saying "+ in-game mod menu (mod.io): 2", and "installed or updated from the
in-game mod menu (mod.io)" lines under the changes since the last clean session. No rule fires.
"""
import io, os, sys, shutil, json, datetime

ROOT = sys.argv[1] if len(sys.argv) > 1 else "."
NOW = datetime.datetime.now().replace(microsecond=0)
TOKEN = "FAKE-MODIO-TOKEN-must-never-be-read-0000"


def W(path, text=""):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    io.open(path, "w", encoding="utf-8").write(text)


fx = os.path.join(ROOT, "modio")
if os.path.isdir(fx):
    shutil.rmtree(fx)
game = os.path.join(fx, "game")
W(os.path.join(game, "bin", "x64", "Cyberpunk2077.exe"), "not a real executable\n")
W(os.path.join(game, "red4ext", "RED4ext.dll"), "dll")
os.makedirs(os.path.join(fx, "AppData", "CrashDoctor"), exist_ok=True)
os.makedirs(os.path.join(fx, "LocalAppData", "REDEngine", "ReportQueue"), exist_ok=True)

# one normal session two days ago
start = NOW - datetime.timedelta(days=2, hours=3)
ts = lambda dt, ms=0: dt.strftime("%Y-%m-%d %H:%M:%S") + ".%03d" % ms
W(os.path.join(game, "red4ext", "logs", "red4ext-%s.log" % start.strftime("%Y-%m-%d-%H-%M-%S")), "\n".join([
    "[%s] [info    ] [ 40968] [RED4ext] RED4ext (v1.31.0) is initializing..." % ts(start, 738),
    "[%s] [info    ] [ 40968] [RED4ext] Product version: 2.50" % ts(start, 738),
    "[%s] [info    ] [ 40968] [RED4ext] File version: 3.0.88.46235" % ts(start, 738),
    "[%s] [info    ] [ 40968] [RED4ext] RED4ext has been successfully initialized" % ts(start + datetime.timedelta(seconds=2)),
    "[%s] [info    ] [ 40968] [RED4ext] Loading plugins..." % ts(start + datetime.timedelta(seconds=2)),
    "[%s] [info    ] [ 40968] [RED4ext] RED4ext has been started" % ts(start + datetime.timedelta(seconds=9)),
    "[%s] [info    ] [ 40968] [RED4ext] RED4ext has been shut down" % ts(start + datetime.timedelta(minutes=75), 880),
]) + "\n")
for dirpath, _, files in os.walk(os.path.join(game, "red4ext", "logs")):
    for f in files:
        t = (start + datetime.timedelta(minutes=75, seconds=1)).timestamp()
        os.utime(os.path.join(dirpath, f), (t, t))

store = os.path.join(fx, "Public", "mod.io")
mods = os.path.join(store, "5784", "mods")
W(os.path.join(mods, "6272372", "NeonRain", "neonrain.archive"), "archive")
W(os.path.join(mods, "6272372", "scripts", "neonrain.redscripts"), "compiled")
W(os.path.join(mods, "6272372", "scripts", "neonrain.redscripts.h64"), "h64")
W(os.path.join(mods, "6272372", "tweaks", "neonrain_tweak.json"), "{}")
W(os.path.join(mods, "6338259", "QuietStreets", "quietstreets.archive"), "archive")
W(os.path.join(mods, "6406157", "Something", "something.archive"), "archive")
W(os.path.join(store, "5784", "metadata", "state.json"), json.dumps({"Mods": [
    {"ID": 6272372, "Profile": {"id": 6272372, "name": "Neon Rain Retexture", "modfile": {"version": "1.2.0"}},
     "CurrentState": 1, "SizeOnDisk": 12, "PathOnDisk": (mods + "/6272372").replace("\\", "/")},
    {"ID": 6338259, "Profile": {"id": 6338259, "name": "Quiet Streets", "modfile": {"version": "0.9"}},
     "CurrentState": 1, "PathOnDisk": (mods + "/6338259").replace("\\", "/")},
    {"ID": 6999999, "Profile": {"id": 6999999, "name": "Already Uninstalled"}, "CurrentState": 1,
     "PathOnDisk": (mods + "/6999999").replace("\\", "/")},
], "version": 1}))
W(os.path.join(fx, "LocalAppData", "mod.io", "globalsettings.json"), json.dumps({"RootLocalStoragePath": store.replace("\\", "/") + "/"}))
W(os.path.join(fx, "LocalAppData", "mod.io", "5784", "someone", "user.json"), json.dumps({"AuthToken": TOKEN, "UserName": "someone"}))
W(os.path.join(fx, "Saved Games", "CD Projekt Red", "Cyberpunk 2077", "EnabledMods.json"), json.dumps({
    "version": 6, "date": int(NOW.timestamp()), "modio_user_id": 0, "modio_reload_result": 3,
    "enabled_mods": [6272372, 6406157], "mods_config": {}}, indent=4))
print("built modio -> %s (token in user.json: %s)" % (fx, TOKEN))

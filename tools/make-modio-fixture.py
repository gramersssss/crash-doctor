"""Builds an install with mods from the game's own mod menu (patch 2.5, run by mod.io).

    python tools/make-modio-fixture.py C:\\temp\\fixtures
    set CRASHDOCTOR_TEST_ROOT=C:\\temp\\fixtures\\modio
    CrashDoctor.exe --json out.json --game C:\\temp\\fixtures\\modio\\game

The layout is the one seen on Gary's PC on 9 Nov 2026 with "Unlimited Reserve Ammo (Keep Reloading)" 1.0.1 installed
from the menu: C:\\Users\\Public\\mod.io\\5784\\metadata\\state.json = {"Mods":[{ID, NeverRetryCategory, NeverRetryCode,
PathOnDisk, Profile{name, modfile{version}, date_added, ...}, SizeOnDisk, State, SubscriptionCount}], "version":1}, and
the mod's folder 5784\\mods\\<ID> holding about.json, r6\\scripts\\<mod>\\*.reds and the game's own compiled
r6\\cache\\final.redscripts(.h64). Also LocalAppData\\mod.io\\globalsettings.json = {"RootLocalStoragePath":
"C:/Users/Public/mod.io/"} and Saved Games\\CD Projekt Red\\Cyberpunk 2077\\EnabledMods.json (8 Nov; its
"enabled_mods" entry format is still unseen - a list of ids is a guess). After an in-game uninstall the entry and the
folder are both gone. What is here:

- 6481578 "Unlimited Reserve Ammo (Keep Reloading)" 1.0.1: the real layout; listed as enabled
- 6338259 "Quiet Streets" 0.9: an archive mod (archive layout not yet seen for real); NOT in enabled_mods -> "disabled"
- 6406157: a folder with no state.json entry; its about.json names it "Something From About"
- 6999999: a state.json entry whose folder is gone (must not be listed)
- the root comes from globalsettings.json (forward slashes), as the game writes it
- LocalAppData\\mod.io\\5784\\someone\\user.json holds a fake sign-in token that must never appear in any output
- one RED4ext session that ended normally two days ago, so the menu installs (their folders were created now) show
  under "what changed since your last clean session"

Expected: 3 rows marked mod.io (2 enabled, 1 disabled), the first typed "redscript" and NOT listed under redscript in
Requirements (the game compiles menu mods itself), the chip "+ in-game mod menu (mod.io): 2", and "installed or updated
from the in-game mod menu (mod.io)" under the changes since the last clean session. No rule fires. Then delete
mods\\6481578 and its state.json entry (an in-game uninstall) and scan again: the install is still listed under
"what changed", marked "no longer installed" (remembered in AppData\\CrashDoctor\\modio-seen.json).
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
ura = os.path.join(mods, "6481578")
W(os.path.join(ura, "about.json"), json.dumps({"name": "Unlimited Reserve Ammo (Keep Reloading)", "version": "1.0.1"}))
W(os.path.join(ura, "r6", "scripts", "UnlimitedReserveAmmo", "UnlimitedReserveAmmo.reds"), "// script\n")
W(os.path.join(ura, "r6", "cache", "final.redscripts"), "compiled by the game")
W(os.path.join(ura, "r6", "cache", "final.redscripts.h64"), "h64")
W(os.path.join(mods, "6338259", "about.json"), json.dumps({"name": "Quiet Streets"}))
W(os.path.join(mods, "6338259", "QuietStreets", "quietstreets.archive"), "archive")
W(os.path.join(mods, "6406157", "about.json"), json.dumps({"name": "Something From About", "version": "2.0"}))
W(os.path.join(mods, "6406157", "Something", "something.archive"), "archive")


def entry(mid, name, version=None, size=4096):
    profile = {"id": mid, "name": name, "date_added": int(NOW.timestamp()), "date_live": int(NOW.timestamp()),
               "date_updated": int(NOW.timestamp()), "dependencies": False, "community_options": 0,
               "description": "<p>%s</p>" % name}
    if version:
        profile["modfile"] = {"version": version}
    return {"ID": mid, "NeverRetryCategory": 0, "NeverRetryCode": 0, "PathOnDisk": os.path.join(mods, str(mid)),
            "Profile": profile, "SizeOnDisk": size, "State": 1, "SubscriptionCount": 1}


W(os.path.join(store, "5784", "metadata", "state.json"), json.dumps({"Mods": [
    entry(6481578, "Unlimited Reserve Ammo (Keep Reloading)", "1.0.1"),
    entry(6338259, "Quiet Streets", "0.9"),
    entry(6999999, "Already Uninstalled", "3.0"),
], "version": 1}))
W(os.path.join(fx, "LocalAppData", "mod.io", "globalsettings.json"), json.dumps({"RootLocalStoragePath": store.replace("\\", "/") + "/"}))
W(os.path.join(fx, "LocalAppData", "mod.io", "5784", "someone", "user.json"), json.dumps({"AuthToken": TOKEN, "UserName": "someone"}))
W(os.path.join(fx, "Saved Games", "CD Projekt Red", "Cyberpunk 2077", "EnabledMods.json"), json.dumps({
    "version": 6, "date": int(NOW.timestamp()), "modio_user_id": 0, "modio_reload_result": 3,
    "enabled_mods": [6481578, 6406157], "mods_config": {}}, indent=4))
print("built modio -> %s (token in user.json: %s)" % (fx, TOKEN))

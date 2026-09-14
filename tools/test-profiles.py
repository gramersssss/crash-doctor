"""Exercise graphics profiles end to end against a sandbox copy of this machine's real settings file.

    python tools/test-profiles.py <scratch folder> [path to CrashDoctor.exe]

Copies UserSettings.json into <scratch folder>/profile-sandbox and points CRASHDOCTOR_TEST_ROOT at it, so every save,
edit, apply and undo happens to the copy. The last check hashes the real file before and after and fails if it
changed. The fixtures' settings files are simplified stand-ins that the profile code does not read, which is why
this test needs a real one: it only works on a PC where the game has been run at least once.
"""
import hashlib, io, json, os, shutil, subprocess, sys

if len(sys.argv) < 2:
    sys.exit(__doc__)
SC = os.path.abspath(sys.argv[1])
BOX = os.path.join(SC, "profile-sandbox")
EXE = sys.argv[2] if len(sys.argv) > 2 else os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src", "bin", "Release", "net8.0-windows", "CrashDoctor.exe")
REAL = os.path.join(os.environ["LOCALAPPDATA"], "CD Projekt Red", "Cyberpunk 2077", "UserSettings.json")
SETTINGS = os.path.join(BOX, "LocalAppData", "CD Projekt Red", "Cyberpunk 2077", "UserSettings.json")
ORIG = os.path.join(SC, "UserSettings.original.json")

sha = lambda p: hashlib.sha256(open(p, "rb").read()).hexdigest()
if not os.path.isfile(REAL):
    sys.exit("No UserSettings.json on this PC - run the game once first.")
real_before = sha(REAL)

if os.path.isdir(BOX):
    shutil.rmtree(BOX)
os.makedirs(os.path.dirname(SETTINGS))
os.makedirs(os.path.join(BOX, "AppData", "CrashDoctor"))
shutil.copy(REAL, SETTINGS)
shutil.copy(REAL, ORIG)
env = dict(os.environ, CRASHDOCTOR_TEST_ROOT=BOX)


def run(*a):
    p = subprocess.run([EXE, *a], capture_output=True, text=True, env=env)
    return p.returncode, (p.stdout + p.stderr).strip()


def opts(path):
    d = json.load(io.open(path, encoding="utf-8"))
    return {g["group_name"] + "/" + o["name"]: o for g in d["data"] for o in g["options"]}


results = []


def check(name, cond, detail=""):
    results.append(bool(cond))
    print(("PASS " if cond else "FAIL ") + name + ("   -> " + detail if detail else ""))


c, out = run("--profile-save", "Current"); check("save Current", c == 0, out)
c, out = run("--profile-save", "High quality"); check("save 'High quality' (name with a space)", c == 0 and "High quality" in out, out)
c, out = run("--profile-save", 'Bad "name"'); check("refuse a name that would break the page or a file name", c == 1 and "cannot contain" in out, out)
c, out = run("--profile-set", "High quality", "TextureQuality", "High"); check("set textures High", c == 0, out)
c, out = run("--profile-set", "High quality", "RayTracing", "false"); check("set ray tracing off (bool)", c == 0, out)
c, out = run("--profile-set", "High quality", "TextureQuality", "Ultra"); check("reject a value the game does not allow", c == 1 and "must be one of" in out, out)
c, out = run("--profile-set", "High quality", "Resolution", "2560x1440"); check("refuse to hand-edit a hardware-dependent option", c == 1 and "hardware" in out, out)
c, out = run("--profile-set", "High quality", "FieldOfView", "150"); check("reject a number out of range", c == 1 and "between" in out, out)

before = opts(SETTINGS)
c, out = run("--profile-apply", "High quality"); check("apply High quality", c == 0 and "changed" in out, out)
after = opts(SETTINGS)
tq = after["/graphics/presets/TextureQuality"]
check("textures now High with a matching index", tq["value"] == "High" and tq["index"] == 2, str({k: tq[k] for k in ("value", "index")}))
check("ray tracing now off", after["/graphics/raytracing/RayTracing"]["value"] is False)
changed = sorted(k for k in before if before[k] != after.get(k))
check("nothing else in the file changed",
      set(changed) <= {"/graphics/presets/TextureQuality", "/graphics/raytracing/RayTracing", "/graphics/presets/QuickPresets"}, str(changed))
d0 = json.load(io.open(ORIG, encoding="utf-8"))
d1 = json.load(io.open(SETTINGS, encoding="utf-8"))
check("file version and group list untouched", d0["version"] == d1["version"] and [g["group_name"] for g in d0["data"]] == [g["group_name"] for g in d1["data"]])
nongfx = lambda d: [g for g in d["data"] if not (g["group_name"].startswith("/graphics") or g["group_name"] == "/video/display")]
check("controls, audio, key bindings identical", nongfx(d0) == nongfx(d1))
backups = os.listdir(os.path.join(BOX, "AppData", "CrashDoctor", "settings-backups"))
check("a backup was written before the change", len(backups) == 1, str(backups))

c, out = run("--profile-apply", "High quality"); check("applying again changes nothing", c == 0 and "already" in out, out)
c, out = run("--profiles"); check("list shows High quality as active", "High quality  (active)" in out)

c, out = run("--profile-apply", "Current"); check("apply Current back", c == 0, out)
now, orig = opts(SETTINGS), opts(ORIG)
check("settings values match the original again", all(now[k]["value"] == orig[k]["value"] for k in orig))

c, out = run("--profile-undo"); check("undo 1 (back to High quality)", c == 0 and opts(SETTINGS)["/graphics/presets/TextureQuality"]["value"] == "High", out)
c, out = run("--profile-undo"); check("undo 2 (back to the original file)", c == 0, out)
check("after two undos the file is byte-identical to the original", sha(SETTINGS) == sha(ORIG))
c, out = run("--profile-undo"); check("a third undo has nothing left to undo", c == 1 and "nothing" in out, out)

check("the real settings file was never touched", sha(REAL) == real_before)
print("\n%d/%d passed" % (sum(results), len(results)))
sys.exit(0 if all(results) else 1)

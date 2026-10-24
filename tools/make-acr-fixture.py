"""Builds an install with Advanced Crash Reporter's reports beside real crashes.

    python tools/make-acr-fixture.py <outdir>
    set CRASHDOCTOR_TEST_ROOT=<outdir>\\acr
    CrashDoctor.exe --json out.json --game <outdir>\\acr\\game

Copies four real crashes from this PC - the engine's crash report folder AND Advanced Crash Reporter's text report
for each (ACR by Akiway, Nexus 34403; reading its reports was agreed with the author, Oct 2026) - and writes a
RED4ext session log around each, so each crash has a session. Read only from the real folders; everything is
written under <outdir>. What each one exercises (AdvancedCrashReporter.cs, Analyzer.Judge):

- 2 Oct 00:12  the game froze, the watchdog ended it; ACR found Cyber Engine Tweaks' code on the blocked worker,
               after three earlier "thread stalled" reports from the same process      -> a "froze" verdict, CET named
- 2 Oct 03:46  the same kind of freeze, Steam's overlay on the main thread               -> "froze", overlay as a low lead
- 15 Oct 23:32 a native fault in the game's own code, no mod code running, a plugin error 0.09 s before
                                                                                        -> function name in the evidence
- 14 Oct 21:37 the same report DOCTORED: its "Mod code RUNNING" list names TrainerBridge.dll, which this fixture
               installs as a native plugin                                              -> "crashed inside its code"

ACR's own folder layout is kept (bin\\x64\\plugins\\AdvancedCrashReporter.asi, ...\\reports\\*.txt, config.ini).
"""
import io, os, re, sys, shutil, datetime

ROOT = sys.argv[1] if len(sys.argv) > 1 else "."
GAME_REAL = os.environ.get("CD_GAME") or r"C:\Program Files (x86)\Steam\steamapps\common\Cyberpunk 2077"
ACR_REAL = os.path.join(GAME_REAL, "bin", "x64", "plugins", "AdvancedCrashReporter")
QUEUE_REAL = os.path.join(os.environ["LOCALAPPDATA"], "REDEngine", "ReportQueue")

fx = os.path.join(ROOT, "acr")
if os.path.isdir(fx):
    shutil.rmtree(fx)
game = os.path.join(fx, "game")
os.makedirs(os.path.join(game, "bin", "x64", "plugins"), exist_ok=True)
io.open(os.path.join(game, "bin", "x64", "Cyberpunk2077.exe"), "w").write("not a real executable\n")
io.open(os.path.join(game, "bin", "x64", "plugins", "AdvancedCrashReporter.asi"), "w").write("asi")
io.open(os.path.join(game, "bin", "x64", "plugins", "cyber_engine_tweaks.asi"), "w").write("asi")
acr = os.path.join(game, "bin", "x64", "plugins", "AdvancedCrashReporter")
os.makedirs(os.path.join(acr, "reports"), exist_ok=True)
if os.path.exists(os.path.join(ACR_REAL, "config.ini")):
    shutil.copy2(os.path.join(ACR_REAL, "config.ini"), os.path.join(acr, "config.ini"))
else:   # ACR purged by the mod manager: its reports stay behind, its config does not
    io.open(os.path.join(acr, "config.ini"), "w").write("[general]\n")
for d in ("TrainerBridge", "ArchiveXL"):
    os.makedirs(os.path.join(game, "red4ext", "plugins", d), exist_ok=True)
    io.open(os.path.join(game, "red4ext", "plugins", d, d + ".dll"), "w").write("dll")
queue = os.path.join(fx, "LocalAppData", "REDEngine", "ReportQueue")
os.makedirs(queue, exist_ok=True)
os.makedirs(os.path.join(fx, "AppData", "CrashDoctor"), exist_ok=True)

CASES = [
    ("Cyberpunk2077-20261002-001252-33384-24428", ["20261002-001122-hang-tid39928-01.txt", "20261002-001158-hang-tid39928-02.txt",
                                                    "20261002-001238-hang-tid39928-03.txt", "20261002-001252-crash-tid24428-04.txt"], None),
    ("Cyberpunk2077-20261002-034657-29476-2492", ["20261002-034657-crash-tid2492-01.txt"], None),
    ("Cyberpunk2077-20261015-233238-37808-40688", ["20261015-233238-crash-tid40688-01.txt"], None),
    ("Cyberpunk2077-20261014-213759-20700-14364", ["20261014-213759-crash-tid14364-01.txt"], "TrainerBridge.dll"),
]
log_lines = []
for folder, reports, doctor in CASES:
    src = os.path.join(QUEUE_REAL, folder)
    if not os.path.isdir(src):
        print("missing on this PC, skipped:", folder); continue
    shutil.copytree(src, os.path.join(queue, folder))
    at = datetime.datetime.strptime(folder[14:29], "%Y%m%d-%H%M%S")
    uptime = None
    for rep in reports:
        text = io.open(os.path.join(ACR_REAL, "reports", rep), encoding="utf-8").read()
        m = re.search(r"^Process uptime\s*:\s*(?:(\d+)m\s*)?([\d.]+)s", text, re.M)
        if m and rep == reports[-1]: uptime = int(m.group(1) or 0) * 60 + float(m.group(2))
        if doctor:
            # put a mod's native code on the faulting thread, the case none of the real reports has
            text = re.sub(r"(Mod code RUNNING on this thread:\n)  none\n",
                          r"\1  %s               [MOD-RED4EXT ] frame #0\n  -> The strongest evidence there is: this mod's own code was executing when it\n     happened. Update or remove it first.\n" % doctor, text, count=1)
            text = text.replace("Process id     : 20700", "Process id     : 20700")
        io.open(os.path.join(acr, "reports", rep), "w", encoding="utf-8", newline="\n").write(text)
    start = at - datetime.timedelta(seconds=uptime or 600)
    t = lambda d, ms=0: (start + datetime.timedelta(seconds=d)).strftime("%Y-%m-%d %H:%M:%S") + ".%03d" % ms
    L = ["[%s] [info    ] [  4120] [RED4ext] RED4ext (v1.30.0) is initializing..." % t(0, 101),
         "[%s] [info    ] [  4120] [RED4ext] Product version: 2.31" % t(0, 102),
         "[%s] [info    ] [  4120] [RED4ext] File version: 3.0.80.51928" % t(0, 103),
         "[%s] [info    ] [  4120] [RED4ext] Loading plugins..." % t(1, 400),
         "[%s] [info    ] [  4120] [RED4ext] TrainerBridge (version: 1.0.0, author(s): someone) has been loaded" % t(2, 0),
         "[%s] [info    ] [  4120] [RED4ext] ArchiveXL (version: 1.27.4, author(s): psiberx) has been loaded" % t(2, 10),
         "[%s] [info    ] [  4120] [RED4ext] 2 plugin(s) loaded" % t(6, 0),
         "[%s] [info    ] [  4120] [RED4ext] RED4ext has been started" % t(6, 200)]
    os.makedirs(os.path.join(game, "red4ext", "logs"), exist_ok=True)
    io.open(os.path.join(game, "red4ext", "logs", "red4ext-%s.log" % start.strftime("%Y-%m-%d-%H-%M-%S")), "w", encoding="utf-8").write("\n".join(L) + "\n")
    log_lines.append("%s|INFO|Redbug.Reporter.CP2077.Preparation.ReportCreator|Matching crash data directory: '%s'.\n"
                     % (at.strftime("%m/%d/%Y %H:%M:%S -07:00"), folder))
io.open(os.path.join(fx, "LocalAppData", "REDEngine", "CrashReporter.log"), "w").write("".join(log_lines))
print("built acr      ->", fx)

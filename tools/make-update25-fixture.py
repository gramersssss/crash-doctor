"""Builds Gary's 22 Oct 2026 launch on game 2.5 with the 2.31-era frameworks, in two flavours.

    python tools/make-update25-fixture.py <outdir>
    set CRASHDOCTOR_TEST_ROOT=<outdir>\\updated25          (or <outdir>\\updated25-new)
    CrashDoctor.exe --json out.json --game <outdir>\\updated25\\game

What really happened (red4ext-2026-10-22-19-07-22.log and -19-08-07.log on Gary's PC): RED4ext 1.30.0 started on
game 2.5 (Product version 2.50, File version 3.0.88.46235), refused AppearanceRuntimePlugin 1.4.0 ("incompatible
with the current patch. The requested runtime of the plugin is 3.0.80.51928"), began loading ArchiveXL 1.27.5 -
whose own log got as far as "ArchiveXL 1.27.5 is starting..." - and the game died three seconds after launch
(c0000005, Windows BEX64, no engine crash report). Advanced Crash Reporter did write a report: a fault in an unmapped
address called from the game, under RED4ext's PluginSystem::Load, its own verdict "Unknown culprit". It happened
twice in a row.

updated25: three clean sessions on 2.31 (RED4ext 1.30.0, ArchiveXL 1.27.5, TweakXL 1.11.5, Codeware 1.20.6,
RedFileSystem 0.15.1, CET 1.37.1), the exe rewritten yesterday, then the two launches above. The first has an ACR
report (synthetic, same landmarks as the real one); the second has none, and Windows' own crash record cannot be
faked, so it stays unconfirmed. Expected:
  - Health, first: high "Your game updated to 2.5, and your frameworks do not all support it yet", saying
    RED4ext 1.30.0 is older than 1.31.0 (the first release for 2.5) and to update it; the game closed while RED4ext
    was loading ArchiveXL; ArchiveXL 1.27.5 / TweakXL 1.11.5 / Codeware 1.20.6 older than 1.28.0 / 1.12.0 / 1.21.0,
    the first releases for 2.5 (25 Oct 2026);
    AppearanceRuntimePlugin refused, in plain words (built for 3.0.80.51928 (2.31)); RedFileSystem 0.15.1 older than
    0.16.0; CET 1.37.1 older than 1.38.0; the "update these, wait for the rest" advice.
  - Requirements: RED4ext, CET, ArchiveXL, TweakXL, Codeware "outdated".
  - The two newest sessions: "The game closed 3 s after it started, while RED4ext was loading the native plugin
    ArchiveXL", ArchiveXL the high suspect, plus "ArchiveXL 1.27.5 is older than 1.28.0 ... Update it".
  - The rule "Native plugin refuses to load on this patch: AppearanceRuntimePlugin" with the plain-words sentence.
  Scan twice: the second scan reads the remembered change and must say the same.

updated25-new: the same two launches, but no 2.31 session left (a new Crash Doctor user, or RED4ext's 2.31 logs have
rotated away). Crash Doctor cannot see the build change, so it falls back on the release table: 3.0.88.x is 2.5,
released 22 Oct 2026. Expected: the same high Health item, led by "Your game is 2.5, the update released on 22 Oct."
Only fires within 30 days of 22 Oct 2026 (the table's release date); after that this flavour expects no update item.

updated25-redeployed: updated25 after the frameworks were updated (Gary's state on 25 Oct: RED4ext 1.31.0, ArchiveXL
1.28.0, TweakXL 1.12.0, Codeware 1.21.1 deployed, game not started since). Every framework file is newer than the last
launch, so the 22 Oct logs describe the old files. Expected: a MEDIUM "Your game updated to 2.5" item that says each
was installed or updated after the game last ran and to start the game once and scan again; no framework "outdated" in
Requirements; no AppearanceRuntimePlugin refusal in the update item (its file was replaced too). The two 22 Oct
sessions keep their ArchiveXL verdict - that is history and stays true.
"""
import io, os, sys, shutil, datetime

ROOT = sys.argv[1] if len(sys.argv) > 1 else "."
NOW = datetime.datetime.now().replace(microsecond=0)


def W(path, text=""):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    io.open(path, "w", encoding="utf-8").write(text)


def build(name, with_old_sessions, redeployed=False):
    fx = os.path.join(ROOT, name)
    if os.path.isdir(fx):
        shutil.rmtree(fx)
    game = os.path.join(fx, "game")
    exe = os.path.join(game, "bin", "x64", "Cyberpunk2077.exe")
    W(exe, "not a real executable\n")
    W(os.path.join(game, "red4ext", "RED4ext.dll"), "dll")
    W(os.path.join(game, "bin", "x64", "plugins", "cyber_engine_tweaks.asi"), "asi")
    W(os.path.join(game, "bin", "x64", "plugins", "AdvancedCrashReporter.asi"), "asi")
    for p in ("AppearanceRuntimePlugin", "ArchiveXL", "TweakXL", "Codeware", "RedFileSystem"):
        W(os.path.join(game, "red4ext", "plugins", p, p + ".dll"), "dll")
    W(os.path.join(game, "archive", "pc", "mod", "nicerhair.xl"), "resource:\n  patch: {}\n")
    os.makedirs(os.path.join(fx, "AppData", "CrashDoctor"), exist_ok=True)
    os.makedirs(os.path.join(fx, "LocalAppData", "REDEngine", "ReportQueue"), exist_ok=True)
    plugdir = os.path.join(game, "red4ext", "plugins")

    def ts(dt, ms=0):
        return dt.strftime("%Y-%m-%d %H:%M:%S") + ".%03d" % ms

    def red4ext_log(start, product, filever, lines):
        L = ["[%s] [info    ] [ 40968] [RED4ext] RED4ext (v1.30.0) is initializing..." % ts(start, 738),
             "[%s] [info    ] [ 40968] [RED4ext] Product version: %s" % (ts(start, 738), product),
             "[%s] [info    ] [ 40968] [RED4ext] File version: %s" % (ts(start, 738), filever),
             "[%s] [info    ] [ 40968] [RED4ext] RED4ext has been successfully initialized" % ts(start + datetime.timedelta(seconds=2), 678),
             "[%s] [info    ] [ 40968] [RED4ext] Loading plugins..." % ts(start + datetime.timedelta(seconds=2), 935)]
        L += lines
        W(os.path.join(game, "red4ext", "logs", "red4ext-%s.log" % start.strftime("%Y-%m-%d-%H-%M-%S")), "\n".join(L) + "\n")

    OLD = [("AppearanceRuntimePlugin", "1.4.0"), ("ArchiveXL", "1.27.5"), ("TweakXL", "1.11.5"), ("Codeware", "1.20.6"), ("RedFileSystem", "0.15.1")]
    cet = []
    if with_old_sessions:
        for day, minutes in ((4, 80), (3, 125), (2, 60)):
            start = NOW - datetime.timedelta(days=day, hours=3)
            end = start + datetime.timedelta(minutes=minutes)
            body = ["[%s] [info    ] [ 40968] [RED4ext] %s (version: %s, author(s): someone) has been loaded" % (ts(start + datetime.timedelta(seconds=3 + i)), n, v) for i, (n, v) in enumerate(OLD)]
            body += ["[%s] [info    ] [ 40968] [RED4ext] RED4ext has been started" % ts(start + datetime.timedelta(seconds=9)),
                     "[%s] [info    ] [ 40968] [RED4ext] RED4ext has been shut down" % ts(end, 880)]
            red4ext_log(start, "2.31", "3.0.80.51928", body)
            st = start.strftime("%Y-%m-%d %H:%M:%S")
            cet += ["[%s UTC-07:00] [info] [Options] [9548] Cyber Engine Tweaks is starting..." % st,
                    "[%s UTC-07:00] [info] [Options] [9548] CET version v1.37.1 [HEAD]" % st,
                    "[%s UTC-07:00] [info] [Options] [9548] Game version 3.0.80.51928" % st]
    else:
        # CET's log from a session long gone, so the installed version is still readable
        st = (NOW - datetime.timedelta(days=40)).strftime("%Y-%m-%d %H:%M:%S")
        cet += ["[%s UTC-07:00] [info] [Options] [9548] Cyber Engine Tweaks is starting..." % st,
                "[%s UTC-07:00] [info] [Options] [9548] CET version v1.37.1 [HEAD]" % st]
    W(os.path.join(game, "bin", "x64", "plugins", "cyber_engine_tweaks", "cyber_engine_tweaks.log"), "\n".join(cet) + "\n")

    # the update: the exe is rewritten yesterday afternoon
    upd = NOW - datetime.timedelta(days=1, hours=6)
    os.utime(exe, (upd.timestamp(), upd.timestamp()))
    # the frameworks were installed well before every session here; a framework file newer than the last launch would
    # read as "updated since the game last ran" (see the updated25-redeployed flavour)
    old = (NOW - datetime.timedelta(days=10)).timestamp() if not redeployed else NOW.timestamp()
    for rel in [os.path.join("red4ext", "RED4ext.dll"), os.path.join("bin", "x64", "plugins", "cyber_engine_tweaks.asi")] +                [os.path.join("red4ext", "plugins", p, p + ".dll") for p in ("AppearanceRuntimePlugin", "ArchiveXL", "TweakXL", "Codeware", "RedFileSystem")]:
        os.utime(os.path.join(game, rel), (old, old))

    # the two launches on 2.5, each dying three seconds in while RED4ext loads ArchiveXL
    for k, start in enumerate((NOW - datetime.timedelta(days=1, hours=2), NOW - datetime.timedelta(days=1, hours=2) + datetime.timedelta(seconds=45))):
        t = lambda sec, ms=0: ts(start + datetime.timedelta(seconds=sec), ms)
        red4ext_log(start, "2.50", "3.0.88.46235", [
            "[%s] [info    ] [ 40968] [RED4ext] Loading plugin from '%s'..." % (t(2, 937), os.path.join(plugdir, "AppearanceRuntimePlugin", "AppearanceRuntimePlugin.dll")),
            "[%s] [warning ] [ 40968] [RED4ext] AppearanceRuntimePlugin (version: 1.4.0) is incompatible with the current patch. The requested runtime of the plugin is 3.0.80.51928" % t(2, 943),
            "[%s] [info    ] [ 40968] [RED4ext] Loading plugin from '%s'..." % (t(2, 944), os.path.join(plugdir, "ArchiveXL", "ArchiveXL.dll")),
        ])
        W(os.path.join(plugdir, "ArchiveXL", "ArchiveXL-%s.log" % (start + datetime.timedelta(seconds=2)).strftime("%Y-%m-%d-%H-%M-%S")),
          "[%s] [40968] [info] ArchiveXL 1.27.5 is starting...\n" % t(2, 956))
        if k == 0:
            gen = start + datetime.timedelta(seconds=2, milliseconds=983)
            W(os.path.join(game, "bin", "x64", "plugins", "AdvancedCrashReporter", "reports", "%s-crash-tid40968-01.txt" % gen.strftime("%Y%m%d-%H%M%S")), "\n".join([
                "=" * 80,
                "# ADVANCED CRASH REPORTER - NATIVE FAULT (first chance)",
                "=" * 80,
                "Report version : 1.4.0",
                "Generated      : %s" % ts(gen, 983),
                "Process uptime : 3.138s",
                "Process id     : 40628",
                "Faulting thread: 40968  (MAIN THREAD)",
                "Main thread    : 40968",
                "Summary        : The game's own code crashed. No mod's code was running, and no mod left a clue.",
                "Verdict        : Unknown culprit",
                "Technical      : main thread faulted in Cyberpunk2077.exe [GAME-EXE]; no mod code executing (mod hooks on call path: RED4ext.dll)",
                "",
                "=" * 80,
                "# WHAT HAPPENED",
                "=" * 80,
                "Exception code : 0xC0000005  (EXCEPTION_ACCESS_VIOLATION)",
                "Faulting code  : 0x000001A6723A0140  <unmapped>",
                "Called from    : 0x00007FF64D37802C  Cyberpunk2077.exe            +0x201802C  [GAME-EXE]",
                "",
                "=" * 80,
                "# MOD INVOLVEMENT",
                "=" * 80,
                "Mod code RUNNING on this thread:",
                "  none",
                "",
                "Script mod RUNNING on this thread (redscript):",
                "  none",
                "",
                "=" * 80,
                "# PRIMARY STACK",
                "=" * 80,
                "  >> [06] 0x00007FFE0C91DCD8  RED4ext.dll                  +0x7DCD8  (PluginBase::Main+0x128, src/dll/PluginBase.cpp:98)  [LOADER]",
                "  >> [07] 0x00007FFE0C92991C  RED4ext.dll                  +0x8991C  (PluginSystem::Load+0x84C, src/dll/Systems/PluginSystem.cpp:311)  [LOADER]",
            ]) + "\n")
    if redeployed:
        # the logs were written when those sessions ran, not now (a real log's time is its last line's): without this
        # every log is as new as the redeployed framework files and nothing reads as "updated since the game last ran"
        import re
        for dirpath, _, files in os.walk(game):
            for f in files:
                if not (f.endswith(".log") or f.endswith(".txt")):
                    continue
                p = os.path.join(dirpath, f)
                stamps = re.findall(r"(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)", io.open(p, encoding="utf-8").read())
                if stamps:
                    t = datetime.datetime.strptime(stamps[-1], "%Y-%m-%d %H:%M:%S").timestamp() + 1
                    os.utime(p, (t, t))
    print("built %-14s -> %s" % (name, fx))


build("updated25", True)
build("updated25-new", False)
build("updated25-redeployed", True, redeployed=True)

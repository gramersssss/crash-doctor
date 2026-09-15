# tools

## make-fixtures.py

Builds synthetic Cyberpunk installs so Crash Doctor can be scanned in isolation, without a second PC and without
being judged against the developer's own crash history.

    python tools/make-fixtures.py C:\temp\fixtures

Two scenarios:

- `fresh` - game installed, never launched, no mods, no logs, no settings
- `healthy` - 12 mods, three sessions that all quit normally, nothing wrong anywhere

Scan one with the machine-data roots redirected, so the real profile is untouched:

    set CRASHDOCTOR_TEST_ROOT=C:\temp\fixtures\healthy
    CrashDoctor.exe --json out.json --html out.html --game C:\temp\fixtures\healthy\game

`CRASHDOCTOR_TEST_ROOT` redirects LocalAppData and AppData together, which is where the crash reports, the game
settings and Crash Doctor's own history live. Without it a pretend game folder is still measured against the real
machine, which is how a "never played" fixture first reported five sessions and named a mod it had never seen.

`CRASHDOCTOR_TIMING=1` prints how long each source and phase took. Useful when a scan crawls on someone else's
machine and there is nothing else to go on.

`CRASHDOCTOR_TEST_SYSTEM=vramgb=8;ramgb=16;driverdate=2023-04-01` replaces what WMI reported about the machine.
`CRASHDOCTOR_TEST_ROOT` is enough for every rule that reads a file, but two of them read the hardware - the card's
size and the driver's date - and no fixture can fake a graphics card. Without this seam those two rules could only
ever be watched firing on a PC that happened to have the wrong card in it. Ignored unless set.

Note that the GUI ignores `--game` and always locates the real install, so a fixture must be scanned through the
command line.

## make-twobuild-fixture.py

    python tools/make-twobuild-fixture.py C:\temp\fixtures

Copies two real crash reports and rewrites the faulting module's VS_FIXEDFILEINFO in one of them, producing the
same faulting offset recorded under two different game builds. That is the case build-keyed signatures exist for,
and it is the one case a single machine's crash history cannot produce on its own: an install only ever crashes on
the build it is running. Scan it the same way and both offsets should appear as two separate groups, each saying
why.

## make-rules-fixture.py

    python tools/make-rules-fixture.py C:\temp\fixtures
    set CRASHDOCTOR_TEST_ROOT=C:\temp\fixtures\broken
    set OneDrive=C:\temp\fixtures\broken\OneDrive
    CrashDoctor.exe --json out.json --game C:\temp\fixtures\broken\game

Builds an install that deliberately trips every rule in src/Engine/Rules.cs. A rule that has never been seen to
fire is not a rule, it is a hope, and real crash data cannot produce these on demand - the whole point of them is
that they go wrong on other people's machines. What it manufactures:

- an upscaler enabler sitting beside the game, and two archives that downloaded as nothing
- saves inside a synced folder (the scan is run with OneDrive pointed at the fixture)
- three copies of real crash reports, each doctored for one rule: a module renamed to a capture hook, an exception
  code rewritten to a stack overflow, and the engine's own out-of-memory flag flipped on in the telemetry
- three RED4ext logs for sessions that each die about a minute in, one of which also names a native plugin the
  loader refused, and an ArchiveXL log with a failing world-sector patch and a missing resource
- a redscript log with one script that does not compile and one method two mods both replace
- a CET mod log with twenty-one identical errors, and one script bundled byte-for-byte by two mods
- graphics settings with frame generation, ray tracing and path tracing all on
- a Vortex deployment manifest listing five files, three of which are not on disk
- a video memory recording (the CSV `VramMonitor.cs` writes while the game runs) for the newest of the three
  sessions, climbing to 97 % of an 8006 MB card over two and a half minutes
- for "what changed since your last clean session" (`Changes.cs`): a fourth session, four days back, that quit
  through the menu with ArchiveXL 1.27.1, and a `AppData\Vortex\vortex.log` recording an install and a deployment
  the day after it. Every crash should then list three things: the install, the deployment, and the ArchiveXL
  version change 1.27.1 to 1.27.2

`CrashDoctor.exe --vram-probe` prints the graphics adapters DXGI reports and one reading of the Windows GPU memory
counters, the way the recorder takes it. It is the thing to ask someone with an AMD or Intel card to run and paste,
because it says which adapter was picked and how big it is without anyone having to play first.

The crash reports are copied from the real machine rather than generated, because a minidump is not worth writing
by hand and because the rest of the report staying real is what makes the fixture honest.

Every rule should fire here and none of them should fire against a healthy install or the `healthy` fixture. The
fixture is self-sufficient on purpose: several of these conditions happen to be true on the author's own machine
too, and verification that leans on that quietly stops covering them the day they get fixed.

## make-icon.py

    python tools/make-icon.py            # writes src/app.ico
    python tools/make-icon.py --sheet    # every palette side by side, writes nothing

Draws `src/app.ico`, which `<ApplicationIcon>` embeds in the exe. The mark is a vital-signs trace that runs flat,
spikes once and carries on, on a tile with its top-right corner cut away. The shipped palette is the game's own -
Night City yellow tile, black trace, hot-red spike - chosen by Gary on 14 Sep so the icon matches the game; the
original grey Instrument version and two dark ones (pause-menu yellow, terminal cyan) are kept as alternatives in
`PALETTES`. No game art, logo or typeface is used.

Nine sizes are drawn separately rather than resized from one, because the detailed trace turns to mush at 16 px;
below 48 px a simplified trace with a heavier stroke is used instead. It also writes `test/icon-sheet.png` (not
committed), which shows every size on light and dark backgrounds. Judge the 16 px one there before committing -
that is the size Explorer and the taskbar actually use.

The icon was a generated placeholder with no source for the first eleven commits. This file is that source now;
change the geometry here, never the .ico.

## make-header.py

    python tools/make-header.py docs/screenshots/01-diagnosis-vram.png docs/screenshots/00-header.png

Composes the Nexus page header (1300x372) with PIL: the app icon from `src/app.ico`, "Crash Doctor" and the
tagline in the Neon theme's colours and the app's own fonts (Bahnschrift, Segoe UI, Consolas), a cut-corner crop
of the real diagnosis card from the first gallery screenshot, and the icon's vital-signs trace along the foot. No
generated imagery anywhere in it, at Gary's request. Re-run it after re-rendering the screenshots.

## make-screenshots.py

    CrashDoctor.exe --json out.json --html report.html
    python tools/make-screenshots.py report.html docs/screenshots [--size 1920x1080] [--theme pause]

Defaults are what Nexus asks for in a gallery image (1920x1080) in the app's default theme, Neon (stored id
`pause`). Two flags change that. Ten views are rendered; the what-changed shot scrolls its panel into view.
`--run-all-compositor-stages-before-draw` is what makes the page's fade-in finish before the capture - without it
the main panel came out empty - and the script waits for each PNG because Edge returns before writing it.

Renders the Nexus screenshots from a saved report with headless Edge: one image per view, all the same size and
zoom, reproducible after a UI change. Photographing the window by hand gives eight slightly different pictures and
has to be redone from scratch every time the layout moves.

A saved report hides the Scan button and replaces the rail footer with a "saved copy" notice. The harness puts
both back, because the screenshots are of the app, not of a saved file. That is the only thing it changes - there
is no invented data anywhere in them.

The three diagnosis shots name a session id, because the newest crash on a machine is whatever happened last and
is rarely the one worth showing. Those ids are in the report; update them when the report changes.

Re-rendering writes all eight files even when nothing has changed, because the header carries the time of the
scan. Check `git diff` before committing them: if the only difference is a few pixels in the "Last scan" chip,
revert them rather than committing eight new binaries.

## test-profiles.py

    python tools/test-profiles.py <scratch folder>

End-to-end test of graphics profiles: save, edit, reject invalid values, apply, apply again, undo twice back to a
byte-identical file, and a refused third undo. It runs against a copy of this machine's real UserSettings.json in
`<scratch folder>/profile-sandbox`, with `CRASHDOCTOR_TEST_ROOT` pointed there, and its last check hashes the real
file before and after - it fails if the real one changed at all.

It needs a real settings file because the fixtures' ones are simplified stand-ins in an older shape that the profile
code does not read. So it only runs on a PC where the game has been launched at least once.

What it cannot test from the command line: the refusal to apply while Cyberpunk 2077 is running (the check is a
process lookup; start the game and click Apply to see it), and the native confirmation dialogs in the window.

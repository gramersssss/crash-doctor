# Crash Doctor for Cyberpunk 2077

Tells you *why* your modded game crashed, in plain language, from the evidence already on your PC.

After a crash, open Crash Doctor and press **Scan now**. It reads the mod loaders' logs (RED4ext, Cyber Engine Tweaks,
ArchiveXL, redscript), the game's crash reporter, the Windows display-driver events and your graphics settings, then
gives every play session a verdict: GPU fault, mod/script error, engine error, or closed normally. For a crash it shows the
evidence in the seconds before it, names the suspect mods, and offers actions: open the mod on Nexus, apply a reviewed
one-click fix (always backed up, always revertible), or jump to the setting that is over budget.

It also keeps a **Health** list of standing problems it finds before you crash (plugins that refuse to load on your patch,
world-sector patches that fail every launch, mods pointing at missing files, error spam, settings that do not fit your
VRAM) and a **Requirements** screen that detects each framework your installed mods depend on and takes you to the right
download.

**Graphics profiles** save the game's graphics settings under a name — Quality, Performance, Streaming — so you can
switch between them in one click, and edit them without launching the game. Only options the game's own settings file
lists as valid can be edited; resolution and upscaler modes, which depend on your monitor and card, are saved and
restored exactly as the game wrote them.

**Crash logs are kept.** The game deletes its logs after a few launches. The first time Crash Doctor sees a crash it
zips that crash's report and logs into `%APPDATA%\CrashDoctor\crash-logs`, plus the three most recent clean sessions
to compare against. It only copies, and it can be switched off under Settings.

**Video memory is recorded while you play.** The game only writes how full the card was into crash reports, so a
session that did not crash leaves no number at all. While the Crash Doctor window is open (minimised is fine) it reads
the Windows GPU memory counters every 5 seconds, the same ones Task Manager shows, on NVIDIA, AMD and Intel cards
alike, and keeps one small file per launch in `%APPDATA%\CrashDoctor\vram-logs`. Each session then shows its peak,
median and minutes above 90 %, with a curve of the whole session, and Health sums up the recent ones. When the game
closes, the scan runs again by itself. Nothing runs after the window is closed; it can be switched off under Settings.
`CrashDoctor.exe --vram-probe` prints one reading and which adapter was picked.

**What changed since your last clean session.** For every crash, a dated list of what is different between this
install and the one that last played without a problem: mods installed or updated in Vortex (from its own log) and
how many files its deployments added and removed, native plugins that changed version between the two sessions (from
the RED4ext logs, so it works with any mod manager), mod files written by hand, and a game update. It is a list of
facts, and it says so: being new is not evidence of guilt, but it is the shortest list to test first. The no-crash
screen shows the same list for right now.

**New crashes are flagged.** If the game crashed since you last looked, a notice sits at the top of every page until you
open the crash or dismiss it, and those crashes are marked new in Sessions.

**Appearance:** Neon by default, a skin in the game's own style. Or pick light, dark, match Windows, or the other
skin, Terminal, like the computers you jack into.

Crash Doctor reads by default. It changes something only when you ask: a reviewed fix to a mod file, or applying a
graphics profile. Both ask for confirmation, back up what they change first, and can be undone in one click. Profiles
touch only graphics and display settings — never controls, audio or key bindings — and refuse to apply while the game
is running, because the game rewrites its settings when it exits.

## Requirements
- Windows 10 / 11, 64-bit
- Cyberpunk 2077 (Steam, GOG or Epic; or pick the folder by hand)
- Microsoft WebView2 runtime (already on every up-to-date Windows 11; Crash Doctor offers to install it if missing)

## Install
**Manual (recommended):** unzip anywhere, run `CrashDoctor.exe`.
**Vortex:** install like any mod; the files land in `bin\x64\tools\CrashDoctor\` inside the game folder. Add
`CrashDoctor.exe` to Vortex's dashboard as a tool if you want a launch button there.

## Headless use
```
CrashDoctor.exe --json report.json [--html report.html] [--game "D:\Games\Cyberpunk 2077"] [--no-mods]
```

Graphics profiles, for a launcher script that picks a quality before starting the game:
```
CrashDoctor.exe --profiles
CrashDoctor.exe --profile-save "Quality"
CrashDoctor.exe --profile-set "Quality" TextureQuality High
CrashDoctor.exe --profile-apply "Quality"
CrashDoctor.exe --profile-undo
```

## Building
.NET 8 SDK. `build.ps1 -Version x.y.z` produces a single-file, self-contained exe (no .NET needed on the
user's PC) in `dist\CrashDoctor-<version>\`, plus the manual-install and Vortex zips for Nexus.

## Privacy
Nothing leaves your PC, with one opt-in exception: the update check under Settings, off by default, fetches one small
version file at most once a day and sends nothing about you or your install. A saved report contains your game path,
hardware model, mod names and log excerpts; share it only if you are comfortable with that.

## Licence
MIT.

# Crash Doctor for Cyberpunk 2077

Tells you *why* your modded game crashed, in plain language, from the evidence already on your PC.

After a crash, open Crash Doctor and press **Scan now**. It reads the mod loaders' logs (RED4ext, Cyber Engine Tweaks,
ArchiveXL, redscript), the game's crash reporter, the Windows display-driver events and your graphics settings, then
gives every play session a verdict: GPU fault, mod/script error, engine error, or closed normally. For a crash it shows the
evidence in the seconds before it, names the suspect mods, and offers actions: open the mod on Nexus, apply a reviewed
one-click fix where one exists (always backed up, always revertible), or jump to the setting that is over budget.
0.6.0 ships no fixes: a fix contains the mod author's own file, so one is added only with that author's permission.

It also keeps a **Health** list of standing problems it finds before you crash (plugins that refuse to load on your patch,
world-sector patches that fail every launch, mods pointing at missing files, error spam, settings that do not fit your
VRAM) and a **Requirements** screen that detects each framework your installed mods depend on and takes you to the right
download.

**Graphics profiles** save the game's graphics settings under a name — Quality, Performance, Streaming — so you can
switch between them in one click, and edit them without launching the game. Only options the game's own settings file
lists as valid can be edited; resolution and upscaler modes, which depend on your monitor and card, are saved and
restored exactly as the game wrote them. **Play with this** backs up your settings, puts the profile in, starts the
game (through Steam on a Steam install) and puts your settings back when the game closes, so a one-off lighter
profile leaves your usual setup untouched. `CrashDoctor.exe --play "Name" --restore` does the same from a shortcut.

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

**Mod Organizer 2 is read directly.** With MO2 the mods (and, once the game has closed, often the frameworks and
their logs) are not in the game folder at all, so Crash Doctor finds the MO2 instance that manages the game (global
instances under `%LOCALAPPDATA%\ModOrganizer`, portable ones it has registered, a running MO2, or the folder you put in
`Mo2Path` in `%APPDATA%\CrashDoctor\config.json`), reads the selected profile's `modlist.txt` and every mod's folder in
priority order, Root Builder's `Root` folders included, and reads the logs from MO2's overwrite folder. The mod count,
the file owners behind every finding, the rules and Find it then work the same as with Vortex. Two MO2-only findings:
a mod the profile enables whose folder is missing, and script or plugin files one mod hides from another. It never
edits anything in MO2; you do not need to start Crash Doctor through MO2.

**Advanced Crash Reporter, if you have it.** [Advanced Crash Reporter](https://www.nexusmods.com/cyberpunk2077/mods/34403)
by Akiway (optional, not included) writes a detailed report for every crash and freeze. Crash Doctor reads those
reports, defensively, and adds the name of the function the game died in and whose native code was running on that
thread to the evidence. A mod's own code on the faulting thread is named as a suspect; a freeze the engine's watchdog
ended is shown as a freeze rather than a crash. ACR's own verdict is shown as its reading, in its own words.

**After a game update.** When the game's build changes, Health says so first, and lists what each framework's own log
said the first time the game ran on the new build: RED4ext and the plugins it refused, Cyber Engine Tweaks, redscript.
Frameworks usually need their own update after a game patch; Crash Doctor does not keep a table of which versions
support which game version, it reports what the logs say.

**Your language.** The window, the diagnosis and the saved report are available in English and the 18 languages the
game itself ships (Arabic, Chinese simplified and traditional, Czech, French, German, Hungarian, Italian, Japanese,
Korean, Polish, Portuguese (Brazil), Russian, Spanish (Spain and Mexico), Thai, Turkish, Ukrainian). It starts in your
Windows display language when it has it, else English. The language button at the top (a globe and the language's own
name) is always visible; English is first in its list, and **Ctrl+L** switches back to English from anywhere. The
translations are machine-assisted and use the game's own words for its settings; English is the reference. The
summary you copy for a forum can be copied in English whatever language the window is in, and a saved report carries
an English copy when it was saved in another language.

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
- Optional: [Advanced Crash Reporter](https://www.nexusmods.com/cyberpunk2077/mods/34403) by Akiway, for function names
  and freezes in the diagnosis

## Install
**Install:** unzip anywhere, run `CrashDoctor.exe`.
It is not a mod and there is no Vortex package: it does not belong in the game folder. If you want a launch button
in Vortex, add `CrashDoctor.exe` to its dashboard as a tool.

## Headless use
```
CrashDoctor.exe --json report.json [--html report.html] [--game "D:\Games\Cyberpunk 2077"] [--no-mods] [--lang de]
```
The command line writes English unless `--lang <code>` is given (codes: en, ar, cs, de, es-ES, es-MX, fr, hu, it, ja,
ko, pl, pt-BR, ru, th, tr, uk, zh-CN, zh-TW).

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
user's PC) in `dist\CrashDoctor-<version>\`, plus the zip for Nexus.

## Privacy
Nothing leaves your PC, with one opt-in exception: the update check under Settings, off by default, fetches one small
version file at most once a day and sends nothing about you or your install. A saved report contains your game path,
hardware model, mod names and log excerpts; share it only if you are comfortable with that.

## Source and licence
Source at https://github.com/gramersssss/crash-doctor. MIT. `version.json` at the root of the repository is what the
opt-in update check reads; bump it when a release goes up on Nexus.

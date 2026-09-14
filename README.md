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

**Appearance:** light, dark, or match Windows, plus two optional skins in the game's own style — Terminal, like the
computers you jack into, and Pause menu.

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
Nothing leaves your PC. A saved report contains your game path, hardware model, mod names and log excerpts; share it
only if you are comfortable with that.

## Licence
MIT.

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

Crash Doctor only reads. It never changes the game or your mods unless you click a fix and confirm it.

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
CrashDoctor.exe --json report.json [--html report.html] [--game "D:\Games\Cyberpunk 2077"]
```

## Building
.NET 8 SDK. `build.ps1` produces a single-file, self-contained `dist\CrashDoctor.exe`.

## Privacy
Nothing leaves your PC. A saved report contains your game path, hardware model, mod names and log excerpts; share it
only if you are comfortable with that.

## Licence
MIT.

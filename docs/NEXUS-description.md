# Nexus page draft

**Name:** Crash Doctor
**Category:** Utilities
**Tagline:** Tells you why your modded game crashed, in plain language, from the logs already on your PC.

## Description

Your game crashed. Was it the GPU, a mod, or the engine? Crash Doctor reads what the mod loaders and Windows already
wrote down, works out what happened in the seconds before the crash, and tells you in one sentence, with the evidence
underneath and the suspect mods named.

**What it reads**
RED4ext, Cyber Engine Tweaks, ArchiveXL and redscript logs; the game's crash reporter; the Windows display-driver
events; your graphics settings; your Vortex or manual mod list. Nothing else, and nothing leaves your PC.

**What you get**
- **Diagnosis.** One sentence per crash: "The GPU faulted while frame generation and ray tracing were both on",
  "Script error in <mod> 5 s before the crash", "Crashed while a world sector patch was being applied". Confidence
  shown honestly. Evidence listed as seconds-before-the-crash.
- **What to do.** Suspect mods with a button to their Nexus page, the setting to change, or a reviewed one-click fix
  for known problems (always backed up, one click to revert).
- **Session trace.** Every launch as a bar: how long it ran and how it ended. Patterns like "dies every 3 minutes"
  vs "dies after 4 hours" are half the diagnosis.
- **Health.** Standing problems found before you crash: native plugins that refuse to load on your patch, world
  patches that fail every launch, mods pointing at missing files, scripts that error every 12 seconds, settings that
  do not fit your VRAM.
- **Requirements.** Detects RED4ext, redscript, CET, ArchiveXL, TweakXL, Codeware and the Microsoft runtimes, shows
  which of your installed mods need each one, and takes you to the download.
- **Report.** Copy a summary for a help thread, or save the full report as a single HTML file.

**Install**
Manual: unzip anywhere, run CrashDoctor.exe. Vortex: install the Vortex file like any mod; it lands in
bin\x64\tools\CrashDoctor inside the game folder. Add it to Vortex's dashboard if you want a button.

**Requirements**
Windows 10/11 64-bit. Microsoft WebView2 runtime (included with Windows 11; Crash Doctor offers to install it).
No .NET install needed.

**It never changes your game or mods** unless you click a fix and confirm. Fixes are backed up and revertible from
inside the app.

## Permissions / credits
MIT licence. Source on request. Built with .NET 8 and WebView2.

## Screenshots to take
1. Diagnosis view with a real crash (verdict + evidence + what to do).
2. Session trace close-up.
3. Health list.
4. Requirements screen.

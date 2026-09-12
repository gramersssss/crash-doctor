# Nexus page

This is the finished page copy. Section 1 is the form fields, section 2 is the description in Markdown (the
canonical wording), section 3 is the same description as BBCode to paste straight into the Nexus editor, and
section 4 lists the screenshots and their captions.

Copy rules from DESIGN.md apply here too: nothing on this page may promise more than the tool delivers. The
audience has been sold confident crash tools before and they can smell it.

---

## 1. Form fields

| Field | Value |
|---|---|
| Name | Crash Doctor |
| Category | Utilities |
| Tagline | Reads your crash dumps and mod logs, and tells you what actually happened. |
| Version | 0.5.0 |
| Tags | Utilities, Modders Resources, Bug Fix, Performance, Debug |
| Requirements | Windows 10/11 64-bit. Microsoft WebView2 runtime (already present on up-to-date Windows 11). No .NET install needed. |
| Permissions | Read-only. Does not modify the game or any mod unless you click a fix and confirm it. |
| Licence | MIT |

---

## 2. Description

### You do not have 30 crashes. You have three problems.

Crash Doctor reads the crash dumps, mod-loader logs and Windows driver events already sitting on your PC, and
tells you what happened in plain language — which crashes are the same crash, what was running when each one
died, what is ruled out, and what to test next.

It is the only tool on this page that opens the minidump.

### What you actually get

**The fault, not a guess.** Every crash report the game saves contains a minidump. Crash Doctor reads the
faulting instruction, the exception code, the loaded modules and the video memory in use, and puts a name on
the crash: `Cyberpunk2077.exe+0x2A41E06`.

**Thirty crashes sorted into the few problems they really are.** Crashes are grouped by that fault address,
keyed to the game build they happened on. "This exact fault has happened 14 times between 16 Aug and 6 Sep"
is a completely different conversation from "the game crashes sometimes".

**Things ruled out, with the reason shown.** If a mod's errors also appear in sessions that ended perfectly
normally, it is noise, and Crash Doctor says so and demotes it instead of blaming it. It will not draw that
conclusion at all until it has at least two clean sessions to compare against — with no clean baseline it
tells you that rather than pretending.

**The video memory number nobody else shows you.** Used and total, at the instant of the crash, straight out
of the crash report. "8129 of 8006 MB in use — past what the card physically holds" ends an argument that
otherwise runs for days.

**Where you were.** Player position, district and tracked quest at the crash, plus which mods were rewriting
the world sectors underfoot at that moment.

**Health.** Standing problems found before you ever crash: native plugins that refuse to load on your patch,
world-sector patches that fail every launch, mods pointing at files that do not exist, scripts erroring every
twelve seconds, interrupted downloads that left an empty archive, a DLSS/FSR enabler that overwrote files the
game now ships itself, saves sitting in a OneDrive folder.

**Requirements.** Detects RED4ext, redscript, Cyber Engine Tweaks, ArchiveXL, TweakXL, Codeware, Equipment-EX
and the Microsoft runtimes, shows how many of your installed mods depend on each, and takes you to the right
download.

**Find it — guided bisect.** For the crash with no signature that nobody has seen before. Crash Doctor picks a
fault worth hunting, tells you which half of your mod list to switch off, and then *verifies what is actually
deployed* before it will accept the result. It never enables or disables anything itself. And "it didn't
crash" only counts when the run lasted meaningfully longer than that fault's own typical time-to-crash — a
threshold it computes from your history and shows you, so you can judge it yourself.

**A report you can paste into a help thread.** Copy a summary, or save the whole thing as one HTML file.
Personal folder paths are stripped, and you can leave your mod list out of the saved copy.

### What it will not do

It will not fix every crash, and it does not pretend to. The evidence work is reliable and takes ten seconds;
naming the culprit is a bonus, not the promise. Its confidence is allowed to reach zero, and "I don't know
yet — here is what is ruled out and here is the next test" is an answer it will give you.

What it reliably replaces is a week of switching mods off at random.

### Install

**Manual (recommended)** — unzip anywhere and run `CrashDoctor.exe`. It is not a mod; it does not go in the
game folder unless you want it to.

**Vortex** — install the Vortex file like any mod. It deploys to `bin\x64\tools\CrashDoctor\` inside the game
folder. Add `CrashDoctor.exe` to Vortex's dashboard as a tool if you want a launch button there.

Press **Scan now**. Nothing leaves your PC, ever.

### It only reads

Crash Doctor does not change your game or your mods. The one exception is a small catalogue of reviewed fixes
for specific known problems: those are applied only when you click and confirm, the original file is backed
up first, and one click puts it back.

### Requirements

- Windows 10 or 11, 64-bit
- Cyberpunk 2077 — Steam, GOG or Epic, found automatically, or pick the folder by hand
- Microsoft WebView2 runtime — already on up-to-date Windows 11; Crash Doctor offers to install it if missing
- No .NET install needed

### Command line

For scripts and for pasting into a thread:

```
CrashDoctor.exe --json report.json [--html report.html] [--game "D:\Games\Cyberpunk 2077"] [--no-mods]
```

### Credits

MIT licence. Built with .NET 8 and WebView2. Bug reports and rules for the catalogue are very welcome — if
you have a crash it read badly, post the saved report.

---

## 3. BBCode, ready to paste

```bbcode
[size=5][b]You do not have 30 crashes. You have three problems.[/b][/size]

Crash Doctor reads the crash dumps, mod-loader logs and Windows driver events already sitting on your PC, and tells you what happened in plain language — which crashes are the same crash, what was running when each one died, what is ruled out, and what to test next.

[b]It is the only tool on this page that opens the minidump.[/b]

[size=4][b]What you actually get[/b][/size]

[list]
[*][b]The fault, not a guess.[/b] Every crash report the game saves contains a minidump. Crash Doctor reads the faulting instruction, the exception code, the loaded modules and the video memory in use, and puts a name on the crash: [font=Courier New]Cyberpunk2077.exe+0x2A41E06[/font].
[*][b]Thirty crashes sorted into the few problems they really are.[/b] Crashes are grouped by that fault address, keyed to the game build they happened on. "This exact fault has happened 14 times between 16 Aug and 6 Sep" is a completely different conversation from "the game crashes sometimes".
[*][b]Things ruled out, with the reason shown.[/b] If a mod's errors also appear in sessions that ended perfectly normally, it is noise, and Crash Doctor says so and demotes it instead of blaming it. It will not draw that conclusion at all until it has at least two clean sessions to compare against.
[*][b]The video memory number nobody else shows you.[/b] Used and total, at the instant of the crash, straight out of the crash report. "8129 of 8006 MB in use — past what the card physically holds" ends an argument that otherwise runs for days.
[*][b]Where you were.[/b] Player position, district and tracked quest at the crash, plus which mods were rewriting the world sectors underfoot at that moment.
[*][b]Health.[/b] Standing problems found before you ever crash: native plugins that refuse to load on your patch, world-sector patches that fail every launch, mods pointing at files that do not exist, scripts erroring every twelve seconds, interrupted downloads that left an empty archive, a DLSS/FSR enabler that overwrote files the game now ships itself, saves sitting in a OneDrive folder.
[*][b]Requirements.[/b] Detects RED4ext, redscript, Cyber Engine Tweaks, ArchiveXL, TweakXL, Codeware, Equipment-EX and the Microsoft runtimes, shows how many of your installed mods depend on each, and takes you to the right download.
[*][b]Find it — guided bisect.[/b] For the crash with no signature that nobody has seen before. Crash Doctor tells you which half of your mod list to switch off, and then verifies what is actually deployed before it will accept the result. It never enables or disables anything itself. And "it didn't crash" only counts when the run lasted meaningfully longer than that fault's own typical time-to-crash.
[*][b]A report you can paste into a help thread.[/b] Copy a summary, or save the whole thing as one HTML file. Personal folder paths are stripped, and you can leave your mod list out of the saved copy.
[/list]

[size=4][b]What it will not do[/b][/size]

It will not fix every crash, and it does not pretend to. The evidence work is reliable and takes ten seconds; naming the culprit is a bonus, not the promise. Its confidence is allowed to reach zero, and "I don't know yet — here is what is ruled out and here is the next test" is an answer it will give you.

What it reliably replaces is a week of switching mods off at random.

[size=4][b]Install[/b][/size]

[b]Manual (recommended)[/b] — unzip anywhere and run CrashDoctor.exe. It is not a mod; it does not go in the game folder unless you want it to.

[b]Vortex[/b] — install the Vortex file like any mod. It deploys to [font=Courier New]bin\x64\tools\CrashDoctor\[/font] inside the game folder. Add CrashDoctor.exe to Vortex's dashboard as a tool if you want a launch button there.

Press [b]Scan now[/b]. Nothing leaves your PC, ever.

[size=4][b]It only reads[/b][/size]

Crash Doctor does not change your game or your mods. The one exception is a small catalogue of reviewed fixes for specific known problems: those are applied only when you click and confirm, the original file is backed up first, and one click puts it back.

[size=4][b]Requirements[/b][/size]

[list]
[*]Windows 10 or 11, 64-bit
[*]Cyberpunk 2077 — Steam, GOG or Epic, found automatically, or pick the folder by hand
[*]Microsoft WebView2 runtime — already on up-to-date Windows 11; Crash Doctor offers to install it if missing
[*]No .NET install needed
[/list]

[size=4][b]Credits[/b][/size]

MIT licence. Built with .NET 8 and WebView2. Bug reports and rules for the catalogue are very welcome — if you have a crash it read badly, post the saved report.
```

---

## 4. Screenshots

Rendered from a real scan of a 169-mod install; personal folder paths are scrubbed. Source images are produced
by `tools/make-screenshots.py`; see `tools/README.md`. Upload in this order — Nexus uses the first as the card
image, so the video-memory diagnosis leads.

| File | Caption |
|---|---|
| `01-diagnosis-vram.png` | One sentence, the evidence under it, and the video memory number at the instant of the crash. |
| `02-diagnosis-gpu.png` | The same fault, twelve times. Video memory was fine here, and it says so. |
| `03-diagnosis-streaming.png` | A world-sector patch failed sixteen seconds before the crash. Confidence: fair. |
| `04-sessions.png` | Every launch and how it ended. "Dies every 3 minutes" and "dies after 4 hours" are different problems. |
| `05-health.png` | Standing problems found before you crash. |
| `06-mods.png` | Your mod list, flagged — suspect, patch failing, plugin not loading, ruled out, fix applied. |
| `07-find-it.png` | Guided bisect: pick the fault to hunt, and it drives the disable-play-halve loop. |
| `08-requirements.png` | Every framework your installed mods actually depend on, and where to get it. |

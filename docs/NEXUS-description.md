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
| Version | 0.5.1 |
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

**What the messages mean.** When a crash's evidence contains a line somebody already understands — ArchiveXL
saying a world patch no longer fits the map, a Lua mod calling a function that never loaded, Windows resetting the
graphics driver — Crash Doctor explains it in plain language, with the exact line that matched and where the
knowledge comes from. It explains messages; it never uses them to blame a mod.

**A report you can paste into a help thread.** One button copies a plain-text summary short enough for a mod's
Bugs tab, a forum post or a Discord message. Another saves the whole thing as a single self-contained HTML file
that opens in any browser — useful where files can be attached, and as your own before-and-after record when you
change something. Personal folder paths are stripped from both, and you can leave your mod list out of the saved
copy.

**Copy for an AI.** Builds the same evidence into a question for Claude, ChatGPT or whatever assistant you already
use — the fault, the video memory at the crash, what the clean sessions have ruled out and why — and asks it not to
invent mod names. Crash Doctor sends nothing anywhere; it only puts text on your clipboard.

**Graphics profiles.** Save the game's graphics settings under a name and switch between them in one click —
a quality profile for exploring, a lighter one for dense districts or streaming. Edit a profile without launching
the game: only values the game's own settings file allows are offered, and resolution and upscaler modes, which
depend on your monitor and card, are kept exactly as the game wrote them. Applying backs up your current settings
first, refuses while the game is running, and Undo puts them back.

**Light, dark, or Night City.** Match your Windows theme, or pick one of two skins in the game's own style:
Terminal, like the computers you jack into, and Neon.

### What it will not do

It will not fix every crash, and it does not pretend to. The evidence work is reliable and takes ten seconds;
naming the culprit is a bonus, not the promise. Its confidence is allowed to reach zero, and "I don't know
yet — here is what is ruled out and here is the next test" is an answer it will give you.

What it reliably replaces is a week of switching mods off at random.

### This is an early release, and here is exactly how early

Version 0.5.1. Everything it does has been built and tested against **one** machine: a Steam install, Vortex,
an NVIDIA card with 8 GB, 169 mods, and a few weeks of real crashes. That install is why the tool exists, and it
is also the whole of its experience.

What it has never seen: an AMD card, a GOG or Epic install, Mod Organizer 2 or a hand-installed setup, REDmod
deployment, or a card bigger than 8 GB. The evidence it reads is the same everywhere, so most of it should simply
work — but "should" is doing real work in that sentence, and the video memory reading in particular comes from a
Windows field that is known to misreport on some cards.

So: if it tells you something that is obviously wrong, that is worth more to me than a thank-you. Post the saved
report on the Bugs tab. Every report says which build produced it on its first line, so a two-month-old one is
still useful. The catalogue of things it recognises grows directly from those.

### Windows will warn you the first time

Crash Doctor is an unsigned executable, so SmartScreen shows "Windows protected your PC" the first time you run
it. Click **More info** and then **Run anyway**. A code-signing certificate costs a few hundred pounds a year,
which is not something a free tool is going to carry — the same warning appears for most small Windows utilities
on this site.

If you would rather check than trust: the file is 63 MB because it carries the whole .NET runtime inside it, so it
does not need .NET installed. The source is MIT and available, and the report the tool saves shows you the commit
it was built from.

### Install

**Manual (recommended)** — unzip anywhere and run `CrashDoctor.exe`. It is not a mod; it does not go in the
game folder unless you want it to.

**Vortex** — install the Vortex file like any mod. It deploys to `bin\x64\tools\CrashDoctor\` inside the game
folder. Add `CrashDoctor.exe` to Vortex's dashboard as a tool if you want a launch button there.

Press **Scan now**. Nothing leaves your PC, ever.

### It changes nothing unless you ask

Scanning only reads. Crash Doctor changes something in exactly two cases, both because you clicked and confirmed:
applying one of a small catalogue of reviewed fixes to a specific mod file, and applying a graphics profile. Either
way the original is backed up first and one click puts it back. Profiles touch graphics and display settings only —
never controls, audio or key bindings. Crash Doctor never enables, disables or deletes a mod.

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
[*][b]What the messages mean.[/b] When a crash's evidence contains a line somebody already understands — ArchiveXL saying a world patch no longer fits the map, a Lua mod calling a function that never loaded, Windows resetting the graphics driver — Crash Doctor explains it in plain language, with the exact line that matched and where the knowledge comes from. It explains messages; it never uses them to blame a mod.
[*][b]A report you can paste into a help thread.[/b] One button copies a plain-text summary short enough for a mod's Bugs tab, a forum post or a Discord message. Another saves the whole thing as a single self-contained HTML file that opens in any browser — useful where files can be attached, and as your own before-and-after record when you change something. Personal folder paths are stripped from both, and you can leave your mod list out of the saved copy.
[*][b]Copy for an AI.[/b] Builds the same evidence into a question for Claude, ChatGPT or whatever assistant you already use — the fault, the video memory at the crash, what the clean sessions have ruled out and why — and asks it not to invent mod names. Crash Doctor sends nothing anywhere; it only puts text on your clipboard.
[*][b]Graphics profiles.[/b] Save the game's graphics settings under a name and switch between them in one click — a quality profile for exploring, a lighter one for dense districts or streaming. Edit a profile without launching the game: only values the game's own settings file allows are offered, and resolution and upscaler modes, which depend on your monitor and card, are kept exactly as the game wrote them. Applying backs up your current settings first, refuses while the game is running, and Undo puts them back.
[*][b]Light, dark, or Night City.[/b] Match your Windows theme, or pick one of two skins in the game's own style: Terminal, like the computers you jack into, and Neon.
[/list]

[size=4][b]What it will not do[/b][/size]

It will not fix every crash, and it does not pretend to. The evidence work is reliable and takes ten seconds; naming the culprit is a bonus, not the promise. Its confidence is allowed to reach zero, and "I don't know yet — here is what is ruled out and here is the next test" is an answer it will give you.

What it reliably replaces is a week of switching mods off at random.

[size=4][b]This is an early release, and here is exactly how early[/b][/size]

Version 0.5.1. Everything it does has been built and tested against [b]one[/b] machine: a Steam install, Vortex, an NVIDIA card with 8 GB, 169 mods, and a few weeks of real crashes. That install is why the tool exists, and it is also the whole of its experience.

What it has never seen: an AMD card, a GOG or Epic install, Mod Organizer 2 or a hand-installed setup, REDmod deployment, or a card bigger than 8 GB. The evidence it reads is the same everywhere, so most of it should simply work — but "should" is doing real work in that sentence, and the video memory reading in particular comes from a Windows field that is known to misreport on some cards.

So: if it tells you something that is obviously wrong, that is worth more to me than a thank-you. Post the saved report on the Bugs tab. Every report says which build produced it on its first line, so a two-month-old one is still useful. The catalogue of things it recognises grows directly from those.

[size=4][b]Windows will warn you the first time[/b][/size]

Crash Doctor is an unsigned executable, so SmartScreen shows "Windows protected your PC" the first time you run it. Click [b]More info[/b] and then [b]Run anyway[/b]. A code-signing certificate costs a few hundred pounds a year, which is not something a free tool is going to carry — the same warning appears for most small Windows utilities on this site.

If you would rather check than trust: the file is 63 MB because it carries the whole .NET runtime inside it, so it does not need .NET installed. The source is MIT and available, and the report the tool saves shows you the commit it was built from.

[size=4][b]Install[/b][/size]

[b]Manual (recommended)[/b] — unzip anywhere and run CrashDoctor.exe. It is not a mod; it does not go in the game folder unless you want it to.

[b]Vortex[/b] — install the Vortex file like any mod. It deploys to [font=Courier New]bin\x64\tools\CrashDoctor\[/font] inside the game folder. Add CrashDoctor.exe to Vortex's dashboard as a tool if you want a launch button there.

Press [b]Scan now[/b]. Nothing leaves your PC, ever.

[size=4][b]It changes nothing unless you ask[/b][/size]

Scanning only reads. Crash Doctor changes something in exactly two cases, both because you clicked and confirmed: applying one of a small catalogue of reviewed fixes to a specific mod file, and applying a graphics profile. Either way the original is backed up first and one click puts it back. Profiles touch graphics and display settings only — never controls, audio or key bindings. Crash Doctor never enables, disables or deletes a mod.

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

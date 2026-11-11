# Nexus page

This is the finished page copy. Section 1 is the form fields, section 2 is the description in Markdown (the
canonical wording), section 3 is the same description as BBCode to paste straight into the Nexus editor, and
section 4 lists the screenshots and their captions.

The description follows the five headings Nexus lays out for a mod page - Description, Installation instructions,
Main features, Requirements, Shout outs - and says in its first paragraph that this is a work in progress and where
to report problems.

Copy rules from DESIGN.md apply here too: nothing on this page may promise more than the tool delivers. The
audience has been sold confident crash tools before and they can smell it.

Refreshed for 0.8.2 (11 Nov 2026): version line, the three things it can change (fix, graphics profile, save repair),
Mod Organizer 2, mod.io, Advanced Crash Reporter, 19 languages, Clear history, the save repair, Find it's groups,
Always on and your own links, and the update check (on by default, once per start).

---

## 1. Form fields

| Field | Value |
|---|---|
| Name | Crash Doctor |
| Category | Utilities |
| Tagline | Reads your crash dumps and mod logs, and tells you what actually happened. |
| Short description (approved 14 Sep, 310 chars) | Find out why your modded Cyberpunk 2077 crashed. Crash Doctor reads the crash reports and mod logs already on your PC, groups repeat crashes together, rules out mods that also act up when the game runs fine, and shows how full your graphics card was. It can also save crash logs and graphics settings profiles. |
| Version | 0.8.2 |
| Tags | Utilities, Modders Resources, Bug Fix, Performance, Debug |
| Requirements | Windows 10/11 64-bit. Microsoft WebView2 runtime (already present on up-to-date Windows 11). No .NET install needed. Optional: Advanced Crash Reporter (34403), read when installed. |
| Permissions | Reads by default. Changes a mod file or the game's graphics settings, or writes a repaired copy of a save, only when you click and confirm; always backed up or kept, and undoable. |
| Licence | MIT |

---

## 2. Description

### Description

**This is a work in progress.** Version 0.8 has been built and tested mainly against one PC: a Steam install, Vortex,
an NVIDIA card with 8 GB, 169 mods and a few months of real crashes. If it tells you something that is obviously wrong
on your PC, or something does not work at all, please say so in the **Bugs** section or in **Posts**. That is worth
more to me than a thank-you, and it is how the tool gets better. Every report it saves says which build produced it
on its first line, so a two-month-old one is still useful.

**You do not have 30 crashes. You have three problems.** Crash Doctor reads the crash dumps, mod-loader logs and
Windows driver events already sitting on your PC, and tells you what happened in plain language: which crashes are
the same crash, what was running when each one died, what is ruled out, and what to test next. It is the only tool
on this page that opens the minidump.

**What it will not do.** It will not fix every crash, and it does not pretend to. The evidence work is reliable and
takes ten seconds; naming the culprit is a bonus, not the promise. Its confidence is allowed to reach zero, and
"I don't know yet, here is what is ruled out and here is the next test" is an answer it will give you. What it
reliably replaces is a week of switching mods off at random.

**It changes nothing unless you ask.** Scanning only reads. Crash Doctor changes something in exactly three cases,
each only because you clicked and confirmed: applying a reviewed fix to a specific mod file (none ships yet; a fix is
added only with that mod author's permission), applying a graphics profile, and repairing a save. A fix or a profile
backs up the original first and one click puts it back; a save repair writes a new save beside the old one, never
changes the original, and Undo removes the copy. Profiles touch graphics and display settings only, never controls,
audio or key bindings. Clear old logs moves the game's logs into Crash Doctor's own folder, deletes nothing, and Put
them back returns them. Crash Doctor never enables, disables or deletes a mod. Nothing leaves your PC, with one
exception: the update check. Each time Crash Doctor starts it fetches one small version file, and it sends nothing
about you or your install. It is on by default; switch it off under Settings > Updates and nothing is sent anywhere.

**What it has never seen:** an AMD or Intel card, a GOG or Epic install, a real Mod Organizer 2 install (it reads MO2
directly, but has only been tested against a test install built to look like one), a hand-installed setup, REDmod
deployment, or a card bigger than 8 GB. Mods from the game's own mod menu (mod.io) are listed too, but that is checked
against one real mod so far. The evidence it reads is the same everywhere, so most of it should simply work, but
"should" is doing real work in that sentence. If you have one of those, your report is the one I most want to see.

### Installation instructions

**Install.** Unzip anywhere and run `CrashDoctor.exe`. It is not a mod; it does not go in the game
folder unless you want it to. The same zip is on GitHub: github.com/gramersssss/crash-doctor/releases/tag/v0.8.2

**There is no Vortex package**, on purpose. Crash Doctor is not a mod and does not belong in the game folder. If you
want a launch button in Vortex, add `CrashDoctor.exe` to its dashboard as a tool. With Mod Organizer 2 you do not need
to start it through MO2: it finds the MO2 instance that manages your game by itself.

**Windows will warn you the first time.** Crash Doctor is an unsigned executable, so SmartScreen shows "Windows
protected your PC" the first time you run it. Click **More info** and then **Run anyway**. A code-signing
certificate costs a few hundred pounds a year, which is not something a free tool is going to carry; the same warning
appears for most small Windows utilities on this site. If you would rather check than trust: the exe is about 130 MB
because it carries the whole .NET runtime inside it, so it does not need .NET installed. The source is MIT at
github.com/gramersssss/crash-doctor, and the report the tool saves shows you the commit it was built from.

Then press **Scan now**. If you want video memory recorded while you play, leave the window open (minimised is fine)
and it does the rest. It starts in your Windows language (19 languages); the language button at the top changes it,
and Ctrl+L goes back to English.

### Main features

**The fault, not a guess.** Every crash report the game saves contains a minidump. Crash Doctor reads the faulting
instruction, the exception code, the loaded modules and the video memory in use, and puts a name on the crash:
`Cyberpunk2077.exe+0x2A41E06`. With Advanced Crash Reporter installed it also reads its reports, which add the
function the game crashed in and whose code was running there.

**Thirty crashes sorted into the few problems they really are.** Crashes are grouped by that fault address, keyed to
the game build they happened on. "This exact fault has happened 14 times between 16 Aug and 6 Sep" is a completely
different conversation from "the game crashes sometimes".

**Things ruled out, with the reason shown.** If a mod's errors also appear in sessions that ended perfectly normally,
it is noise, and Crash Doctor says so and demotes it instead of blaming it. It will not draw that conclusion at all
until it has at least two clean sessions to compare against; with no clean baseline it tells you that rather than
pretending.

**The video memory number nobody else shows you.** Used and total, at the instant of the crash, straight out of the
crash report. "8129 of 8006 MB in use, past what the card physically holds" ends an argument that otherwise runs
for days.

**Video memory recorded while you play.** The game only writes that number into crash reports, so a session that did
not crash leaves nothing. While the Crash Doctor window is open it reads the same Windows GPU counters Task Manager
uses, on NVIDIA, AMD and Intel cards alike, every 5 seconds, and shows each session's peak, median and minutes above
90 % with a curve of the whole session. That is how you find out whether High textures were actually safe. Nothing
runs after the window is closed, and when the game closes the scan runs again by itself.

**What changed since your last clean session.** For every crash, a dated list of what is different between this
install and the one that last played without a problem: mods installed or updated in Vortex, MO2 or the game's mod
menu, how many files were added and removed, native plugins that changed version between the two sessions (from the
RED4ext logs, so it works with any mod manager), mod files written by hand, and a game update. Being new is not
evidence of guilt, and it says so; it is the shortest list to test first.

**After a game update.** Health says which frameworks loaded on the new version and which refused, from their own
logs, and names a framework that is older than the first release made for that version, with the version to update
to. It also names the native plugin the game died loading, and a script mod that no longer compiles.

**Where you were.** Player position, district and tracked quest at the crash, plus which mods were rewriting the
world sectors underfoot at that moment.

**Health.** Standing problems found before you ever crash: native plugins that refuse to load on your patch,
world-sector patches that fail every launch, mods pointing at files that do not exist, scripts erroring every twelve
seconds, interrupted downloads that left an empty archive, a DLSS/FSR enabler that overwrote files the game now ships
itself, saves sitting in a OneDrive folder.

**Mox Gorilla Arms save repair.** A game bug with male V, Phantom Liberty, the Legendary Gorilla Arms and the Mox style
can leave a save with the Mox look missing its first-person part: the arms keep the Mox shape whatever style you pick,
and Mox and Barghest are invisible in first person. Crash Doctor finds such saves and, when you press Repair and
confirm, writes a repaired copy as a new save beside it. The original is never changed, and Undo removes the copy.
Checked in the game on one save so far; female V saves are left alone.

**Requirements check.** Detects RED4ext, redscript, Cyber Engine Tweaks, ArchiveXL, TweakXL, Codeware, Equipment-EX
and the Microsoft runtimes, shows how many of your installed mods depend on each, and takes you to the right download.

**Find it, a guided bisect.** For the crash with no signature that nobody has seen before. Crash Doctor picks a fault
worth hunting, tells you which half of your mod list to switch off, and then *verifies what is actually deployed*
before it will accept the result. It switches a mod off together with every mod that needs it (from Vortex rules,
redscript imports, Cyber Engine Tweaks mods and files inside another mod's folder), so the game can still start. Mods
you mark **Always on** are never switched off, nor anything they need, and you can add your own "this mod needs that
one" links. It never enables or disables anything itself. And "it didn't crash" only counts when the run lasted
meaningfully longer than that fault's own typical time-to-crash, a threshold it computes from your history and shows
you, so you can judge it yourself.

**What the messages mean.** When a crash's evidence contains a line somebody already understands (ArchiveXL saying a
world patch no longer fits the map, a Lua mod calling a function that never loaded, Windows resetting the graphics
driver) Crash Doctor explains it in plain language, with the exact line that matched and where the knowledge comes
from. It explains messages; it never uses them to blame a mod.

**A report you can paste into a help thread.** One button copies a plain-text summary short enough for a mod's Bugs
tab, a forum post or a Discord message, in English if you like, whatever language the window is in. Another saves the
whole thing as a single self-contained HTML file that opens in any browser, useful where files can be attached, and as
your own before-and-after record when you change something. Personal folder paths are stripped from both, and you can
leave your mod list out of the saved copy.

**Copy for an AI.** Builds the same evidence into a question for Claude, ChatGPT or whatever assistant you already
use (the fault, the video memory at the crash and through the session, what the clean sessions have ruled out and
why) and asks it not to invent mod names. Crash Doctor sends nothing anywhere; it only puts text on your clipboard.

**Graphics profiles.** Save the game's graphics settings under a name and switch between them in one click: a quality
profile for exploring, a lighter one for dense districts or streaming. Edit a profile without launching the game;
only values the game's own settings file allows are offered, and resolution and upscaler modes, which depend on your
monitor and card, are kept exactly as the game wrote them. Applying backs up your current settings first, refuses
while the game is running, and Undo puts them back. **Play with this** backs up your settings, puts the profile in,
starts the game, and puts your settings back when it closes, so a lighter profile for one session leaves your usual
setup untouched.

**Crash logs, kept.** The game deletes its logs after a few launches, which is how most crashes end up with nothing
left to examine. The first time Crash Doctor sees a crash it zips the crash report and that session's logs, and keeps
the three most recent clean sessions to compare against. It only copies, and you can switch it off.

**Clear old logs, clear history, and put them back.** Clear old logs moves every log out of the game folder, so the
next session starts clean; Put them back returns them. Clear history forgets the crashes Crash Doctor has seen so far
(it asks first, and Undo brings them back). Old crashes whose cause has since changed are marked "looks settled"
instead of leading the page.

**New crashes flagged.** If the game crashed since you last looked, a notice sits at the top of every page until you
open it or dismiss it.

**Update check.** On by default. Each time Crash Doctor starts it fetches one small version file and tells you when a
newer build is out, with a button to the download page. It never downloads or installs anything itself, and it
sends nothing about you or your install. Switch it off under Settings > Updates.

**Night City by default, or light and dark.** It opens in Neon, a skin in the game's own style. Pick Terminal, like
the computers you jack into, or a plain light or dark look that matches your Windows theme.

**Command line**, for scripts and for pasting into a thread:

```
CrashDoctor.exe --json report.json [--html report.html] [--game "D:\Games\Cyberpunk 2077"] [--no-mods]
CrashDoctor.exe --vram-probe
```

The second prints one reading of your graphics card's memory and which adapter it picked. If you have an AMD or Intel
card, that one line pasted into Posts tells me whether the recording works on it.

### Requirements

- Windows 10 or 11, 64-bit
- Cyberpunk 2077 on Steam, GOG or Epic, found automatically, or pick the folder by hand
- Microsoft WebView2 runtime, already on up-to-date Windows 11; Crash Doctor offers to install it if missing
- No .NET install needed
- Optional: Advanced Crash Reporter by Akiway. Not included; when it is installed, Crash Doctor reads its reports too.
- Nothing else. It reads the logs your mod loaders already write; it does not need any mod installed to run.

### Shout outs

Everything Crash Doctor reads exists because other people wrote the loaders that log it: WopsS (RED4ext), yamashi
(Cyber Engine Tweaks), psiberx (ArchiveXL, TweakXL, Codeware) and jac3km4 (redscript), and Akiway, whose Advanced
Crash Reporter it reads with permission. The diagnosis is only as good as their logs, and their logs are very good.

MIT licence. Bug reports and rules for the catalogue are very welcome: if you have a crash it read badly, post the
saved report.

---

## 3. BBCode, ready to paste

```bbcode
[size=4][b]Description[/b][/size]

[b]This is a work in progress.[/b] Version 0.8 has been built and tested mainly against one PC: a Steam install, Vortex, an NVIDIA card with 8 GB, 169 mods and a few months of real crashes. If it tells you something that is obviously wrong on your PC, or something does not work at all, please say so in the [b]Bugs[/b] section or in [b]Posts[/b]. That is worth more to me than a thank-you, and it is how the tool gets better. Every report it saves says which build produced it on its first line, so a two-month-old one is still useful.

[b]You do not have 30 crashes. You have three problems.[/b] Crash Doctor reads the crash dumps, mod-loader logs and Windows driver events already sitting on your PC, and tells you what happened in plain language: which crashes are the same crash, what was running when each one died, what is ruled out, and what to test next. It is the only tool on this page that opens the minidump.

[b]What it will not do.[/b] It will not fix every crash, and it does not pretend to. The evidence work is reliable and takes ten seconds; naming the culprit is a bonus, not the promise. Its confidence is allowed to reach zero, and "I don't know yet, here is what is ruled out and here is the next test" is an answer it will give you. What it reliably replaces is a week of switching mods off at random.

[b]It changes nothing unless you ask.[/b] Scanning only reads. Crash Doctor changes something in exactly three cases, each only because you clicked and confirmed: applying a reviewed fix to a specific mod file (none ships yet; a fix is added only with that mod author's permission), applying a graphics profile, and repairing a save. A fix or a profile backs up the original first and one click puts it back; a save repair writes a new save beside the old one, never changes the original, and Undo removes the copy. Profiles touch graphics and display settings only, never controls, audio or key bindings. Clear old logs moves the game's logs into Crash Doctor's own folder, deletes nothing, and Put them back returns them. Crash Doctor never enables, disables or deletes a mod. Nothing leaves your PC, with one exception: the update check. Each time Crash Doctor starts it fetches one small version file, and it sends nothing about you or your install. It is on by default; switch it off under Settings > Updates and nothing is sent anywhere.

[b]What it has never seen:[/b] an AMD or Intel card, a GOG or Epic install, a real Mod Organizer 2 install (it reads MO2 directly, but has only been tested against a test install built to look like one), a hand-installed setup, REDmod deployment, or a card bigger than 8 GB. Mods from the game's own mod menu (mod.io) are listed too, but that is checked against one real mod so far. The evidence it reads is the same everywhere, so most of it should simply work, but "should" is doing real work in that sentence. If you have one of those, your report is the one I most want to see.

[size=4][b]Installation instructions[/b][/size]

[b]Install.[/b] Unzip anywhere and run CrashDoctor.exe. It is not a mod; it does not go in the game folder unless you want it to. The same zip is on GitHub: [url=https://github.com/gramersssss/crash-doctor/releases/tag/v0.8.2]github.com/gramersssss/crash-doctor/releases/tag/v0.8.2[/url]

[b]There is no Vortex package[/b], on purpose. Crash Doctor is not a mod and does not belong in the game folder. If you want a launch button in Vortex, add CrashDoctor.exe to its dashboard as a tool. With Mod Organizer 2 you do not need to start it through MO2: it finds the MO2 instance that manages your game by itself.

[b]Windows will warn you the first time.[/b] Crash Doctor is an unsigned executable, so SmartScreen shows "Windows protected your PC" the first time you run it. Click [b]More info[/b] and then [b]Run anyway[/b]. A code-signing certificate costs a few hundred pounds a year, which is not something a free tool is going to carry; the same warning appears for most small Windows utilities on this site. If you would rather check than trust: the exe is about 130 MB because it carries the whole .NET runtime inside it, so it does not need .NET installed. The source is MIT at github.com/gramersssss/crash-doctor, and the report the tool saves shows you the commit it was built from.

Then press [b]Scan now[/b]. If you want video memory recorded while you play, leave the window open (minimised is fine) and it does the rest. It starts in your Windows language (19 languages); the language button at the top changes it, and Ctrl+L goes back to English.

[size=4][b]Main features[/b][/size]

[list]
[*][b]The fault, not a guess.[/b] Every crash report the game saves contains a minidump. Crash Doctor reads the faulting instruction, the exception code, the loaded modules and the video memory in use, and puts a name on the crash: [font=Courier New]Cyberpunk2077.exe+0x2A41E06[/font]. With Advanced Crash Reporter installed it also reads its reports, which add the function the game crashed in and whose code was running there.
[*][b]Thirty crashes sorted into the few problems they really are.[/b] Crashes are grouped by that fault address, keyed to the game build they happened on. "This exact fault has happened 14 times between 16 Aug and 6 Sep" is a completely different conversation from "the game crashes sometimes".
[*][b]Things ruled out, with the reason shown.[/b] If a mod's errors also appear in sessions that ended perfectly normally, it is noise, and Crash Doctor says so and demotes it instead of blaming it. It will not draw that conclusion at all until it has at least two clean sessions to compare against.
[*][b]The video memory number nobody else shows you.[/b] Used and total, at the instant of the crash, straight out of the crash report. "8129 of 8006 MB in use, past what the card physically holds" ends an argument that otherwise runs for days.
[*][b]Video memory recorded while you play.[/b] The game only writes that number into crash reports, so a session that did not crash leaves nothing. While the Crash Doctor window is open it reads the same Windows GPU counters Task Manager uses, on NVIDIA, AMD and Intel cards alike, every 5 seconds, and shows each session's peak, median and minutes above 90 % with a curve of the whole session. That is how you find out whether High textures were actually safe. Nothing runs after the window is closed, and when the game closes the scan runs again by itself.
[*][b]What changed since your last clean session.[/b] For every crash, a dated list of what is different between this install and the one that last played without a problem: mods installed or updated in Vortex, MO2 or the game's mod menu, how many files were added and removed, native plugins that changed version between the two sessions (from the RED4ext logs, so it works with any mod manager), mod files written by hand, and a game update. Being new is not evidence of guilt, and it says so; it is the shortest list to test first.
[*][b]After a game update.[/b] Health says which frameworks loaded on the new version and which refused, from their own logs, and names a framework that is older than the first release made for that version, with the version to update to. It also names the native plugin the game died loading, and a script mod that no longer compiles.
[*][b]Where you were.[/b] Player position, district and tracked quest at the crash, plus which mods were rewriting the world sectors underfoot at that moment.
[*][b]Health.[/b] Standing problems found before you ever crash: native plugins that refuse to load on your patch, world-sector patches that fail every launch, mods pointing at files that do not exist, scripts erroring every twelve seconds, interrupted downloads that left an empty archive, a DLSS/FSR enabler that overwrote files the game now ships itself, saves sitting in a OneDrive folder.
[*][b]Mox Gorilla Arms save repair.[/b] A game bug with male V, Phantom Liberty, the Legendary Gorilla Arms and the Mox style can leave a save with the Mox look missing its first-person part: the arms keep the Mox shape whatever style you pick, and Mox and Barghest are invisible in first person. Crash Doctor finds such saves and, when you press Repair and confirm, writes a repaired copy as a new save beside it. The original is never changed, and Undo removes the copy. Checked in the game on one save so far; female V saves are left alone.
[*][b]Requirements check.[/b] Detects RED4ext, redscript, Cyber Engine Tweaks, ArchiveXL, TweakXL, Codeware, Equipment-EX and the Microsoft runtimes, shows how many of your installed mods depend on each, and takes you to the right download.
[*][b]Find it, a guided bisect.[/b] For the crash with no signature that nobody has seen before. Crash Doctor tells you which half of your mod list to switch off, and then verifies what is actually deployed before it will accept the result. It switches a mod off together with every mod that needs it (from Vortex rules, redscript imports, Cyber Engine Tweaks mods and files inside another mod's folder), so the game can still start. Mods you mark [b]Always on[/b] are never switched off, nor anything they need, and you can add your own "this mod needs that one" links. It never enables or disables anything itself. And "it didn't crash" only counts when the run lasted meaningfully longer than that fault's own typical time-to-crash.
[*][b]What the messages mean.[/b] When a crash's evidence contains a line somebody already understands (ArchiveXL saying a world patch no longer fits the map, a Lua mod calling a function that never loaded, Windows resetting the graphics driver) Crash Doctor explains it in plain language, with the exact line that matched and where the knowledge comes from. It explains messages; it never uses them to blame a mod.
[*][b]A report you can paste into a help thread.[/b] One button copies a plain-text summary short enough for a mod's Bugs tab, a forum post or a Discord message, in English if you like, whatever language the window is in. Another saves the whole thing as a single self-contained HTML file that opens in any browser, useful where files can be attached, and as your own before-and-after record when you change something. Personal folder paths are stripped from both, and you can leave your mod list out of the saved copy.
[*][b]Copy for an AI.[/b] Builds the same evidence into a question for Claude, ChatGPT or whatever assistant you already use (the fault, the video memory at the crash and through the session, what the clean sessions have ruled out and why) and asks it not to invent mod names. Crash Doctor sends nothing anywhere; it only puts text on your clipboard.
[*][b]Graphics profiles.[/b] Save the game's graphics settings under a name and switch between them in one click: a quality profile for exploring, a lighter one for dense districts or streaming. Edit a profile without launching the game; only values the game's own settings file allows are offered, and resolution and upscaler modes, which depend on your monitor and card, are kept exactly as the game wrote them. Applying backs up your current settings first, refuses while the game is running, and Undo puts them back. [b]Play with this[/b] backs up your settings, puts the profile in, starts the game, and puts your settings back when it closes, so a lighter profile for one session leaves your usual setup untouched.
[*][b]Crash logs, kept.[/b] The game deletes its logs after a few launches, which is how most crashes end up with nothing left to examine. The first time Crash Doctor sees a crash it zips the crash report and that session's logs, and keeps the three most recent clean sessions to compare against. It only copies, and you can switch it off.
[*][b]Clear old logs, clear history, and put them back.[/b] Clear old logs moves every log out of the game folder, so the next session starts clean; Put them back returns them. Clear history forgets the crashes Crash Doctor has seen so far (it asks first, and Undo brings them back). Old crashes whose cause has since changed are marked "looks settled" instead of leading the page.
[*][b]New crashes flagged.[/b] If the game crashed since you last looked, a notice sits at the top of every page until you open it or dismiss it.
[*][b]Update check.[/b] On by default. Each time Crash Doctor starts it fetches one small version file and tells you when a newer build is out, with a button to the download page. It never downloads or installs anything itself, and it sends nothing about you or your install. Switch it off under Settings > Updates.
[*][b]Night City by default, or light and dark.[/b] It opens in Neon, a skin in the game's own style. Pick Terminal, like the computers you jack into, or a plain light or dark look that matches your Windows theme.
[*][b]Command line[/b], for scripts and for pasting into a thread: [font=Courier New]CrashDoctor.exe --json report.json [--html report.html] [--game "D:\Games\Cyberpunk 2077"] [--no-mods][/font] and [font=Courier New]CrashDoctor.exe --vram-probe[/font]. The second prints one reading of your graphics card's memory and which adapter it picked. If you have an AMD or Intel card, that one line pasted into Posts tells me whether the recording works on it.
[/list]

[size=4][b]Requirements[/b][/size]

[list]
[*]Windows 10 or 11, 64-bit
[*]Cyberpunk 2077 on Steam, GOG or Epic, found automatically, or pick the folder by hand
[*]Microsoft WebView2 runtime, already on up-to-date Windows 11; Crash Doctor offers to install it if missing
[*]No .NET install needed
[*]Optional: Advanced Crash Reporter by Akiway. Not included; when it is installed, Crash Doctor reads its reports too.
[*]Nothing else. It reads the logs your mod loaders already write; it does not need any mod installed to run.
[/list]

[size=4][b]Shout outs[/b][/size]

Everything Crash Doctor reads exists because other people wrote the loaders that log it: WopsS (RED4ext), yamashi (Cyber Engine Tweaks), psiberx (ArchiveXL, TweakXL, Codeware) and jac3km4 (redscript), and Akiway, whose Advanced Crash Reporter it reads with permission. The diagnosis is only as good as their logs, and their logs are very good.

MIT licence. Bug reports and rules for the catalogue are very welcome: if you have a crash it read badly, post the saved report.
```

---

## 4. Screenshots

All in `docs/screenshots/`, rendered on 14 Sep from a real scan of a 169-mod install in the Neon theme at the sizes
Nexus asks for: the header at 1300x372, the gallery at 1920x1080. Personal folder paths are scrubbed. Nothing is
generated imagery: `tools/make-screenshots.py` renders the views with headless Edge, and `tools/make-header.py`
composes the header from the app icon, a crop of the real diagnosis card, and type in the theme's colours. See
`tools/README.md`. Upload the gallery in this order - Nexus uses the first as the card image, so the video-memory
diagnosis leads. (They are from 0.6 and still show the English, pre-0.8 layout; they are not wrong, only older.)

**Header:** `00-header.png`.

| File | Caption |
|---|---|
| `01-diagnosis-vram.png` | One sentence, the evidence under it, and the video memory number at the instant of the crash. |
| `02-diagnosis-gpu.png` | The same fault, twelve times. Video memory was fine here, and it says so. |
| `03-diagnosis-streaming.png` | A world-sector patch failed sixteen seconds before the crash. Confidence: fair. |
| `04-what-changed.png` | What changed between the last clean session and this crash: a mod installed an hour before, read from Vortex's own log. |
| `05-sessions.png` | Every launch and how it ended. "Dies every 3 minutes" and "dies after 4 hours" are different problems. |
| `06-health.png` | Standing problems found before you crash. |
| `07-mods.png` | Your mod list, flagged - suspect, patch failing, plugin not loading, ruled out, fix applied. |
| `08-find-it.png` | Guided bisect: it tells you which half to switch off, then checks what is actually deployed. |
| `09-requirements.png` | Every framework your installed mods actually depend on, and where to get it. |
| `10-settings.png` | Crash logs kept, video memory recorded while you play, five looks, graphics profiles. |

The mod list in `07-mods.png` and `08-find-it.png` is the real one and includes adult mods by name; that is the
author's own install and their call to publish.

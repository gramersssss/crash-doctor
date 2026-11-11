# Crash Doctor 0.8.2 - release texts

Approved by Gary 11 Nov 2026 ("release crash doctor") after testing test4 (Z1-Z6 passed; earlier rounds: Mox repair
X1-X3 in the game, test3 Y1-Y6).

Zip: `dist\CrashDoctor-0.8.2-manual.zip` - 61,914,395 bytes (59.0 MB), MD5 E7745A948A0F2C555136F402EB622807,
SHA-256 A1D85A62F03224456CC85CD28D40297DEC1F0F521DA9782630D612BD65CEFB95
(the test4 build of 11 Nov 2026, commit 3767ccc, tag v0.8.2, reused unchanged: the exe says
0.8.2+3767cccc31625a0b4051161f5e535c1f17e20912, exe 138,419,107 bytes, MD5 A040D714C81F40F392ECFB059497010A - the exe
Gary tested - SHA-256 2A43FFBFA98BD4B6D0C2CAE279566276C902D7FF5419A6C3644EB8F16CBB09C3; ui\index.html MD5
118BB428F00B04BB71E6A423303E81DD). All 31 files in the zip checked byte-identical to the tested folder; ui, fixes,
knowledge and README match commit 3767ccc (line endings aside). Over 10 MB: Gary uploads it to Nexus himself.
Push `version.json` (0.8.2) only after the Nexus and GitHub files are live.

## GitHub release (tag v0.8.2, title "Crash Doctor 0.8.2")

Notes: `dist\GITHUB-RELEASE-0.8.2.txt` (same text as below). Attach `CrashDoctor-0.8.2-manual.zip`.

**What's new**
- **Mox Gorilla Arms save repair.** A game bug with male V, Phantom Liberty, the Legendary Gorilla Arms and the Mox
  style can leave a save whose arms keep the Mox shape whatever style you pick, with Mox and Barghest invisible in
  first person. Crash Doctor finds such saves ("1 save can be repaired" at the top of every page, and first in Health).
  **Repair** asks first and writes a repaired copy as a new save beside the old one; the original is never changed,
  and **Undo** removes the copy. Checked in the game on one save so far; female V saves and anything it does not
  recognise are left alone.
- **Find it understands mods that need each other.** A round switches a mod off together with every mod that needs it
  (Vortex rules, redscript imports, Cyber Engine Tweaks mods, files inside another mod's folder), so the game can still
  start. Mark mods **Always on** and they - and what they need - are never asked off. Add your own "this mod needs
  that one" links; they get their own list, and Remove asks first (with "Don't show this again", and a switch under
  Settings > Find it). "Continue with my current setup" carries on when a round cannot be set up exactly as asked.
- **Update check on by default.** Once per start it fetches one small version file and sends nothing about you or
  your install. Switch it off under Settings > Updates.

**Fixed**
- Notices at the top and the pages below now share one width on wide windows, and a notice's button sits at the end
  of its bar (mirrored for right-to-left languages).

**Footer:** "Changes nothing unless you press a button that says so." - still true (the save repair asks first, never
changes the original save, and can be undone).

## Nexus file
- Name: CrashDoctor 0.8.2 | Version: 0.8.2 | tick "Update mod version" | update of 0.8.1 (0.8.1 to Old files) | Main, primary
- File description (236 characters, BBCode counted):
  Mox Gorilla Arms save repair (new save, original untouched, Undo), Find it keeps mods that need each other together, update check on by default. Also try my [url=https://www.nexusmods.com/cyberpunk2077/mods/34227]Skin Changer Plus[/url]
- Changelog:
  - New: Mox Gorilla Arms save repair - finds saves hit by the game's Mox bug; Repair writes a new save beside the old one, the original is untouched, Undo removes it.
  - New: Find it keeps a mod and the mods that need it together, so the game still starts; Always on pins mods it must never switch off.
  - New: add your own "this mod needs that one" links in Find it; Remove asks first (with "Don't show this again").
  - Changed: the update check is on by default, once per start, and sends nothing about you; switch it off in Settings > Updates.
  - Fixed: notices and pages share one width on wide windows; notice buttons sit at the end of the bar.

## Nexus description
Replace the whole description with the BBCode below (= `docs\NEXUS-description.md` section 3, refreshed for 0.8.2:
version, three things it can change, save repair, Find it groups / Always on / own links, update check on by default,
MO2 / mod.io / ACR, 19 languages, GitHub link to v0.8.2). Form-field Version: 0.8.2.

Short description (if it is edited): 0.8.2: Mox Gorilla Arms save repair (a new save, original untouched, Undo), Find
it keeps mods that need each other together, and the update check is on by default.

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

## Pinned post (sticky 177076443) - new first line
> **0.8.2 is out:** it can now repair saves hit by the Mox Gorilla Arms bug (as a new save, the original stays as it
> was), Find it keeps mods that depend on each other together, and the update check is on by default.

## Messages (send once each, after the Nexus file is live)

DemonGent (Nexus inbox):
```
Hi - the repair for the Mox arms save is out now, in Crash Doctor 0.8.2 (on the Crash Doctor Nexus page). Open it and press Scan; if a save has the problem you'll see "1 save can be repaired" at the top. Press Repair and confirm: it writes a fixed copy as a new save next to the old one, and your original save isn't touched. Load the new one in the game and the arm styles should switch again. If you change your mind, Undo removes the copy.
It's for the case you found: male V, Phantom Liberty, Legendary Gorilla Arms with the Mox style. Your save was one of the ones it was tested against, so thanks again for sending it. If it doesn't find or fix yours, tell me and I'll look.
```

WangChangZhang (comment reply, no links):
```
Good news on the Find it problem you ran into: 0.8.2 is out and Find it now understands mods that depend on each other. When it switches a mod off, the mods that need it go off with it, so the game can still load (no more AMM off with its add-ons still on). You can mark mods "Always on" so they are never part of the hunt, and if it misses a link between two mods you can add your own "this needs that". Thanks for reporting it - it's what made this happen.
```

## After both are live
- version.json 0.8.2 (notes line + GitHub link .../releases/tag/v0.8.2), commit + push, check the raw file.
- Send the two messages above.
- Reddit post about the Mox repair: Gary's call, after release (draft + check each sub's rules first).

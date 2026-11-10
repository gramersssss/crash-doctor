# Crash Doctor 0.8.1 - release texts

Approved by Gary 9 Nov 2026 ("release it") after testing test2 (freeze wording, settled crashes, Clear history, Undo).

Zip: `dist\CrashDoctor-0.8.1-manual.zip` - 61,825,980 bytes (59.0 MB), MD5 0ED6C01A543808C207B1C3C8349DDF77,
SHA-256 4F411E8081A28A24B83C0609C6495A7B8702BB4560538361E0EF849694808CDA
(built 9 Nov 2026 from commit 5a236bb, tag v0.8.1; the exe says 0.8.1+5a236bba56aabfe1a6acb98f0d3abde4d2f0615d,
exe MD5 2C536D08E8AB4CAC91264F546AA09CD8 - byte-identical to the test2 exe Gary tested - SHA-256
FD3B18041D62EB2E74B924DE886EA28E50FDF7CF2D62ADB3BC09CF2541DFDBD6). Over 10 MB: Gary uploads it to Nexus himself.
Push `version.json` (0.8.1) only after the Nexus and GitHub files are live.

## GitHub release (tag v0.8.1, title "Crash Doctor 0.8.1")

**What's new**
- **Settings â†’ History â†’ Clear history.** Forgets every crash and freeze Crash Doctor has seen so far, with their
  warnings. It asks first, keeps normal sessions (they are what rules mods out), changes only Crash Doctor's own
  history - never the game's files, logs or saves - and **Undo** brings it all back.
- **Old crashes stop shouting.** Every crash now also shows what has happened since: how many times the game has ended
  normally after it, and - when what it was blamed on has changed since (a framework that could not run on a new game
  version and now does, or every mod it named updated, switched off or removed) and the game has ended normally after
  that - "looks settled", with the reason. A settled crash no longer leads the first page or flags its mods; it stays
  in the list of sessions. A crash nobody could explain is never called settled just because time has passed.
- **Mods from the game's own mod menu (mod.io, 2.5) - work in progress.** They are listed with your other mods, marked
  mod.io, with name, version and on/off, and an install or update shows under "what changed since your last clean
  session". Read from mod.io's own folder; your mod.io sign-in is never read. Crash Doctor remembers each menu install
  it has seen, so a crash during an install still lists it after the mod is uninstalled again. So far checked against
  one real script mod only; if a menu mod is missing or misnamed, please send a saved report.

**Fixed**
- Two weeks after a game update, a crash could still be told "before every framework had shown that it runs on that
  version" because an unrelated native plugin was refused. Only the frameworks count now, as in Health.
- A freeze that Advanced Crash Reporter saw ended by the engine's watchdog was shown as "the engine stopped itself" and
  counted as "one repeating problem" with every earlier freeze. It now reads as a freeze, and the count says that every
  freeze ends at the same place.

**Footer:** "Changes nothing unless you press a button that says so." - still true (Clear history asks first and can be
undone).

## Nexus file
- Name: CrashDoctor 0.8.1 | Version: 0.8.1 | tick "Update mod version" | update of 0.8.0 (0.8.0 to Old files)
- File description (231 characters):
  Clear history button (asks first, Undo brings it back), old crashes marked "looks settled" once their cause has
  changed, and mods from the game's own mod menu (mod.io) listed - work in progress. Unzip anywhere, run CrashDoctor.exe.
- Changelog:
  - New: Settings > History > Clear history - asks first, keeps normal sessions, only touches Crash Doctor's own history; Undo brings it back.
  - New: each crash shows how often the game ended normally since; once its cause has changed it is marked "looks settled" and stops leading the first page.
  - New (work in progress): mods from the in-game mod menu (mod.io) are listed, marked mod.io, with version and on/off; installs show in "what changed".
  - Fixed: an unrelated refused native plugin no longer keeps a crash "before every framework had shown it runs" weeks after an update.
  - Fixed: a freeze ended by the engine's watchdog (seen by Advanced Crash Reporter) reads as a freeze, not "the engine stopped itself".

## Nexus description edits
- Version line: 0.8.0 -> 0.8.1.
- Replace the line "in-game mod menu (mod.io) mods are not listed yet" (wording on the page may differ slightly; replace
  that whole sentence) with:
  > Mods installed through the game's own mod menu (mod.io) are listed too, marked mod.io - work in progress, checked
  > against one real mod so far. If one is missing or misnamed, a saved report helps.
- Main features: add one bullet:
  > **Clear history, and put it back.** Forget every crash seen so far (it asks first; Undo restores it). Old crashes
  > whose cause has since changed are marked "looks settled" instead of leading the page.
- "Where to download": point the GitHub link at the v0.8.1 release.
- Short description (if it is edited): 0.8.1: a "Clear history" button, old crashes marked "looks settled" once their
  cause has changed, and mods from the game's own mod menu (mod.io) in the mod list (work in progress).

## Pinned post (sticky 177076443) - new first line
> **0.8.1 is out:** Clear history (with Undo), old crashes marked "looks settled", and in-game mod menu (mod.io) mods
> listed - that part is work in progress, so a saved report from a mod.io user helps most.

## After both are live
- version.json 0.8.1 (notes line + GitHub link .../releases/tag/v0.8.1), commit + push.
- Tell LyciaPintella (mod.io + Clear history were promised on 8 Nov).

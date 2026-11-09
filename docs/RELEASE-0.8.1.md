# Crash Doctor 0.8.1 - release notes (DRAFT, not released)

Test build: `dist-test\CrashDoctor-0.8.1-manual.zip` (see the hashes in the report that came with it). Before release:
Gary tests the list below, then the usual: build into `dist\` with the app closed, GitHub release, Nexus file, and only
after the files are live bump and push `version.json`.

## Changelog (Nexus / GitHub)

- New: **Settings → History → Clear history.** Forgets every crash and freeze Crash Doctor has seen so far, with their
  warnings. It asks first, keeps normal sessions (they are what rules mods out), changes only Crash Doctor's own
  history - never the game's files, logs or saves - and **Undo** brings it all back.
- Old crashes stop shouting. Every crash now also shows what has happened since: how many times the game has ended
  normally after it, and - when what it was blamed on has changed since (a framework that could not run on a new game
  version and now does, or every mod it named updated, switched off or removed) and the game has ended normally after
  that - "looks settled", with the reason. A settled crash no longer leads the first page or flags its mods; it stays
  in the list of sessions. A crash nobody could explain is never called settled just because time has passed.
- Fixed: two weeks after a game update, a crash could still be told "before every framework had shown that it runs on
  that version" because an unrelated native plugin was refused. Only the frameworks count now, as in Health.
- New: mods installed through the game's own mod menu (mod.io, 2.5) are listed with your other mods, marked mod.io,
  with name, version and on/off, and an install or update shows under "what changed since your last clean session".
  Read from mod.io's own folder; your mod.io sign-in is never read. Work in progress: no PC with menu mods was
  available to check the layout, so if a menu mod is missing or misnamed, please send a saved report.

## Short description line (if the page needs one)

0.8.1: a "Clear history" button, old crashes marked "looks settled" once their cause has changed, and mods from the
game's own mod menu (mod.io) in the mod list.

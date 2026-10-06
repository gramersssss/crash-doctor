# Crash Doctor 0.7.0 - release texts (DRAFT - nothing posted yet)

Zip: `dist\CrashDoctor-0.7.0-manual.zip` - 58.3 MB
SHA-256: e49905e1ee30c5a737184ed55b1017d27f51a73a479e1e35fd018abe4ebeed77
Checked 6 Oct 2026: Gary's test (steps 1-6 pass; 59 logs put back, the other 41 kept because the game had written
newer ones - checked file by file); fixtures fresh / healthy / broken give the same findings as 0.6.1, plus the new
game-logs section; the Release exe scans the broken fixture the same as the Debug build; the zip holds only Crash
Doctor, its ui / fixes / knowledge folders, the README and the Microsoft runtime DLLs (as in 0.6.1).

## GitHub release (tag v0.7.0, title "Crash Doctor 0.7.0")

**What's new**
- **Clear old logs** (Settings): moves every log out of the game folder, so the next time you play, the only logs
  there are from that session. Nothing is deleted - **Put them back** returns them, and any log the game has
  rewritten since stays as the new one. Settings also shows how many logs there are and how much space they take.
- **Mod count** in the top bar and the Mods tab. When no mod manager is found, it says "mod files and folders",
  because one mod is often several of those.
- **Sessions closed without a trace** (no shutdown, no crash report - for example ended from Task Manager) used to show
  as 0.2 minutes. They now say how long the game ran at least, from the last thing any log wrote. If the whole PC went
  down at that moment, it says so: that is power, heat or a hard reset, not a mod.

**Fixed**
- The scan failed with "An item with the same key has already been added" when two mods had the same name.

**Footer:** "Changes nothing unless you press a button that says so." - true for every version so far.

Download: `CrashDoctor-0.7.0-manual.zip` below. Unzip anywhere and run CrashDoctor.exe - no install, no .NET needed.
SHA-256: e49905e1ee30c5a737184ed55b1017d27f51a73a479e1e35fd018abe4ebeed77

## Nexus file (Gary uploads the zip - over 10 MB)
- Upload as a NEW Main file (not "update" - 0.6.1 stays until 0.7.0 passes Nexus's file scan, then archive 0.6.1)
- Name: CrashDoctor 0.7.0 | Version: 0.7.0 | tick "Update mod version"
- File description: Clear old logs and put them back, a mod count, real session lengths for games closed without a
  trace, and a fix for mods that share a name. Unzip anywhere and run CrashDoctor.exe.
- Changelog (one line each):
  - New: Clear old logs in Settings - moves the logs out of the game folder; Put them back returns them.
  - New: mod count in the top bar and the Mods tab.
  - Sessions closed without a shutdown or crash now show how long they ran instead of 0.2 minutes.
  - Fixed: the scan failed when two mods had the same name.
- Description page: the "Where to download" block points at the v0.7.0 GitHub release instead of v0.6.1.

## After both are live
- version.json -> 0.7.0 (the in-app update check), commit + push.
- Tell Nuttyboy812 (same-name fix) and lynsis (clear logs was their idea) - each reply worded differently.

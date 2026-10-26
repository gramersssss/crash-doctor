# Crash Doctor 0.8.0 - release texts (DRAFT - nothing posted yet)

Zip: `dist\CrashDoctor-0.8.0-manual.zip` - 61,789,062 bytes (58.9 MB), SHA-256
864763E95DB1FDD0FE3B2CF869CCF39EE8DDF796EA048C57378B38C3F4C0CC79
(built 26 Oct 2026 from commit a14bc49, tag v0.8.0 moved there; the exe says
0.8.0+a14bc49c53b1d71b72bad0dc9f263b1e3d02cd68, exe SHA-256
955336E9374496A6DC970B9524CDD3D49B1ACD57599FAFE2A06607FD8F50DF81). Rebuilding from the tag gives a different zip hash
(timestamps), as with 0.7.0: if the tag is rebuilt, record the new hash here.
Not released yet: Gary scans once on 2.5 first. Push `version.json` (0.8.0) only after the Nexus and GitHub files are
live, or every copy with the update check on will announce a version that cannot be downloaded yet. version.json is
NOT in the commits above on purpose (it is modified in the working tree only).

## GitHub release (tag v0.8.0, title "Crash Doctor 0.8.0")

**What's new**
- **Ready for game update 2.5.** When the game updates, Health says so first and lists what RED4ext, Cyber Engine
  Tweaks and redscript each wrote in their own logs on the new version - which frameworks loaded, which refused. On top
  of that it now carries a small, dated list of framework releases checked against their own release notes: for 2.5,
  RED4ext 1.31.0, Cyber Engine Tweaks 1.38.0, RedFileSystem 0.16.0, ArchiveXL 1.28.0, TweakXL 1.12.0 and Codeware
  1.21.0 are the first releases for it, so an older one is named with the version to update to. A framework updated
  after the game last ran is not judged by its old logs: Crash Doctor asks you to start the game once and scan again.
  A version newer than the list knows is never flagged.
- **"The game closed while RED4ext was loading X."** When RED4ext's log stops at "Loading plugin ... X.dll" and the
  game is gone seconds later, Crash Doctor names that plugin: the game died inside its start-up. This is what a
  2.31-era ArchiveXL does on 2.5 - no crash report from the game, and Advanced Crash Reporter's own reading is
  "unknown culprit".
- **RED4ext's "incompatible with the current patch / requested runtime" line, in plain words:** the plugin was built
  for another game build, so RED4ext switched it off; it is not broken.
- **A script mod that 2.5 broke.** 2.5 changed two inventory methods that some script mods hook; such a mod no longer
  compiles, and one script that does not compile switches off every redscript mod. Crash Doctor names the mod from
  the file the error points at.
- **Mod Organizer 2.** Crash Doctor now finds the MO2 instance that manages your game and reads its mod list and mod
  folders directly, Root Builder's Root folders included, and the logs from MO2's overwrite folder. The mod count,
  which mod owns a file, every rule and Find it work the same as with Vortex - you do not need to start Crash Doctor
  through MO2. Two MO2-only checks: an enabled mod whose folder is missing, and script/plugin files one mod hides from
  another. Nothing in MO2 is ever changed. Tested against a test install built to look like an MO2 + Root Builder
  setup, not yet on a real one: if you use MO2, a saved report helps more than anything.
- **Advanced Crash Reporter** (optional, by Akiway - not included). If you have it, Crash Doctor reads its reports and
  adds the name of the function the game crashed in and whose code was running there. A mod's own code on the thread
  that crashed is named as a suspect; a freeze that the game's watchdog ended now shows as a freeze, not a crash.
- **19 languages.** English and every language the game ships. It starts in your Windows language; the language button
  at the top (globe + the language's own name) is always there, English is first in the list, and Ctrl+L switches back
  to English from anywhere. The summary for a forum can be copied in English whatever language the window is in.
  Translations are machine-assisted and use the game's own words for its settings; English is the reference.

**Not yet:** mods installed through the game's own in-game mod menu (mod.io, new in 2.5) are not listed among your
mods.

**Fixed**
- The "Hunt this fault" confirmation showed "{n} mods" and "{steps} rounds" instead of the numbers.
- When WebView2 is installed but fails to start (for example under Mod Organizer 2), the message no longer tells you to
  install WebView2.
- After a mod manager purge, the frameworks' leftover log folders no longer count as installed frameworks.

**Footer:** "Changes nothing unless you press a button that says so." - still true.

## Nexus file
- Name: CrashDoctor 0.8.0 | Version: 0.8.0 | tick "Update mod version"
- File description: Ready for game update 2.5: says which frameworks need updating for it, and to which version.
  Reads Mod Organizer 2 directly, uses Advanced Crash Reporter's reports when installed, and speaks 19 languages
  (Ctrl+L for English). Unzip anywhere, run CrashDoctor.exe.
- Changelog:
  - New: after a game update, names frameworks that are behind (for 2.5: RED4ext 1.31.0, CET 1.38.0, RedFileSystem 0.16.0, ArchiveXL 1.28.0, TweakXL 1.12.0, Codeware 1.21.0; table checked 26 Oct 2026).
  - New: names the native plugin the game died loading (RED4ext's log stops at "Loading plugin ... X.dll").
  - New: RED4ext's "requested runtime" refusals explained in plain words.
  - New: names a script mod broken by 2.5's inventory-filter change (it switches off every redscript mod).
  - New: Mod Organizer 2 support - instances, profiles, mod folders, Root Builder and the overwrite folder are read directly.
  - New: reads Advanced Crash Reporter reports (optional requirement, by Akiway; not bundled).
  - New: 19 languages, language button always at the top, Ctrl+L for English.
  - Fixed: the bisect confirmation showed placeholders instead of numbers.
- Requirements tab: add Advanced Crash Reporter (34403) as OPTIONAL, "reads its reports when installed" (Akiway agreed
  to being listed and read; not to bundling).
- Description: keep MO2 in "What it has never seen", reworded honestly: it is read directly now, but only ever tested
  against a fixture built to look like lynsis's setup, never a real MO2 install - an MO2 user's saved report is the one
  most wanted. Add the language line, the ACR line, the 2.5 line ("says which frameworks need a 2.5 update, from their
  logs and a dated list of releases") and "in-game mod menu (mod.io) mods are not listed yet". The GitHub notes above
  carry the same caveats.

## Keeping the release table current
`knowledge\releases\frameworks.json` holds the dated table (all six 2.5 releases are in it, checked 26 Oct 2026;
redscript 0.5.32 is recorded but cannot be checked, scc.exe has no version stamp). For a 2.5 hotfix with a new build
number or the next patch, add a "for" entry and move "asOf". It ships next to the exe, so a corrected table can go
out as a small update without code changes.

## After both are live
- version.json 0.8.0, commit + push.
- Tell lynsis (MO2 was their request) and thank Akiway - each worded differently.

# Crash Doctor 0.8.0 - release texts (DRAFT - nothing posted yet)

Zip: `dist\CrashDoctor-0.8.0-manual.zip` - 61,760,213 bytes (58.9 MB), SHA-256 386AEF0D6265976D20C507FEB8317802C3497B56697560393455BD93A13CAFF2
(built 20 Oct 2026 from uncommitted source, so the exe says 0.8.0+1039e45; rebuild after the release commit, as for 0.7.0)
Not released yet: waits for Gary's test. Push `version.json` (0.8.0) only after the Nexus and GitHub files are live,
or every copy with the update check on will announce a version that cannot be downloaded yet.

## GitHub release (tag v0.8.0, title "Crash Doctor 0.8.0")

**What's new**
- **Mod Organizer 2.** Crash Doctor now finds the MO2 instance that manages your game and reads its mod list and mod
  folders directly, Root Builder's Root folders included, and the logs from MO2's overwrite folder. The mod count,
  which mod owns a file, every rule and Find it work the same as with Vortex - you do not need to start Crash Doctor
  through MO2. Two MO2-only checks: an enabled mod whose folder is missing, and script/plugin files one mod hides from
  another. Nothing in MO2 is ever changed. Tested against a test install built to look like an MO2 + Root Builder
  setup, not yet on a real one: if you use MO2, a saved report helps more than anything.
- **Advanced Crash Reporter** (optional, by Akiway - not included). If you have it, Crash Doctor reads its reports and
  adds the name of the function the game crashed in and whose code was running there. A mod's own code on the thread
  that crashed is named as a suspect; a freeze that the game's watchdog ended now shows as a freeze, not a crash.
- **After a game update.** When the game updates, Health says so first and lists what RED4ext, Cyber Engine Tweaks and
  redscript each wrote in their own logs on the new version - which frameworks loaded, which refused. If they do not
  support the new version yet, it tells you to wait for their updates rather than hunt through your mods.
- **19 languages.** English and every language the game ships. It starts in your Windows language; the language button
  at the top (globe + the language's own name) is always there, English is first in the list, and Ctrl+L switches back
  to English from anywhere. The summary for a forum can be copied in English whatever language the window is in.
  Translations are machine-assisted and use the game's own words for its settings; English is the reference.

**Fixed**
- The "Hunt this fault" confirmation showed "{n} mods" and "{steps} rounds" instead of the numbers.
- When WebView2 is installed but fails to start (for example under Mod Organizer 2), the message no longer tells you to
  install WebView2.

**Footer:** "Changes nothing unless you press a button that says so." - still true.

## Nexus file
- Name: CrashDoctor 0.8.0 | Version: 0.8.0 | tick "Update mod version"
- File description: Reads Mod Organizer 2 directly, uses Advanced Crash Reporter's reports when installed, explains
  game updates that outrun your frameworks, and speaks 19 languages (Ctrl+L for English). Unzip anywhere, run
  CrashDoctor.exe.
- Changelog:
  - New: Mod Organizer 2 support - instances, profiles, mod folders, Root Builder and the overwrite folder are read directly.
  - New: reads Advanced Crash Reporter reports (optional requirement, by Akiway; not bundled).
  - New: a clear message after a game update, from what each framework's own log says.
  - New: 19 languages, language button always at the top, Ctrl+L for English.
  - Fixed: the bisect confirmation showed placeholders instead of numbers.
- Requirements tab: add Advanced Crash Reporter (34403) as OPTIONAL, "reads its reports when installed" (Akiway agreed
  to being listed and read; not to bundling).
- Description: keep MO2 in "What it has never seen", reworded honestly: it is read directly now, but only ever tested
  against a fixture built to look like lynsis's setup, never a real MO2 install - an MO2 user's saved report is the one
  most wanted. Add the language line and the ACR line. The GitHub notes above should carry the same caveat.

## After both are live
- version.json 0.8.0, commit + push.
- Tell lynsis (MO2 was their request) and thank Akiway - each worded differently.

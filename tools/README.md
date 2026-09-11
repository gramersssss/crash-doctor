# tools

## make-fixtures.py

Builds synthetic Cyberpunk installs so Crash Doctor can be scanned in isolation, without a second PC and without
being judged against the developer's own crash history.

    python tools/make-fixtures.py C:\temp\fixtures

Two scenarios:

- `fresh` - game installed, never launched, no mods, no logs, no settings
- `healthy` - 12 mods, three sessions that all quit normally, nothing wrong anywhere

Scan one with the machine-data roots redirected, so the real profile is untouched:

    set CRASHDOCTOR_TEST_ROOT=C:\temp\fixtures\healthy
    CrashDoctor.exe --json out.json --html out.html --game C:\temp\fixtures\healthy\game

`CRASHDOCTOR_TEST_ROOT` redirects LocalAppData and AppData together, which is where the crash reports, the game
settings and Crash Doctor's own history live. Without it a pretend game folder is still measured against the real
machine, which is how a "never played" fixture first reported five sessions and named a mod it had never seen.

`CRASHDOCTOR_TIMING=1` prints how long each source and phase took. Useful when a scan crawls on someone else's
machine and there is nothing else to go on.

Note that the GUI ignores `--game` and always locates the real install, so a fixture must be scanned through the
command line.

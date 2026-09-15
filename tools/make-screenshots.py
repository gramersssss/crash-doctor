"""Render the Nexus screenshots from a saved report.

    CrashDoctor.exe --json out.json --html report.html
    python tools/make-screenshots.py report.html docs/screenshots [--size 1920x1080] [--theme pause]

Takes a saved report (ui/index.html with window.EXPORTED baked in), writes one copy per view with a small boot
hook that navigates to that view, and screenshots each with headless Edge. Doing it this way rather than
photographing the window means every picture is the same size, the same zoom and reproducible after a UI change.

The hook undoes only the two things *saving* changes about the page - it hides the Scan button and replaces the
rail footer with a "saved copy" notice - so the pictures show the app as it looks when you run it, not as a saved
file looks. Nothing else is touched: no invented data, no hidden panels.

The diagnosis shots name a session id, because the newest crash on the machine is whatever happened last and is
usually not the one worth showing. Those ids come from the report; change them when the report changes.

Defaults are what Nexus wants for gallery images (1920x1080) in the app's default theme (Neon, stored id "pause").
"""
import io, os, subprocess, sys, shutil, time

EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
SIZE = (1920, 1080)
THEME = "pause"
LIVE_FOOT = "Reads logs only. Never changes the game or your mods."

# (file name, what to do once the page has loaded). A call may end by scrolling something into view; the hook
# resets the scroll position *before* running it, so that scroll is what the picture shows.
SHOTS = [
    ("01-diagnosis-vram", "showSession('s20260906155134')"),
    ("02-diagnosis-gpu", "showSession('c20260901212456')"),
    ("03-diagnosis-streaming", "showSession('s20260906164928')"),
    ("04-what-changed", "showSession('s20260911204528'); document.getElementById('dx-changes').scrollIntoView({block:'start'})"),
    ("05-sessions", "go('sessions')"),
    ("06-health", "go('health')"),
    ("07-mods", "go('mods'); setModFilter('kind', 'problems')"),
    ("08-find-it", "go('bisect')"),
    ("09-requirements", "go('req')"),
    ("10-settings", "go('settings')"),
]


def hook(call, theme):
    return "\n".join([
        "<script>",
        "window.EXPORTED.privacy=null;",
        # pin the theme: headless Edge follows Windows dark mode, and the Nexus shots must not depend on it
        "document.documentElement.setAttribute('data-theme','%s');" % theme,
        "try{localStorage.setItem('cd.theme','%s')}catch(e){}" % theme,
        "document.addEventListener('DOMContentLoaded',function(){",
        "  var b=document.getElementById('btn-scan'); if(b) b.style.display='';",
        '  document.getElementById("foot").textContent="' + LIVE_FOOT + '";',
        "  var m=document.querySelector('.main'); if(m) m.scrollTop=0;",
        "  " + call + ";",
        "});",
        "</script>",
    ])


def main(argv):
    args = [a for a in argv if not a.startswith("--")]
    size, theme = SIZE, THEME
    for i, a in enumerate(argv):
        if a == "--size" and i + 1 < len(argv):
            w, h = argv[i + 1].lower().split("x"); size = (int(w), int(h))
        if a == "--theme" and i + 1 < len(argv):
            theme = argv[i + 1]
    if len(args) < 2:
        print(__doc__)
        return 2
    src, out = os.path.abspath(args[0]), os.path.abspath(args[1])
    if not os.path.isfile(EDGE):
        print("Microsoft Edge not found at " + EDGE)
        return 1
    work = os.path.join(out, "pages")
    for d in (out, work):
        if not os.path.isdir(d):
            os.makedirs(d)
    html = io.open(src, encoding="utf-8").read()
    if "window.EXPORTED" not in html:
        print(src + " is not a saved report (no window.EXPORTED). Run CrashDoctor.exe --html first.")
        return 1

    for name, call in SHOTS:
        page = os.path.join(work, name + ".html")
        io.open(page, "w", encoding="utf-8").write(html.replace("</body>", hook(call, theme) + "\n</body>"))
        png = os.path.join(out, name + ".png")
        if os.path.isfile(png):
            os.remove(png)
        # --run-all-compositor-stages-before-draw is what makes the .fade animation finish before the capture; without
        # it the main panel came out empty at 1920x1080. Edge also returns before the PNG is on disk, so wait for it.
        subprocess.run([
            EDGE, "--headless=new", "--disable-gpu", "--hide-scrollbars",
            "--force-device-scale-factor=1", "--virtual-time-budget=10000",
            "--run-all-compositor-stages-before-draw",
            "--window-size=%d,%d" % size, "--screenshot=" + png,
            "file:///" + page.replace("\\", "/"),
        ], capture_output=True)
        for _ in range(40):
            if os.path.isfile(png):
                break
            time.sleep(0.5)
        ok = os.path.isfile(png)
        print("%-24s %s" % (name, ("%.0f KB" % (os.path.getsize(png) / 1024.0)) if ok else "FAILED"))
    shutil.rmtree(work, ignore_errors=True)
    return 0


sys.exit(main(sys.argv[1:]))

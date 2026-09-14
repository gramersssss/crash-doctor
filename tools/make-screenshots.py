"""Render the Nexus screenshots from a saved report.

    CrashDoctor.exe --json out.json --html report.html
    python tools/make-screenshots.py report.html docs/screenshots

Takes a saved report (ui/index.html with window.EXPORTED baked in), writes one copy per view with a small boot
hook that navigates to that view, and screenshots each with headless Edge. Doing it this way rather than
photographing the window means every picture is the same size, the same zoom and reproducible after a UI change.

The hook undoes only the two things *saving* changes about the page - it hides the Scan button and replaces the
rail footer with a "saved copy" notice - so the pictures show the app as it looks when you run it, not as a saved
file looks. Nothing else is touched: no invented data, no hidden panels.

The diagnosis shots name a session id, because the newest crash on the machine is whatever happened last and is
usually not the one worth showing. Those ids come from the report; change them when the report changes.
"""
import io, os, subprocess, sys, shutil

EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
SIZE = (1600, 1000)
LIVE_FOOT = "Reads logs only. Never changes the game or your mods."

SHOTS = [
    ("01-diagnosis-vram", "showSession('s20260906155134')"),
    ("02-diagnosis-gpu", "showSession('c20260901212456')"),
    ("03-diagnosis-streaming", "showSession('s20260906164928')"),
    ("04-sessions", "go('sessions')"),
    ("05-health", "go('health')"),
    ("06-mods", "go('mods'); setModFilter('kind', 'problems')"),
    ("07-find-it", "go('bisect')"),
    ("08-requirements", "go('req')"),
]


def hook(call):
    return "\n".join([
        "<script>",
        "window.EXPORTED.privacy=null;",
        # pin the default theme: headless Edge follows Windows dark mode, and the Nexus shots must not depend on it
        "document.documentElement.setAttribute('data-theme','instrument');",
        "try{localStorage.setItem('cd.theme','instrument')}catch(e){}",
        "document.addEventListener('DOMContentLoaded',function(){",
        "  var b=document.getElementById('btn-scan'); if(b) b.style.display='';",
        '  document.getElementById("foot").textContent="' + LIVE_FOOT + '";',
        "  " + call + ";",
        "  var m=document.querySelector('.main'); if(m) m.scrollTop=0;",
        "});",
        "</script>",
    ])


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    src, out = os.path.abspath(argv[0]), os.path.abspath(argv[1])
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
        io.open(page, "w", encoding="utf-8").write(html.replace("</body>", hook(call) + "\n</body>"))
        png = os.path.join(out, name + ".png")
        if os.path.isfile(png):
            os.remove(png)
        subprocess.run([
            EDGE, "--headless=new", "--disable-gpu", "--hide-scrollbars",
            "--force-device-scale-factor=1", "--virtual-time-budget=4000",
            "--window-size=%d,%d" % SIZE, "--screenshot=" + png,
            "file:///" + page.replace("\\", "/"),
        ], capture_output=True)
        ok = os.path.isfile(png)
        print("%-24s %s" % (name, ("%.0f KB" % (os.path.getsize(png) / 1024.0)) if ok else "FAILED"))
    shutil.rmtree(work, ignore_errors=True)
    return 0


sys.exit(main(sys.argv[1:]))

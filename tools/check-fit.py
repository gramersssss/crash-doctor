"""Looks for text that does not fit, in every language, with headless Edge.

    CrashDoctor.exe --lang de --html de.html --game <fixture>\\game      (one saved report per language)
    python tools/check-fit.py de.html ru.html pl.html ... [--size 1280x820] [--size 900x700]

For each saved report and each window size it opens every view and lists the elements whose text is cut off or
squeezed: side-menu buttons, header readouts, buttons, table headers, chips and marks whose text is wider than the
box (scrollWidth > clientWidth), and buttons that wrap onto more lines than they should. 1280x820 is the window's
opening size and 900 px its minimum width. Prints one line per problem and a count; 0 is the bar for the labels the
translators were asked to keep short (see the brief in the release notes).
"""
import io, os, re, subprocess, sys, tempfile, time

EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
VIEWS = ['dx', 'sessions', 'health', 'mods', 'bisect', 'req', 'settings', 'report']
PROBE = r"""<script>
document.addEventListener('DOMContentLoaded',function(){ setTimeout(function(){
  var out=[];
  function check(view){
    go(view);
    var sel='.rail button,.bezel .chip .k,.bezel .scan,.bezel .langbtn,.btn,th,.fchip,.mark,.eyebrow,h1,.legend span,.gfx .k,.kv .k,.bstat dt';
    document.querySelectorAll(sel).forEach(function(el){
      var r=el.getBoundingClientRect(); if(!r.width||!r.height) return;
      var cs=getComputedStyle(el), text=(el.innerText||'').trim().replace(/\s+/g,' ');
      if(!text) return;
      var lh=parseFloat(cs.lineHeight)||parseFloat(cs.fontSize)*1.3;
      var cut=el.scrollWidth>el.clientWidth+1 && cs.overflow!=='visible';
      var overflowRight=r.right>window.innerWidth+1;
      // wrapping: the height it has now against the height it would have on one line
      var lines=1, wraps=false;
      if(el.matches('.btn,.rail button,.bezel .scan,.fchip,.mark,th')){
        var h0=el.getBoundingClientRect().height, ws=el.style.whiteSpace; el.style.whiteSpace='nowrap';
        var h1=el.getBoundingClientRect().height; el.style.whiteSpace=ws;
        lines=h1>0?Math.round(h0/h1):1; wraps=lines>2;
      }
      if(cut||overflowRight||wraps) out.push('FIT '+view+' | '+el.tagName.toLowerCase()+'.'+(el.className||'').toString().split(' ')[0]+' | '+(cut?'cut':overflowRight?'off-screen':'wraps '+lines+' lines')+' | '+text.slice(0,70));
    });
  }
  VIEWS.forEach(check);
  console.log('FITDONE '+out.length+' || '+out.join(' || ')+' || END');
},300); });
</script>"""


def main(argv):
    sizes = [(1280, 820), (900, 700)]
    files, i = [], 0
    custom = []
    while i < len(argv):
        if argv[i] == '--size':
            w, h = argv[i + 1].lower().split('x'); custom.append((int(w), int(h))); i += 2; continue
        files.append(argv[i]); i += 1
    if custom: sizes = custom
    total = 0
    for f in files:
        html = io.open(f, encoding='utf-8').read()
        page = html.replace('</body>', PROBE.replace('VIEWS', repr(VIEWS)) + '\n</body>')
        tmp = os.path.join(tempfile.gettempdir(), 'cd-fit-' + os.path.basename(f))
        io.open(tmp, 'w', encoding='utf-8').write(page)
        for w, h in sizes:
            r = subprocess.run([EDGE, '--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1',
                                '--virtual-time-budget=8000', '--enable-logging=stderr', '--v=0', '--window-size=%d,%d' % (w, h),
                                '--dump-dom', 'file:///' + tmp.replace('\\', '/')], capture_output=True, text=True, encoding='utf-8', errors='replace')
            log = r.stderr or ''
            m = re.search(r'FITDONE (\d+) \|\| (.*?) \|\| END', log, re.S)
            if not m:
                print('%s %dx%d: no result (page error?)' % (os.path.basename(f), w, h)); continue
            n = int(m.group(1)); total += n
            print('%s %dx%d: %d' % (os.path.basename(f), w, h, n))
            for line in m.group(2).split(' || ')[:60]:
                if line.strip(): print('   ', line)
        os.remove(tmp)
    print('total problems:', total)
    return 1 if total else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))

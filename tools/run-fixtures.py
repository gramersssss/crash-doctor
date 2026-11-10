"""Scans every fixture with one build and keeps the normalised results, so two builds can be compared.

    python tools/run-fixtures.py <CrashDoctor.exe> <fixtures folder> <results folder>
    python tools/run-fixtures.py --compare <results A> <results B>

<fixtures folder> holds what the make-*.py scripts built (one folder per scenario, each with a game\\ folder). Every
scenario is copied to a fresh folder first, because a scan writes Crash Doctor's history and config into the fixture
and the second build must start from the same state as the first. CRASHDOCTOR_TEST_ROOT always points at the copy.
`broken` also gets OneDrive and CRASHDOCTOR_TEST_SYSTEM as make-rules-fixture.py says.

Normalised: generatedAt and appVersion removed, the copy's path replaced by <FX>. --compare prints the differences per
scenario (key path, A, B) and exits 1 when there are any.
"""
import json, os, re, shutil, subprocess, sys

SKIP = {'updated25-fixed-rotated'}      # needs another scenario's history copied in first (tools\README.md)


def norm(o, fx):
    if isinstance(o, dict):
        return {k: norm(v, fx) for k, v in o.items() if k not in ('generatedAt', 'appVersion')}
    if isinstance(o, list):
        return [norm(v, fx) for v in o]
    if isinstance(o, str):
        return o.replace(fx, '<FX>').replace(fx.replace('\\', '/'), '<FX>')
    return o


def run(exe, src, out):
    os.makedirs(out, exist_ok=True)
    work = os.path.join(out, '_work')
    for name in sorted(os.listdir(src)):
        if name in SKIP or not os.path.isdir(os.path.join(src, name, 'game')):
            continue
        fx = os.path.join(work, name)
        if os.path.exists(fx):
            shutil.rmtree(fx)
        shutil.copytree(os.path.join(src, name), fx)
        env = dict(os.environ, CRASHDOCTOR_TEST_ROOT=fx)
        if name == 'broken':
            env['OneDrive'] = os.path.join(fx, 'OneDrive')
            env['CRASHDOCTOR_TEST_SYSTEM'] = 'vramgb=8;ramgb=16;driverdate=2023-04-01'
        j = os.path.join(out, name + '.raw.json')
        p = subprocess.run([exe, '--json', j, '--game', os.path.join(fx, 'game')], env=env, capture_output=True, text=True,
                           encoding='utf-8', errors='replace', timeout=600)
        print('%-24s exit %d  %s' % (name, p.returncode, (p.stdout or p.stderr).strip()[:150]))
        if os.path.exists(j):
            data = norm(json.load(open(j, encoding='utf-8')), fx)
            json.dump(data, open(os.path.join(out, name + '.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False, sort_keys=True)
            os.remove(j)


def diff(a, b, path, out):
    if type(a) != type(b):
        out.append((path, a, b)); return
    if isinstance(a, dict):
        for k in sorted(set(a) | set(b)):
            if k not in a or k not in b:
                out.append((path + '.' + k, a.get(k, '<absent>'), b.get(k, '<absent>')))
            else:
                diff(a[k], b[k], path + '.' + k, out)
    elif isinstance(a, list):
        if len(a) != len(b):
            out.append((path + '[len]', len(a), len(b)))
        for i, (x, y) in enumerate(zip(a, b)):
            diff(x, y, '%s[%d]' % (path, i), out)
    elif a != b:
        out.append((path, a, b))


# Differences two runs of the SAME build show (measured 10 Nov 2026): the crash-log zips' times and sizes, the copy
# time of files that "what changed" dates by their copy, and the length of a session still counted up to now.
NOISE = [re.compile(p) for p in (r'^\.archive\.', r'^\.recentChanges\[\d+\]\.at$', r'^\.sessions\[\d+\]\.changes\[\d+\]\.at$')]


def noise(p, x, y):
    if any(n.search(p) for n in NOISE):
        return True
    return p.endswith('.minutes') and isinstance(x, (int, float)) and isinstance(y, (int, float)) and abs(x - y) < 5


def compare(ra, rb):
    bad = 0
    for name in sorted(set(os.listdir(ra)) | set(os.listdir(rb))):
        if not name.endswith('.json'):
            continue
        pa, pb = os.path.join(ra, name), os.path.join(rb, name)
        if not (os.path.exists(pa) and os.path.exists(pb)):
            print('%-28s only in one of the two' % name); bad += 1; continue
        d = []
        diff(json.load(open(pa, encoding='utf-8')), json.load(open(pb, encoding='utf-8')), '', d)
        n = len(d)
        d = [x for x in d if not noise(*x)]
        print('%-28s %s%s' % (name, 'identical' if not d else '%d difference(s)' % len(d),
                              '  (%d timing noise ignored)' % (n - len(d)) if n > len(d) else ''))
        for p, x, y in d[:40]:
            print('    %s\n      A: %s\n      B: %s' % (p, json.dumps(x, ensure_ascii=False)[:200], json.dumps(y, ensure_ascii=False)[:200]))
        bad += bool(d)
    return 1 if bad else 0


if __name__ == '__main__':
    if len(sys.argv) == 4 and sys.argv[1] == '--compare':
        sys.exit(compare(sys.argv[2], sys.argv[3]))
    if len(sys.argv) != 4:
        print(__doc__); sys.exit(2)
    run(os.path.abspath(sys.argv[1]), os.path.abspath(sys.argv[2]), os.path.abspath(sys.argv[3]))

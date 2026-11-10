"""End-to-end test of the Mox Gorilla Arms save repair (src/Engine/MoxSave.cs) against the prototype's test vectors.

    python tools/test-savefix.py <CrashDoctor.exe> <testvectors folder> <scratch folder>

<testvectors folder> is glamdocs-unlock\\tools\\savefix\\testvectors (expected.json + one folder per case). They hold
real player saves: they are copied into <scratch folder> and never into this repository.

What it checks:
  1. --repair-save on every case: the two broken saves are repaired with the md5s the prototype produced (whole
     sav.dat, joined chunk data, node table, metadata), the other three are refused for the expected reason, and no
     output folder is left behind by a refusal. The input folders are byte-identical afterwards.
  2. Damaged input: a truncated sav.dat and one with the metadata's fileSize not matching are refused.
  3. The scan, as the window runs it: a pretend install whose save folder holds all the cases. Health must list the two
     broken saves with a Repair action, the female one as "cannot repair yet", and nothing for the healthy / not-Mox ones.
  4. The Repair button (--repair-save-here) writes the next free ManualSave-N with the expected sav.dat md5, the scan
     then shows it as repaired with an Undo action, and Undo (--undo-save-repair) removes exactly that folder.
Exit code 1 on any failure.
"""
import hashlib, json, os, shutil, struct, subprocess, sys

EXE, TV, SCR = (os.path.abspath(a) for a in sys.argv[1:4])
fails = []


def check(ok, what):
    print(('  ok   ' if ok else '  FAIL ') + what)
    if not ok:
        fails.append(what)


def md5(b):
    return hashlib.md5(b).hexdigest()


def fmd5(p):
    return md5(open(p, 'rb').read())


def lz4_decompress(src, dsz):
    dst = bytearray(); i = 0; n = len(src)
    while True:
        tok = src[i]; i += 1; lit = tok >> 4
        if lit == 15:
            while True:
                x = src[i]; i += 1; lit += x
                if x != 255: break
        dst += src[i:i + lit]; i += lit
        if i >= n: break
        off = src[i] | (src[i + 1] << 8); i += 2
        ml = tok & 15
        if ml == 15:
            while True:
                x = src[i]; i += 1; ml += x
                if x != 255: break
        ml += 4; s = len(dst) - off
        for k in range(ml): dst.append(dst[s + k])
    assert len(dst) == dsz
    return bytes(dst)


def data_and_table(raw):
    n = struct.unpack_from('<i', raw, 29)[0]
    blob = bytearray()
    for k in range(n):
        off, csz, dsz = struct.unpack_from('<iii', raw, 33 + 12 * k)
        blob += lz4_decompress(raw[off + 8:off + csz], dsz)
    off, csz, _ = struct.unpack_from('<iii', raw, 33 + 12 * (n - 1))
    return bytes(blob), raw[off + csz:len(raw) - 8]


def run(args, root):
    env = dict(os.environ, CRASHDOCTOR_TEST_ROOT=root)
    p = subprocess.run([EXE] + args, env=env, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=600)
    return p.returncode, (p.stdout + p.stderr).strip()


def copy_save(src, dst):
    os.makedirs(dst)
    for f in os.listdir(src):
        if os.path.isfile(os.path.join(src, f)):
            shutil.copyfile(os.path.join(src, f), os.path.join(dst, f))


def folder_md5s(d):
    return {f: fmd5(os.path.join(d, f)) for f in sorted(os.listdir(d)) if os.path.isfile(os.path.join(d, f))}


exp = json.load(open(os.path.join(TV, 'expected.json'), encoding='utf-8'))
if os.path.exists(SCR):
    shutil.rmtree(SCR)
root = os.path.join(SCR, 'root'); os.makedirs(os.path.join(root, 'AppData', 'CrashDoctor'))
work = os.path.join(SCR, 'cases')

print('1. --repair-save on every test vector')
reasons = {'refused:already healthy': 'already healthy', 'refused:not this problem': 'not this problem', 'refused:female V': 'female V'}
for c in exp['cases']:
    src = os.path.join(work, c['case'], os.path.basename(c['input']))
    copy_save(os.path.join(TV, c['input']), src)
    before = folder_md5s(src)
    check(before['sav.dat'] == c['input_sav_md5'], '%s: input copy is the expected save' % c['case'])
    out = os.path.join(work, c['case'], 'out', c.get('output_folder_name', 'ManualSave-1690'))
    os.makedirs(os.path.dirname(out))
    code, text = run(['--repair-save', src, '--out', out], root)
    if c['expected'] == 'repaired':
        check(code == 0, '%s: repaired (%s)' % (c['case'], text.splitlines()[0] if text else ''))
        if code == 0:
            raw = open(os.path.join(out, 'sav.dat'), 'rb').read()
            blob, table = data_and_table(raw)
            check(len(raw) == c['output_size'] and md5(raw) == c['output_sav_md5'], '%s: sav.dat md5 %s (prototype %s)' % (c['case'], md5(raw), c['output_sav_md5']))
            check(len(blob) == c['output_data_size'] and md5(blob) == c['output_data_md5'], '%s: decompressed data md5' % c['case'])
            check(md5(table) == c['output_nodetable_md5'], '%s: node table md5' % c['case'])
            check(fmd5(os.path.join(out, 'metadata.9.json')) == c['output_metadata_md5'], '%s: metadata.9.json md5' % c['case'])
            check(('insert_at: %d' % c['insert_virtual_offset']) in text, '%s: inserted at %d' % (c['case'], c['insert_virtual_offset']))
            check(fmd5(os.path.join(out, 'screenshot.png')) == before.get('screenshot.png'), '%s: screenshot copied' % c['case'])
    else:
        check(code != 0 and reasons[c['expected']] in text, '%s: refused "%s" (%s)' % (c['case'], reasons[c['expected']], text[:120]))
        check(not os.path.exists(out), '%s: nothing written' % c['case'])
    check(folder_md5s(src) == before, '%s: input folder unchanged' % c['case'])

print('2. damaged input')
base = os.path.join(TV, 'gary-autosave4-broken', 'input', 'AutoSave-4')
t = os.path.join(work, 'truncated', 'AutoSave-4'); copy_save(base, t)
raw = open(os.path.join(t, 'sav.dat'), 'rb').read(); open(os.path.join(t, 'sav.dat'), 'wb').write(raw[:len(raw) - 1000])
code, text = run(['--repair-save', t, '--out', os.path.join(work, 'truncated', 'out')], root)
check(code != 0 and 'metadata' in text, 'truncated sav.dat refused (%s)' % text[:100])
m = open(os.path.join(t, 'metadata.9.json'), encoding='utf-8').read().replace('"fileSize": %d' % (len(raw)), '"fileSize": %d' % (len(raw) - 1000))
open(os.path.join(t, 'metadata.9.json'), 'w', encoding='utf-8', newline='').write(m)
code, text = run(['--repair-save', t, '--out', os.path.join(work, 'truncated', 'out')], root)
check(code != 0 and not os.path.exists(os.path.join(work, 'truncated', 'out')), 'truncated sav.dat with matching metadata refused (%s)' % text[:100])
code, text = run(['--repair-save', base, '--out', base], root)
check(code != 0, 'output = input refused')

print('3. the scan')
fx = os.path.join(SCR, 'savefix')
game = os.path.join(fx, 'game', 'bin', 'x64'); os.makedirs(game)
open(os.path.join(game, 'Cyberpunk2077.exe'), 'wb').close()
os.makedirs(os.path.join(fx, 'AppData', 'CrashDoctor')); os.makedirs(os.path.join(fx, 'LocalAppData'))
saves = os.path.join(fx, 'Saved Games', 'CD Projekt Red', 'Cyberpunk 2077')
names = {'gary-autosave4-broken': 'AutoSave-4', 'demongent-manualsave4-broken': 'ManualSave-4', 'gary-manualsave1615-healthy': 'ManualSave-1615',
         'gary-autosave11-not-mox': 'AutoSave-11', 'female-metadata': 'AutoSave-5'}
for c in exp['cases']:
    copy_save(os.path.join(TV, c['input']), os.path.join(saves, names[c['case']]))
fem = os.path.join(saves, 'AutoSave-5', 'metadata.9.json')
txt = open(fem, encoding='utf-8', newline='').read()
open(fem, 'w', encoding='utf-8', newline='').write(txt.replace('"name": "AutoSave-4"', '"name": "AutoSave-5"'))
orig = {n: folder_md5s(os.path.join(saves, n)) for n in os.listdir(saves)}
out = os.path.join(SCR, 'scan1.json')
code, text = run(['--json', out, '--game', os.path.join(fx, 'game')], fx)
r = json.load(open(out, encoding='utf-8'))
items = [h for h in r['health'] if 'Gorilla' in h['title']]
for h in items:
    print('     [%s] %s  -> %s' % (h['severity'], h['title'], (h.get('action') or {}).get('id')))
rep = sorted(h['action']['id'] for h in items if h.get('action') and h['action']['id'].startswith('savefix:repair:'))
check(rep == ['savefix:repair:AutoSave-4', 'savefix:repair:ManualSave-4'], 'Repair offered for exactly the two broken saves: %s' % rep)
check(any(h['severity'] == 'info' and 'AutoSave-5' in h['detail'] and 'female' in h['detail'] for h in items), 'female save listed as not repairable yet')
check(not any('1615' in h['title'] + h['detail'] or 'AutoSave-11' in h['title'] + h['detail'] for h in items), 'healthy and not-Mox saves not mentioned')

print('4. Repair button and Undo')
code, text = run(['--repair-save-here', 'AutoSave-4'], fx)
new = os.path.join(saves, 'ManualSave-1616')
check(code == 0 and os.path.isdir(new), 'repaired copy written as ManualSave-1616 (%s)' % text.splitlines()[0])
if os.path.isdir(new):
    check(fmd5(os.path.join(new, 'sav.dat')) == exp['cases'][0]['output_sav_md5'], 'its sav.dat is the expected repair')
    meta = json.load(open(os.path.join(new, 'metadata.9.json'), encoding='utf-8'))['Data']['metadata']
    check(meta['name'] == 'ManualSave-1616' and meta['fileSize'] == exp['cases'][0]['output_size'], 'its metadata names ManualSave-1616 and the new size')
code, text = run(['--repair-save-here', 'AutoSave-11'], fx)
check(code != 0 and not os.path.exists(os.path.join(saves, 'ManualSave-1617')), 'not-Mox save refused by the button too')
code, text = run(['--json', out, '--game', os.path.join(fx, 'game')], fx)
r = json.load(open(out, encoding='utf-8'))
items = [h for h in r['health'] if 'Gorilla' in h['title'] or 'repaired' in h['title']]
for h in items:
    print('     [%s] %s  -> %s' % (h['severity'], h['title'], (h.get('action') or {}).get('id')))
check(any((h.get('action') or {}).get('id') == 'savefix:undo:ManualSave-1616' for h in items), 'scan shows AutoSave-4 as repaired, with Undo')
check(not any((h.get('action') or {}).get('id') == 'savefix:repair:AutoSave-4' for h in items), 'and no longer offers to repair it')
code, text = run(['--undo-save-repair', 'ManualSave-1616'], fx)
check(code == 0 and not os.path.exists(new), 'Undo removed ManualSave-1616 (%s)' % text[:100])
code, text = run(['--undo-save-repair', 'ManualSave-1616'], fx)
check(code != 0, 'a second Undo refuses')
check({n: folder_md5s(os.path.join(saves, n)) for n in os.listdir(saves)} == orig, 'every original save byte-identical at the end, nothing else left')

print('\n%d failure(s)' % len(fails))
sys.exit(1 if fails else 0)

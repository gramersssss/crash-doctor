"""Builds a Vortex install whose mods need each other, and drives Find it through it (0.8.2: groups, pins, continue).

    python tools/test-findit.py <CrashDoctor.exe> <scratch folder>

The fixture (<scratch folder>\\findit, synthetic, nothing copied from this PC):
  - AppearanceMenuMod (a CET mod), "AMM Props Pack" (files inside AMM's own folder, AppearanceMenuMod\\Collabs) and
    "AMM Helper" (its Lua calls GetMod("AppearanceMenuMod")): both need AMM - WangChangZhang's case
  - "Shared Lib" (redscript "module SharedLib.Core") and "Script User" ("import SharedLib.Core.*")
  - Codeware (a framework) and "UI Tweak" importing Codeware.UI: that link must be dropped, frameworks never go off
  - "Vortex Base" and "Vortex Child", tied only by a Vortex "requires" rule in AppData\\Vortex\\temp\\state_backups_full
  - six plain archive mods
Switching a mod off is done the way Vortex does it: its files leave vortex.deployment.json and its folder stays in staging.
Every CLI call runs with CRASHDOCTOR_TEST_ROOT pointing at the fixture. Exit code 1 on any failure.
"""
import json, os, shutil, subprocess, sys

EXE, SCR = os.path.abspath(sys.argv[1]), os.path.abspath(sys.argv[2])
FX = os.path.join(SCR, 'findit'); GAME = os.path.join(FX, 'game'); STAGE = os.path.join(FX, 'staging')
CET = 'bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\'
MODS = {   # name -> (vortex folder, {rel: content})
    'AppearanceMenuMod': ('AppearanceMenuMod-790-2-0-1773080000', {CET + 'AppearanceMenuMod\\init.lua': 'return {}\n'}),
    'AMM Props Pack': ('AMM Props Pack-1001-1-0-1773080001', {CET + 'AppearanceMenuMod\\Collabs\\PropsPack.lua': 'return {}\n'}),
    'AMM Helper': ('AMM Helper-1002-1-0-1773080002', {CET + 'AmmHelper\\init.lua': 'local amm = GetMod("AppearanceMenuMod")\n'}),
    'Shared Lib': ('Shared Lib-1003-1-0-1773080003', {'r6\\scripts\\SharedLib\\Core.reds': 'module SharedLib.Core\npublic func Hello() -> Int32 { return 1; }\n'}),
    'Script User': ('Script User-1004-1-0-1773080004', {'r6\\scripts\\ScriptUser\\main.reds': 'module ScriptUser\nimport SharedLib.Core.*\n'}),
    'Codeware': ('Codeware-7780-1-21-0-1773080005', {'red4ext\\plugins\\Codeware\\Codeware.dll': 'x', 'r6\\scripts\\Codeware\\UI.reds': 'module Codeware.UI\n'}),
    'UI Tweak': ('UI Tweak-1005-1-0-1773080006', {'r6\\scripts\\UITweak\\ui.reds': 'import Codeware.UI\n'}),
    'Vortex Base': ('Vortex Base-1006-1-0-1773080007', {'archive\\pc\\mod\\vbase.archive': 'x'}),
    'Vortex Child': ('Vortex Child-1007-1-0-1773080008', {'archive\\pc\\mod\\vchild.archive': 'x'}),
}
for i, n in enumerate('ABCDEF'):
    MODS['Plain ' + n] = ('Plain %s-20%02d-1-0-17730801%02d' % (n, i, i), {'archive\\pc\\mod\\plain%s.archive' % n.lower(): 'x'})
fails = []


def check(ok, what):
    print(('  ok   ' if ok else '  FAIL ') + what)
    if not ok:
        fails.append(what)


def deploy(off=()):
    """Vortex's manifest with every mod except `off` deployed; staging holds every mod's folder."""
    files = []
    for name, (folder, fl) in MODS.items():
        for rel, txt in fl.items():
            p = os.path.join(GAME, rel)
            if name in off:
                if os.path.exists(p): os.remove(p)
            else:
                os.makedirs(os.path.dirname(p), exist_ok=True); open(p, 'w').write(txt)
                files.append({'relPath': rel, 'source': folder, 'target': '', 'time': 1773082675000})
    json.dump({'instance': 'fixture', 'stagingPath': STAGE, 'files': files}, open(os.path.join(GAME, 'vortex.deployment.json'), 'w'), indent=2)


def make():
    if os.path.exists(FX): shutil.rmtree(FX)
    os.makedirs(os.path.join(GAME, 'bin', 'x64')); open(os.path.join(GAME, 'bin', 'x64', 'Cyberpunk2077.exe'), 'wb').close()
    os.makedirs(os.path.join(FX, 'AppData', 'CrashDoctor')); os.makedirs(os.path.join(FX, 'LocalAppData'))
    for name, (folder, fl) in MODS.items():
        for rel, txt in fl.items():
            p = os.path.join(STAGE, folder, rel); os.makedirs(os.path.dirname(p), exist_ok=True); open(p, 'w').write(txt)
    # Vortex's state backup: Vortex Child "requires" Vortex Base (reference by Vortex's own mod id)
    mods = {}
    for name, (folder, _) in MODS.items():
        mods[folder] = {'id': folder, 'installationPath': folder, 'state': 'installed', 'attributes': {'modId': int(folder.split('-')[1]), 'logicalFileName': name, 'name': folder}}
    mods[MODS['Vortex Child'][0]]['rules'] = [{'type': 'requires', 'reference': {'id': MODS['Vortex Base'][0]}}, {'type': 'before', 'reference': {'id': MODS['Plain A'][0]}}]
    sb = os.path.join(FX, 'AppData', 'Vortex', 'temp', 'state_backups_full'); os.makedirs(sb)
    json.dump({'persistent': {'mods': {'cyberpunk2077': mods}}}, open(os.path.join(sb, 'hourly.json'), 'w'))
    deploy()


def scan(*extra):
    out = os.path.join(SCR, 'findit-out.json')
    if os.path.exists(out): os.remove(out)
    p = subprocess.run([EXE, '--json', out, '--game', GAME] + list(extra), env=dict(os.environ, CRASHDOCTOR_TEST_ROOT=FX),
                       capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=600)
    if not os.path.exists(out):
        print(p.stdout, p.stderr); fails.append('scan failed'); return {}, p.stdout
    return json.load(open(out, encoding='utf-8')), p.stdout


def links(r):
    return sorted((l['mod'], l['needs'], l['source']) for l in r['findIt']['links'])


def dependents(lk, m):
    res, todo = {m}, [m]
    while todo:
        x = todo.pop()
        for a, b, _ in lk:
            if b == x and a not in res: res.add(a); todo.append(a)
    return res


make()
print('1. what is read')
r, _ = scan()
lk = links(r)
for l in lk: print('     %s needs %s (%s)' % l)
check(lk == sorted([('AMM Props Pack', 'AppearanceMenuMod', 'folder'), ('AMM Helper', 'AppearanceMenuMod', 'cet'),
                    ('Script User', 'Shared Lib', 'redscript'), ('Vortex Child', 'Vortex Base', 'vortex')]), 'exactly the four real links (Codeware dropped, "before" rule ignored)')
check(sorted(r['findIt']['sources']) == ['cet', 'folder', 'redscript', 'vortex'], 'sources: %s' % r['findIt']['sources'])
check('Codeware' not in r['findIt']['mods'] and len(r['findIt']['mods']) == 14, 'pickers list the 14 testable mods, no framework')

print('2. a manual link and a pin')
r, _ = scan('--findit-link', 'Plain A', 'Plain B', '--findit-pin', 'Script User')
lk = links(r)
check(('Plain A', 'Plain B', 'manual') in lk, 'manual link "Plain A needs Plain B" listed')
check(r['findIt']['pinned'] == ['Script User'] and r['findIt']['pinnedNeeds'] == ['Shared Lib'], 'Script User pinned, Shared Lib kept because it needs it')

print('3. a hunt keeps groups whole and pinned mods on')
r, _ = scan('--bisect-start', 'Cyberpunk2077.exe+0x1@1')
b = r.get('bisect') or {}
print('     round %s off: %s' % (b.get('stepNumber'), b.get('off')))
off = set(b.get('off', []))
check(b.get('status') == 'running' and b.get('candidates') == 12, '12 candidates (14 minus Script User and Shared Lib): %s' % b.get('candidates'))
check(not off & {'Script User', 'Shared Lib'}, 'nothing kept is asked to go off')
check(all(dependents(lk, m) <= off for m in off), 'every mod going off takes the mods that need it along')
check(sorted(b.get('kept', [])) == ['Script User', 'Shared Lib'], 'kept list shown: %s' % b.get('kept'))
check(set(b.get('turnOff', [])) == off and not b.get('ready'), 'not set up yet: all %d still on' % len(off))

print('4. set the round up in "Vortex" -> ready and the clock starts')
deploy(off)
r, _ = scan()
b = r['bisect']
check(b['ready'] and b['playing'] and not b['turnOff'] and not b['turnOn'] and not b.get('broken'), 'ready, playing, nothing broken')

print('5. pinning AMM before a round is under way redraws it')
deploy(); scan('--bisect-stop'); scan('--bisect-start', 'Cyberpunk2077.exe+0x1@1')
r, _ = scan('--findit-pin', 'AppearanceMenuMod')
b = r['bisect']; off = set(b['off'])
print('     round %s off: %s' % (b['stepNumber'], b['off']))
check('AppearanceMenuMod' not in off and 'AppearanceMenuMod' in b['kept'] and b['stepNumber'] == 1, 'AMM never asked off, kept, still round 1')
check(all(dependents(lk, m) <= off for m in off), 'groups still whole')

print('6. "continue with my current setup" when AMM goes off but its add-ons stay on (the stuck case)')
scan('--findit-unpin', 'AppearanceMenuMod', '--bisect-stop')
r, _ = scan('--bisect-start', 'Cyberpunk2077.exe+0x1@1')
deploy({'AppearanceMenuMod', 'Plain C'})
r, _ = scan()
b = r['bisect']
print('     broken: %s' % b.get('broken'))
check(not b['ready'] and len(b.get('broken', [])) == 2, 'not ready; warns that both AMM add-ons need AMM')
lk2 = links(r)
check(('AMM Helper', 'AppearanceMenuMod', 'cet') in lk2 and ('AMM Props Pack', 'AppearanceMenuMod', 'folder') in lk2
      and not any(a == 'AMM Helper' and n == 'AMM Props Pack' for a, n, _ in lk2), 'with AMM off its links are still listed, and no add-on is taken for AMM')
r, out = scan('--bisect-continue')
b = r['bisect']
check('continued' in out and b['continued'] and b['ready'] and b['playing'], 'continued: this round = what is on now, playing')
check(sorted(b['off']) == ['AppearanceMenuMod', 'Plain C'], 'the round is exactly what is off: %s' % b['off'])
deploy(set(r['findIt']['mods']))   # what was on goes off, the two that were off come back on
scan('--bisect-stop'); scan('--bisect-start', 'Cyberpunk2077.exe+0x1@1')
r, out = scan('--bisect-continue')
check('not continued' in out and not r['bisect'].get('continued'), 'refused when nothing still in the test is off (nothing to learn): ' + out.strip().splitlines()[0])
deploy()

print('7. the saved report without the mod list carries no Find it names')
html = os.path.join(SCR, 'findit.html')
subprocess.run([EXE, '--html', html, '--no-mods', '--game', GAME], env=dict(os.environ, CRASHDOCTOR_TEST_ROOT=FX), capture_output=True, timeout=600)
check('"findIt"' not in open(html, encoding='utf-8').read(), 'no findIt in the --no-mods report')

print('\n%d failure(s)' % len(fails))
sys.exit(1 if fails else 0)

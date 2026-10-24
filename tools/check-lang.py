"""Checks the language files against the code and against English.

    python tools/check-lang.py            # everything
    python tools/check-lang.py de fr      # only these translations

ui/lang/en.json is the reference. Every sentence the app shows is a key there; the engine refers to keys through
L.T / L.P / L.TF (src/Engine/Lang.cs) and the page through t() / tp() / data-i18n (ui/index.html).

What it checks:
  - every key the code or the page uses exists in en.json (a missing one would show as the bare key)
  - every L.T / L.P call with a literal key passes as many arguments as the English text has placeholders
  - every translation has exactly the English keys, the same {placeholders} in every text, the plural forms its
    language needs (CLDR: one/few/many/other ...), the same <b> tags in the -html texts, and nothing it cannot have
    (the engine's private-use token characters)
Unused English keys are listed as a warning only. Exit code 1 when anything else is wrong.
"""
import io, json, os, re, sys, glob

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LANG = os.path.join(ROOT, 'ui', 'lang')
PH = re.compile(r'\{(\w+)\}')

# CLDR cardinal categories each language needs (integers; "other" is always required)
FORMS = {
    'en': ['one', 'other'], 'de': ['one', 'other'], 'es-ES': ['one', 'other'], 'es-MX': ['one', 'other'],
    'it': ['one', 'other'], 'hu': ['one', 'other'], 'tr': ['one', 'other'], 'fr': ['one', 'other'],
    'pt-BR': ['one', 'other'], 'ru': ['one', 'few', 'many', 'other'], 'uk': ['one', 'few', 'many', 'other'],
    'pl': ['one', 'few', 'many', 'other'], 'cs': ['one', 'few', 'other'], 'ar': ['zero', 'one', 'two', 'few', 'many', 'other'],
    'ja': ['other'], 'ko': ['other'], 'zh-CN': ['other'], 'zh-TW': ['other'], 'th': ['other'],
}


def load(code):
    return json.load(io.open(os.path.join(LANG, code + '.json'), encoding='utf-8'))


def texts(v):
    return list(v.values()) if isinstance(v, dict) else [v]


def names(v):
    out = set()
    for t in texts(v):
        out |= set(PH.findall(t))
    return out


def split_args(s, i):
    """s[i] is '(' ; returns (list of top-level argument strings, index after ')')."""
    depth, start, args, j, q = 0, i + 1, [], i, None
    while j < len(s):
        c = s[j]
        if q:
            if c == '\\': j += 2; continue
            if c == q: q = None
        elif c in '"\'': q = c
        elif c in '([{': depth += 1
        elif c in ')]}':
            depth -= 1
            if depth == 0:
                args.append(s[start:j]); return [a.strip() for a in args if a.strip()], j + 1
        elif c == ',' and depth == 1:
            args.append(s[start:j]); start = j + 1
        j += 1
    return [], j


def main():
    en = load('en')
    problems, warnings = [], []
    prefixes = sorted({k.split('.')[0] for k in en if '.' in k and not k.startswith('_')})
    used = set()
    checked = [0]

    # ---- the engine
    for f in glob.glob(os.path.join(ROOT, 'src', '**', '*.cs'), recursive=True):
        if os.sep + 'obj' + os.sep in f or os.sep + 'bin' + os.sep in f: continue
        src = io.open(f, encoding='utf-8').read()
        rel = os.path.relpath(f, ROOT)
        for m in re.finditer(r'"((?:%s)\.[A-Za-z0-9_.\-]+)"' % '|'.join(map(re.escape, prefixes)), src):
            k = m.group(1)
            if k.endswith('.') or re.search(r'\.(json|log|txt|ini|dll|exe)$', k): continue
            used.add(k)
            if k not in en: problems.append('%s: key "%s" is not in en.json' % (rel, k))
        for m in re.finditer(r'\bL\.(T|P)\(', src):
            args, _ = split_args(src, m.end() - 1)
            if not args or not re.fullmatch(r'"[\w.\-]+"', args[0]): continue
            k = args[0][1:-1]
            if k not in en: continue
            want = names(en[k]) - ({'n'} if m.group(1) == 'P' else set())
            got = len(args) - 1 - (1 if m.group(1) == 'P' else 0)
            if m.group(1) == 'P' and not isinstance(en[k], dict): problems.append('%s: L.P("%s") but the English text has no plural forms' % (rel, k))
            if m.group(1) == 'T' and isinstance(en[k], dict): problems.append('%s: L.T("%s") but the English text is a plural; use L.P' % (rel, k))
            checked[0] += 1
            if got != len(want): problems.append('%s: L.%s("%s") passes %d argument(s), the English text has %d: %s' % (rel, m.group(1), k, got, len(want), sorted(want)))
        # the keys the collectors build from the source names: Safe(d, "red4ext", ...) -> "what.red4ext"
        for m in re.finditer(r'Safe\(d,\s*"(\w+)"', src):
            k = 'what.' + m.group(1); used.add(k)
            if k not in en: problems.append('%s: key "%s" is not in en.json' % (rel, k))

    # ---- the page
    page = io.open(os.path.join(ROOT, 'ui', 'index.html'), encoding='utf-8').read()
    page = re.sub(r'/\*.*?\*/', '', page, flags=re.S)   # comments may name example keys
    for m in re.finditer(r"""(?:\btp?\(|data-i18n(?:-html|-placeholder)?=)["']([\w.\-]+)["']""", page):
        k = m.group(1)
        if k.endswith('.'): continue
        used.add(k)
        if k not in en: problems.append('ui/index.html: key "%s" is not in en.json' % k)
    for m in re.finditer(r"""['"](ui\.[\w.\-]+[\w])['"]""", page):
        used.add(m.group(1))
        if m.group(1) not in en: problems.append('ui/index.html: key "%s" is not in en.json' % m.group(1))
    # keys built in the page: 'ui.kind.'+k, 'ui.theme.'+id+'.name' ... are covered by their whole families
    for fam in ('ui.kind.', 'ui.conf.', 'ui.sev.', 'ui.level.', 'ui.status.', 'ui.type.', 'ui.chg.', 'ui.req.st.', 'ui.kb.', 'ui.theme.', 'gfxv.', 'gfxl.', 'gfxg.', 'kb.', 'fmt.', '_lang.', 'hook.', 'inj.', 'what.', 'chg.state'):
        used |= {k for k in en if k.startswith(fam)}
    unused = sorted(k for k in en if k not in used)
    if unused: warnings.append('%d English key(s) not referenced anywhere: %s' % (len(unused), ', '.join(unused)))

    # ---- the translations
    only = sys.argv[1:]
    for f in sorted(glob.glob(os.path.join(LANG, '*.json'))):
        code = os.path.basename(f)[:-5]
        if code == 'en' or (only and code not in only): continue
        try: tr = load(code)
        except Exception as e: problems.append('%s.json does not parse: %s' % (code, e)); continue
        need = FORMS.get(code, ['one', 'other'])
        miss = [k for k in en if k not in tr]
        extra = [k for k in tr if k not in en]
        if miss: problems.append('%s: %d missing key(s): %s' % (code, len(miss), ', '.join(miss[:12]) + (' ...' if len(miss) > 12 else '')))
        if extra: problems.append('%s: %d key(s) English does not have: %s' % (code, len(extra), ', '.join(extra[:12])))
        for k, v in tr.items():
            if k not in en: continue
            e = en[k]
            for t in texts(v):
                if not isinstance(t, str): problems.append('%s %s: not text' % (code, k)); continue
                if any(c in t for c in ''): problems.append('%s %s: contains a token character' % (code, k))
                bad = set(PH.findall(t)) - names(e)
                if bad: problems.append('%s %s: placeholder(s) English does not have: %s' % (code, k, sorted(bad)))
            if isinstance(e, dict):
                if not isinstance(v, dict): problems.append('%s %s: English is a plural, the translation is not' % (code, k)); continue
                for form in need:
                    if form not in v: problems.append('%s %s: plural form "%s" missing' % (code, k, form))
                if 'other' in v and names(e) - {'n'} - set(PH.findall(v['other'])):
                    problems.append('%s %s: "other" form drops %s' % (code, k, sorted(names(e) - {'n'} - set(PH.findall(v['other'])))))
            else:
                if isinstance(v, dict): problems.append('%s %s: translation is a plural, English is not' % (code, k)); continue
                lost = names(e) - set(PH.findall(v))
                if lost and k not in ('fmt.date.dm',): problems.append('%s %s: drops placeholder(s) %s' % (code, k, sorted(lost)))
                if k.endswith('Help') or k.startswith('ui.report.'):
                    if v.count('<b>') != e.count('<b>'): problems.append('%s %s: <b> tags differ from English' % (code, k))
        months = tr.get('fmt.months', '')
        if len(months.split('|')) != 12: problems.append('%s fmt.months: needs 12 names separated by |' % code)

    for w in warnings: print('warning:', w)
    for p in problems: print('PROBLEM:', p)
    print('%d keys in English; %d engine calls checked for arguments; %d problem(s)' % (len(en), checked[0], len(problems)))
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())

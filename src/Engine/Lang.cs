using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrashDoctor.Engine;

// Every sentence the app shows lives in ui\lang\<code>.json, English in en.json, which is the reference.
//
// The engine never writes finished text. It writes *tokens*: a key and its arguments wrapped in three private-use
// characters, so a verdict is stored as "verdict.cleanExitdur.hm134" rather than
// "Closed normally after 1 h 34 min.". Tokens concatenate and nest like ordinary strings, so the reasoning code reads
// exactly as it did; only the moment of showing turns them into words, in whichever language is chosen then.
//
// That is what makes the language switch cheap and safe:
//   - switching re-renders the report already in memory, with no rescan;
//   - sessions remembered in history.json and the bisect plan keep tokens, so a verdict written last month reads in
//     today's language, and a wrong pick is undone by picking again;
//   - the saved HTML report and the command line render once, in the language asked for.
//
// Placeholders are named, e.g. "{mod}". Arguments are positional in code and fill the names in the order they first
// appear in the ENGLISH template, so a translation may put them in any order it needs. Plural keys hold an object of
// CLDR forms ({"one": "...", "other": "..."}); L.P passes the count, which is always the placeholder {n}.
public sealed class Lang
{
    public string Code { get; }
    public string Name { get; }          // in its own language, e.g. "Deutsch"
    public string English { get; }       // its name in English, e.g. "German"
    public bool Rtl { get; }
    readonly Dictionary<string, JsonNode?> _map;
    public JsonObject Raw { get; }

    Lang(string code, JsonObject raw)
    {
        Code = code; Raw = raw;
        _map = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var kv in raw) _map[kv.Key] = kv.Value;
        Name = Str("_lang.name") ?? code;
        English = Str("_lang.english") ?? code;
        Rtl = Str("_lang.dir") == "rtl";
    }

    public JsonNode? Node(string key) => _map.TryGetValue(key, out var n) ? n : null;
    public string? Str(string key) => Node(key) is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    public static Lang? Load(string file, string code)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(file, Encoding.UTF8), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return root is JsonObject o ? new Lang(code, o) : null;
        }
        catch { return null; }
    }

    public static Lang Empty(string code) => new(code, new JsonObject());
}

public static class L
{
    public const char Open = '', Sep = '', Close = '';
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ------------------------------------------------------------------ making tokens

    /// <summary>A sentence by key. Arguments fill the placeholders in the order they appear in the English text.</summary>
    public static string T(string key, params object?[] args) => Make(key, args);

    /// <summary>A sentence with a count: the template is chosen by the language's plural rules for <paramref name="n"/>, which fills {n}.</summary>
    public static string P(string key, double n, params object?[] args)
    {
        var all = new object?[args.Length + 1]; all[0] = n; Array.Copy(args, 0, all, 1, args.Length);
        return Make(key + "#", all);
    }

    /// <summary>A key whose text may not exist in any language file (e.g. knowledge-base entries); the fallback is shown then.</summary>
    public static string TF(string key, string fallback) => Make("@or", key, fallback);

    /// <summary>A date, formatted in the reader's language. Formats: dm (6 Sep), dmt (6 Sep 14:05), my (Sep 2026), t (14:05), full.</summary>
    public static string Date(DateTime d, string format = "dm") => Make("@date." + format, d.ToString("yyyy-MM-ddTHH:mm:ss", Inv));

    /// <summary>
    /// Sentences one after another, joined the way the language joins sentences (a space, or nothing in Chinese and
    /// Japanese). Empty parts are skipped, so optional sentences can be passed as "" without leaving double spaces.
    /// </summary>
    public static string Cat(params string?[] parts)
    {
        var keep = parts.Where(p => !string.IsNullOrEmpty(p)).Cast<object?>().ToArray();
        return keep.Length switch { 0 => "", 1 => (string)keep[0]!, _ => Make("@cat", keep) };
    }

    /// <summary>"a, b and c" in the reader's language.</summary>
    public static string List(IEnumerable<string> items) => Make("@list", items.Cast<object?>().ToArray());

    /// <summary>A play time: "under a minute", "34 min", "1 h 34 min".</summary>
    public static string Dur(double minutes) =>
        minutes < 1 ? T("dur.underMin") : minutes < 60 ? T("dur.min", Math.Round(minutes)) : T("dur.hm", (int)(minutes / 60), Math.Round(minutes % 60));

    /// <summary>A percentage of a ratio, e.g. 0.953 -> "95%". Numbers and the % sign are written the same everywhere.</summary>
    public static string Pct(double ratio) => Math.Round(ratio * 100).ToString(Inv) + "%";

    public static bool IsToken(string? s) => s != null && s.IndexOf(Open) >= 0;

    static string Make(string key, params object?[] args)
    {
        var sb = new StringBuilder(key.Length + 16);
        sb.Append(Open).Append(key);
        foreach (var a in args) sb.Append(Sep).Append(Arg(a));
        return sb.Append(Close).ToString();
    }

    static string Arg(object? a) => a switch
    {
        null => "",
        string s => Clean(s),
        double d => d.ToString("0.##", Inv),
        float f => f.ToString("0.##", Inv),
        DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss", Inv),
        IFormattable fm => fm.ToString(null, Inv),
        _ => Clean(a.ToString() ?? ""),
    };

    // A raw value (a mod name, a log line) can never carry our delimiters on purpose; if one ever does, drop them
    // rather than let it break the sentence around it. Tokens passed as arguments are kept whole.
    static string Clean(string s) => s;

    // ------------------------------------------------------------------ languages

    public static string LangDir => Path.Combine(AppContext.BaseDirectory, "ui", "lang");

    /// <summary>The languages shipped, English first, then in the order of their English names.</summary>
    public static readonly string[] Codes = { "en", "ar", "zh-CN", "zh-TW", "cs", "fr", "de", "hu", "it", "ja", "ko", "pl", "pt-BR", "ru", "es-ES", "es-MX", "th", "tr", "uk" };

    static readonly Dictionary<string, Lang> _cache = new(StringComparer.OrdinalIgnoreCase);
    static Lang? _en;
    public static Lang En => _en ??= Get("en");
    public static Lang Current { get; private set; } = null!;

    public static Lang Get(string code)
    {
        code = Normalise(code) ?? "en";
        lock (_cache)
        {
            if (_cache.TryGetValue(code, out var l)) return l;
            l = Lang.Load(Path.Combine(LangDir, code + ".json"), code) ?? (code == "en" ? Lang.Empty("en") : null);
            if (l == null) return En;
            return _cache[code] = l;
        }
    }

    public static void Use(string? code) => Current = Get(code ?? "en");

    static L() { Current = Lang.Empty("en"); }

    /// <summary>One of our codes for any language tag, or null when we do not ship that language.</summary>
    public static string? Normalise(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var t = tag.Trim().Replace('_', '-');
        foreach (var c in Codes) if (c.Equals(t, StringComparison.OrdinalIgnoreCase)) return c;
        var lower = t.ToLowerInvariant();
        var lang = lower.Split('-')[0];
        switch (lang)
        {
            case "zh":
                // Traditional script: Taiwan, Hong Kong, Macau, or an explicit Hant tag
                return lower.Contains("hant") || lower.EndsWith("-tw") || lower.EndsWith("-hk") || lower.EndsWith("-mo") ? "zh-TW" : "zh-CN";
            case "es":
                // Spain gets the Spain translation; every other Spanish-speaking region the Latin American one
                return lower == "es" || lower.EndsWith("-es") ? "es-ES" : "es-MX";
            case "pt": return "pt-BR";
            case "ua": return "uk";
            case "cz": return "cs";
            case "jp": return "ja";
            case "kr": return "ko";
        }
        foreach (var c in Codes) if (c.Split('-')[0].Equals(lang, StringComparison.OrdinalIgnoreCase)) return c;
        return null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetUserPreferredUILanguages(uint flags, out uint count, char[]? buffer, ref uint size);

    /// <summary>The Windows display language, as one of our codes, or null when we do not ship it.</summary>
    public static string? WindowsLanguage()
    {
        try
        {
            uint count, size = 0;
            const uint MUI_LANGUAGE_NAME = 0x8;
            if (GetUserPreferredUILanguages(MUI_LANGUAGE_NAME, out count, null, ref size) && size > 0)
            {
                var buf = new char[size];
                if (GetUserPreferredUILanguages(MUI_LANGUAGE_NAME, out count, buf, ref size))
                    foreach (var tag in new string(buf).Split('\0', StringSplitOptions.RemoveEmptyEntries))
                        if (Normalise(tag) is { } c) return c;
                // the first preferred language is the display language; if we do not ship it, English, not the second
                return null;
            }
        }
        catch { }
        return null;
    }

    /// <summary>The language to start in: the saved choice, else the Windows display language, else English.</summary>
    public static string StartLanguage(string? saved) => Normalise(saved) ?? WindowsLanguage() ?? "en";

    /// <summary>Every shipped language that actually has a file, for the picker.</summary>
    public static List<object> Available()
    {
        var list = new List<object>();
        foreach (var c in Codes)
        {
            if (c != "en" && !File.Exists(Path.Combine(LangDir, c + ".json"))) continue;
            var l = Get(c);
            list.Add(new { code = c, name = l.Name, english = l.English, rtl = l.Rtl });
        }
        return list;
    }

    // ------------------------------------------------------------------ rendering

    public static string R(string? s) => Render(s, Current);
    public static string REn(string? s) => Render(s, En);

    public static string Render(string? s, Lang lang)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf(Open) < 0) return s ?? "";
        var sb = new StringBuilder(s.Length * 2);
        int i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == Open) { var end = Match(s, i); if (end < 0) { sb.Append(s, i, s.Length - i); break; } sb.Append(RenderOne(s.Substring(i + 1, end - i - 1), lang)); i = end + 1; }
            else { sb.Append(c); i++; }
        }
        return sb.ToString();
    }

    // index of the Close matching the Open at i, or -1
    static int Match(string s, int i)
    {
        int depth = 0;
        for (int j = i; j < s.Length; j++)
        {
            if (s[j] == Open) depth++;
            else if (s[j] == Close && --depth == 0) return j;
        }
        return -1;
    }

    // splits "key<sep>a<sep>b" at depth 0
    static List<string> Split(string inner)
    {
        var parts = new List<string>(); int depth = 0, start = 0;
        for (int j = 0; j < inner.Length; j++)
        {
            if (inner[j] == Open) depth++;
            else if (inner[j] == Close) depth--;
            else if (inner[j] == Sep && depth == 0) { parts.Add(inner[start..j]); start = j + 1; }
        }
        parts.Add(inner[start..]);
        return parts;
    }

    static string RenderOne(string inner, Lang lang)
    {
        var parts = Split(inner);
        var key = parts[0];
        var args = parts.Skip(1).Select(a => Render(a, lang)).ToList();
        try
        {
            if (key == "@list") return JoinList(args, lang);
            if (key == "@cat") return string.Join(lang.Node("fmt.sentenceSep") is JsonValue sv && sv.TryGetValue<string>(out var sep) ? sep : " ", args.Where(x => x.Length > 0));
            if (key == "@or") return args.Count > 0 && Template(args[0], lang) is { } t ? t : args.Count > 1 ? args[1] : "";
            if (key.StartsWith("@date.")) return FormatDate(key[6..], args.FirstOrDefault(), lang);
            if (key.EndsWith('#'))
            {
                var k = key[..^1];
                double.TryParse(args.FirstOrDefault(), NumberStyles.Float, Inv, out var n);
                var tpl = PluralTemplate(k, n, lang, out var enNames);
                var rest = args.Skip(1).ToList();
                var values = new Dictionary<string, string> { ["n"] = args.FirstOrDefault() ?? "" };
                int ai = 0;
                foreach (var name in enNames) if (name != "n" && !values.ContainsKey(name)) values[name] = ai < rest.Count ? rest[ai++] : "";
                return Fill(tpl ?? k, values);
            }
            var template = Template(key, lang);
            var names = Placeholders(Template(key, En) ?? template ?? "");
            var vals = new Dictionary<string, string>();
            for (int a = 0; a < names.Count; a++) vals[names[a]] = a < args.Count ? args[a] : "";
            return Fill(template ?? key, vals);
        }
        catch { return key; }
    }

    static string? Template(string key, Lang lang)
    {
        var s = lang.Str(key) ?? Other(lang, key);
        if (s == null && !ReferenceEquals(lang, En)) s = En.Str(key) ?? Other(En, key);
        return s;
    }
    // a plural key asked for as a plain one: its "other" form beats showing the key
    static string? Other(Lang l, string key) => l.Node(key) is JsonObject o && o["other"] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    static string? PluralTemplate(string key, double n, Lang lang, out List<string> enNames)
    {
        string? Pick(Lang l, string cat)
        {
            if (l.Node(key) is JsonObject o)
            {
                if (o[cat] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0) return s;
                if (o["other"] is JsonValue ov && ov.TryGetValue<string>(out var so) && so.Length > 0) return so;
            }
            else if (l.Str(key) is { } plain) return plain;
            return null;
        }
        var enOther = Pick(En, "other") ?? "";
        enNames = Placeholders(enOther);
        foreach (var nm in Placeholders(Pick(En, "one") ?? "")) if (!enNames.Contains(nm)) enNames.Add(nm);
        return Pick(lang, Plural.Category(lang.Code, n)) ?? Pick(En, Plural.Category("en", n));
    }

    static List<string> Placeholders(string template)
    {
        var names = new List<string>();
        int i = 0;
        while ((i = template.IndexOf('{', i)) >= 0)
        {
            var j = template.IndexOf('}', i + 1);
            if (j < 0) break;
            var name = template.Substring(i + 1, j - i - 1);
            if (name.Length > 0 && name.Length < 32 && name.All(ch => char.IsLetterOrDigit(ch) || ch == '_') && !names.Contains(name)) names.Add(name);
            i = j + 1;
        }
        return names;
    }

    static string Fill(string template, Dictionary<string, string> values)
    {
        if (template.IndexOf('{') < 0) return template;
        var sb = new StringBuilder(template.Length + 32);
        int i = 0;
        while (i < template.Length)
        {
            if (template[i] == '{')
            {
                var j = template.IndexOf('}', i + 1);
                if (j > i && values.TryGetValue(template.Substring(i + 1, j - i - 1), out var v))
                {
                    sb.Append(v); i = j + 1;
                    // an abbreviation at the end of a value ("2 Okt.", "55 Min.") already carries the sentence's full stop
                    if (v.EndsWith('.') && !v.EndsWith("..") && i < template.Length && template[i] == '.' && (i + 1 >= template.Length || template[i + 1] != '.')) i++;
                    continue;
                }
            }
            sb.Append(template[i]); i++;
        }
        return sb.ToString();
    }

    static string JoinList(List<string> items, Lang lang)
    {
        items = items.Where(x => x.Length > 0).ToList();
        if (items.Count == 0) return "";
        if (items.Count == 1) return items[0];
        var sep = Template("fmt.listSep", lang) ?? ", ";
        var and = Template("fmt.listAnd", lang) ?? "{a} and {b}";
        var head = string.Join(sep, items.Take(items.Count - 1));
        return Fill(and, new() { ["a"] = head, ["b"] = items[^1] });
    }

    static string FormatDate(string fmt, string? iso, Lang lang)
    {
        if (!DateTime.TryParseExact(iso, "yyyy-MM-ddTHH:mm:ss", Inv, DateTimeStyles.None, out var d)) return iso ?? "";
        var pattern = Template("fmt.date." + fmt, lang) ?? fmt switch { "dm" => "d MMM", "dmt" => "d MMM HH:mm", "my" => "MMM yyyy", "t" => "HH:mm", _ => "d MMM yyyy HH:mm" };
        var months = (Template("fmt.months", lang) ?? "Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec").Split('|');
        var month = months.Length == 12 ? months[d.Month - 1] : d.ToString("MMM", Inv);
        // MMM is replaced with a quoted literal so the invariant formatter leaves the language's month name alone
        var p = pattern.Replace("MMM", "'" + month.Replace("'", "") + "'");
        try { return d.ToString(p, Inv); } catch { return d.ToString("yyyy-MM-dd HH:mm", Inv); }
    }

    /// <summary>Renders every string in a JSON tree, property names included, in place.</summary>
    public static JsonNode? RenderNode(JsonNode? node, Lang lang)
    {
        switch (node)
        {
            case JsonValue v when v.TryGetValue<string>(out var s) && s.IndexOf(Open) >= 0:
                return JsonValue.Create(Render(s, lang));
            case JsonObject o:
                foreach (var kv in o.ToList())
                {
                    var rendered = RenderNode(kv.Value, lang);
                    var name = kv.Key.IndexOf(Open) >= 0 ? Render(kv.Key, lang) : kv.Key;
                    if (name != kv.Key) { o.Remove(kv.Key); if (!o.ContainsKey(name)) o[name] = rendered?.DeepClone() ?? null; }
                    else if (!ReferenceEquals(rendered, kv.Value)) o[kv.Key] = rendered;
                }
                return o;
            case JsonArray a:
                for (int i = 0; i < a.Count; i++) { var r = RenderNode(a[i], lang); if (!ReferenceEquals(r, a[i])) a[i] = r; }
                return a;
        }
        return node;
    }

    /// <summary>Serialises anything to JSON with every token rendered in <paramref name="lang"/>.</summary>
    public static string Json(object o, Lang lang, JsonSerializerOptions opts)
    {
        var node = JsonSerializer.SerializeToNode(o, o.GetType(), opts);
        return RenderNode(node, lang)?.ToJsonString(opts) ?? "null";
    }
}

/// <summary>CLDR cardinal plural categories for the languages we ship (integers and simple decimals).</summary>
public static class Plural
{
    public static string Category(string code, double n)
    {
        var lang = code.Split('-')[0].ToLowerInvariant();
        var isInt = Math.Abs(n % 1) < 1e-9;
        var i = (long)Math.Abs(Math.Truncate(n));
        switch (lang)
        {
            case "ja": case "ko": case "zh": case "th": return "other";
            case "fr": return i is 0 or 1 ? "one" : "other";
            case "pt": return i is 0 or 1 ? "one" : "other";
            case "ru": case "uk":
                if (!isInt) return "other";
                if (i % 10 == 1 && i % 100 != 11) return "one";
                if (i % 10 is >= 2 and <= 4 && !(i % 100 is >= 12 and <= 14)) return "few";
                return "many";
            case "pl":
                if (!isInt) return "other";
                if (i == 1) return "one";
                if (i % 10 is >= 2 and <= 4 && !(i % 100 is >= 12 and <= 14)) return "few";
                return "many";
            case "cs":
                if (!isInt) return "many";
                if (i == 1) return "one";
                if (i is >= 2 and <= 4) return "few";
                return "other";
            case "ar":
                if (!isInt) return "other";
                if (i == 0) return "zero";
                if (i == 1) return "one";
                if (i == 2) return "two";
                if (i % 100 is >= 3 and <= 10) return "few";
                if (i % 100 is >= 11 and <= 99) return "many";
                return "other";
            default: return isInt && i == 1 ? "one" : "other";   // en, de, es, it, hu, tr
        }
    }
}

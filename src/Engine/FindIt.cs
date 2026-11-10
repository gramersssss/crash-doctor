using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrashDoctor.Engine;

// "Find it" understands dependencies (0.8.2, promised to WangChangZhang on Nexus, 10 Nov 2026). Their hunt asked them to
// switch off AMM while keeping on mods that need AMM, so the game could not load and the hunt was stuck. Three things:
//
// 1. Groups. A mod and the mods that need it stay together: a round that switches a mod off also switches off every
//    enabled mod that needs it, so the game can still start. What "needs" means is only what can be read, never guessed:
//      - Vortex's own "requires" rules (its state backup, AppData\Vortex\temp\state_backups_full, newest file)
//      - redscript: a mod that imports a module another mod declares ("import Foo.*" / "module Foo")
//      - Cyber Engine Tweaks: a mod whose Lua calls GetMod("X") needs the mod that owns CET mod folder X
//      - files: a mod that puts files inside another mod's CET or RED4ext plugin folder (AMM add-ons in
//        AppearanceMenuMod\Collabs, ...) needs that mod
//      - and what the player says ("this needs that"), when none of the above knows.
//    Mod Organizer 2 keeps no dependency information, and Nexus requirement lists are not stored on the PC by either
//    manager (checked 10 Nov 2026: Vortex's state holds no requirements unless a rule was set), so neither is read.
//    Links to frameworks are dropped: frameworks are never switched off by Find it (Bisect.NeverTouch).
// 2. "Always on" pins: a pinned mod is never asked to be switched off, and neither is anything it needs.
// 3. "Continue with my current setup": when a round cannot or will not be set up as asked, the player can take what is
//    actually enabled now as this round (Bisect.ContinueAsIs).
public sealed class DepLink
{
    public string Mod { get; set; } = "";       // this mod ...
    public string Needs { get; set; } = "";     // ... needs this one
    public string Source { get; set; } = "";    // vortex | redscript | cet | folder | manual
    public string? Detail { get; set; }         // what was read, e.g. "import Codeware.UI" or a folder
}

public sealed class FindItPrefs
{
    public List<string> Pinned { get; set; } = new();
    public List<DepLink> Manual { get; set; } = new();
}

// What the Find it page shows about groups and pins (Report.FindIt)
public sealed class FindItInfo
{
    public List<DepLink> Links { get; set; } = new();     // every link in use, detected and manual
    public List<string> Pinned { get; set; } = new();
    public List<string> PinnedNeeds { get; set; } = new(); // kept on because a pinned mod needs them
    public List<string> Mods { get; set; } = new();       // enabled mods Find it could test, for the pickers
    public List<string> Sources { get; set; } = new();    // what was read for links (vortex, redscript, cet, folder)
    public int Groups { get; set; }                       // mods that have at least one link to another enabled mod
}

public static class FindIt
{
    static string PrefsFile => Path.Combine(GamePaths.AppData, "find-it.json");

    public static FindItPrefs LoadPrefs()
    {
        try { if (File.Exists(PrefsFile)) return JsonSerializer.Deserialize<FindItPrefs>(File.ReadAllText(PrefsFile), Scanner.Json) ?? new(); } catch { }
        return new();
    }

    public static void SavePrefs(FindItPrefs p)
    {
        Directory.CreateDirectory(GamePaths.AppData);
        File.WriteAllText(PrefsFile, JsonSerializer.Serialize(p, Scanner.Json));
    }

    public static bool Pin(string mod, bool on)
    {
        var p = LoadPrefs();
        p.Pinned.RemoveAll(x => x.Equals(mod, StringComparison.OrdinalIgnoreCase));
        if (on) p.Pinned.Add(mod);
        SavePrefs(p); return true;
    }

    public static bool Link(string mod, string needs, bool on)
    {
        if (mod.Equals(needs, StringComparison.OrdinalIgnoreCase) || mod.Length == 0 || needs.Length == 0) return false;
        var p = LoadPrefs();
        p.Manual.RemoveAll(x => x.Mod.Equals(mod, StringComparison.OrdinalIgnoreCase) && x.Needs.Equals(needs, StringComparison.OrdinalIgnoreCase));
        if (on) p.Manual.Add(new DepLink { Mod = mod, Needs = needs, Source = "manual" });
        SavePrefs(p); return true;
    }

    /// <summary>Every "needs" link between enabled mods that can be read, plus the manual ones. Never throws.</summary>
    public static List<DepLink> Detect(GamePaths g, ModInventory inv, List<string> sources)
    {
        var links = new List<DepLink>();
        var enabled = inv.Mods.Where(m => m.Status == "enabled" && m.Source == null).ToList();
        var names = new HashSet<string>(enabled.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
        void Add(string mod, string needs, string src, string? detail)
        {
            if (mod.Equals(needs, StringComparison.OrdinalIgnoreCase) || !names.Contains(mod) || !names.Contains(needs)) return;
            if (Bisect.IsFramework(needs)) return;    // frameworks are never switched off, so the link changes nothing
            if (links.Any(l => l.Mod.Equals(mod, StringComparison.OrdinalIgnoreCase) && l.Needs.Equals(needs, StringComparison.OrdinalIgnoreCase))) return;
            links.Add(new DepLink { Mod = mod, Needs = needs, Source = src, Detail = detail });
        }
        try { if (inv.Manager == "Vortex" && Vortex(enabled, Add)) sources.Add("vortex"); } catch { }
        try { if (Folders(enabled, inv, Add)) sources.Add("folder"); } catch { }
        try { if (Redscript(g, enabled, Add)) sources.Add("redscript"); } catch { }
        try { if (Cet(g, enabled, inv, Add)) sources.Add("cet"); } catch { }
        return links;
    }

    // ---- Vortex: persistent.mods.cyberpunk2077.<id>.rules[] {type:"requires", reference:{id | repo.modId | logicalFileName}}
    static bool Vortex(List<ModRow> enabled, Action<string, string, string, string?> add)
    {
        var dir = Path.Combine(GamePaths.VortexDir, "temp", "state_backups_full");
        if (!Directory.Exists(dir)) return false;
        var file = new DirectoryInfo(dir).EnumerateFiles("*.json").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
        if (file == null || file.Length > 200_000_000) return false;
        using var doc = JsonDocument.Parse(Files.ReadAllTextShared(file.FullName));
        if (!doc.RootElement.TryGetProperty("persistent", out var p) || !p.TryGetProperty("mods", out var ms) || !ms.TryGetProperty("cyberpunk2077", out var mods) || mods.ValueKind != JsonValueKind.Object) return false;
        // Vortex mod id / Nexus mod id / logical file name -> our row (matched by the staging folder the manifest names)
        var byFolder = enabled.GroupBy(m => m.Folder, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<string, ModRow>(StringComparer.OrdinalIgnoreCase);
        var byNexus = new Dictionary<string, List<ModRow>>(); var byLogical = new Dictionary<string, List<ModRow>>(StringComparer.OrdinalIgnoreCase);
        string? S(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null } : null;
        foreach (var m in mods.EnumerateObject())
        {
            var path = S(m.Value, "installationPath") ?? m.Name;
            if (!byFolder.TryGetValue(path, out var row)) continue;
            byId[m.Name] = row;
            if (m.Value.TryGetProperty("attributes", out var at))
            {
                if (S(at, "modId") is { } nid) { if (!byNexus.TryGetValue(nid, out var l)) byNexus[nid] = l = new(); l.Add(row); }
                if (S(at, "logicalFileName") is { } lf) { if (!byLogical.TryGetValue(lf, out var l)) byLogical[lf] = l = new(); l.Add(row); }
            }
        }
        foreach (var m in mods.EnumerateObject())
        {
            if (!byId.TryGetValue(m.Name, out var row) || !m.Value.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array) continue;
            foreach (var r in rules.EnumerateArray())
            {
                if (S(r, "type") != "requires" || !r.TryGetProperty("reference", out var re)) continue;
                var targets = new List<ModRow>();
                if (S(re, "id") is { } id && byId.TryGetValue(id, out var t)) targets.Add(t);
                else if (re.TryGetProperty("repo", out var repo) && S(repo, "modId") is { } rn && byNexus.TryGetValue(rn, out var tl)) targets.AddRange(tl);
                else if (S(re, "logicalFileName") is { } rl && byLogical.TryGetValue(rl, out var ll)) targets.AddRange(ll);
                foreach (var tg in targets) add(row.Name, tg.Name, "vortex", null);
            }
        }
        return true;
    }

    // ---- files inside another mod's CET mod folder or RED4ext plugin folder
    static readonly string CetPrefix = @"bin\x64\plugins\cyber_engine_tweaks\mods\", R4Prefix = @"red4ext\plugins\";
    static bool Folders(List<ModRow> enabled, ModInventory inv, Action<string, string, string, string?> add)
    {
        // who owns each CET mod folder (its init.lua) and each plugin folder (a .dll in it)
        var cetOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var r4Owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in enabled)
            foreach (var f in m.Files)
            {
                if (f.StartsWith(CetPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var rest = f[CetPrefix.Length..]; var cut = rest.IndexOf('\\');
                    if (cut > 0 && rest[(cut + 1)..].Equals("init.lua", StringComparison.OrdinalIgnoreCase)) cetOwner[rest[..cut]] = inv.OwnerOf(f) ?? m.Name;
                }
                else if (f.StartsWith(R4Prefix, StringComparison.OrdinalIgnoreCase) && f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    var rest = f[R4Prefix.Length..]; var cut = rest.IndexOf('\\');
                    if (cut > 0 && !rest[(cut + 1)..].Contains('\\')) r4Owner.TryAdd(rest[..cut], inv.OwnerOf(f) ?? m.Name);
                }
            }
        foreach (var m in enabled)
            foreach (var f in m.Files)
            {
                string? folder = null, owner = null;
                if (f.StartsWith(CetPrefix, StringComparison.OrdinalIgnoreCase)) { var rest = f[CetPrefix.Length..]; var cut = rest.IndexOf('\\'); if (cut > 0) { folder = rest[..cut]; cetOwner.TryGetValue(folder, out owner); } }
                else if (f.StartsWith(R4Prefix, StringComparison.OrdinalIgnoreCase)) { var rest = f[R4Prefix.Length..]; var cut = rest.IndexOf('\\'); if (cut > 0) { folder = rest[..cut]; r4Owner.TryGetValue(folder, out owner); } }
                if (owner != null && !owner.Equals(m.Name, StringComparison.OrdinalIgnoreCase)) add(m.Name, owner, "folder", folder);
            }
        return cetOwner.Count + r4Owner.Count > 0;
    }

    // ---- redscript: "module A.B" declared by one mod, "import A.B.*" / "import A.B.C" / "import A.B.{X, Y}" in another
    static readonly Regex ModuleRx = new(@"^\s*module\s+([A-Za-z_][\w.]*)", RegexOptions.Multiline);
    static readonly Regex ImportRx = new(@"^\s*import\s+([A-Za-z_][\w.]*?)(\.\*|\.\{[^}]*\})?\s*$", RegexOptions.Multiline);
    static bool Redscript(GamePaths g, List<ModRow> enabled, Action<string, string, string, string?> add)
    {
        var declares = new Dictionary<string, string>(StringComparer.Ordinal);   // module -> mod
        var imports = new List<(string mod, string module, string? item)>();
        int read = 0;
        foreach (var m in enabled)
            foreach (var f in m.Files.Where(f => f.EndsWith(".reds", StringComparison.OrdinalIgnoreCase)))
            {
                if (++read > 6000) break;
                string text; try { var full = g.Find(f); if (full == null || new FileInfo(full).Length > 2_000_000) continue; text = Files.ReadAllTextShared(full); } catch { continue; }
                foreach (Match mm in ModuleRx.Matches(text)) declares.TryAdd(mm.Groups[1].Value, m.Name);
                foreach (Match im in ImportRx.Matches(text))
                {
                    var path = im.Groups[1].Value;
                    if (im.Groups[2].Success) imports.Add((m.Name, path, null));
                    else { var dot = path.LastIndexOf('.'); imports.Add((m.Name, dot > 0 ? path[..dot] : path, path)); }
                }
            }
        if (read == 0) return false;
        foreach (var (mod, module, item) in imports)
        {
            // "import A.B.C" is item C of module A.B, or the whole module A.B.C
            if (declares.TryGetValue(module, out var owner) || (item != null && declares.TryGetValue(item, out owner)))
                add(mod, owner, "redscript", "import " + (item ?? module));
        }
        return true;
    }

    // ---- CET: GetMod("X") in a mod's Lua needs the mod owning CET folder X
    static readonly Regex GetModRx = new(@"GetMod\s*\(\s*[""']([^""']+)[""']\s*\)");
    static bool Cet(GamePaths g, List<ModRow> enabled, ModInventory inv, Action<string, string, string, string?> add)
    {
        int read = 0;
        foreach (var m in enabled)
            foreach (var f in m.Files.Where(f => f.StartsWith(CetPrefix, StringComparison.OrdinalIgnoreCase) && f.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)))
            {
                if (++read > 6000) break;
                string text; try { var full = g.Find(f); if (full == null || new FileInfo(full).Length > 2_000_000) continue; text = Files.ReadAllTextShared(full); } catch { continue; }
                foreach (Match gm in GetModRx.Matches(text))
                {
                    // the folder's init.lua only: with that mod switched off, an add-on's files inside its folder
                    // would otherwise be taken for the owner (seen 10 Nov 2026: "AMM Helper needs AMM Props Pack")
                    var owner = inv.OwnerOf(CetPrefix + gm.Groups[1].Value + "\\init.lua");
                    if (owner != null) add(m.Name, owner, "cet", "GetMod(\"" + gm.Groups[1].Value + "\")");
                }
            }
        return read > 0;
    }

    /// <summary>The page's view: detected links, the manual ones, pins, and the mods that can be picked.</summary>
    public static FindItInfo Build(GamePaths g, ModInventory inv)
    {
        var info = new FindItInfo();
        var prefs = LoadPrefs();
        var detected = Detect(g, inv, info.Sources);
        var enabled = inv.Mods.Where(m => m.Status == "enabled" && m.Source == null).Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        info.Links = detected.Concat(prefs.Manual.Where(x => !detected.Any(d => d.Mod.Equals(x.Mod, StringComparison.OrdinalIgnoreCase) && d.Needs.Equals(x.Needs, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(l => l.Needs, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.Mod, StringComparer.OrdinalIgnoreCase).ToList();
        info.Pinned = prefs.Pinned.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        info.PinnedNeeds = Needed(info.Pinned, info.Links).Where(x => !info.Pinned.Contains(x, StringComparer.OrdinalIgnoreCase)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        info.Mods = enabled.Where(n => !Bisect.IsFramework(n)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        info.Groups = info.Links.SelectMany(l => new[] { l.Mod, l.Needs }).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return info;
    }

    /// <summary>Everything the given mods need, transitively (not including themselves unless needed by another).</summary>
    public static HashSet<string> Needed(IEnumerable<string> mods, List<DepLink> links)
    {
        var res = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>(mods);
        while (stack.Count > 0)
        {
            var m = stack.Pop();
            foreach (var l in links.Where(l => l.Mod.Equals(m, StringComparison.OrdinalIgnoreCase)))
                if (res.Add(l.Needs)) stack.Push(l.Needs);
        }
        return res;
    }

    /// <summary>The mod and every enabled mod that needs it, transitively: switching it off means switching all of these off.</summary>
    public static HashSet<string> WithDependents(string mod, List<DepLink> links)
    {
        var res = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { mod };
        var stack = new Stack<string>(); stack.Push(mod);
        while (stack.Count > 0)
        {
            var m = stack.Pop();
            foreach (var l in links.Where(l => l.Needs.Equals(m, StringComparison.OrdinalIgnoreCase)))
                if (res.Add(l.Mod)) stack.Push(l.Mod);
        }
        return res;
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrashDoctor.Engine;

public sealed class ModInventory
{
    public string Manager { get; set; } = "manual";
    public string? StagingPath { get; set; }
    public List<ModRow> Mods { get; } = new();
    // relPath (lowercase) -> mod display name
    public Dictionary<string, string> FileOwner { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int XlFiles, RedsFolders, CetMods, TweakFiles, Red4extPlugins, Archives;

    public string? OwnerOf(string relPath) => FileOwner.TryGetValue(relPath, out var m) ? m : null;
    public string? OwnerOfCetMod(string folder) => OwnerOf($@"bin\x64\plugins\cyber_engine_tweaks\mods\{folder}\init.lua") ?? FileOwner.FirstOrDefault(kv => kv.Key.StartsWith($@"bin\x64\plugins\cyber_engine_tweaks\mods\{folder}\", StringComparison.OrdinalIgnoreCase)).Value;
    public string? OwnerOfXl(string xlName) => OwnerOf($@"archive\pc\mod\{xlName}");
    public string? OwnerOfPlugin(string pluginFolder) => FileOwner.FirstOrDefault(kv => kv.Key.StartsWith($@"red4ext\plugins\{pluginFolder}\", StringComparison.OrdinalIgnoreCase)).Value;
    public ModRow? Find(string name) => Mods.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    public static ModInventory Read(GamePaths g)
    {
        var inv = new ModInventory();
        CountArtifacts(g, inv);
        if (File.Exists(g.VortexManifest) && ReadVortex(g, inv)) inv.Manager = "Vortex";
        else ReadManual(g, inv);
        foreach (var m in inv.Mods) m.Type = Classify(m.Files);
        return inv;
    }

    static void CountArtifacts(GamePaths g, ModInventory inv)
    {
        if (Directory.Exists(g.ArchiveMods)) { inv.XlFiles = Directory.EnumerateFiles(g.ArchiveMods, "*.xl").Count(); inv.Archives = Directory.EnumerateFiles(g.ArchiveMods, "*.archive").Count(); }
        if (Directory.Exists(g.Scripts)) inv.RedsFolders = Directory.EnumerateDirectories(g.Scripts).Count();
        if (Directory.Exists(g.CetMods)) inv.CetMods = Directory.EnumerateDirectories(g.CetMods).Count(d => File.Exists(Path.Combine(d, "init.lua")));
        if (Directory.Exists(g.Tweaks)) inv.TweakFiles = Directory.EnumerateFiles(g.Tweaks, "*.*", SearchOption.AllDirectories).Count(f => f.EndsWith(".yaml") || f.EndsWith(".yml") || f.EndsWith(".tweak"));
        if (Directory.Exists(g.Red4extPlugins)) inv.Red4extPlugins = Directory.EnumerateDirectories(g.Red4extPlugins).Count(d => Directory.EnumerateFiles(d, "*.dll").Any());
    }

    static bool ReadVortex(GamePaths g, ModInventory inv)
    {
        try
        {
            using var doc = JsonDocument.Parse(Files.ReadAllTextShared(g.VortexManifest));
            var root = doc.RootElement;
            if (root.TryGetProperty("stagingPath", out var sp)) inv.StagingPath = sp.GetString();
            var byFolder = new Dictionary<string, ModRow>();
            foreach (var f in root.GetProperty("files").EnumerateArray())
            {
                var rel = f.GetProperty("relPath").GetString() ?? ""; var src = f.GetProperty("source").GetString() ?? "";
                if (!byFolder.TryGetValue(src, out var row)) { row = FromFolder(src); row.Status = "enabled"; byFolder[src] = row; }
                row.Files.Add(rel); inv.FileOwner[rel] = row.Name;
            }
            inv.Mods.AddRange(byFolder.Values);
            if (inv.StagingPath != null && Directory.Exists(inv.StagingPath))
                foreach (var dir in Directory.EnumerateDirectories(inv.StagingPath))
                {
                    var name = Path.GetFileName(dir); if (name == "__vortex_staging_folder" || byFolder.ContainsKey(name)) continue;
                    var row = FromFolder(name); row.Status = "disabled";
                    row.Files.AddRange(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(dir, p)));
                    inv.Mods.Add(row);
                }
            return inv.Mods.Count > 0;
        }
        catch { return false; }
    }

    static void ReadManual(GamePaths g, ModInventory inv)
    {
        void Add(string name, string type, IEnumerable<string> files) { var row = new ModRow { Name = name, Folder = name, Status = "enabled" }; row.Files.AddRange(files); foreach (var f in row.Files) inv.FileOwner[f] = name; inv.Mods.Add(row); }
        if (Directory.Exists(g.ArchiveMods)) foreach (var f in Directory.EnumerateFiles(g.ArchiveMods)) if (f.EndsWith(".archive") || f.EndsWith(".xl")) Add(Path.GetFileNameWithoutExtension(f).Replace(".archive", ""), "archive", new[] { Path.Combine("archive", "pc", "mod", Path.GetFileName(f)) });
        if (Directory.Exists(g.CetMods)) foreach (var d in Directory.EnumerateDirectories(g.CetMods)) Add(Path.GetFileName(d), "CET", Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(g.GameDir, p)));
        if (Directory.Exists(g.Red4extPlugins)) foreach (var d in Directory.EnumerateDirectories(g.Red4extPlugins)) Add(Path.GetFileName(d), "native plugin", Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(g.GameDir, p)));
        if (Directory.Exists(g.Scripts)) foreach (var d in Directory.EnumerateDirectories(g.Scripts)) Add(Path.GetFileName(d), "redscript", Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(g.GameDir, p)));
        // merge duplicates by name
        var merged = inv.Mods.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Select(grp => { var first = grp.First(); foreach (var o in grp.Skip(1)) first.Files.AddRange(o.Files); return first; }).ToList();
        inv.Mods.Clear(); inv.Mods.AddRange(merged);
    }

    public static ModRow FromFolder(string folder)
    {
        var row = new ModRow { Folder = folder };
        var id = Regex.Match(folder, @"-(\d{3,6})-[^-]"); if (!id.Success) id = Regex.Match(folder, @"\s(\d{3,6})\s");
        row.NexusId = id.Success ? id.Groups[1].Value : null;
        row.Name = Regex.Replace(Regex.Replace(Regex.Replace(folder, @"\.(zip|7z|rar)$", ""), @"-\d{3,6}-.*$", ""), @"\s+\d{4,6}\s+.*$", "").Trim();
        var v = Regex.Match(folder, @"-\d{3,6}-(.+?)-\d{10}$"); if (!v.Success) v = Regex.Match(folder, @"\s\d{3,6}\s(\S+)\s");
        row.Version = v.Success ? v.Groups[1].Value.Replace('-', '.') : "";
        return row;
    }

    public static string Classify(List<string> files)
    {
        bool dll = files.Any(f => f.StartsWith(@"red4ext\plugins\", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        bool lua = files.Any(f => f.Contains(@"cyber_engine_tweaks\mods\", StringComparison.OrdinalIgnoreCase) && f.EndsWith("init.lua", StringComparison.OrdinalIgnoreCase));
        bool reds = files.Any(f => f.EndsWith(".reds", StringComparison.OrdinalIgnoreCase));
        bool xl = files.Any(f => f.EndsWith(".xl", StringComparison.OrdinalIgnoreCase));
        bool tweak = files.Any(f => f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".tweak", StringComparison.OrdinalIgnoreCase));
        bool archive = files.Any(f => f.EndsWith(".archive", StringComparison.OrdinalIgnoreCase));
        var parts = new List<string>();
        if (dll) parts.Add("native plugin"); if (reds) parts.Add("redscript"); if (lua) parts.Add("CET"); if (xl) parts.Add("ArchiveXL"); if (tweak) parts.Add("TweakXL"); if (archive && parts.Count == 0) parts.Add("archive"); else if (archive) parts.Add("archive");
        return parts.Count == 0 ? "other" : string.Join(" + ", parts);
    }

    public bool Needs(ModRow m, string framework) => framework switch
    {
        "red4ext" => m.Files.Any(f => f.StartsWith(@"red4ext\plugins\", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)),
        "redscript" => m.Files.Any(f => f.EndsWith(".reds", StringComparison.OrdinalIgnoreCase)),
        "cet" => m.Files.Any(f => f.Contains(@"cyber_engine_tweaks\mods\", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)),
        "archivexl" => m.Files.Any(f => f.EndsWith(".xl", StringComparison.OrdinalIgnoreCase)),
        "tweakxl" => m.Files.Any(f => f.StartsWith(@"r6\tweaks\", StringComparison.OrdinalIgnoreCase)),
        _ => false
    };
}

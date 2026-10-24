using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrashDoctor.Engine;

// Which framework release supports which game version - kept small, dated and checked by hand (knowledge\releases\
// frameworks.json).
//
// Until 0.8.0 Crash Doctor kept no such table on purpose (DESIGN.md: "keeps no table of versions it cannot verify"),
// and the logs still come first: what RED4ext, CET and redscript wrote on the new build is the evidence. Patch 2.5
// (22 Oct 2026) showed what the logs alone cannot say:
//   - RED4ext 1.30.0 started on 2.5 and logged nothing wrong, yet 1.31.0 is the release for 2.5 ("the minimum
//     supported game version is now patch 2.5") - and RedFileSystem 0.16.0 needs it;
//   - ArchiveXL 1.27.5 took the game down while it was loading, before it could log anything but "is starting".
// A release note is a checkable fact, unlike a guess about the future, so only two kinds of entry are kept:
//   "for":    the first release that supports a game version (stays true for ever);
//   "latest": the newest release on the asOf date and the game it was built for - always shown with that date,
//             because a newer release can appear the next day.
// An installed version newer than anything in the table is never flagged: the table is old then, not the framework.
// The rule rejected in Rules.cs ("native plugin is older than the game build") guessed from dates; this does not.
public sealed class KnownReleaseData
{
    public string AsOf { get; set; } = "";
    public List<KnownGame> Games { get; set; } = new();
    public List<KnownFramework> Frameworks { get; set; } = new();
}
public sealed class KnownGame { public string Build { get; set; } = ""; public string Version { get; set; } = ""; public string? Released { get; set; } }
public sealed class KnownFramework
{
    public string Name { get; set; } = "";
    public string? Nexus { get; set; }
    public List<KnownFor> For { get; set; } = new();
    public string? Latest { get; set; }
    public string? LatestFor { get; set; }
    public string? Source { get; set; }
}
public sealed class KnownFor { public string Game { get; set; } = ""; public string From { get; set; } = ""; public string? Released { get; set; } }

public sealed class KnownCheck
{
    public string Name { get; set; } = "";
    public string Installed { get; set; } = "";
    public string Kind { get; set; } = "";        // old (a release for this game exists) | noRelease (none known on asOf)
    public string Text { get; set; } = "";        // token
    public string? Nexus { get; set; }
}

public static class KnownReleases
{
    static KnownReleaseData? _data;
    static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static KnownReleaseData Data
    {
        get
        {
            if (_data != null) return _data;
            try
            {
                var f = Path.Combine(Knowledge.Dir, "releases", "frameworks.json");
                _data = File.Exists(f) ? JsonSerializer.Deserialize<KnownReleaseData>(Files.ReadAllTextShared(f), Opts) : null;
            }
            catch { _data = null; }
            return _data ??= new KnownReleaseData();
        }
    }

    /// <summary>The game version a build number belongs to ("3.0.88.46235" -> "2.5"), when the table knows it.</summary>
    public static string? GameOfBuild(string? build) =>
        string.IsNullOrEmpty(build) ? null : Data.Games.FirstOrDefault(g => g.Build.Length > 0 && build.StartsWith(g.Build, StringComparison.Ordinal))?.Version;

    /// <summary>"2.50" (RED4ext's log) and "2.5" (the exe) are the same patch.</summary>
    public static string Normalise(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "";
        var m = Regex.Match(v.Trim(), @"^(\d+)\.(\d+)");
        if (!m.Success) return v.Trim();
        var minor = m.Groups[2].Value;
        if (minor.Length >= 2 && minor.EndsWith("0")) minor = minor.TrimEnd('0');
        return m.Groups[1].Value + "." + (minor.Length == 0 ? "0" : minor);
    }

    /// <summary>The game version to judge against: the build first (exact), then the product version.</summary>
    public static string GameKey(string fileVersion, string version) => GameOfBuild(fileVersion) ?? Normalise(version);

    /// <summary>"1.27.5" out of "v1.38.0 [HEAD]", "1.27.5.2610192005" or "1.31.0".</summary>
    public static System.Version? Parse(string? v)
    {
        if (string.IsNullOrEmpty(v)) return null;
        var m = Regex.Match(v, @"(\d+)\.(\d+)(?:\.(\d+))?");
        if (!m.Success) return null;
        return new System.Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
    }
    static int Cmp(string? a, string? b) => (Parse(a), Parse(b)) is (System.Version x, System.Version y) ? x.CompareTo(y) : 0;
    // game versions are decimals, not dotted versions: 2.5 came after 2.31 (and 2.12 before 2.2)
    static int CmpGame(string a, string b) =>
        decimal.TryParse(Normalise(a), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var x)
        && decimal.TryParse(Normalise(b), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var y) ? x.CompareTo(y) : 0;
    static string Short(string v) => Parse(v)?.ToString() ?? v;

    static readonly Dictionary<string, string[]> Files_ = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RED4ext"] = new[] { @"red4ext\RED4ext.dll" },
        ["Cyber Engine Tweaks"] = new[] { @"bin\x64\plugins\cyber_engine_tweaks.asi" },
    };
    static string[] FilesOf(string name) => Files_.TryGetValue(name, out var f) ? f : new[] { Path.Combine(GamePaths.RelRed4extPlugins, name, name + ".dll") };

    /// <summary>Whether the framework's own file is installed now (not just a folder its old logs left behind).</summary>
    public static bool Installed(CollectedData d, string name) => FilesOf(name).Any(f => d.Paths.Exists(f)) || (name == "RED4ext" && d.Paths.Exists(@"bin\x64\winmm.dll"));

    /// <summary>
    /// The version installed now: the file's own version stamp first (what is on disk), then what the logs say ran
    /// (RED4ext's plugin list, the plugin's own "X 1.27.5 is starting" line, CET's log).
    /// </summary>
    public static string? InstalledVersion(CollectedData d, string name)
    {
        foreach (var rel in FilesOf(name))
        {
            var full = d.Paths.Find(rel);
            if (full == null) continue;
            try
            {
                var fi = FileVersionInfo.GetVersionInfo(full);
                var v = Parse(fi.ProductVersion) ?? (fi.FileMajorPart + fi.FileMinorPart + fi.FileBuildPart > 0 ? new System.Version(fi.FileMajorPart, fi.FileMinorPart, fi.FileBuildPart) : null);
                if (v != null) return v.ToString();
            }
            catch { }
        }
        var newest = d.Red4ext.OrderByDescending(x => x.Start).ToList();
        if (name == "RED4ext") return newest.Select(x => x.Red4extVersion).FirstOrDefault(v => v.Length > 0);
        if (name == "Cyber Engine Tweaks") return Parse(d.CetVersion)?.ToString();
        foreach (var s in newest)
        {
            var p = s.PluginsLoaded.FirstOrDefault(x => x.name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (p.name != null) return Short(p.version);
            var inc = s.Incompatible.Select(i => Regex.Match(i, @"^(.+?) \(version: ([^,)]+)")).FirstOrDefault(m => m.Success && m.Groups[1].Value.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (inc != null) return Short(inc.Groups[2].Value);
        }
        var starting = d.Events.Where(e => e.Text.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase)).OrderByDescending(e => e.At)
                               .Select(e => Regex.Match(e.Text, "^" + Regex.Escape(name) + @" v?(\d+\.\d+(?:\.\d+)?) is starting", RegexOptions.IgnoreCase)).FirstOrDefault(m => m.Success);
        return starting?.Groups[1].Value;
    }

    /// <summary>What the table says about one framework on this game version, or null when it has nothing to add.</summary>
    public static KnownCheck? Check(string name, string? installed, string game)
    {
        var fw = Data.Frameworks.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (fw == null || string.IsNullOrEmpty(installed) || Parse(installed) == null || game.Length == 0) return null;
        var forGame = fw.For.FirstOrDefault(f => Normalise(f.Game) == game);
        if (forGame != null)
        {
            if (Cmp(installed, forGame.From) >= 0) return null;
            return new KnownCheck { Name = fw.Name, Installed = installed, Kind = "old", Nexus = fw.Nexus,
                Text = forGame.Released is { } rel && DateTime.TryParse(rel, out var rd)
                    ? L.T("known.old", fw.Name, installed, forGame.From, game, L.Date(rd, "dmy"))
                    : L.T("known.oldNoDate", fw.Name, installed, forGame.From, game) };
        }
        // no release for this game in the table: say so only for a game newer than the one the latest release was
        // built for, and only for an installed version no newer than that latest release
        if (fw.LatestFor != null && fw.Latest != null && CmpGame(game, fw.LatestFor) > 0 && Cmp(installed, fw.Latest) <= 0
            && DateTime.TryParse(Data.AsOf, out var asOf))
            return new KnownCheck { Name = fw.Name, Installed = installed, Kind = "noRelease", Nexus = fw.Nexus,
                Text = L.T("known.noRelease", fw.Name, installed, fw.LatestFor, game, L.Date(asOf, "dmy")) };
        return null;
    }

    /// <summary>Every framework in the table that is installed now and that the table says is behind this game.</summary>
    public static List<KnownCheck> All(CollectedData d, string game)
    {
        var list = new List<KnownCheck>();
        if (game.Length == 0) return list;
        foreach (var fw in Data.Frameworks)
        {
            if (!Installed(d, fw.Name)) continue;
            try { if (Check(fw.Name, InstalledVersion(d, fw.Name), game) is { } c) list.Add(c); } catch { }
        }
        return list;
    }

    /// <summary>"3.0.80.51928 (2.31)" for a build the table knows, else the build alone.</summary>
    public static string BuildLabel(string build) => GameOfBuild(build) is { } v ? build + " (" + v + ")" : build;
}

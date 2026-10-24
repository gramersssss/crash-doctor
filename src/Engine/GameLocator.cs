using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CrashDoctor.Engine;

public sealed class GamePaths
{
    public string GameDir { get; init; } = "";
    public string Store { get; init; } = "";
    public string Exe => Path.Combine(GameDir, "bin", "x64", "Cyberpunk2077.exe");
    public string Bin => Path.Combine(GameDir, "bin", "x64");
    public string Red4extLogs => Path.Combine(GameDir, "red4ext", "logs");
    public string Red4extPlugins => Path.Combine(GameDir, "red4ext", "plugins");
    public string Cet => Path.Combine(GameDir, "bin", "x64", "plugins", "cyber_engine_tweaks");
    public string CetMods => Path.Combine(Cet, "mods");
    public string ArchiveMods => Path.Combine(GameDir, "archive", "pc", "mod");
    public string Scripts => Path.Combine(GameDir, "r6", "scripts");
    public string Tweaks => Path.Combine(GameDir, "r6", "tweaks");
    public string RedscriptLogs => Path.Combine(GameDir, "r6", "logs");
    public string VortexManifest => Path.Combine(GameDir, "vortex.deployment.json");

    // The same places relative to the game folder, for reading through Mod Organizer 2 (below).
    public const string RelBin = @"bin\x64", RelRed4extLogs = @"red4ext\logs", RelRed4extPlugins = @"red4ext\plugins",
        RelCet = @"bin\x64\plugins\cyber_engine_tweaks", RelCetMods = @"bin\x64\plugins\cyber_engine_tweaks\mods",
        RelArchiveMods = @"archive\pc\mod", RelScripts = @"r6\scripts", RelTweaks = @"r6\tweaks", RelRedscriptLogs = @"r6\logs";

    // ------------------------------------------------------------------ Mod Organizer 2
    //
    // MO2 keeps every mod in its own folder and merges them over the game folder only while the game runs through it
    // (its virtual file system, or Root Builder copying files in and out). Scanned from outside, the game folder is
    // missing most of the mods and, once the game has closed, often the frameworks and their logs too - which is
    // exactly what lynsis saw: "Crash Doctor loses CET when the game is closed". So when an MO2 instance manages this
    // game, every read goes through the same merge MO2 would make: the game folder at the bottom, the profile's
    // enabled mods above it in priority order, and the overwrite folder (where MO2 puts files the game and the mods
    // create, logs included) on top. Nothing is ever written through this; it only decides where to read.
    public Mo2Instance? Mo2 { get; set; }
    /// <summary>Enabled MO2 mod folders, lowest priority first, then the overwrite folder. Empty without MO2.</summary>
    public List<Overlay> Overlays { get; } = new();

    /// <summary>Every real folder that holds files for this game-relative folder, lowest priority first.</summary>
    public List<string> Dirs(string relDir)
    {
        var list = new List<string>();
        void Add(string p) { if (Directory.Exists(p) && !list.Contains(p, StringComparer.OrdinalIgnoreCase)) list.Add(p); }
        Add(Path.Combine(GameDir, relDir));
        foreach (var o in Overlays) foreach (var p in o.MapDir(relDir)) Add(p);
        return list;
    }

    /// <summary>The file the game would see at this game-relative path: the highest-priority copy, or null.</summary>
    public string? Find(string rel)
    {
        for (int i = Overlays.Count - 1; i >= 0; i--)
            foreach (var p in Overlays[i].MapFile(rel)) if (File.Exists(p)) return p;
        var g = Path.Combine(GameDir, rel);
        return File.Exists(g) ? g : null;
    }
    public bool Exists(string rel) => Find(rel) != null;
    public bool DirExists(string relDir) => Dirs(relDir).Count > 0;

    /// <summary>
    /// Files matching <paramref name="pattern"/> under a game-relative folder, merged the way MO2 would: one entry per
    /// game-relative path, the highest priority winning - or, for logs, the newest file winning, because a log is
    /// wherever it was last written, not wherever a mod happens to sit in the list.
    /// </summary>
    public List<(string full, string rel)> FilesIn(string relDir, string pattern, bool recurse = false, bool newestWins = false)
    {
        var byRel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Take(string dir, string relBase, SearchOption opt)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, pattern, opt).ToList(); } catch { return; }
            foreach (var f in files)
            {
                if (f.EndsWith(".mohidden", StringComparison.OrdinalIgnoreCase)) continue;
                var rel = Path.Combine(relBase, Path.GetRelativePath(dir, f));
                if (newestWins && byRel.TryGetValue(rel, out var have) && SafeTime(have) >= SafeTime(f)) continue;
                byRel[rel] = f;
            }
        }
        var opt = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var g = Path.Combine(GameDir, relDir);
        if (Directory.Exists(g)) Take(g, relDir, opt);
        foreach (var o in Overlays)
        {
            foreach (var p in o.MapDir(relDir)) if (Directory.Exists(p)) Take(p, relDir, opt);
            // a mod folder whose archives sit at its top level, the way MO2 lays them out when its data folder is archive\pc\mod
            if (!recurse && relDir.Equals(RelArchiveMods, StringComparison.OrdinalIgnoreCase) && Directory.Exists(o.Root))
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(o.Root, pattern, SearchOption.TopDirectoryOnly))
                        if (Overlay.IsLooseArchive(f)) byRel[Path.Combine(relDir, Path.GetFileName(f))] = f;
                }
                catch { }
            }
        }
        return byRel.Select(kv => (kv.Value, kv.Key)).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Immediate sub-folders of a game-relative folder, merged; name -> every real folder carrying it.</summary>
    public Dictionary<string, List<string>> SubDirs(string relDir)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in Dirs(relDir))
        {
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(d))
                {
                    var name = Path.GetFileName(sub);
                    if (!map.TryGetValue(name, out var l)) map[name] = l = new();
                    l.Add(sub);
                }
            }
            catch { }
        }
        return map;
    }

    /// <summary>Where a real file sits relative to the game folder, through the MO2 merge when it is in a mod folder.</summary>
    public string RelOf(string full)
    {
        var rel = Path.GetRelativePath(GameDir, full);
        if (!rel.StartsWith("..") && !Path.IsPathRooted(rel)) return rel;
        foreach (var o in Overlays) if (o.RelOf(full) is { } r) return r;
        return full;
    }

    static DateTime SafeTime(string f) { try { return File.GetLastWriteTime(f); } catch { return DateTime.MinValue; } }
    // Most of what Crash Doctor reads is not in the game folder at all: crash reports, settings and its own history
    // live under the user's profile. That makes the app impossible to test against a made-up install, because a
    // pretend game folder would still be judged against the real machine's crash history.
    //
    // CRASHDOCTOR_TEST_ROOT redirects those three roots at once, so a whole synthetic install - clean, empty, broken,
    // whatever - can be assembled in a directory and scanned in isolation. Unset in every normal run.
    public static string? TestRoot => Environment.GetEnvironmentVariable("CRASHDOCTOR_TEST_ROOT") is { Length: > 0 } r ? r : null;
    public static string LocalAppData => TestRoot is { } t ? Path.Combine(t, "LocalAppData") : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static string RoamingAppData => TestRoot is { } t ? Path.Combine(t, "AppData") : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static string CdprLocal => Path.Combine(LocalAppData, "CD Projekt Red", "Cyberpunk 2077");
    public static string UserSettings => Path.Combine(CdprLocal, "UserSettings.json");
    public static string CrashInfo => Path.Combine(CdprLocal, "CrashInfo.json");
    public static string CrashReporterLog => Path.Combine(LocalAppData, "REDEngine", "CrashReporter.log");
    public static string ReportQueue => Path.Combine(LocalAppData, "REDEngine", "ReportQueue");
    public static string AppData => Path.Combine(RoamingAppData, "CrashDoctor");
    public static string VortexDir => Path.Combine(RoamingAppData, "Vortex");   // vortex.log, vortex1.log ... (install history)
    public static string ConfigFile => Path.Combine(AppData, "config.json");
}

public static class GameLocator
{
    public sealed class Config
    {
        public string? GamePath { get; set; }
        public bool? KeepCrashLogs { get; set; }
        public bool? LogVideoMemory { get; set; }
        public DateTime? CrashesSeenUntil { get; set; }
        // the opt-in update check (Updates.cs): the switch, and the cached answer so the page never has to ask twice a day
        public bool? CheckForUpdates { get; set; }
        public string? UpdateUrl { get; set; }
        public DateTime? LastUpdateCheck { get; set; }
        public string? LatestVersion { get; set; }
        public string? LatestVersionUrl { get; set; }
        public string? LatestVersionNotes { get; set; }
        public string? LastUpdateProblem { get; set; }
        // the interface language (ui\lang\<code>.json); null = follow Windows, else English
        public string? Language { get; set; }
        // a Mod Organizer 2 instance to read instead of looking for one: the instance folder or its ModOrganizer.ini
        public string? Mo2Path { get; set; }
        // the game build seen on the last scan, so an update is noticed even after the logs have rotated (GameUpdate.cs)
        public string? LastGameVersion { get; set; }
        public string? LastGameFileVersion { get; set; }
        public DateTime? GameVersionChangedAt { get; set; }
        public string? PreviousGameVersion { get; set; }
        public string? PreviousGameFileVersion { get; set; }
    }

    public static Config LoadConfig()
    {
        try { if (File.Exists(GamePaths.ConfigFile)) return JsonSerializer.Deserialize<Config>(File.ReadAllText(GamePaths.ConfigFile)) ?? new Config(); } catch { }
        return new Config();
    }
    public static void SaveConfig(Config c)
    {
        Directory.CreateDirectory(GamePaths.AppData);
        File.WriteAllText(GamePaths.ConfigFile, JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static GamePaths? Locate()
    {
        var cfg = LoadConfig();
        if (!string.IsNullOrWhiteSpace(cfg.GamePath) && IsGameDir(cfg.GamePath)) return new GamePaths { GameDir = cfg.GamePath, Store = GuessStore(cfg.GamePath) };
        foreach (var (dir, store) in Candidates()) if (IsGameDir(dir)) return new GamePaths { GameDir = dir, Store = store };
        return null;
    }

    public static bool IsGameDir(string dir) => !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "bin", "x64", "Cyberpunk2077.exe"));

    static string GuessStore(string dir) => dir.Contains("steamapps", StringComparison.OrdinalIgnoreCase) ? "Steam" : dir.Contains("GOG", StringComparison.OrdinalIgnoreCase) ? "GOG" : dir.Contains("Epic", StringComparison.OrdinalIgnoreCase) ? "Epic" : "manual";

    static IEnumerable<(string dir, string store)> Candidates()
    {
        // Steam: every library folder
        foreach (var lib in SteamLibraries())
        {
            var manifest = Path.Combine(lib, "steamapps", "appmanifest_1091500.acf");
            if (File.Exists(manifest))
            {
                var m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                var name = m.Success ? m.Groups[1].Value : "Cyberpunk 2077";
                yield return (Path.Combine(lib, "steamapps", "common", name), "Steam");
            }
            yield return (Path.Combine(lib, "steamapps", "common", "Cyberpunk 2077"), "Steam");
        }
        // GOG
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            var gog = GogPath(hive); if (!string.IsNullOrEmpty(gog)) yield return (gog!, "GOG");
        }
        // Epic
        var epicManifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (Directory.Exists(epicManifests))
            foreach (var f in Directory.EnumerateFiles(epicManifests, "*.item"))
            {
                string? loc = null;
                try { using var doc = JsonDocument.Parse(File.ReadAllText(f)); var r = doc.RootElement; if (r.TryGetProperty("DisplayName", out var dn) && dn.GetString()?.Contains("Cyberpunk", StringComparison.OrdinalIgnoreCase) == true && r.TryGetProperty("InstallLocation", out var il)) loc = il.GetString(); } catch { }
                if (loc != null) yield return (loc, "Epic");
            }
        // common manual spots
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
            foreach (var rel in new[] { @"Games\Cyberpunk 2077", @"Cyberpunk 2077", @"GOG Games\Cyberpunk 2077", @"Program Files\Epic Games\Cyberpunk 2077", @"SteamLibrary\steamapps\common\Cyberpunk 2077" })
                yield return (Path.Combine(drive.RootDirectory.FullName, rel), GuessStore(rel));
    }

    static string? GogPath(RegistryHive hive)
    {
        try { using var k = RegistryKey.OpenBaseKey(hive, RegistryView.Registry32).OpenSubKey(@"SOFTWARE\GOG.com\Games\1423049311"); return k?.GetValue("path") as string; } catch { return null; }
    }

    static IEnumerable<string> SteamLibraries()
    {
        string? steam = null;
        try { steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string; } catch { }
        if (string.IsNullOrEmpty(steam)) { try { steam = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(@"SOFTWARE\Valve\Steam")?.GetValue("InstallPath") as string; } catch { } }
        if (string.IsNullOrEmpty(steam)) yield break;
        steam = steam.Replace('/', '\\');
        yield return steam;
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            yield return m.Groups[1].Value.Replace(@"\\", @"\");
    }

    public static (string fileVersion, string productVersion) ExeVersion(GamePaths g)
    {
        // Use the numeric parts, not FileVersion: the string form reads "3.0.5294808" while every other source -
        // the RED4ext log, and the module record inside a crash dump - says "3.0.80.51928". Matching them means the
        // build a crash was measured in can be compared against the build now installed.
        try
        {
            var v = FileVersionInfo.GetVersionInfo(g.Exe);
            var numeric = $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}.{v.FilePrivatePart}";
            return (numeric == "0.0.0.0" ? v.FileVersion ?? "" : numeric, v.ProductVersion ?? "");
        }
        catch { return ("", ""); }
    }

    public static bool GameRunning() => Process.GetProcessesByName("Cyberpunk2077").Length > 0;
}

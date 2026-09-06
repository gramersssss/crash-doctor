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
    public static string CdprLocal => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CD Projekt Red", "Cyberpunk 2077");
    public static string UserSettings => Path.Combine(CdprLocal, "UserSettings.json");
    public static string CrashInfo => Path.Combine(CdprLocal, "CrashInfo.json");
    public static string CrashReporterLog => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "REDEngine", "CrashReporter.log");
    public static string AppData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrashDoctor");
    public static string ConfigFile => Path.Combine(AppData, "config.json");
}

public static class GameLocator
{
    public sealed class Config { public string? GamePath { get; set; } }

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
        try { var v = FileVersionInfo.GetVersionInfo(g.Exe); return (v.FileVersion ?? "", v.ProductVersion ?? ""); } catch { return ("", ""); }
    }

    public static bool GameRunning() => Process.GetProcessesByName("Cyberpunk2077").Length > 0;
}

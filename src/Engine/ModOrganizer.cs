using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace CrashDoctor.Engine;

// Mod Organizer 2 (promised to lynsis on Nexus, Sep 2026).
//
// An MO2 install keeps each mod in <instance>\mods\<name>\, the enabled set and its order in
// profiles\<profile>\modlist.txt, and every file the game or a mod creates while running through MO2 in overwrite\.
// None of it is in the game folder until MO2 starts the game. This file finds the instance that manages the game,
// reads the profile's mod list, and hands GamePaths the folders to merge (GameLocator.cs), so every collector and
// rule reads what the game would see when started from MO2.
//
// Read-only, all of it. Crash Doctor never edits modlist.txt, never moves a mod and never touches MO2's settings;
// like the bisect with Vortex, it tells you what to switch and checks the result.
//
// Read defensively, because the layout is MO2's to change and the ini is Qt's own dialect:
//   - ModOrganizer.ini: [General] gameName, gamePath (often @ByteArray(...) with doubled backslashes), selected_profile;
//     [Settings] base_directory, mod_directory, profiles_directory, overwrite_directory, any of which may use %BASE_DIR%.
//   - modlist.txt: one line per mod, the FIRST line has the HIGHEST priority. "+name" enabled, "-name" disabled,
//     "*name" a game file MO2 does not manage. Separators are folders whose name ends in "_separator".
//   - A mod folder maps onto the game folder (Cyberpunk's data folder in MO2 is the game root). Root Builder keeps
//     root files in a "Root" sub-folder; both are read. A mod whose .archive/.xl files sit loose at its top level is
//     read as archive\pc\mod, the layout of setups whose data folder is that folder.
//   - Files ending in .mohidden are ones MO2 has hidden; they do not exist as far as the game is concerned.
public sealed class Mo2Instance
{
    public string Ini { get; set; } = "";
    public string BaseDir { get; set; } = "";
    public string ModsDir { get; set; } = "";
    public string ProfilesDir { get; set; } = "";
    public string OverwriteDir { get; set; } = "";
    public string Profile { get; set; } = "Default";
    public string ProfileDir => Path.Combine(ProfilesDir, Profile);
    public string ModList => Path.Combine(ProfileDir, "modlist.txt");
    public string? GamePath { get; set; }
    public string? GameName { get; set; }
    /// <summary>Managed mods in modlist.txt order: highest priority first.</summary>
    public List<(string name, bool enabled)> Entries { get; } = new();
    public string Name => Path.GetFileName(BaseDir.TrimEnd('\\', '/'));
}

/// <summary>One folder merged over the game folder: an enabled MO2 mod, or MO2's overwrite folder.</summary>
public sealed class Overlay
{
    public string Root { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsOverwrite { get; init; }

    public IEnumerable<string> MapDir(string relDir)
    {
        yield return Path.Combine(Root, relDir);
        yield return Path.Combine(Root, "Root", relDir);
    }

    public IEnumerable<string> MapFile(string rel)
    {
        yield return Path.Combine(Root, rel);
        yield return Path.Combine(Root, "Root", rel);
        if (rel.StartsWith(GamePaths.RelArchiveMods + "\\", StringComparison.OrdinalIgnoreCase) && rel.Count(c => c == '\\') == 3)
            yield return Path.Combine(Root, Path.GetFileName(rel));
    }

    /// <summary>The game-relative path of a file inside this folder, or null when it is not inside.</summary>
    public string? RelOf(string full)
    {
        var rel = Path.GetRelativePath(Root, full);
        if (rel.StartsWith("..") || Path.IsPathRooted(rel)) return null;
        return ModOrganizer.MapRel(rel);
    }

    public static bool IsLooseArchive(string f) => f.EndsWith(".archive", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xl", StringComparison.OrdinalIgnoreCase);
}

public static class ModOrganizer
{
    public const string ManagerName = "Mod Organizer 2";

    /// <summary>Find the MO2 instance for this game, if one exists, and fill in the folders to merge.</summary>
    public static void Attach(GamePaths g)
    {
        g.Overlays.Clear(); g.Mo2 = null;
        Mo2Instance? inst = null;
        try { inst = Find(g.GameDir); } catch { }
        if (inst == null) return;
        g.Mo2 = inst;
        // lowest priority first: modlist.txt lists the highest first
        foreach (var (name, enabled) in Enumerable.Reverse(inst.Entries))
            if (enabled) g.Overlays.Add(new Overlay { Root = Path.Combine(inst.ModsDir, name), Name = name });
        if (Directory.Exists(inst.OverwriteDir)) g.Overlays.Add(new Overlay { Root = inst.OverwriteDir, Name = "Overwrite", IsOverwrite = true });
    }

    public static Mo2Instance? Find(string gameDir)
    {
        var found = new List<Mo2Instance>();
        foreach (var ini in CandidateInis().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var inst = ReadInstance(ini);
            if (inst == null) continue;
            var managesThisGame = inst.GamePath != null && SamePath(inst.GamePath, gameDir);
            var isCyberpunk = (inst.GameName ?? "").Contains("Cyberpunk", StringComparison.OrdinalIgnoreCase);
            // an instance for another game, or for a different Cyberpunk install, is not this one
            if (!managesThisGame && !(isCyberpunk && inst.GamePath == null)) continue;
            found.Add(inst);
        }
        if (found.Count == 0) return null;
        // several instances for one game: the one whose mod list was changed most recently is the one in use
        return found.OrderByDescending(i => SafeTime(i.ModList)).First();
    }

    static IEnumerable<string> CandidateInis()
    {
        // 1. the folder the user pointed at, in config.json
        var cfg = GameLocator.LoadConfig().Mo2Path;
        if (!string.IsNullOrWhiteSpace(cfg))
        {
            if (File.Exists(cfg)) yield return cfg;
            else yield return Path.Combine(cfg, "ModOrganizer.ini");
        }
        // 2. global instances: %LOCALAPPDATA%\ModOrganizer\<instance>\ModOrganizer.ini
        var global = Path.Combine(GamePaths.LocalAppData, "ModOrganizer");
        if (Directory.Exists(global))
        {
            foreach (var d in SafeDirs(global)) yield return Path.Combine(d, "ModOrganizer.ini");
            // 3. portable instances MO2 registered as the Nexus download handler
            foreach (var exe in NxmHandlers(Path.Combine(global, "nxmhandler.ini"))) yield return Path.Combine(Path.GetDirectoryName(exe) ?? "", "ModOrganizer.ini");
        }
        // Everything below looks at the real machine, so a fixture scan skips it: a test install must not pick up the
        // developer's own MO2.
        if (GamePaths.TestRoot != null) yield break;
        // 4. a running MO2
        foreach (var exe in RunningMo2()) yield return Path.Combine(Path.GetDirectoryName(exe) ?? "", "ModOrganizer.ini");
        // 5. the usual portable spots
        foreach (var drive in SafeDrives())
            foreach (var rel in new[] { "MO2", @"Modding\MO2", "Mod Organizer 2", "ModOrganizer", "ModOrganizer2", @"Games\MO2", @"Modding\Mod Organizer 2", @"Games\Mod Organizer 2", @"Program Files\Mod Organizer 2", @"Cyberpunk 2077 MO2", @"Modding\Cyberpunk 2077" })
                yield return Path.Combine(drive, rel, "ModOrganizer.ini");
    }

    static IEnumerable<string> SafeDirs(string d) { try { return Directory.EnumerateDirectories(d).ToList(); } catch { return Array.Empty<string>(); } }
    static IEnumerable<string> SafeDrives() { try { return DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed).Select(d => d.RootDirectory.FullName).ToList(); } catch { return Array.Empty<string>(); } }

    static IEnumerable<string> NxmHandlers(string ini)
    {
        if (!File.Exists(ini)) yield break;
        string[] lines; try { lines = Files.ReadAllLinesShared(ini); } catch { yield break; }
        foreach (var line in lines)
        {
            var m = Regex.Match(line, @"^\s*\d+\\executable\s*=\s*(.+?)\s*$", RegexOptions.IgnoreCase);
            if (m.Success) yield return Unquote(m.Groups[1].Value).Replace('/', '\\');
        }
    }

    static IEnumerable<string> RunningMo2()
    {
        var list = new List<string>();
        try
        {
            foreach (var p in Process.GetProcessesByName("ModOrganizer"))
                using (p) { try { if (p.MainModule?.FileName is { } f) list.Add(f); } catch { } }
        }
        catch { }
        return list;
    }

    // ------------------------------------------------------------------ the instance

    public static Mo2Instance? ReadInstance(string iniPath)
    {
        if (!File.Exists(iniPath)) return null;
        Dictionary<string, Dictionary<string, string>> ini;
        try { ini = ReadIni(iniPath); } catch { return null; }
        string? Get(string section, string key) => ini.TryGetValue(section, out var s) && s.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

        var instDir = Path.GetDirectoryName(Path.GetFullPath(iniPath))!;
        var baseDir = Get("Settings", "base_directory") is { } b ? Full(b, instDir, instDir) : instDir;
        string Dir(string key, string def) => Get("Settings", key) is { } v ? Full(v, baseDir, instDir) : Path.Combine(baseDir, def);
        var inst = new Mo2Instance
        {
            Ini = iniPath,
            BaseDir = baseDir,
            ModsDir = Dir("mod_directory", "mods"),
            ProfilesDir = Dir("profiles_directory", "profiles"),
            OverwriteDir = Dir("overwrite_directory", "overwrite"),
            GamePath = Get("General", "gamePath") is { } gp ? Full(gp, instDir, instDir) : null,
            GameName = Get("General", "gameName"),
            Profile = Get("General", "selected_profile") ?? "Default",
        };
        if (!Directory.Exists(inst.ProfileDir))
        {
            // the selected profile was renamed or deleted: fall back to the one MO2 creates, then to any
            var any = Directory.Exists(inst.ProfilesDir) ? SafeDirs(inst.ProfilesDir).Select(Path.GetFileName).FirstOrDefault(n => File.Exists(Path.Combine(inst.ProfilesDir, n!, "modlist.txt"))) : null;
            inst.Profile = Directory.Exists(Path.Combine(inst.ProfilesDir, "Default")) ? "Default" : any ?? inst.Profile;
        }
        if (!Directory.Exists(inst.ModsDir) && !File.Exists(inst.ModList)) return null;   // an ini with nothing behind it
        ReadModList(inst);
        return inst;
    }

    static void ReadModList(Mo2Instance inst)
    {
        inst.Entries.Clear();
        if (!File.Exists(inst.ModList)) return;
        string[] lines; try { lines = Files.ReadAllLinesShared(inst.ModList); } catch { return; }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length < 2 || line.StartsWith('#')) continue;
            var flag = line[0]; var name = line[1..].Trim();
            if (flag is not ('+' or '-')) continue;                                         // '*' = not managed by MO2
            if (name.EndsWith("_separator", StringComparison.OrdinalIgnoreCase)) continue;   // a heading in MO2's list, not a mod
            if (!seen.Add(name)) continue;
            inst.Entries.Add((name, flag == '+'));
        }
    }

    // Qt's INI: [Section], key=value, values may be "quoted", @ByteArray(...), with \\ for a backslash and \xNN escapes.
    static Dictionary<string, Dictionary<string, string>> ReadIni(string path)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var section = "General";
        foreach (var raw in Files.ReadAllLinesShared(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
            var eq = line.IndexOf('='); if (eq <= 0) continue;
            var key = line[..eq].Trim(); var val = line[(eq + 1)..].Trim();
            if (!result.TryGetValue(section, out var s)) result[section] = s = new(StringComparer.OrdinalIgnoreCase);
            s[key] = QtValue(val);
        }
        return result;
    }

    internal static string QtValue(string v)
    {
        var bytes = false;
        if (v.StartsWith("@ByteArray(") && v.EndsWith(")")) { v = v[11..^1]; bytes = true; }
        v = Unquote(v);
        if (v.IndexOf('\\') < 0) return v;
        var sb = new StringBuilder(); var pending = new List<byte>();
        void FlushBytes() { if (pending.Count > 0) { sb.Append(Encoding.UTF8.GetString(pending.ToArray())); pending.Clear(); } }
        for (int i = 0; i < v.Length; i++)
        {
            var c = v[i];
            if (c == '\\' && i + 1 < v.Length)
            {
                var n = v[i + 1];
                if (n == 'x')
                {
                    // \xNN in a byte array is one UTF-8 byte; \xNNNN in a string is one UTF-16 unit
                    var hex = new string(v.Skip(i + 2).TakeWhile(Uri.IsHexDigit).Take(bytes ? 2 : 4).ToArray());
                    if (hex.Length > 0)
                    {
                        var code = Convert.ToInt32(hex, 16);
                        if (bytes) pending.Add((byte)code); else { FlushBytes(); sb.Append((char)code); }
                        i += 1 + hex.Length; continue;
                    }
                }
                FlushBytes();
                sb.Append(n switch { '\\' => '\\', '"' => '"', 'n' => '\n', 't' => '\t', '0' => '\0', _ => n });
                i++; continue;
            }
            FlushBytes(); sb.Append(c);
        }
        FlushBytes();
        return sb.ToString();
    }

    static string Unquote(string v) => v.Length >= 2 && v[0] == '"' && v[^1] == '"' ? v[1..^1] : v;

    static string Full(string v, string baseDir, string instDir)
    {
        v = v.Replace("%BASE_DIR%", baseDir, StringComparison.OrdinalIgnoreCase).Replace('/', '\\');
        try { return Path.GetFullPath(Path.IsPathRooted(v) ? v : Path.Combine(instDir, v)); } catch { return v; }
    }

    static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    static DateTime SafeTime(string f) { try { return File.Exists(f) ? File.GetLastWriteTime(f) : DateTime.MinValue; } catch { return DateTime.MinValue; } }

    // ------------------------------------------------------------------ files inside a mod folder

    /// <summary>A path inside a mod folder, as a path inside the game folder.</summary>
    public static string MapRel(string rel)
    {
        var parts = rel.Split('\\', '/');
        if (parts.Length > 1 && parts[0].Equals("Root", StringComparison.OrdinalIgnoreCase)) return string.Join('\\', parts.Skip(1));   // Root Builder
        if (parts.Length == 1 && Overlay.IsLooseArchive(rel)) return Path.Combine(GamePaths.RelArchiveMods, rel);
        return string.Join('\\', parts);
    }

    // MO2's own bookkeeping inside a mod folder: not part of the mod as the game sees it
    static bool Bookkeeping(string rel) =>
        rel.Equals("meta.ini", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".mohidden", StringComparison.OrdinalIgnoreCase)
        || rel.StartsWith("fomod\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>The game-relative files a mod folder provides (hidden files and MO2's own files left out).</summary>
    public static List<string> FilesOf(string modDir, int cap = 20000)
    {
        var list = new List<string>();
        if (!Directory.Exists(modDir)) return list;
        try
        {
            foreach (var f in Directory.EnumerateFiles(modDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(modDir, f);
                if (Bookkeeping(rel)) continue;
                list.Add(MapRel(rel));
                if (list.Count >= cap) break;   // a mod this size is already pathological; do not stall the scan
            }
        }
        catch { }
        return list;
    }

    /// <summary>meta.ini: Nexus id, version, and when MO2 last wrote it (an install or an update).</summary>
    public static (string? nexusId, string version, DateTime? written) Meta(string modDir)
    {
        var f = Path.Combine(modDir, "meta.ini");
        if (!File.Exists(f)) return (null, "", null);
        try
        {
            var ini = ReadIni(f);
            string? Get(string k) => ini.TryGetValue("General", out var s) && s.TryGetValue(k, out var v) && v.Length > 0 ? v : null;
            var id = Get("modid");
            if (id != null && (!long.TryParse(id, out var n) || n <= 0)) id = null;
            var ver = Get("version") ?? "";
            if (Regex.IsMatch(ver, @"^0(\.0)*$")) ver = "";
            return (id, ver, File.GetLastWriteTime(f));
        }
        catch { return (null, "", null); }
    }
}

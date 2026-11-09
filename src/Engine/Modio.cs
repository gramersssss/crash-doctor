using System.Text.Json;

namespace CrashDoctor.Engine;

// Mods installed through the game's own mod menu (patch 2.5, run by mod.io).
//
// The game embeds the mod.io SDK v2 (its exe carries "Modio SDK v2", "state.json", "PathOnDisk", "{}/mods/{}"). The SDK
// keeps everything under one storage root - C:\Users\Public\mod.io by default, or the RootLocalStoragePath in
// LocalAppData\mod.io\globalsettings.json - and under it, per game (Cyberpunk 2077 is game 5784 on mod.io):
//
//   <root>\5784\mods\<mod id>\...        the installed files (an archive folder, scripts\, tweaks\)
//   <root>\5784\metadata\state.json      {"Mods":[{... "Profile":{name, modfile:{version}}, "CurrentState", "PathOnDisk"}]}
//
// and the game writes Saved Games\CD Projekt Red\Cyberpunk 2077\EnabledMods.json with "enabled_mods".
//
// Only those three files are read, and only by landmarks: on 8 Nov 2026 no install was available to look at (Gary's
// state.json says {"Mods":[],"version":1}), so every key is looked up under several spellings and anything missing just
// means less detail. A folder under mods\ is what makes a mod installed; state.json only names it.
//
// Never read: LocalAppData\mod.io\<game>\<user>\user.json. It holds the player's mod.io sign-in and has nothing a crash
// diagnosis needs.
public sealed class ModioMod
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Dir { get; set; } = "";
    public DateTime? InstalledAt { get; set; }
    public bool? Enabled { get; set; }      // null = the game's list could not be read or matched
}

public static class Modio
{
    public const string GameId = "5784";

    /// <summary>The storage roots to look in: the override from globalsettings.json, and the default.</summary>
    public static List<string> Roots()
    {
        var roots = new List<string>();
        try
        {
            if (File.Exists(GamePaths.ModioSettings))
            {
                using var doc = JsonDocument.Parse(Files.ReadAllTextShared(GamePaths.ModioSettings));
                if (Str(doc.RootElement, "RootLocalStoragePath", "rootLocalStoragePath") is { Length: > 0 } p)
                {
                    p = p.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
                    // a fixture may only point inside itself
                    if (GamePaths.TestRoot is not { } t || Path.GetFullPath(p).StartsWith(Path.GetFullPath(t), StringComparison.OrdinalIgnoreCase)) roots.Add(p);
                }
            }
        }
        catch { }
        // the game can ignore the override ("Ignoring RootLocalStoragePath override. Mods will be installed to the default directory.")
        roots.Add(GamePaths.ModioDefaultRoot);
        return roots.Where(Directory.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<ModioMod> Read()
    {
        var list = new List<ModioMod>();
        foreach (var root in Roots())
        {
            var game = Path.Combine(root, GameId);
            var modsDir = Path.Combine(game, "mods");
            var meta = ReadState(Path.Combine(game, "metadata", "state.json"));
            var dirs = new List<(string id, string dir)>();
            try { if (Directory.Exists(modsDir)) dirs.AddRange(Directory.EnumerateDirectories(modsDir).Select(d => (Path.GetFileName(d), d))); } catch { }
            // a PathOnDisk somewhere else than mods\<id> (the SDK lets a game choose its install folder)
            foreach (var m in meta.Values)
                if (m.Dir.Length > 0 && Directory.Exists(m.Dir) && !dirs.Any(x => x.dir.Equals(Path.GetFullPath(m.Dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) dirs.Add((m.Id, m.Dir));
            foreach (var (id, dir) in dirs)
            {
                if (list.Any(x => x.Dir.Equals(dir, StringComparison.OrdinalIgnoreCase))) continue;
                bool hasFiles; try { hasFiles = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any(); } catch { hasFiles = false; }
                if (!hasFiles) continue;   // an emptied folder (an uninstall in progress) is not an installed mod
                meta.TryGetValue(id, out var known);
                DateTime? at = null; try { at = Directory.GetCreationTime(dir); } catch { }
                list.Add(new ModioMod { Id = id, Dir = dir, Name = known?.Name is { Length: > 0 } n ? n : "mod.io " + id, Version = known?.Version ?? "", InstalledAt = at });
            }
        }
        MarkEnabled(list);
        return list;
    }

    /// <summary>The game's list of switched-on mods. Its entry format is not documented, so an entry matches a mod by any id, name or folder it contains.</summary>
    public static string EnabledFile => Path.Combine(GamePaths.SavedGames, "EnabledMods.json");

    static void MarkEnabled(List<ModioMod> mods)
    {
        if (mods.Count == 0 || !File.Exists(EnabledFile)) return;
        try
        {
            using var doc = JsonDocument.Parse(Files.ReadAllTextShared(EnabledFile));
            if (!doc.RootElement.TryGetProperty("enabled_mods", out var arr) || arr.ValueKind != JsonValueKind.Array) return;
            var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in arr.EnumerateArray()) Collect(e, words);
            bool Hit(ModioMod m) => words.Contains(m.Id) || words.Contains(m.Name) || words.Contains(Path.GetFileName(m.Dir));
            // only believe the list once it names at least one installed mod: an empty or unrecognised list says nothing
            if (!mods.Any(Hit)) return;
            foreach (var m in mods) m.Enabled = Hit(m);
        }
        catch { }
    }

    static void Collect(JsonElement e, HashSet<string> into)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Number: into.Add(e.GetRawText()); break;
            case JsonValueKind.String: if (e.GetString() is { Length: > 0 } s) { into.Add(s); into.Add(Path.GetFileName(s.TrimEnd('/', '\\'))); } break;
            case JsonValueKind.Array: foreach (var x in e.EnumerateArray()) Collect(x, into); break;
            case JsonValueKind.Object: foreach (var p in e.EnumerateObject()) Collect(p.Value, into); break;
        }
    }

    static Dictionary<string, ModioMod> ReadState(string file)
    {
        var byId = new Dictionary<string, ModioMod>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(file)) return byId;
            using var doc = JsonDocument.Parse(Files.ReadAllTextShared(file));
            if (Prop(doc.RootElement, "Mods", "mods") is not { ValueKind: JsonValueKind.Array } mods) return byId;
            foreach (var m in mods.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.Object) continue;
                var profile = Prop(m, "Profile", "profile", "ModProfile");
                var id = Str(m, "ID", "Id", "id", "ModID", "ModId") ?? (profile is { } p0 ? Str(p0, "id", "ModId", "ID") : null);
                if (string.IsNullOrEmpty(id)) continue;
                var mod = new ModioMod { Id = id, Dir = Str(m, "PathOnDisk", "pathOnDisk")?.Replace('/', Path.DirectorySeparatorChar) ?? "" };
                if (profile is { } p)
                {
                    mod.Name = Str(p, "name", "ProfileName", "Name") ?? "";
                    if (Prop(p, "modfile", "FileInfo", "Modfile") is { ValueKind: JsonValueKind.Object } mf) mod.Version = Str(mf, "version", "Version") ?? "";
                    if (mod.Version.Length == 0) mod.Version = Str(p, "version", "Version") ?? "";
                }
                byId[id] = mod;
            }
        }
        catch { }
        return byId;
    }

    static JsonElement? Prop(JsonElement e, params string[] names)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var n in names) if (e.TryGetProperty(n, out var v)) return v;
        return null;
    }

    static string? Str(JsonElement e, params string[] names) => Prop(e, names) is { } v
        ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null } : null;
}

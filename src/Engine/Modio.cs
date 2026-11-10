using System.Text.Json;

namespace CrashDoctor.Engine;

// Mods installed through the game's own mod menu (patch 2.5, run by mod.io).
//
// The game embeds the mod.io SDK v2 (its exe carries "Modio SDK v2", "state.json", "PathOnDisk", "{}/mods/{}"). The SDK
// keeps everything under one storage root - C:\Users\Public\mod.io by default, or the RootLocalStoragePath in
// LocalAppData\mod.io\globalsettings.json - and under it, per game (Cyberpunk 2077 is game 5784 on mod.io):
//
//   <root>\5784\mods\<mod id>\...        the installed files: about.json and the game's own layout (r6\scripts\...)
//   <root>\5784\metadata\state.json      {"Mods":[{"ID", "Profile":{name, modfile:{version}, ...}, "State", "PathOnDisk", ...}]}
//
// and the game writes Saved Games\CD Projekt Red\Cyberpunk 2077\EnabledMods.json with "enabled_mods".
//
// Only those files are read, and only by landmarks, so anything missing just means less detail. A folder under mods\
// is what makes a mod installed; state.json only names it. Checked against a real install on 9 Nov 2026 (Gary:
// "Unlimited Reserve Ammo (Keep Reloading)" 1.0.1, mod 6481578): each Mods[] entry had ID, NeverRetryCategory,
// NeverRetryCode, PathOnDisk (C:\Users\Public\mod.io\5784\mods\6481578), Profile (name, modfile.version, date_added,
// description ...), SizeOnDisk, State and SubscriptionCount; the folder held about.json, r6\scripts\<mod>\*.reds and the
// game's own compiled r6\cache\final.redscripts(.h64). After the in-game uninstall the entry and the folder were both
// gone - so Crash Doctor remembers every menu mod it has seen (modio-seen.json in its own folder), or a crash during
// an install could never be connected to it afterwards.
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
                if (m.Dir.Length > 0 && !dirs.Any(x => x.id == m.Id) && Directory.Exists(m.Dir) && !dirs.Any(x => x.dir.Equals(Path.GetFullPath(m.Dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) dirs.Add((m.Id, m.Dir));
            foreach (var (id, dir) in dirs)
            {
                if (list.Any(x => x.Dir.Equals(dir, StringComparison.OrdinalIgnoreCase))) continue;
                bool hasFiles; try { hasFiles = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any(); } catch { hasFiles = false; }
                if (!hasFiles) continue;   // an emptied folder (an uninstall in progress) is not an installed mod
                meta.TryGetValue(id, out var known);
                DateTime? at = null; try { at = Directory.GetCreationTime(dir); } catch { }
                var about = ReadAbout(dir);
                var name = known?.Name is { Length: > 0 } n ? n : about.name is { Length: > 0 } an ? an : "mod.io " + id;
                var version = known?.Version is { Length: > 0 } v ? v : about.version ?? "";
                list.Add(new ModioMod { Id = id, Dir = dir, Name = name, Version = version, InstalledAt = at });
            }
        }
        MarkEnabled(list);
        return list;
    }

    // about.json, which every menu mod carries: the fallback for the name and version when state.json has none
    static (string? name, string? version) ReadAbout(string dir)
    {
        try
        {
            var f = Path.Combine(dir, "about.json");
            if (!File.Exists(f)) return (null, null);
            using var doc = JsonDocument.Parse(Files.ReadAllTextShared(f));
            return (Str(doc.RootElement, "name", "Name", "title", "Title"), Str(doc.RootElement, "version", "Version"));
        }
        catch { return (null, null); }
    }

    public sealed class Seen
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public DateTime InstalledAt { get; set; }
        public DateTime LastSeen { get; set; }
    }
    static string SeenFile => Path.Combine(GamePaths.AppData, "modio-seen.json");

    /// <summary>
    /// Every menu mod seen on a scan, kept in Crash Doctor's own folder, so an install that was uninstalled again (the menu
    /// deletes both the folder and the state.json entry) still shows in "what changed" for the crashes it came before.
    /// Returns the remembered installs that are not there now. Each is kept 60 days after it was last seen.
    /// </summary>
    public static List<Seen> Remember(List<ModioMod> now)
    {
        var all = new List<Seen>();
        try { if (File.Exists(SeenFile)) all = JsonSerializer.Deserialize<List<Seen>>(File.ReadAllText(SeenFile)) ?? new(); } catch { }
        bool Same(Seen s, ModioMod m) => s.Id == m.Id && m.InstalledAt is { } at && Math.Abs((at - s.InstalledAt).TotalMinutes) < 1;
        var changed = false;
        foreach (var m in now.Where(m => m.InstalledAt != null))
        {
            var s = all.FirstOrDefault(x => Same(x, m));
            if (s == null) { all.Add(new Seen { Id = m.Id, Name = m.Name, Version = m.Version, InstalledAt = m.InstalledAt!.Value, LastSeen = DateTime.Now }); changed = true; }
            else if (s.LastSeen < DateTime.Now.AddHours(-12) || s.Name != m.Name || s.Version != m.Version) { s.LastSeen = DateTime.Now; s.Name = m.Name; s.Version = m.Version; changed = true; }
        }
        changed |= all.RemoveAll(x => x.LastSeen < DateTime.Now.AddDays(-60)) > 0;
        if (changed) { try { Directory.CreateDirectory(GamePaths.AppData); File.WriteAllText(SeenFile, JsonSerializer.Serialize(all)); } catch { } }
        return all.Where(x => !now.Any(m => Same(x, m))).ToList();
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

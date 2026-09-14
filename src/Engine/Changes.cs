using System.Globalization;
using System.Text.RegularExpressions;

namespace CrashDoctor.Engine;

// "What changed since your last clean session?"
//
// The most useful question on the night of 11 Sep was not "what was logged before the crash" but "what is different
// between this install and the one that played for five days without a problem". It took reading Vortex's own log by
// hand to answer. This answers it on every crash: everything that changed between the last session that ended
// normally and the crash, from four sources that do not depend on each other.
//
//   - Vortex's log, %APPDATA%\Vortex\vortex*.log: every install or update ("Extracted mod info from path"), and every
//     deployment with the number of files it added and removed. Timestamps are UTC there.
//   - The RED4ext logs of the two sessions, which list every native plugin and its version - so a framework update
//     between them (ArchiveXL 1.27.1 -> 1.27.2 was the real case) is caught whatever mod manager is in use.
//   - Write times of files inside the mod folders, for hand-installed mods where nothing else records the install.
//   - The game exe's write time (a game patch) and the settings file's write time.
//
// It is layer 1 in DESIGN.md's terms, a list of facts, and the copy says so: being new is not evidence of guilt.
// It is the shortest list to test first, which is a different and more honest claim.

public sealed class Change
{
    public DateTime At { get; set; }
    public string Kind { get; set; } = "";     // installed | enabled | disabled | deployed | plugin | files | game | settings
    public string Text { get; set; } = "";
    public string? Mod { get; set; }
}

public sealed class ModInstall { public DateTime At { get; set; } public string Folder { get; set; } = ""; public string Name { get; set; } = ""; public string Version { get; set; } = ""; }
public sealed class Deployment { public DateTime At { get; set; } public int Added { get; set; } public int Removed { get; set; } public int Modified { get; set; } }

public static class ChangeLog
{
    static readonly Regex Ts = new(@"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z)", RegexOptions.Compiled);
    static readonly Regex Extracted = new(@"Extracted mod info from path: (.+?)\s*$", RegexOptions.Compiled);
    static readonly Regex Deployed = new(@"\] deployment \{""added"":(\d+),""removed"":(\d+),""source changed"":(\d+),""modified"":(\d+)\}", RegexOptions.Compiled);

    // ---------------------------------------------------------------- collecting

    /// <summary>Installs and deployments from Vortex's rotating logs, newest file first, back to <paramref name="since"/>.</summary>
    public static void ReadVortexLog(CollectedData d, DateTime since)
    {
        var dir = GamePaths.VortexDir;
        if (!Directory.Exists(dir)) return;
        var logs = new[] { "vortex.log", "vortex1.log", "vortex2.log", "vortex3.log" }.Select(n => Path.Combine(dir, n)).Where(File.Exists)
                       .Where(f => File.GetLastWriteTime(f) >= since).ToList();
        if (logs.Count == 0) return;
        d.VortexLogRead = true;
        foreach (var f in logs)
        {
            string[] lines; try { lines = Files.ReadAllTextShared(f).Split('\n'); } catch { continue; }
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                var m = Ts.Match(line); if (!m.Success) continue;
                if (line.Contains("Extracted mod info from path:"))
                {
                    var e = Extracted.Match(line); if (!e.Success) continue;
                    if (!DateTime.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)) continue;
                    at = at.ToLocalTime(); if (at < since) continue;
                    var folder = Path.GetFileName(e.Groups[1].Value.Trim().TrimEnd('\\', '/'));
                    if (folder.EndsWith(".installing", StringComparison.OrdinalIgnoreCase)) folder = folder[..^".installing".Length];
                    var row = ModInventory.FromFolder(folder);
                    d.ModInstalls.Add(new ModInstall { At = at, Folder = folder, Name = row.Name, Version = row.Version });
                }
                else if (line.Contains("] deployment {"))
                {
                    var dep = Deployed.Match(line); if (!dep.Success) continue;
                    if (!DateTime.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)) continue;
                    at = at.ToLocalTime();
                    if (at >= since)
                        d.Deployments.Add(new Deployment { At = at, Added = int.Parse(dep.Groups[1].Value), Removed = int.Parse(dep.Groups[2].Value), Modified = int.Parse(dep.Groups[4].Value) });
                }
            }
        }
        d.ModInstalls.Sort((a, b) => a.At.CompareTo(b.At));
        d.Deployments.Sort((a, b) => a.At.CompareTo(b.At));
    }

    /// <summary>Files inside the mod folders written since <paramref name="since"/>, and the game exe's own write time.</summary>
    public static void ReadFileTimes(CollectedData d, DateTime since)
    {
        var g = d.Paths;
        try { if (File.Exists(g.Exe)) d.GameExeWritten = File.GetLastWriteTime(g.Exe); } catch { }
        var roots = new[] { (g.ArchiveMods, false), (g.CetMods, true), (g.Scripts, true), (g.Red4extPlugins, true), (g.Tweaks, true) };
        foreach (var (root, recurse) in roots)
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*", recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly); } catch { continue; }
            foreach (var f in files)
            {
                // only what a mod is made of. CET mods write their settings and databases into their own folders while
                // the game runs, and the loaders keep logs there; counting those would name half the mod list every time.
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is not (".archive" or ".xl" or ".reds" or ".lua" or ".dll" or ".yaml" or ".yml" or ".tweak")) continue;
                DateTime t; try { t = File.GetLastWriteTime(f); } catch { continue; }
                if (t < since || t > DateTime.Now.AddMinutes(5)) continue;   // a clock from the future is noise, not a change
                d.ChangedFiles.Add((Path.GetRelativePath(g.GameDir, f), t));
                if (d.ChangedFiles.Count >= 3000) return;
            }
        }
    }

    // ---------------------------------------------------------------- comparing

    /// <summary>Everything that changed in (from, to], oldest first. <paramref name="before"/>/<paramref name="after"/> are the RED4ext sessions at each end, when their logs still exist.</summary>
    public static List<Change> Between(CollectedData d, DateTime from, DateTime to, Red4extSession? before, Red4extSession? after)
    {
        var list = new List<Change>();
        var inv = d.Mods;

        // installs and updates, from Vortex
        foreach (var i in d.ModInstalls.Where(x => x.At > from && x.At <= to))
        {
            var row = inv.Find(i.Name);
            var older = inv.Mods.Any(m => m.Name.Equals(i.Name, StringComparison.OrdinalIgnoreCase) && !m.Folder.Equals(i.Folder, StringComparison.OrdinalIgnoreCase));
            var what = older ? "updated" : "installed";
            var state = row == null ? " (no longer installed)" : row.Status == "disabled" ? " (now disabled)" : row.Folder.Equals(i.Folder, StringComparison.OrdinalIgnoreCase) ? "" : " (since replaced)";
            var ver = i.Version.Length > 0 && !i.Name.EndsWith(i.Version, StringComparison.OrdinalIgnoreCase) ? " " + i.Version : "";
            list.Add(new Change { At = i.At, Kind = "installed", Mod = i.Name, Text = $"{i.Name}{ver} {what} in Vortex{state}" });
        }

        // Deployments, folded into one line: the file counts are exact and a "removed" count is the only trace a
        // disable leaves. Which mods came and went is not read off Vortex's progress lines - they are thinned when it
        // is quick, and on the reference machine that produced the same two mods "enabled" at three different times.
        var deps = d.Deployments.Where(x => x.At > from && x.At <= to && x.Added + x.Removed + x.Modified > 0).ToList();
        if (deps.Count > 0)
        {
            int added = deps.Sum(x => x.Added), removed = deps.Sum(x => x.Removed), modified = deps.Sum(x => x.Modified);
            list.Add(new Change { At = deps[^1].At, Kind = "deployed", Text = $"Vortex deployed {deps.Count} time{(deps.Count == 1 ? "" : "s")}: {added} file{(added == 1 ? "" : "s")} added, {removed} removed" + (modified > 0 ? $", {modified} modified" : "") + (removed > 0 ? " (files removed usually means a mod was disabled or uninstalled)" : "") });
        }

        // native plugins, from the two sessions' own logs - works for every mod manager and for hand installs
        if (before != null && after != null && before.PluginsLoaded.Count > 0 && after.PluginsLoaded.Count > 0)
        {
            var b = before.PluginsLoaded.GroupBy(p => p.name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().version, StringComparer.OrdinalIgnoreCase);
            var a = after.PluginsLoaded.GroupBy(p => p.name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().version, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in a)
            {
                var owner = inv.OwnerOfPlugin(kv.Key) ?? kv.Key;
                if (!b.TryGetValue(kv.Key, out var old)) list.Add(new Change { At = after.Start, Kind = "plugin", Mod = owner, Text = $"native plugin {kv.Key} {kv.Value} loaded for the first time" });
                else if (!old.Equals(kv.Value, StringComparison.OrdinalIgnoreCase)) list.Add(new Change { At = after.Start, Kind = "plugin", Mod = owner, Text = $"native plugin {kv.Key} changed version, {old} to {kv.Value}" });
            }
            foreach (var kv in b.Where(kv => !a.ContainsKey(kv.Key)))
                list.Add(new Change { At = after.Start, Kind = "plugin", Mod = inv.OwnerOfPlugin(kv.Key) ?? kv.Key, Text = $"native plugin {kv.Key} {kv.Value} no longer loads" });
        }

        // files written in the mod folders, grouped by the mod that owns them. Files Vortex deployed keep the times the
        // mod author built them with, which can fall anywhere, so under Vortex only files it does not own count: the
        // hand-installed ones, which nothing else records. On a manual install everything counts.
        var vortex = inv.Manager == "Vortex";
        var touched = d.ChangedFiles.Where(x => x.at > from && x.at <= to && (!vortex || inv.OwnerOf(x.rel) == null)).ToList();
        if (touched.Count > 0)
        {
            var known = new HashSet<string>(list.Where(c => c.Mod != null).Select(c => c.Mod!), StringComparer.OrdinalIgnoreCase);
            foreach (var grp in touched.GroupBy(x => inv.OwnerOf(x.rel) ?? "(not from any known mod)"))
            {
                if (known.Contains(grp.Key)) continue;   // the install above already explains these
                var newest = grp.Max(x => x.at); var n = grp.Count();
                var sample = string.Join(", ", grp.OrderByDescending(x => x.at).Take(3).Select(x => Path.GetFileName(x.rel)));
                list.Add(new Change { At = newest, Kind = "files", Mod = grp.Key.StartsWith("(") ? null : grp.Key, Text = $"{n} file{(n == 1 ? "" : "s")} written under {grp.Key}: {sample}{(n > 3 ? ", …" : "")}" });
            }
        }

        // the game rewrites its settings file on every exit, so that file's time says nothing here; a settings change
        // inside the crashed session is already in its evidence
        if (d.GameExeWritten is { } gw && gw > from && gw <= to) list.Add(new Change { At = gw, Kind = "game", Text = "the game itself was updated (Cyberpunk2077.exe was rewritten)" });

        return list.OrderBy(c => c.At).ToList();
    }

    /// <summary>The sentence above the list. All wording lives here so the engine, the page and the pasted summary agree.</summary>
    public static string Note(CollectedData d, List<Change> changes, DateTime? from, bool forCrash)
    {
        if (from == null) return forCrash
            ? "No session that ended normally is on record before this crash, so there is nothing to compare it against yet."
            : "No session that ended normally is on record yet, so there is nothing to compare against.";
        var sources = new List<string>();
        if (d.VortexLogRead) sources.Add("nothing was installed, updated or deployed in Vortex");
        sources.Add("no native plugin changed version"); sources.Add("no mod file (archive, script, plugin, tweak) was written");
        if (changes.Count == 0)
            return (forCrash ? $"Nothing changed between the last session that ended normally ({from:d MMM HH:mm}) and this crash that Crash Doctor can see: " : $"Nothing has changed since the last session that ended normally ({from:d MMM HH:mm}) that Crash Doctor can see: ")
                 + string.Join(", ", sources) + (d.VortexLogRead ? "." : ". (No Vortex log was found, so installs made with another manager are only seen through file times.)");
        var n = changes.Count;
        return forCrash
            ? $"{n} thing{(n == 1 ? "" : "s")} changed between the last session that ended normally ({from:d MMM HH:mm}) and this crash. Being new is not evidence of guilt, but it is the shortest list to test first."
            : $"{n} thing{(n == 1 ? "" : "s")} changed since the last session that ended normally ({from:d MMM HH:mm}). If the next session crashes, this is the shortest list to test first.";
    }

    /// <summary>The session log that belongs to a session, when it still exists on disk.</summary>
    public static Red4extSession? LogOf(CollectedData d, Session? s) =>
        s == null ? null : d.Red4ext.FirstOrDefault(x => x.File == s.Red4extLog) ?? d.Red4ext.FirstOrDefault(x => Math.Abs((x.Start - s.Start).TotalSeconds) < 90);
}

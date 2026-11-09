namespace CrashDoctor.Engine;

// Old crashes kept their warnings for as long as history.json remembered them (LyciaPintella, Nexus, 8 Nov 2026: warnings
// about out-of-date core mods from days before, with everything up to date by then). The verdict, the suspects and the
// evidence are written when a crash is first read and are history - true about that night. What was missing was the
// other half: what has happened since. That is worked out here on every scan, from what is on disk now:
//
// - how many times the game has ended normally since the crash (shown on every crash), and
// - whether what the crash was blamed on has changed since, with a normal session after the change. Then the crash
//   "looks settled", says why, and stops being the headline on the first page. It stays in the list of sessions, and
//   its suspects stop being flagged on the mods page.
//
// Only two causes can settle a crash, both checkable: (1) it came after a game update before every framework had run
// on the new build, and they all have since; (2) every mod named as a high or medium suspect has been updated, turned
// off or removed since. A crash nobody could explain is never settled by quiet time alone: that would be guessing, and
// "it has not happened for three sessions" is shown as exactly that instead.
public static class Settle
{
    public static void All(CollectedData d, List<Session> sessions)
    {
        foreach (var s in sessions) One(d, s, sessions);
    }

    static void One(CollectedData d, Session s, List<Session> all)
    {
        s.Settled = null; s.CleanSince = 0; s.CleanSinceLast = null;
        if (s.EndKind is EndKind.Clean or EndKind.Running) return;
        var at = s.End ?? s.Start;
        var clean = all.Where(x => x.EndKind == EndKind.Clean && x.Start >= at.AddSeconds(-30)).ToList();
        s.CleanSince = clean.Count;
        s.CleanSinceLast = clean.Count > 0 ? clean.Max(x => x.End ?? x.Start) : null;
        if (clean.Count == 0) return;

        // (1) the night after a patch: "your frameworks do not support this game version yet" - and now they all do
        if (s.Verdict.Contains("verdict.afterUpdate", StringComparison.Ordinal)
            && (d.GameChange == null || (d.FrameworkChecks.Count > 0 && !GameUpdate.FrameworksBehind(d.FrameworkChecks))))
        {
            s.Settled = L.T("settled.update", d.GameVersion.Length > 0 ? d.GameVersion : "?", clean.Count);
            return;
        }

        // (2) everything it was blamed on has changed since
        var named = s.Suspects.Where(x => x.Level is "high" or "medium").Select(x => x.Mod).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // a suspect that is not a mod (a setting, the video memory ceiling, the driver, "a script problem") cannot be updated away
        if (named.Count == 0 || named.Any(L.IsToken)) return;
        var items = new List<string>(); DateTime? lastChange = null;
        foreach (var name in named)
        {
            var (state, when) = ChangedSince(d, name, at);
            if (state == null) return;
            items.Add(state switch
            {
                "updated" => L.T("settled.updated", name, L.Date(when!.Value, "dm")),
                "disabled" => L.T("settled.disabled", name),
                _ => L.T("settled.removed", name),
            });
            if (when is { } w && (lastChange == null || w > lastChange)) lastChange = w;
        }
        // and the game has ended normally after the last of those changes, not just after the crash
        var after = lastChange is { } lc ? clean.Count(x => x.Start >= lc) : clean.Count;
        if (after == 0) return;
        s.Settled = L.T("settled.mods", L.List(items), after);
    }

    /// <summary>"updated" (with when), "disabled" or "removed" since <paramref name="t"/>; null when the mod is as it was.</summary>
    internal static (string? state, DateTime? when) ChangedSince(CollectedData d, string name, DateTime t)
    {
        var slack = t.AddMinutes(1);
        // only files a mod ships and nothing rewrites: a CET mod saves its settings (.json, .sqlite3) into its own folder
        // while the game runs, and that is not an update
        static bool Code(string f) => Path.GetExtension(f).ToLowerInvariant() is ".dll" or ".asi" or ".lua" or ".reds" or ".redscripts" or ".archive" or ".xl" or ".yaml" or ".yml" or ".tweak";
        var row = d.Mods.Find(name);
        if (row?.Status == "disabled") return ("disabled", null);
        // a framework or a native plugin: its own file's time (KnownReleases.cs)
        if (KnownReleases.FileTime(d, name) is { } ft) return ft > slack ? ("updated", ft) : (null, null);
        DateTime? newest = null;
        void Seen(DateTime x) { if (x > slack && (newest == null || x > newest)) newest = x; }
        foreach (var i in d.ModInstalls.Where(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) Seen(i.At);
        foreach (var x in d.Mods.Mo2Installed.Where(x => x.name.Equals(name, StringComparison.OrdinalIgnoreCase))) Seen(x.at);
        foreach (var x in d.Mods.ModioInstalled.Where(x => x.name.Equals(name, StringComparison.OrdinalIgnoreCase))) Seen(x.at);
        if (row != null)
        {
            foreach (var rel in row.Files.Where(Code).Take(400))
            {
                var full = rel.StartsWith("mod.io\\", StringComparison.OrdinalIgnoreCase) ? null : d.Paths.Find(rel);
                if (full != null) { try { Seen(File.GetLastWriteTime(full)); } catch { } }
            }
            return newest is { } n ? ("updated", n) : (null, null);
        }
        // no mod by that name: a CET, script or plugin folder, or an archive, under its own name
        var dirs = new[] { Path.Combine(GamePaths.RelCetMods, name), Path.Combine(GamePaths.RelScripts, name), Path.Combine(GamePaths.RelRed4extPlugins, name) };
        var files = new[] { Path.Combine(GamePaths.RelArchiveMods, name + ".archive"), Path.Combine(GamePaths.RelArchiveMods, name + ".archive.xl"), Path.Combine(GamePaths.RelArchiveMods, name + ".xl") };
        bool present = false;
        foreach (var dir in dirs.Where(d.Paths.DirExists))
        {
            present = true;
            foreach (var (full, _) in d.Paths.FilesIn(dir, "*.*", recurse: true).Where(x => Code(x.full)).Take(400)) { try { Seen(File.GetLastWriteTime(full)); } catch { } }
        }
        foreach (var f in files) if (d.Paths.Find(f) is { } full) { present = true; try { Seen(File.GetLastWriteTime(full)); } catch { } }
        // "No longer installed" needs proof the name was a mod in the first place: an install a mod manager recorded.
        // A name nothing on disk or in any install record knows could be anything - a setting or the video memory
        // ceiling from a history written before 0.8.0, when suspects were plain English (Gary's history, 8 Nov 2026,
        // read "Video memory ceiling" as a removed mod) - so it never settles a crash.
        var known = d.ModInstalls.Any(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                 || d.Mods.Mo2Installed.Any(x => x.name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (!present) return newest is { } n0 ? ("updated", n0) : known ? ("removed", null) : (null, null);
        return newest is { } n1 ? ("updated", n1) : (null, null);
    }
}

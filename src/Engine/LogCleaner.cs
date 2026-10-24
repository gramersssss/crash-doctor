using System.Text.Json;

namespace CrashDoctor.Engine;

// "Clear old logs" (asked for on Nexus by lynsis, Sep 2026): a clean slate, so the next launch's logs are the only
// ones there. Nothing is deleted. Every log is MOVED into Crash Doctor's own folder, under the time it was cleared,
// keeping its path, and the last clearing can be put back. The game and its frameworks write fresh logs the next time
// they start.
//
// What counts as a log is exactly what the collectors read (Collectors.cs), so nothing is cleared that the app does
// not already know about: red4ext\logs, each RED4ext plugin's own *.log (ArchiveXL, TweakXL, Codeware...), Cyber
// Engine Tweaks' logs and each CET mod's *.log, and r6\logs. Crash reports are not logs and are left alone: they are
// the only record of a crash, and the Crash logs setting already keeps copies of those.
public sealed class LogClearSummary
{
    public int Files { get; set; }                    // logs in the game folder right now
    public long Bytes { get; set; }
    public string Folder { get; set; } = "";          // where cleared logs are kept
    public string? LastCleared { get; set; }          // folder name of the latest clearing, e.g. "2026-09-27 171500"
    public int LastFiles { get; set; }                // how many logs it holds
}

public sealed class LogClearResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
}

public static class LogCleaner
{
    public static string Folder => Path.Combine(GamePaths.AppData, "cleared-logs");
    const string Manifest = "cleared.json";

    // Mod Organizer 2 puts the logs the game writes into its overwrite folder, not the game folder (ModOrganizer.cs),
    // so with MO2 the same log folders are also cleared there. Entries for those carry this prefix in the manifest;
    // the overwrite folder they came from is written beside it in roots.json, so they go back to the same place.
    const string Mo2Prefix = "mo2-overwrite|";
    const string Roots = "roots.json";

    public static List<string> Find(GamePaths g) => FindRooted(g).Select(x => x.full).ToList();

    static List<(string full, string entry)> FindRooted(GamePaths g)
    {
        var found = new List<(string, string)>();
        void Scan(string root, string prefix)
        {
            void Top(string rel) { var dir = Path.Combine(root, rel); if (Directory.Exists(dir)) try { foreach (var f in Directory.EnumerateFiles(dir, "*.log", SearchOption.TopDirectoryOnly)) found.Add((f, prefix + Path.Combine(rel, Path.GetFileName(f)))); } catch { } }
            void Each(string rel) { var dir = Path.Combine(root, rel); if (Directory.Exists(dir)) try { foreach (var d in Directory.EnumerateDirectories(dir)) Top(Path.Combine(rel, Path.GetFileName(d))); } catch { } }
            Top(GamePaths.RelRed4extLogs);
            Each(GamePaths.RelRed4extPlugins);
            Top(GamePaths.RelCet);
            Each(GamePaths.RelCetMods);
            Top(GamePaths.RelRedscriptLogs);
        }
        Scan(g.GameDir, "");
        if (g.Mo2 != null && Directory.Exists(g.Mo2.OverwriteDir)) { Scan(g.Mo2.OverwriteDir, Mo2Prefix); Scan(Path.Combine(g.Mo2.OverwriteDir, "Root"), Mo2Prefix + "Root\\"); }
        return found.GroupBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
    }

    public static LogClearSummary Summary(GamePaths g)
    {
        var s = new LogClearSummary { Folder = Folder };
        foreach (var f in Find(g)) { try { s.Bytes += new FileInfo(f).Length; s.Files++; } catch { } }
        var last = Latest();
        if (last != null) { s.LastCleared = Path.GetFileName(last); s.LastFiles = ReadManifest(last).Count; }
        return s;
    }

    static string? Latest() => Directory.Exists(Folder)
        ? Directory.EnumerateDirectories(Folder).Where(d => File.Exists(Path.Combine(d, Manifest))).OrderBy(d => d, StringComparer.Ordinal).LastOrDefault()
        : null;

    static List<string> ReadManifest(string dir)
    {
        try { return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Path.Combine(dir, Manifest))) ?? new(); } catch { return new(); }
    }

    // A fixture is never the game that is running. Without this, clearing could not be tested on a PC where the real
    // game happens to be open, because the check below looks for the process by name.
    static bool GameBusy() => string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CRASHDOCTOR_TEST_ROOT")) && GameLocator.GameRunning();

    public static LogClearResult Clear(GamePaths g)
    {
        if (GameBusy()) return new() { Message = L.T("logs.closeGame") };
        var logs = FindRooted(g);
        if (logs.Count == 0) return new() { Ok = true, Message = L.T("logs.none") };
        var dest = Path.Combine(Folder, DateTime.Now.ToString("yyyy-MM-dd HHmmss"));
        var moved = new List<string>(); var skipped = 0;
        try
        {
            Directory.CreateDirectory(dest);
            foreach (var (f, entry) in logs)
            {
                if (entry.Contains("..")) { skipped++; continue; }                      // never touch anything outside the log folders
                var to = Path.Combine(dest, entry.Replace(Mo2Prefix, "mo2-overwrite\\"));
                try { Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Move(f, to); moved.Add(entry); }
                catch { skipped++; }                                                    // open in another program, or read-only
            }
            File.WriteAllText(Path.Combine(dest, Manifest), JsonSerializer.Serialize(moved, new JsonSerializerOptions { WriteIndented = true }));
            if (g.Mo2 != null) File.WriteAllText(Path.Combine(dest, Roots), JsonSerializer.Serialize(new Dictionary<string, string> { ["mo2-overwrite"] = g.Mo2.OverwriteDir }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { return new() { Message = moved.Count > 0 ? L.P("logs.failedSome", moved.Count, ex.Message, dest) : L.T("logs.failed", ex.Message) }; }
        if (moved.Count == 0) { try { Directory.Delete(dest, true); } catch { } return new() { Message = L.T("logs.noneMoved") }; }
        return new() { Ok = true, Message = L.Cat(L.P("logs.moved", moved.Count), skipped > 0 ? L.P("logs.skipped", skipped) : "", L.T("logs.writesNew")) };
    }

    public static LogClearResult Restore(GamePaths g)
    {
        if (GameBusy()) return new() { Message = L.T("logs.closeGameShort") };
        var last = Latest();
        if (last == null) return new() { Message = L.T("logs.nothingBack") };
        string? overwrite = null;
        try { if (File.Exists(Path.Combine(last, Roots))) JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(last, Roots)))?.TryGetValue("mo2-overwrite", out overwrite); } catch { }
        int back = 0, kept = 0;
        foreach (var entry in ReadManifest(last))
        {
            var mo2 = entry.StartsWith(Mo2Prefix);
            var rel = mo2 ? entry[Mo2Prefix.Length..] : entry;
            var from = Path.Combine(last, mo2 ? Path.Combine("mo2-overwrite", rel) : rel);
            if (!File.Exists(from)) continue;
            if (mo2 && overwrite == null) { kept++; continue; }
            var to = Path.Combine(mo2 ? overwrite! : g.GameDir, rel);
            // a newer log with the same name is the game's own: it stays, and the old one stays in the cleared folder
            if (File.Exists(to)) { kept++; continue; }
            try { Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Move(from, to); back++; } catch { kept++; }
        }
        if (kept == 0) { try { Directory.Delete(last, true); } catch { } }
        else { try { File.Move(Path.Combine(last, Manifest), Path.Combine(last, "cleared-partly-restored.json"), true); } catch { } }
        return new() { Ok = back > 0 || kept == 0, Message = L.Cat(L.P("logs.putBack", back), kept > 0 ? L.P("logs.keptNewer", kept, last) : "") };
    }
}

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

    public static List<string> Find(GamePaths g)
    {
        var found = new List<string>();
        void Top(string dir) { if (Directory.Exists(dir)) try { found.AddRange(Directory.EnumerateFiles(dir, "*.log", SearchOption.TopDirectoryOnly)); } catch { } }
        void Each(string parent) { if (Directory.Exists(parent)) try { foreach (var d in Directory.EnumerateDirectories(parent)) Top(d); } catch { } }
        Top(g.Red4extLogs);
        Each(g.Red4extPlugins);
        Top(g.Cet);
        Each(g.CetMods);
        Top(g.RedscriptLogs);
        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
        if (GameBusy()) return new() { Message = "Close Cyberpunk 2077 first. The game is writing to its logs while it runs." };
        var logs = Find(g);
        if (logs.Count == 0) return new() { Ok = true, Message = "There are no logs to clear." };
        var dest = Path.Combine(Folder, DateTime.Now.ToString("yyyy-MM-dd HHmmss"));
        var moved = new List<string>(); var skipped = 0;
        try
        {
            Directory.CreateDirectory(dest);
            foreach (var f in logs)
            {
                var rel = Path.GetRelativePath(g.GameDir, f);
                if (rel.StartsWith("..")) { skipped++; continue; }                     // never touch anything outside the game folder
                var to = Path.Combine(dest, rel);
                try { Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Move(f, to); moved.Add(rel); }
                catch { skipped++; }                                                    // open in another program, or read-only
            }
            File.WriteAllText(Path.Combine(dest, Manifest), JsonSerializer.Serialize(moved, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { return new() { Message = "Could not clear the logs: " + ex.Message + (moved.Count > 0 ? $" {moved.Count} were already moved to {dest} and can be put back." : "") }; }
        if (moved.Count == 0) { try { Directory.Delete(dest, true); } catch { } return new() { Message = "None of the logs could be moved. Another program may have them open." }; }
        return new() { Ok = true, Message = $"Moved {moved.Count} log{(moved.Count == 1 ? "" : "s")} out of the game folder." + (skipped > 0 ? $" {skipped} could not be moved and were left." : "") + " The game writes new ones the next time it starts. Put them back from Settings if you need them." };
    }

    public static LogClearResult Restore(GamePaths g)
    {
        if (GameBusy()) return new() { Message = "Close Cyberpunk 2077 first." };
        var last = Latest();
        if (last == null) return new() { Message = "There is nothing to put back." };
        int back = 0, kept = 0;
        foreach (var rel in ReadManifest(last))
        {
            var from = Path.Combine(last, rel); var to = Path.Combine(g.GameDir, rel);
            if (!File.Exists(from)) continue;
            // a newer log with the same name is the game's own: it stays, and the old one stays in the cleared folder
            if (File.Exists(to)) { kept++; continue; }
            try { Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Move(from, to); back++; } catch { kept++; }
        }
        if (kept == 0) { try { Directory.Delete(last, true); } catch { } }
        else { try { File.Move(Path.Combine(last, Manifest), Path.Combine(last, "cleared-partly-restored.json"), true); } catch { } }
        return new() { Ok = back > 0 || kept == 0, Message = $"Put {back} log{(back == 1 ? "" : "s")} back." + (kept > 0 ? $" {kept} stayed in {last} because the game has written a newer file with the same name." : "") };
    }
}

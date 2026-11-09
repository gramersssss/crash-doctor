using System.Text.Json;

namespace CrashDoctor.Engine;

// RED4ext keeps only a handful of session logs. Crash Doctor remembers every session it has analysed so the trace
// keeps its history after the game rotates its logs. Stored in %APPDATA%\CrashDoctor\history.json.
//
// "Clear history" (0.8.1, asked for by LyciaPintella on Nexus, 8 Nov 2026): old crashes and the warnings that came with
// them kept showing days after everything had been updated. Clearing forgets every crash and freeze from before that
// moment - from history.json AND from the scan, because the game's own crash reports and logs would otherwise bring
// them straight back. Sessions that ended normally are kept: they are what rules mods out, and they warn about nothing.
// Only Crash Doctor's own file is touched. A copy is taken first, so Undo puts everything back.
public static class History
{
    static string PathFile => Path.Combine(GamePaths.AppData, "history.json");

    public static List<Session> Load()
    {
        try { if (File.Exists(PathFile)) return JsonSerializer.Deserialize<List<Session>>(File.ReadAllText(PathFile), Scanner.Json) ?? new(); } catch { }
        return new();
    }

    // Fresh sessions win over remembered ones with the same start; remembered full sessions win over fresh partial ones.
    public static List<Session> Merge(List<Session> fresh, List<Session> remembered)
    {
        var result = new List<Session>(fresh);
        foreach (var old in remembered)
        {
            var match = result.FirstOrDefault(s => Math.Abs((s.Start - old.Start).TotalSeconds) < 90 || (s.Partial && old.End != null && s.Start >= old.Start && s.Start <= old.End.Value.AddMinutes(1)));
            if (match == null) { if (!old.Partial || !result.Any(s => !s.Partial && s.Start <= old.Start && (s.End ?? DateTime.Now) >= old.Start)) result.Add(old); }
            else if (match.Partial && !old.Partial) { result.Remove(match); result.Add(old); }
            else if (match.EndKind == EndKind.Running && old.EndKind != EndKind.Running) { /* keep fresh */ }
        }
        return result.OrderBy(s => s.Start).ToList();
    }

    public static void Save(List<Session> sessions)
    {
        try
        {
            Directory.CreateDirectory(GamePaths.AppData);
            var keep = sessions.Where(s => s.EndKind != EndKind.Running && s.Start > DateTime.Now.AddDays(-180)).ToList();
            File.WriteAllText(PathFile, JsonSerializer.Serialize(keep, Scanner.Json));
        }
        catch { }
    }

    /// <summary>When "Clear history" was last pressed (and not undone): crashes that started before it are not shown.</summary>
    public static DateTime? ClearedAt => GameLocator.LoadConfig().HistoryClearedAt;

    /// <summary>A crash or freeze from before the last clearing. Normal sessions are never hidden.</summary>
    public static bool Hidden(Session s, DateTime? clearedAt) => clearedAt is { } c && s.Start < c && s.EndKind is not (EndKind.Clean or EndKind.Running);

    public static HistorySummary Summary()
    {
        var all = Load(); var cfg = GameLocator.LoadConfig();
        return new HistorySummary
        {
            Sessions = all.Count,
            Crashes = all.Count(s => s.EndKind is not (EndKind.Clean or EndKind.Running) && !Hidden(s, cfg.HistoryClearedAt)),
            ClearedAt = cfg.HistoryClearedAt,
            CanUndo = cfg.HistoryBackup is { Length: > 0 } b && File.Exists(Path.Combine(GamePaths.AppData, b)),
        };
    }

    /// <summary>How many crashes a clearing would forget right now (for the confirmation).</summary>
    public static int CrashesToClear(Report? r) => r?.Sessions.Count(s => s.EndKind is not (EndKind.Clean or EndKind.Running)) ?? Summary().Crashes;

    public sealed class Result { public bool Ok { get; set; } public string Message { get; set; } = ""; }

    public static Result Clear()
    {
        try
        {
            Directory.CreateDirectory(GamePaths.AppData);
            var now = DateTime.Now;
            var cfg = GameLocator.LoadConfig();
            // one copy, of the history as it stood before the first clearing still in force: clearing twice keeps the
            // older, fuller copy, so Undo always goes back to everything
            if (cfg.HistoryBackup is not { Length: > 0 } b || !File.Exists(Path.Combine(GamePaths.AppData, b)))
            {
                var name = "history-before-clear-" + now.ToString("yyyy-MM-dd-HHmmss") + ".json";
                if (File.Exists(PathFile)) File.Copy(PathFile, Path.Combine(GamePaths.AppData, name), overwrite: false);
                else File.WriteAllText(Path.Combine(GamePaths.AppData, name), "[]");
                cfg.HistoryBackup = name;
            }
            var all = Load();
            var forgotten = all.Count(s => Hidden(s, now));
            File.WriteAllText(PathFile, JsonSerializer.Serialize(all.Where(s => !Hidden(s, now)).ToList(), Scanner.Json));
            cfg.HistoryClearedAt = now;
            GameLocator.SaveConfig(cfg);
            return new Result { Ok = true, Message = L.T("hist.cleared", forgotten) };
        }
        catch (Exception ex) { return new Result { Message = L.T("hist.failed", ex.Message) }; }
    }

    public static Result Undo()
    {
        try
        {
            var cfg = GameLocator.LoadConfig();
            var backup = cfg.HistoryBackup is { Length: > 0 } b ? Path.Combine(GamePaths.AppData, b) : null;
            if (backup == null || !File.Exists(backup)) return new Result { Message = L.T("hist.nothingBack") };
            List<Session> old;
            try { old = JsonSerializer.Deserialize<List<Session>>(File.ReadAllText(backup), Scanner.Json) ?? new(); }
            catch (Exception ex) { return new Result { Message = L.T("hist.failed", ex.Message) }; }
            // everything recorded since the clearing stays; the copy fills in what came before it
            Save(Merge(Load(), old));
            cfg.HistoryClearedAt = null; cfg.HistoryBackup = null;
            GameLocator.SaveConfig(cfg);
            try { File.Delete(backup); } catch { /* its contents are back in history.json; a leftover copy harms nothing */ }
            return new Result { Ok = true, Message = L.T("hist.restored") };
        }
        catch (Exception ex) { return new Result { Message = L.T("hist.failed", ex.Message) }; }
    }
}

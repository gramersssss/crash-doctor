using System.Text.Json;

namespace CrashDoctor.Engine;

// RED4ext keeps only a handful of session logs. Crash Doctor remembers every session it has analysed so the trace
// keeps its history after the game rotates its logs. Stored in %APPDATA%\CrashDoctor\history.json.
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
}

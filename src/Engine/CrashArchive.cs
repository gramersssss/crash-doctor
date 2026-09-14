using System.IO.Compression;
using System.Text.Json;

namespace CrashDoctor.Engine;

// Keeps a copy of the logs from every crash, before the game throws them away.
//
// The game rotates its logs after a handful of launches. Of the first fifteen crashes at the Japantown fault, fourteen
// had lost their logs before anyone looked, which is why nobody ever found out what the game was doing when it died.
// The one that survived could only be examined because its logs were copied out by hand the next morning. This does
// that copying the moment a scan first sees a crash, for every crash, without being asked.
//
// Each crash becomes one zip in %APPDATA%\CrashDoctor\crash-logs: the engine's crash report (minidump, telemetry,
// screenshot), and that session's RED4ext, ArchiveXL and redscript logs, plus the CET logs written during it.
// ArchiveXL logs run to tens of megabytes of near-identical lines and compress about twenty to one, so a crash
// usually costs a few megabytes.
//
// One crashed log cannot be told apart from a normal session without something normal to compare it with, so the
// three most recent sessions that ended cleanly are kept the same way, in a rolling set.
//
// It only ever copies. Nothing in the game folder is touched, and it can be switched off under Settings.
public sealed class ArchiveEntry
{
    public string SessionId { get; set; } = "";
    public string Kind { get; set; } = "crash";   // crash | clean
    public DateTime Start { get; set; }
    public string? Fault { get; set; }
    public string Zip { get; set; } = "";         // file name inside the archive folder
    public long Bytes { get; set; }
    public int Files { get; set; }
    public bool LogsMissing { get; set; }         // only the crash report was left; the session's logs had already rotated
    public DateTime SavedAt { get; set; }
}

public sealed class ArchiveSummary
{
    public bool Enabled { get; set; } = true;
    public string Folder { get; set; } = "";
    public int Crashes { get; set; }
    public int Clean { get; set; }
    public long Bytes { get; set; }
    public List<ArchiveEntry> Entries { get; set; } = new();
    public List<string> SavedThisScan { get; set; } = new();   // session ids archived by this scan, for the "just saved" note
}

public static class CrashArchive
{
    public static string Folder => Path.Combine(GamePaths.AppData, "crash-logs");
    static string IndexFile => Path.Combine(Folder, "index.json");
    const int KeepClean = 3;
    const int KeepCrashes = 60;                 // oldest crash zips beyond this are removed
    const long MaxSingleFile = 400L * 1024 * 1024;
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static List<ArchiveEntry> Index()
    {
        try { if (File.Exists(IndexFile)) return JsonSerializer.Deserialize<List<ArchiveEntry>>(File.ReadAllText(IndexFile)) ?? new(); } catch { }
        return new();
    }

    static void SaveIndex(List<ArchiveEntry> idx)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(IndexFile, JsonSerializer.Serialize(idx.OrderBy(e => e.Start).ToList(), Opts));
    }

    public static ArchiveSummary Summary(List<string>? savedNow = null)
    {
        var idx = Index().Where(e => File.Exists(Path.Combine(Folder, e.Zip))).ToList();
        return new ArchiveSummary
        {
            Enabled = GameLocator.LoadConfig().KeepCrashLogs != false,
            Folder = Folder,
            Crashes = idx.Count(e => e.Kind == "crash"),
            Clean = idx.Count(e => e.Kind == "clean"),
            Bytes = idx.Sum(e => e.Bytes),
            Entries = idx.OrderByDescending(e => e.Start).ToList(),
            SavedThisScan = savedNow ?? new(),
        };
    }

    /// <summary>Archive every crash this scan can still see logs for, and refresh the rolling clean baseline.</summary>
    public static ArchiveSummary Preserve(CollectedData d, List<Session> sessions)
    {
        if (GameLocator.LoadConfig().KeepCrashLogs == false) return Summary();
        var idx = Index();
        var saved = new List<string>();

        var crashes = sessions.Where(s => s.EndKind is not (EndKind.Clean or EndKind.Running)).ToList();
        foreach (var s in crashes)
        {
            if (idx.Any(e => e.SessionId == s.Id)) continue;
            var entry = Save(d, s, "crash");
            if (entry == null) continue;
            idx.Add(entry); saved.Add(s.Id);
        }

        // the rolling baseline: the most recent clean sessions whose logs still exist
        var clean = sessions.Where(s => s.EndKind == EndKind.Clean && s.Red4extLog != null && File.Exists(s.Red4extLog))
                            .OrderByDescending(s => s.Start).Take(KeepClean).ToList();
        foreach (var s in clean)
        {
            if (idx.Any(e => e.SessionId == s.Id)) continue;
            var entry = Save(d, s, "clean");
            if (entry != null) idx.Add(entry);
        }
        foreach (var old in idx.Where(e => e.Kind == "clean").OrderByDescending(e => e.Start).Skip(KeepClean).ToList())
        {
            TryDelete(Path.Combine(Folder, old.Zip)); idx.Remove(old);
        }
        foreach (var old in idx.Where(e => e.Kind == "crash").OrderByDescending(e => e.Start).Skip(KeepCrashes).ToList())
        {
            TryDelete(Path.Combine(Folder, old.Zip)); idx.Remove(old);
        }

        if (saved.Count > 0 || clean.Count > 0) SaveIndex(idx);
        return Summary(saved);
    }

    static ArchiveEntry? Save(CollectedData d, Session s, string kind)
    {
        var files = new List<(string path, string inZip)>();
        var end = s.End ?? s.CrashTime ?? s.Start.AddHours(12);

        // the engine's own crash report folder, matched on the crash time
        if (kind == "crash" && s.CrashTime is { } ct)
        {
            var rep = d.CrashReports.Where(c => Math.Abs((c.At - ct).TotalSeconds) <= 90).OrderBy(c => Math.Abs((c.At - ct).TotalSeconds)).FirstOrDefault();
            if (rep != null && Directory.Exists(rep.Dir))
                foreach (var f in Directory.EnumerateFiles(rep.Dir, "*", SearchOption.AllDirectories))
                    files.Add((f, Path.Combine("crash-report", Path.GetRelativePath(rep.Dir, f))));
        }

        var logs = 0;
        if (s.Red4extLog != null && File.Exists(s.Red4extLog)) { files.Add((s.Red4extLog, Path.Combine("logs", Path.GetFileName(s.Red4extLog)))); logs++; }

        // ArchiveXL names its log after the launch time, a second or two after RED4ext's
        var axlDir = Path.Combine(d.Paths.Red4extPlugins, "ArchiveXL");
        if (Directory.Exists(axlDir))
        {
            var axl = Directory.EnumerateFiles(axlDir, "ArchiveXL-*.log")
                .Select(f => (f, t: StampOf(Path.GetFileNameWithoutExtension(f), "ArchiveXL-")))
                .Where(x => x.t is { } t && Math.Abs((t - s.Start).TotalSeconds) <= 30)
                .OrderBy(x => Math.Abs((x.t!.Value - s.Start).TotalSeconds)).Select(x => x.f).FirstOrDefault();
            if (axl != null) { files.Add((axl, Path.Combine("logs", Path.GetFileName(axl)))); logs++; }
        }

        // redscript renames its previous log using the NEXT launch's time, so the file that belongs to this session is
        // the one written shortly after this session started, whatever its name says
        if (Directory.Exists(d.Paths.RedscriptLogs))
        {
            var rs = Directory.EnumerateFiles(d.Paths.RedscriptLogs, "redscript_r*.log")
                .Where(f => File.GetLastWriteTime(f) >= s.Start.AddSeconds(-5) && File.GetLastWriteTime(f) <= s.Start.AddMinutes(5))
                .OrderBy(f => File.GetLastWriteTime(f)).FirstOrDefault();
            if (rs != null) files.Add((rs, Path.Combine("logs", "redscript-for-this-session.log")));
        }

        // CET's logs are continuous across launches, so a copy is only worth taking while it still covers this session:
        // written after it started, and not so long after it ended that it is mostly other sessions. A crash whose own
        // logs have already rotated gets none - today's CET log says nothing about a crash from last week.
        if (logs > 0 && Directory.Exists(d.Paths.Cet))
        {
            bool During(string f) { var t = File.GetLastWriteTime(f); return t >= s.Start && t <= end.AddHours(12); }
            foreach (var f in Directory.EnumerateFiles(d.Paths.Cet, "*.log").Where(During))
                files.Add((f, Path.Combine("cet", Path.GetFileName(f))));
            if (Directory.Exists(d.Paths.CetMods))
                foreach (var f in Directory.EnumerateFiles(d.Paths.CetMods, "*.log", SearchOption.AllDirectories).Where(During))
                    files.Add((f, Path.Combine("cet", "mods", Path.GetFileName(Path.GetDirectoryName(f)!), Path.GetFileName(f))));
        }

        if (files.Count == 0) return null;
        if (kind == "crash" && logs == 0 && !files.Any(x => x.inZip.StartsWith("crash-report"))) return null;

        var name = $"{s.Start:yyyy-MM-dd HHmm} {kind}{(s.Fault != null ? " " + s.Fault.Replace(":", "") : "")}.zip";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        Directory.CreateDirectory(Folder);
        var zip = Path.Combine(Folder, name);
        var tmp = zip + ".partial";
        try
        {
            TryDelete(tmp);
            using (var z = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                foreach (var (path, inZip) in files)
                {
                    try
                    {
                        if (new FileInfo(path).Length > MaxSingleFile) continue;
                        var e = z.CreateEntry(inZip.Replace('\\', '/'), CompressionLevel.Optimal);
                        e.LastWriteTime = File.GetLastWriteTime(path);
                        // shared read: the game or a loader may still hold a log open
                        using var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var dst = e.Open();
                        src.CopyTo(dst);
                    }
                    catch { /* one unreadable file must not cost the rest */ }
                }
                // a short note inside the zip, so it still makes sense when someone opens it months later
                using var w = new StreamWriter(z.CreateEntry("about.txt").Open());
                w.WriteLine($"Crash Doctor {App.Short} - saved {DateTime.Now:yyyy-MM-dd HH:mm}");
                w.WriteLine($"Session started {s.Start:yyyy-MM-dd HH:mm:ss}, {(s.DurationKnown ? $"ran {s.Minutes:0.#} min" : "length unknown")}, ended: {s.EndKind}.");
                if (s.Fault != null) w.WriteLine($"Fault: {s.Fault}{(s.Build != null ? " on game build " + s.Build : "")}");
                if (s.Exception != null) w.WriteLine($"Exception: {s.Exception}");
                if (s.VramUsedMB != null) w.WriteLine($"Video memory: {s.VramUsedMB} / {s.VramTotalMB} MB");
                if (!string.IsNullOrEmpty(s.Verdict)) w.WriteLine("\nVerdict at the time:\n" + s.Verdict);
                w.WriteLine("\nNote: redscript renames its previous log using the next launch's time, so its log here was chosen by when it was");
                w.WriteLine("written, not by its name, and saved as redscript-for-this-session.log. CET logs are continuous across launches.");
            }
            File.Move(tmp, zip, overwrite: true);
        }
        catch { TryDelete(tmp); return null; }

        return new ArchiveEntry
        {
            SessionId = s.Id, Kind = kind, Start = s.Start, Fault = s.Fault, Zip = name, Files = files.Count,
            Bytes = new FileInfo(zip).Length, LogsMissing = kind == "crash" && logs == 0, SavedAt = DateTime.Now,
        };
    }

    static DateTime? StampOf(string name, string prefix) =>
        name.StartsWith(prefix) && DateTime.TryParseExact(name[prefix.Length..], "yyyy-MM-dd-HH-mm-ss",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t) ? t : null;

    static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace CrashDoctor.Engine;

// Advanced Crash Reporter (Akiway, Nexus 34403). An optional companion: when it is installed, every crash and freeze
// leaves a text report in <game>\bin\x64\plugins\AdvancedCrashReporter\reports\ that knows things the minidump alone
// does not tell Crash Doctor - the NAME of the function that faulted, which mod's native code was running on the
// thread that died, the script stack, and what the mod logs said in the last ten seconds. Akiway agreed (Oct 2026)
// to Crash Doctor reading these and listing ACR as a requirement; it is never bundled or installed by us.
//
// The layout is ACR's and it changes between versions (1.1 -> 1.3 added Verdict and Technical lines and moved the
// exception block), so this reads by landmarks, never by line numbers, and every field is optional: a report it
// cannot make sense of is skipped, and a field it cannot find is simply left empty. What it relies on:
//   - the title line "# ADVANCED CRASH REPORTER - <what happened>"
//   - "Key : value" lines in the block under it (Generated, Process id, Faulting thread / Stuck thread, Summary, ...)
//   - "Exception code : 0x... (NAME)" and "Faulting code : 0x...  module  +0xOFFSET  (symbol)  [TAG]" anywhere
//   - section headings "# MOD INVOLVEMENT", "# MOD LOG ERRORS JUST BEFORE THE CRASH", "# REDENGINE REPORT ..."
//
// Its own verdict is kept and shown as ACR's reading, in its own words, never restated as Crash Doctor's conclusion.
public sealed class AcrReport
{
    public string File { get; set; } = "";
    public string? Version { get; set; }
    public DateTime At { get; set; }
    public string Kind { get; set; } = "crash";      // crash | hang
    public string Title { get; set; } = "";          // ACR's headline, e.g. "NATIVE FAULT (first chance)"
    public int? Pid { get; set; }
    public int? Thread { get; set; }
    public bool Watchdog { get; set; }               // the engine's watchdog ended a frozen game
    public double? StalledSeconds { get; set; }
    public string? Summary { get; set; }
    public string? Verdict { get; set; }
    public string? ExceptionCode { get; set; }
    public string? ExceptionName { get; set; }
    public string? Module { get; set; }              // where it faulted
    public string? Offset { get; set; }
    public string? Symbol { get; set; }              // e.g. game::StatData::AccumulateModifierValues+0xD9
    public string? ModuleTag { get; set; }           // GAME-EXE, MOD, SYSTEM, ...
    public string? StuckState { get; set; }          // for freezes: what the stuck thread was doing
    public List<AcrModule> Running { get; set; } = new();   // mod code executing on the thread that faulted or stalled
    public List<string> ScriptMods { get; set; } = new();
    public List<AcrLogLine> LogErrors { get; set; } = new();
    public bool KilledGame { get; set; }             // ACR matched this fault to the engine's own crash report
    public string? RedEngineFolder { get; set; }     // that crash report's folder name
    public string? GamePatch { get; set; }

    public string? Where => Module == null ? null : Module + (Offset != null ? "+0x" + Offset : "") + (Symbol != null ? " (" + Symbol + ")" : "");
}

public sealed class AcrModule { public string Name { get; set; } = ""; public string Tag { get; set; } = ""; }
public sealed class AcrLogLine { public double Seconds { get; set; } public string Log { get; set; } = ""; public string Text { get; set; } = ""; }

public static class AdvancedCrashReporter
{
    public const string NexusUrl = "https://www.nexusmods.com/cyberpunk2077/mods/34403";
    const string RelDir = @"bin\x64\plugins\AdvancedCrashReporter";
    const long MaxBytes = 4L * 1024 * 1024;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Whether ACR is installed (the .asi or its folder), through the MO2 merge when there is one.</summary>
    public static string? InstalledDir(GamePaths g)
    {
        if (!g.Exists(@"bin\x64\plugins\AdvancedCrashReporter.asi") && !g.DirExists(RelDir)) return null;
        return g.Dirs(RelDir).LastOrDefault() ?? Path.Combine(g.GameDir, RelDir);
    }

    public static List<AcrReport> Read(GamePaths g, DateTime since)
    {
        var list = new List<AcrReport>();
        var folders = new List<string>();
        foreach (var dir in g.Dirs(RelDir))
        {
            folders.Add(Path.Combine(dir, "reports"));
            // config.ini can send the reports elsewhere ([general] output_dir); a relative path is taken from ACR's folder
            var outDir = OutputDir(Path.Combine(dir, "config.ini"));
            if (outDir != null) folders.Add(Path.IsPathRooted(outDir) ? outDir : Path.Combine(dir, outDir));
        }
        var files = new List<string>();
        foreach (var f in folders.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
        {
            try { files.AddRange(Directory.EnumerateFiles(f, "*.txt", SearchOption.TopDirectoryOnly)); } catch { }
        }
        foreach (var f in files.Distinct(StringComparer.OrdinalIgnoreCase)
                     .Select(f => (f, t: SafeTime(f))).Where(x => x.t >= since).OrderByDescending(x => x.t).Take(200).Select(x => x.f))
        {
            try { var r = Parse(f); if (r != null) list.Add(r); } catch { /* one odd report must not cost the rest */ }
        }
        return list.OrderBy(r => r.At).ToList();
    }

    static string? OutputDir(string ini)
    {
        if (!File.Exists(ini)) return null;
        try
        {
            foreach (var line in Files.ReadAllLinesShared(ini))
            {
                var m = Regex.Match(line, @"^\s*output_dir\s*=\s*(.*?)\s*$", RegexOptions.IgnoreCase);
                if (m.Success && m.Groups[1].Value.Length > 0 && !m.Groups[1].Value.StartsWith(";")) return m.Groups[1].Value.Trim('"');
            }
        }
        catch { }
        return null;
    }

    public static AcrReport? Parse(string file)
    {
        if (new FileInfo(file).Length > MaxBytes) return null;
        var lines = Files.ReadAllLinesShared(file);
        var titleIdx = Array.FindIndex(lines, l => l.StartsWith("# ADVANCED CRASH REPORTER", StringComparison.OrdinalIgnoreCase));
        if (titleIdx < 0) return null;   // not one of ACR's reports
        var r = new AcrReport { File = file };
        var title = lines[titleIdx];
        var dash = title.IndexOf(" - ", StringComparison.Ordinal);
        r.Title = dash > 0 ? title[(dash + 3)..].Trim() : title.TrimStart('#', ' ');
        r.Kind = Regex.IsMatch(r.Title, "STALL|HANG|FROZE|FREEZE", RegexOptions.IgnoreCase) ? "hang" : "crash";
        var st = Regex.Match(r.Title, @"\((?:(\d+)m\s*)?([\d.]+)s\)");
        if (st.Success) r.StalledSeconds = (st.Groups[1].Success ? int.Parse(st.Groups[1].Value) * 60 : 0) + double.Parse(st.Groups[2].Value, Inv);

        // the header: "Key : value" lines until the next section
        for (int i = titleIdx + 1; i < lines.Length && !lines[i].StartsWith("# "); i++)
        {
            var m = Regex.Match(lines[i], @"^([A-Za-z][A-Za-z ]*?)\s*:\s*(.*)$");
            if (!m.Success) continue;
            var key = m.Groups[1].Value.Trim().ToLowerInvariant(); var val = m.Groups[2].Value.Trim();
            switch (key)
            {
                case "report version": r.Version = val; break;
                case "generated":
                    if (DateTime.TryParseExact(val, new[] { "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd HH:mm:ss" }, Inv, DateTimeStyles.None, out var at)) r.At = at;
                    break;
                case "process id": if (int.TryParse(Regex.Match(val, @"\d+").Value, out var pid)) r.Pid = pid; break;
                case "faulting thread":
                case "stuck thread":
                    if (int.TryParse(Regex.Match(val, @"\d+").Value, out var tid)) r.Thread = tid;
                    if (val.Contains("WATCHDOG", StringComparison.OrdinalIgnoreCase)) r.Watchdog = true;
                    break;
                case "summary": r.Summary = val; break;
                case "verdict": r.Verdict = val; break;
            }
        }
        if (r.At == default)
        {
            // fall back to the file name ("20261014-213759-crash-..."), then to the file's own time
            var fm = Regex.Match(Path.GetFileName(file), @"^(\d{8})-(\d{6})");
            r.At = fm.Success && DateTime.TryParseExact(fm.Groups[1].Value + fm.Groups[2].Value, "yyyyMMddHHmmss", Inv, DateTimeStyles.None, out var t) ? t : SafeTime(file);
        }

        string? section = null;
        string? sub = null;
        for (int i = titleIdx + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("# ")) { section = line[2..].Trim().ToUpperInvariant(); sub = null; continue; }
            if (line.StartsWith("=====")) continue;

            var ex = Regex.Match(line, @"^Exception code\s*:\s*(0x[0-9A-Fa-f]+)\s*(?:\(([^)]+)\))?");
            if (ex.Success && r.ExceptionCode == null) { r.ExceptionCode = ex.Groups[1].Value; r.ExceptionName = ex.Groups[2].Success ? ex.Groups[2].Value.Trim() : null; continue; }
            var fc = Regex.Match(line, @"^Faulting code\s*:\s*0x[0-9A-Fa-f]+\s+(\S+)\s+\+0x([0-9A-Fa-f]+)\s*(?:\((.+?)\))?\s*(?:\[([^\]]+)\])?\s*$");
            if (fc.Success && r.Module == null)
            {
                r.Module = fc.Groups[1].Value; r.Offset = fc.Groups[2].Value.ToUpperInvariant();
                r.Symbol = fc.Groups[3].Success ? fc.Groups[3].Value.Trim() : null; r.ModuleTag = fc.Groups[4].Success ? fc.Groups[4].Value.Trim() : null;
                if (r.Symbol != null && r.Symbol.Contains("Watchdog", StringComparison.OrdinalIgnoreCase)) r.Watchdog = true;
                continue;
            }
            if (section == null) continue;

            if (section.StartsWith("WHERE") && section.Contains("STUCK") || section.StartsWith("WHERE EXECUTION STALLED"))
            {
                var sm = Regex.Match(line, @"^State\s*:\s*(.+)$");
                if (sm.Success && r.StuckState == null) r.StuckState = sm.Groups[1].Value.Trim();
            }
            else if (section.StartsWith("MOD INVOLVEMENT"))
            {
                if (Regex.IsMatch(line, @"^Mod code RUNNING", RegexOptions.IgnoreCase)) { sub = "running"; continue; }
                if (Regex.IsMatch(line, @"^Script mod RUNNING", RegexOptions.IgnoreCase)) { sub = "script"; continue; }
                if (line.Length > 0 && !char.IsWhiteSpace(line[0])) { sub = null; continue; }
                if (sub == null || line.Trim().Length == 0 || line.TrimStart().StartsWith("->") || line.Trim().StartsWith("none", StringComparison.OrdinalIgnoreCase) || line.StartsWith("     ")) continue;
                var mm = Regex.Match(line, @"^\s{1,4}(\S+)\s+\[\s*([A-Z0-9\- ]+?)\s*\]");
                if (sub == "running" && mm.Success) r.Running.Add(new AcrModule { Name = mm.Groups[1].Value, Tag = mm.Groups[2].Value.Trim() });
                else if (sub == "script") { var nm = Regex.Match(line, @"^\s{1,4}(\S.*?)\s*$"); if (nm.Success) r.ScriptMods.Add(nm.Groups[1].Value); }
            }
            else if (section.StartsWith("MOD LOG ERRORS"))
            {
                var lm = Regex.Match(line, @"^\s+([\d.]+)s before\s+(\S+)");
                if (lm.Success && r.LogErrors.Count < 6)
                {
                    var text = i + 1 < lines.Length ? lines[i + 1].Trim() : "";
                    r.LogErrors.Add(new AcrLogLine { Seconds = double.Parse(lm.Groups[1].Value, Inv), Log = lm.Groups[2].Value, Text = text.Length > 220 ? text[..220] + "…" : text });
                }
            }
            else if (section.StartsWith("REDENGINE REPORT"))
            {
                var fm = Regex.Match(line, @"^Folder\s*:\s*(Cyberpunk2077-[\w-]+)");
                if (fm.Success) r.RedEngineFolder = fm.Groups[1].Value;
                if (line.Contains("Same thread and same address", StringComparison.OrdinalIgnoreCase)) r.KilledGame = true;
                var gp = Regex.Match(line, @"Game/Patch\s*=\s*(\S+)");
                if (gp.Success) r.GamePatch = gp.Groups[1].Value;
            }
        }
        return r;
    }

    /// <summary>
    /// The ACR report that belongs to a crash: the same process as the engine's crash report folder when both are
    /// known (its name carries the process id), else the nearest report written in the minute before the crash.
    /// Of several in one process - ACR also reports faults the game survived - the one that killed it, then the last.
    /// </summary>
    public static AcrReport? For(CollectedData d, DateTime crashAt, string? reportDir, DateTime sessionStart)
    {
        if (d.AcrReports.Count == 0) return null;
        int? pid = null;
        if (reportDir != null)
        {
            var m = Regex.Match(Path.GetFileName(reportDir), @"^Cyberpunk2077-\d{8}-\d{6}-(\d+)-(\d+)");
            if (m.Success) pid = int.Parse(m.Groups[1].Value);
        }
        var pool = pid != null
            ? d.AcrReports.Where(a => a.Pid == pid || (a.RedEngineFolder != null && reportDir != null && Path.GetFileName(reportDir).Equals(a.RedEngineFolder, StringComparison.OrdinalIgnoreCase))).ToList()
            : new List<AcrReport>();
        if (pool.Count == 0)
            pool = d.AcrReports.Where(a => a.At >= sessionStart.AddSeconds(-5) && a.At <= crashAt.AddSeconds(30) && a.At >= crashAt.AddMinutes(-3)).ToList();
        if (pool.Count == 0) return null;
        return pool.FirstOrDefault(a => a.KilledGame)
            ?? pool.Where(a => a.Kind == "crash").OrderByDescending(a => a.At).FirstOrDefault()
            ?? pool.OrderByDescending(a => a.At).First();
    }

    /// <summary>Freeze reports ACR wrote for the same process before it died, longest stall last.</summary>
    public static List<AcrReport> HangsBefore(CollectedData d, AcrReport final) =>
        d.AcrReports.Where(a => a.Kind == "hang" && a != final && (final.Pid == null || a.Pid == final.Pid) && a.At <= final.At && a.At >= final.At.AddMinutes(-10))
                    .OrderBy(a => a.At).ToList();

    static DateTime SafeTime(string f) { try { return System.IO.File.GetLastWriteTime(f); } catch { return DateTime.MinValue; } }
}

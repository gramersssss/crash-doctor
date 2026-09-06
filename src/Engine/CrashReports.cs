using System.Globalization;
using System.Text.RegularExpressions;

namespace CrashDoctor.Engine;

// Every crash writes a folder under %LOCALAPPDATA%\REDEngine\ReportQueue\Cyberpunk2077-<date>-<time>-<pid>-<pid>\.
// It is the only place the engine records how much video memory was in use when it died, what the exception was, and
// where the player was standing. The game's own logs say none of that, which is why crashes that are really just the
// card running out of memory look like they have no cause at all.
public sealed class CrashReport
{
    public DateTime At { get; set; }
    public string Dir { get; set; } = "";
    public int VramUsedMB { get; set; }
    public int VramTotalMB { get; set; }
    public string? Gpu { get; set; }
    public bool EngineOom { get; set; }          // the engine's own "ran out of system memory" flag
    public string? Exception { get; set; }        // e.g. "EXCEPTION_ACCESS_VIOLATION (0xC0000005)"
    public string? ExceptionDetail { get; set; }  // e.g. "The thread attempted to read inaccessible data at 0x..."
    public string? Position { get; set; }         // last streaming observer position, "[-652, 484, 22]"
    public string? Screenshot { get; set; }       // the frame the game was showing when it died

    public double VramRatio => VramTotalMB > 0 ? (double)VramUsedMB / VramTotalMB : 0;
    public bool VramKnown => VramTotalMB > 0 && VramUsedMB > 0;
}

public static class CrashReports
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly Regex DirName = new(@"^Cyberpunk2077-(\d{8})-(\d{6})-", RegexOptions.Compiled);

    public static List<CrashReport> Read(DateTime since)
    {
        var list = new List<CrashReport>();
        var queue = GamePaths.ReportQueue;
        if (!Directory.Exists(queue)) return list;
        foreach (var dir in Directory.EnumerateDirectories(queue))
        {
            var m = DirName.Match(Path.GetFileName(dir));
            if (!m.Success) continue;
            if (!DateTime.TryParseExact(m.Groups[1].Value + m.Groups[2].Value, "yyyyMMddHHmmss", Inv, DateTimeStyles.None, out var at)) continue;
            if (at < since) continue;
            var r = new CrashReport { At = at, Dir = dir };
            try { ReadStack(r); } catch { }
            try { ReadTelemetry(r); } catch { }
            try { var shot = Path.Combine(dir, "attch", "screenshot.png"); if (File.Exists(shot)) r.Screenshot = shot; } catch { }
            list.Add(r);
        }
        return list.OrderBy(x => x.At).ToList();
    }

    static void ReadStack(CrashReport r)
    {
        var f = Path.Combine(r.Dir, "stacktrace.txt");
        if (!File.Exists(f)) return;
        string? reason = null, expr = null;
        foreach (var line in Files.ReadAllLinesShared(f))
        {
            if (line.StartsWith("Error reason:")) reason = line[13..].Trim();
            else if (line.StartsWith("Expression:")) expr = line[11..].Trim();
            else if (line.StartsWith("Message:")) r.ExceptionDetail = line[8..].Trim();
        }
        r.Exception = expr ?? reason;
        if (expr != null && reason != null && !expr.Contains(reason, StringComparison.OrdinalIgnoreCase)) r.Exception = expr;
    }

    // The attachment is the engine's telemetry dump: thousands of "key@id#TID=0":"value" pairs.
    static void ReadTelemetry(CrashReport r)
    {
        var attch = Path.Combine(r.Dir, "attch");
        if (!Directory.Exists(attch)) return;
        var f = Directory.EnumerateFiles(attch, "*.txt").OrderByDescending(x => new FileInfo(x).Length).FirstOrDefault();
        if (f == null) return;
        var txt = Files.ReadAllTextShared(f);
        r.VramUsedMB = Num(txt, "Gpu/Device/UsedMemoryMB");
        r.VramTotalMB = Num(txt, "Gpu/Device/TotalMemoryMB");
        r.Gpu = Str(txt, "Gpu/Device/Name");
        r.EngineOom = string.Equals(Str(txt, "Engine/OOM"), "true", StringComparison.OrdinalIgnoreCase);
        r.Position = Str(txt, "Streaming/LastObserverPosition");
    }

    static string? Str(string txt, string key)
    {
        var m = Regex.Match(txt, "\"" + Regex.Escape(key) + "@" + @"\d+#TID=\d+" + "\"" + @"\s*:\s*" + "\"([^\"]*)\"");
        return m.Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : null;
    }
    static int Num(string txt, string key) => int.TryParse(Str(txt, key), NumberStyles.Integer, Inv, out var v) ? v : 0;
}

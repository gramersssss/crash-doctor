using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Management;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CrashDoctor.Engine;

// Raw facts gathered from disk. No interpretation here; the Analyzer does that.

public sealed class Red4extSession
{
    public string File { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime LastWrite { get; set; }
    public DateTime? CleanShutdown { get; set; }
    public DateTime? CrashAt { get; set; }
    public string? CrashMessage { get; set; }
    public string? CrashFile { get; set; }
    public string ProductVersion { get; set; } = "";
    public string FileVersion { get; set; } = "";
    public string Red4extVersion { get; set; } = "";
    public List<(string name, string version)> PluginsLoaded { get; set; } = new();
    public List<string> Incompatible { get; set; } = new();  // raw warning lines
}

// ArchiveXL logs every world sector it patches as it streams in, and names each .xl file applied to it. That makes it
// possible to say exactly which mods were rewriting the ground the player was standing on when the game died — and
// which of them were doing it to the same sector as each other.
public sealed class SectorPatch
{
    public DateTime At { get; set; }
    public string Sector { get; set; } = "";      // short name, e.g. exterior_-3_2_0_3
    public List<string> Xls { get; set; } = new(); // the .xl files applied to it
    public bool Incomplete { get; set; }           // ArchiveXL reported some patches were skipped
}

public sealed class LogEvent
{
    public DateTime At { get; set; }
    public string Source { get; set; } = "";   // "CET · <mod>", "ArchiveXL", "redscript", "Windows · NVIDIA driver", ...
    public string Text { get; set; } = "";
    public string Kind { get; set; } = "";     // error | warning | activity | driver | appcrash | power
    public string? Mod { get; set; }           // mod folder / xl name if known
}

public sealed class CollectedData
{
    public GamePaths Paths { get; init; } = null!;
    public List<Red4extSession> Red4ext { get; } = new();
    public List<DateTime> CrashReporterTimes { get; } = new();
    public List<CrashReport> CrashReports { get; set; } = new();
    public List<LogEvent> Events { get; } = new();          // everything with a timestamp
    public List<SectorPatch> SectorPatches { get; } = new();
    public Dictionary<string, string> Settings { get; } = new();
    public DateTime? SettingsWritten { get; set; }
    public JsonElement? CrashInfo { get; set; }
    public string CetVersion { get; set; } = "";
    public string CetGameVersion { get; set; } = "";
    public List<string> RedscriptWarnings { get; } = new();
    public List<string> RedscriptErrors { get; } = new();
    public List<string> Warnings { get; } = new();          // "could not read X"
    public SystemInfo System { get; set; } = new();
    public ModInventory Mods { get; set; } = new();
}

public static class Collectors
{
    static readonly Regex TsBracket = new(@"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})(?:\.\d+)?(?: UTC[+-]\d{2}:\d{2})?\]", RegexOptions.Compiled);
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static CollectedData CollectAll(GamePaths g, DateTime since)
    {
        var d = new CollectedData { Paths = g };
        Safe(d, "RED4ext logs", () => ReadRed4ext(d, since));
        Safe(d, "crash reporter log", () => ReadCrashReporter(d, since));
        Safe(d, "crash reports", () => ReadCrashReports(d, since));
        Safe(d, "CrashInfo.json", () => ReadCrashInfo(d));
        Safe(d, "Cyber Engine Tweaks logs", () => ReadCet(d, since));
        Safe(d, "ArchiveXL logs", () => ReadArchiveXL(d, since));
        Safe(d, "redscript log", () => ReadRedscript(d));
        Safe(d, "Windows event log", () => ReadEventLogs(d, since));
        Safe(d, "UserSettings.json", () => ReadSettings(d));
        Safe(d, "system information", () => d.System = ReadSystem());
        Safe(d, "mod list", () => d.Mods = ModInventory.Read(g));
        return d;
    }

    // CRASHDOCTOR_TIMING=1 prints how long each source took. A scan that crawls on someone else's machine is
    // otherwise impossible to diagnose remotely, and the cost is one environment variable check per source.
    static readonly bool Timing = Environment.GetEnvironmentVariable("CRASHDOCTOR_TIMING") == "1";
    static void Safe(CollectedData d, string what, Action a)
    {
        var sw = Timing ? System.Diagnostics.Stopwatch.StartNew() : null;
        try { a(); }
        catch (Exception ex) { d.Warnings.Add($"Could not read {what}: {ex.Message}"); }
        finally { if (sw != null) Console.Error.WriteLine($"  [timing] {sw.ElapsedMilliseconds,7} ms  {what}"); }
    }

    static bool TryTs(string line, out DateTime at)
    {
        at = default; var m = TsBracket.Match(line); if (!m.Success) return false;
        return DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", Inv, DateTimeStyles.None, out at);
    }

    // ---------------- RED4ext ----------------
    static void ReadRed4ext(CollectedData d, DateTime since)
    {
        if (!Directory.Exists(d.Paths.Red4extLogs)) return;
        foreach (var f in Directory.EnumerateFiles(d.Paths.Red4extLogs, "red4ext-*.log").OrderBy(x => x))
        {
            var fi = new FileInfo(f); if (fi.LastWriteTime < since) continue;
            var s = new Red4extSession { File = f, LastWrite = fi.LastWriteTime };
            string[] lines; try { lines = Files.ReadAllLinesShared(f); } catch { continue; }
            bool inCrash = false;
            foreach (var line in lines)
            {
                if (!TryTs(line, out var at)) continue;
                if (s.Start == default) s.Start = at;
                if (line.Contains("RED4ext (v")) { var m = Regex.Match(line, @"RED4ext \(v([\d.]+)\)"); if (m.Success) s.Red4extVersion = m.Groups[1].Value; }
                else if (line.Contains("Product version:")) s.ProductVersion = line[(line.IndexOf("Product version:") + 16)..].Trim();
                else if (line.Contains("File version:")) s.FileVersion = line[(line.IndexOf("File version:") + 13)..].Trim();
                else if (line.Contains("has been loaded")) { var m = Regex.Match(line, @"\[RED4ext\] (.+?) \(version: ([^,)]+)"); if (m.Success) s.PluginsLoaded.Add((m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim())); }
                else if (line.Contains("is incompatible with the current patch")) s.Incompatible.Add(line[(line.IndexOf("[RED4ext]") + 9)..].Trim());
                else if (line.Contains("RED4ext has been shut down")) s.CleanShutdown = at;
                else if (line.Contains("[RED4ext] Crash report")) { inCrash = true; s.CrashAt = at; }
                else if (inCrash && line.Contains("File:")) s.CrashFile = line[(line.IndexOf("File:") + 5)..].Trim();
                else if (inCrash && line.Contains("Message:")) { s.CrashMessage = line[(line.IndexOf("Message:") + 8)..].Trim(); inCrash = false; }
            }
            if (s.Start != default) d.Red4ext.Add(s);
        }
    }

    // ---------------- crash reporter ----------------
    static void ReadCrashReporter(CollectedData d, DateTime since)
    {
        if (!File.Exists(GamePaths.CrashReporterLog)) return;
        foreach (var line in Files.ReadLinesShared(GamePaths.CrashReporterLog))
        {
            var m = Regex.Match(line, @"Matching crash data directory: 'Cyberpunk2077-(\d{8})-(\d{6})-");
            if (!m.Success) continue;
            if (DateTime.TryParseExact(m.Groups[1].Value + m.Groups[2].Value, "yyyyMMddHHmmss", Inv, DateTimeStyles.None, out var at) && at >= since) d.CrashReporterTimes.Add(at);
        }
    }

    // The ReportQueue folders are the authoritative list of crashes: one folder per crash, each carrying the engine's
    // own telemetry. CrashReporter.log can miss entries (it only logs when the reporter app actually ran), so any time
    // found here that the log did not mention is added to the list of crash times.
    static void ReadCrashReports(CollectedData d, DateTime since)
    {
        d.CrashReports = CrashReports.Read(since);
        foreach (var cr in d.CrashReports)
            if (!d.CrashReporterTimes.Any(t => Math.Abs((t - cr.At).TotalSeconds) < 90)) d.CrashReporterTimes.Add(cr.At);
        d.CrashReporterTimes.Sort();
    }

    static void ReadCrashInfo(CollectedData d)
    {
        if (!File.Exists(GamePaths.CrashInfo)) return;
        var txt = Files.ReadAllTextShared(GamePaths.CrashInfo); if (string.IsNullOrWhiteSpace(txt)) return;
        using var doc = JsonDocument.Parse(txt);
        if (doc.RootElement.TryGetProperty("Data", out var data) && data.TryGetProperty("postMortem", out var pm)) d.CrashInfo = pm.Clone();
    }

    // ---------------- CET ----------------
    static readonly Regex Errorish = new(@"\[error\]|\berror\b|attempt to (call|index|compare|perform)|nil value|stack traceback|must be '|Function '.*' context|bad argument|not found|failed", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static void ReadCet(CollectedData d, DateTime since)
    {
        var main = Path.Combine(d.Paths.Cet, "cyber_engine_tweaks.log");
        if (File.Exists(main))
            foreach (var line in SafeLines(main))
            {
                if (line.Contains("CET version")) { var m = Regex.Match(line, @"CET version v?([\w.\-\[\] ]+)"); if (m.Success) d.CetVersion = m.Groups[1].Value.Trim(); }
                else if (line.Contains("Game version")) { var m = Regex.Match(line, @"Game version ([\d.]+)"); if (m.Success) d.CetGameVersion = m.Groups[1].Value; }
            }
        var scripting = Path.Combine(d.Paths.Cet, "scripting.log");
        if (File.Exists(scripting)) { try { AddCetLog(d, scripting, "CET · scripting", null, since); } catch (Exception ex) { d.Warnings.Add("CET scripting.log: " + ex.Message); } }
        if (Directory.Exists(d.Paths.CetMods))
            foreach (var dir in Directory.EnumerateDirectories(d.Paths.CetMods))
            {
                var mod = Path.GetFileName(dir);
                foreach (var f in Directory.EnumerateFiles(dir, "*.log", SearchOption.TopDirectoryOnly))
                    if (new FileInfo(f).LastWriteTime >= since) { try { AddCetLog(d, f, "CET · " + mod, mod, since); } catch { } }
            }
    }
    static IEnumerable<string> SafeLines(string f) { try { return Files.ReadAllLinesShared(f); } catch { return Array.Empty<string>(); } }
    static void AddCetLog(CollectedData d, string file, string source, string? mod, DateTime since)
    {
        string[] lines; try { lines = Files.ReadAllLinesShared(file); } catch { return; }
        DateTime? cur = null; string? head = null; var trace = new List<string>();
        void Flush() { if (cur is DateTime at && head != null && at >= since && Errorish.IsMatch(head)) d.Events.Add(new LogEvent { At = at, Source = source, Text = head.Length > 220 ? head[..220] + "…" : head, Kind = "error", Mod = mod }); head = null; trace.Clear(); }
        foreach (var raw in lines)
        {
            if (TryTs(raw, out var at)) { Flush(); cur = at; head = Regex.Replace(raw, @"^\[[^\]]+\]\s*(\[\d+\]\s*)?", "").Trim(); }
            else if (head != null && raw.StartsWith("\t") || raw.StartsWith("    ")) trace.Add(raw.Trim());
        }
        Flush();
    }

    // ---------------- ArchiveXL ----------------
    static void ReadArchiveXL(CollectedData d, DateTime since)
    {
        var dir = Path.Combine(d.Paths.Red4extPlugins, "ArchiveXL"); if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "ArchiveXL-*.log"))
        {
            if (new FileInfo(f).LastWriteTime < since) continue;
            string[] lines; try { lines = Files.ReadAllLinesShared(f); } catch { continue; }
            var seenErr = new HashSet<string>();
            // sector-patch blocks interleave between worker threads, so each is tracked per thread id
            var open = new Dictionary<string, SectorPatch>();
            var patches = new List<SectorPatch>();
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i]; if (!TryTs(line, out var at)) continue;
                var tid = Regex.Match(line, @"\]\s*\[(\d+)\]").Groups[1].Value;
                var body = Regex.Replace(line, @"^\[[^\]]+\]\s*\[\d+\]\s*", "");
                if (body.Contains("[WorldStreaming]"))
                {
                    var start = Regex.Match(body, @"Patching sector ""([^""]+)""");
                    if (start.Success) { open[tid] = new SectorPatch { At = at, Sector = ShortSector(start.Groups[1].Value) }; }
                    else if (open.TryGetValue(tid, out var cur))
                    {
                        var apply = Regex.Match(body, @"Applying changes from ""([^""]+)""");
                        if (apply.Success) cur.Xls.Add(apply.Groups[1].Value);
                        else if (body.Contains("patches have been applied to"))
                        {
                            if (body.Contains("Some patches have not been applied")) cur.Incomplete = true;
                            if (cur.Xls.Count > 0) patches.Add(cur);
                            open.Remove(tid);
                        }
                    }
                }
                if (body.StartsWith("[error]") || body.StartsWith("[warning]"))
                {
                    var kind = body.StartsWith("[error]") ? "xl-error" : "xl-warning";
                    var text = body[(body.IndexOf(']') + 1)..].Trim();
                    var key = Regex.Replace(text, "\"[^\"]*\"", "\"\"");
                    if (seenErr.Add(text)) d.Events.Add(new LogEvent { At = at, Source = "ArchiveXL", Text = text.Length > 260 ? text[..260] + "…" : text, Kind = kind, Mod = XlOwner(text) });
                }
                else if (i >= lines.Length - 3 && body.StartsWith("[info]"))
                {
                    // last lines of each session log = what ArchiveXL was doing when the session ended
                    var text = body[6..].Trim();
                    var summary = text.Contains("h1_base_color_patch") ? "NPC loading (hair appearance patch applied)" : text.Contains("WorldStreaming") ? Regex.Replace(text, @"^\[WorldStreaming\]\s*", "world streaming: ") : text.Length > 160 ? text[..160] + "…" : text;
                    if (i == lines.Length - 1) d.Events.Add(new LogEvent { At = at, Source = "ArchiveXL", Text = summary, Kind = "activity" });
                }
            }
            if (patches.Count > 0)
            {
                var last = patches[^1].At;
                d.SectorPatches.AddRange(patches.Where(x => x.At >= last.AddMinutes(-5)));
            }
        }
    }

    // "base\worlds\...\exterior_-3_2_0_3.streamingsector" -> "exterior_-3_2_0_3" (no separator literals needed)
    static string ShortSector(string path) => Path.GetFileNameWithoutExtension(path);

    static string? XlOwner(string text) { var m = Regex.Match(text, @"([\w\-. ]+\.xl):"); return m.Success ? m.Groups[1].Value.Trim() : null; }

    // ---------------- redscript ----------------
    static void ReadRedscript(CollectedData d)
    {
        var f = Path.Combine(d.Paths.RedscriptLogs, "redscript_rCURRENT.log"); if (!File.Exists(f)) return;
        var lines = Files.ReadAllLinesShared(f);
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("[WARN") || lines[i].StartsWith("[ERROR"))
            {
                var block = new List<string> { lines[i] }; for (int j = i + 1; j < Math.Min(lines.Length, i + 5) && !lines[j].StartsWith("["); j++) block.Add(lines[j].Trim());
                var txt = string.Join(" ", block.Where(x => x.Length > 0)).Replace(d.Paths.GameDir, "").Trim();
                (lines[i].StartsWith("[ERROR") ? d.RedscriptErrors : d.RedscriptWarnings).Add(txt.Length > 300 ? txt[..300] + "…" : txt);
            }
        }
    }

    // ---------------- Windows event log ----------------
    static void ReadEventLogs(CollectedData d, DateTime since)
    {
        var sinceUtc = since.ToUniversalTime().ToString("o");
        Query(d, "System", $"*[System[Provider[@Name='nvlddmkm'] and TimeCreated[@SystemTime>='{sinceUtc}']]]", e => new LogEvent { At = e.TimeCreated ?? DateTime.MinValue, Source = "Windows · NVIDIA driver", Text = $"nvlddmkm event {e.Id}: the display driver reported a GPU error", Kind = "driver" });
        Query(d, "System", $"*[System[Provider[@Name='amdkmdag' or @Name='amdkmdap'] and TimeCreated[@SystemTime>='{sinceUtc}']]]", e => new LogEvent { At = e.TimeCreated ?? DateTime.MinValue, Source = "Windows · AMD driver", Text = $"AMD display driver event {e.Id}", Kind = "driver" });
        Query(d, "System", $"*[System[(EventID=4101) and TimeCreated[@SystemTime>='{sinceUtc}']]]", e => new LogEvent { At = e.TimeCreated ?? DateTime.MinValue, Source = "Windows · display", Text = "Display driver stopped responding and was reset (TDR)", Kind = "driver" });
        Query(d, "System", $"*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=41) and TimeCreated[@SystemTime>='{sinceUtc}']]]", e => new LogEvent { At = e.TimeCreated ?? DateTime.MinValue, Source = "Windows · power", Text = "The PC restarted without shutting down cleanly (power loss, overheat, or hard reset)", Kind = "power" });
        Query(d, "Application", $"*[System[Provider[@Name='Application Error'] and (EventID=1000) and TimeCreated[@SystemTime>='{sinceUtc}']]]", e =>
        {
            string msg = ""; try { msg = e.FormatDescription() ?? ""; } catch { }
            if (!msg.Contains("Cyberpunk2077", StringComparison.OrdinalIgnoreCase)) return null;
            var mod = Regex.Match(msg, @"Faulting module name:\s*([^,\r\n]+)"); var code = Regex.Match(msg, @"Exception code:\s*(0x[0-9a-fA-F]+)");
            return new LogEvent { At = e.TimeCreated ?? DateTime.MinValue, Source = "Windows · application error", Text = $"Cyberpunk2077.exe faulted in {(mod.Success ? mod.Groups[1].Value.Trim() : "unknown module")}{(code.Success ? " (" + code.Groups[1].Value + ")" : "")}", Kind = "appcrash", Mod = mod.Success ? mod.Groups[1].Value.Trim() : null };
        });
    }
    static void Query(CollectedData d, string log, string xpath, Func<EventRecord, LogEvent?> map)
    {
        try
        {
            using var reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, xpath));
            for (var e = reader.ReadEvent(); e != null; e = reader.ReadEvent()) { using (e) { var ev = map(e); if (ev != null) d.Events.Add(ev); } }
        }
        catch (Exception ex) { d.Warnings.Add($"Event log '{log}' query failed: {ex.Message}"); }
    }

    // ---------------- settings ----------------
    static readonly string[] SettingKeys = { "FrameGeneration", "FSR3_FrameGeneration", "DLSSFrameGen", "ResolutionScaling", "DLSS", "DLSS_BackendPreset", "FSR3", "XESS", "RayTracing", "RayTracedLighting", "RayTracedReflections", "RayTracedSunShadows", "RayTracedLocalShadows", "RayTracedPathTracing", "TextureQuality", "Resolution", "WindowMode", "VSync", "ReflexMode", "CrowdDensity", "ScreenSpaceReflectionsQuality", "VolumetricFogResolution", "CascadedShadowsResolution", "DistantShadowsResolution", "MirrorQuality", "ColorPrecision", "AmbientOcclusion", "MaxDynamicDecals", "HDRModes" };
    static void ReadSettings(CollectedData d)
    {
        if (!File.Exists(GamePaths.UserSettings)) return;
        d.SettingsWritten = File.GetLastWriteTime(GamePaths.UserSettings);
        using var doc = JsonDocument.Parse(Files.ReadAllTextShared(GamePaths.UserSettings));
        void Walk(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("name", out var n) && el.TryGetProperty("value", out var v) && n.ValueKind == JsonValueKind.String)
                {
                    var key = n.GetString()!; if (SettingKeys.Contains(key) && !d.Settings.ContainsKey(key)) d.Settings[key] = v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ToString();
                }
                foreach (var p in el.EnumerateObject()) Walk(p.Value);
            }
            else if (el.ValueKind == JsonValueKind.Array) foreach (var x in el.EnumerateArray()) Walk(x);
        }
        Walk(doc.RootElement);
    }

    // ---------------- system ----------------
    public static SystemInfo ReadSystem()
    {
        var s = new SystemInfo { Os = Environment.OSVersion.VersionString, Machine = Environment.MachineName };
        try
        {
            using var os = new ManagementObjectSearcher("select Caption, Version from Win32_OperatingSystem");
            foreach (ManagementObject o in os.Get()) { s.Os = ($"{o["Caption"]} {o["Version"]}").Replace("Microsoft ", "").Trim(); break; }
        }
        catch { }
        try
        {
            using var cs = new ManagementObjectSearcher("select Model, TotalPhysicalMemory, PCSystemType from Win32_ComputerSystem");
            foreach (ManagementObject o in cs.Get()) { s.Machine = o["Model"]?.ToString() ?? s.Machine; s.RamGB = (int)Math.Round(Convert.ToDouble(o["TotalPhysicalMemory"]) / 1073741824.0); s.Laptop = Convert.ToInt32(o["PCSystemType"] ?? 0) == 2; break; }
        }
        catch { }
        try
        {
            using var vc = new ManagementObjectSearcher("select Name, DriverVersion, DriverDate, AdapterRAM, PNPDeviceID from Win32_VideoController");
            var gpus = vc.Get().Cast<ManagementObject>().ToList();
            var pick = gpus.FirstOrDefault(g => (g["Name"]?.ToString() ?? "").Contains("NVIDIA") || (g["Name"]?.ToString() ?? "").Contains("AMD") || (g["Name"]?.ToString() ?? "").Contains("Radeon")) ?? gpus.FirstOrDefault();
            if (pick != null)
            {
                s.Gpu = pick["Name"]?.ToString() ?? ""; var drv = pick["DriverVersion"]?.ToString() ?? "";
                s.Driver = s.Gpu.Contains("NVIDIA") ? NvidiaFriendly(drv) : drv;
                var dd = pick["DriverDate"]?.ToString(); if (!string.IsNullOrEmpty(dd)) { try { s.DriverDate = ManagementDateTimeConverter.ToDateTime(dd); } catch { } }
                s.VramGB = VramFromRegistry(pick["PNPDeviceID"]?.ToString()) ?? (int)Math.Round(Convert.ToDouble(pick["AdapterRAM"] ?? 0) / 1073741824.0);
                if (s.Laptop && s.Gpu.Contains("RTX") && !s.Gpu.Contains("Laptop")) s.Gpu += " (laptop)";
            }
        }
        catch { }
        return Override(s);
    }

    // CRASHDOCTOR_TEST_ROOT redirects the file roots, which is enough for every rule that reads a file. Two rules
    // read the machine instead - the card's size and the driver's date - and a fixture cannot fake hardware, so
    // those two could never be watched firing on a developer's own PC. This is the seam that lets them be:
    //
    //   set CRASHDOCTOR_TEST_SYSTEM=vramgb=8;ramgb=16;driverdate=2023-04-01
    //
    // Ignored entirely unless the variable is set, and it only ever replaces what WMI already reported.
    static SystemInfo Override(SystemInfo s)
    {
        var spec = Environment.GetEnvironmentVariable("CRASHDOCTOR_TEST_SYSTEM");
        if (string.IsNullOrWhiteSpace(spec)) return s;
        foreach (var part in spec.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2); if (kv.Length != 2) continue;
            var v = kv[1].Trim();
            switch (kv[0].Trim().ToLowerInvariant())
            {
                case "vramgb": if (int.TryParse(v, out var vram)) s.VramGB = vram; break;
                case "ramgb": if (int.TryParse(v, out var ram)) s.RamGB = ram; break;
                case "driverdate": if (DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) s.DriverDate = dt; break;
                case "gpu": s.Gpu = v; break;
                case "laptop": s.Laptop = v is "1" or "true" or "True"; break;
            }
        }
        return s;
    }
    static string NvidiaFriendly(string drv)
    {
        var digits = new string(drv.Where(char.IsDigit).ToArray());
        if (digits.Length >= 5) { var last5 = digits[^5..]; return $"{last5[..3]}.{last5[3..]}"; }
        return drv;
    }
    static int? VramFromRegistry(string? pnp)
    {
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls == null) return null;
            foreach (var sub in cls.GetSubKeyNames())
            {
                using var k = cls.OpenSubKey(sub); if (k == null) continue;
                var match = k.GetValue("MatchingDeviceId") as string;
                if (pnp != null && match != null && !pnp.Contains(match, StringComparison.OrdinalIgnoreCase)) continue;
                var q = k.GetValue("HardwareInformation.qwMemorySize");
                if (q is long l && l > 0) return (int)Math.Round(l / 1073741824.0);
                if (q is byte[] b && b.Length >= 8) { var v = BitConverter.ToInt64(b, 0); if (v > 0) return (int)Math.Round(v / 1073741824.0); }
            }
        }
        catch { }
        return null;
    }
}

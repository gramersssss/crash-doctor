using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace CrashDoctor.Engine;

// Video memory while the game runs.
//
// The engine writes how full the card was into every crash report, and nowhere else. So after a session that did
// NOT crash there is no way to answer "how close to the ceiling did it get with High textures" - which is exactly the
// question that decides whether a settings change is safe. On 13 Sep that question was answered with a PowerShell
// script polling nvidia-smi, which is not something to ask a Nexus user to do, and does not exist on AMD cards.
//
// This records the number the same way Task Manager gets it: the Windows GPU performance counters, which every WDDM
// driver reports, NVIDIA, AMD and Intel alike. Two counters matter: what the whole card has committed ("Dedicated
// Usage" per adapter, the figure that decides whether the next allocation fails) and how much of that is the game's
// own ("Dedicated Usage" per process). The card's size comes from DXGI, the same source the game itself uses, so the
// percentages here line up with the ones in the crash reports.
//
// It records only while the Crash Doctor window is open - nothing runs in the background after it is closed. Each
// launch of the game becomes one small CSV in %APPDATA%\CrashDoctor\vram-logs, appended one line at a time so a crash
// of the game (or the PC) loses nothing. At scan time each recording is matched to its session by time and summarised:
// peak, median, minutes above 90 %, and a downsampled curve for the page.
//
// It is layer 1 (facts) in DESIGN.md's terms. It measures; it never names a mod.

/// <summary>What one recording says about a session, as the page and the rules see it.</summary>
public sealed class VramLogView
{
    public double Peak { get; set; }              // 0..1+ of the card, whole card
    public double Median { get; set; }
    public double MinutesAbove90 { get; set; }
    public double Minutes { get; set; }           // length of the recording
    public int Samples { get; set; }
    public int IntervalSeconds { get; set; }
    public int TotalMB { get; set; }
    public int PeakMB { get; set; }               // whole card
    public int GamePeakMB { get; set; }           // the game process alone
    public int LastMB { get; set; }               // final sample, i.e. just before the game went away
    public int SharedPeakMB { get; set; }         // driver spill into system memory
    public string Adapter { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool Live { get; set; }                // still being written
    public List<int> Points { get; set; } = new(); // percent of the card, downsampled, max per bucket so peaks survive
    public string File { get; set; } = "";
}

public sealed class VramLogSummary
{
    public bool Enabled { get; set; } = true;
    public string Folder { get; set; } = "";
    public int Recordings { get; set; }
    public long Bytes { get; set; }
    public bool Supported { get; set; } = true;   // the counters answered at least once on this machine
    public string? Problem { get; set; }          // why they did not, in plain words
}

/// <summary>One live reading, for the strip at the top of the window while the game runs.</summary>
public sealed class VramLive
{
    public bool Recording { get; set; }
    public int CardMB { get; set; }
    public int GameMB { get; set; }
    public int TotalMB { get; set; }
    public int PeakMB { get; set; }
    public double Minutes { get; set; }
    public string Adapter { get; set; } = "";
    public string? Problem { get; set; }
}

// ---------------------------------------------------------------- the card itself (DXGI)

public sealed class GpuAdapter
{
    public string Name { get; set; } = "";
    public string LuidKey { get; set; } = "";     // as the performance counters spell it: luid_0x00000000_0x00DA1F0D
    public long DedicatedBytes { get; set; }
    public int DedicatedMB => (int)(DedicatedBytes / 1048576);
}

public static class GpuAdapters
{
    // DXGI, called the way any game calls it. The vtable slots that are never used are placeholders; only their
    // count matters, because COM dispatches by position.
    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDXGIFactory1
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();          // IDXGIObject
        void EnumAdapters(); void MakeWindowAssociation(); void GetWindowAssociation(); void CreateSwapChain(); void CreateSoftwareAdapter(); // IDXGIFactory
        [PreserveSig] int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
        [PreserveSig] int IsCurrent();
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDXGIAdapter1
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();          // IDXGIObject
        void EnumOutputs(); void GetDesc(); void CheckInterfaceSupport();                                        // IDXGIAdapter
        [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 desc);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow; public int LuidHigh;
        public uint Flags;
    }

    [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IDXGIFactory1 factory);
    const int DxgiNotFound = unchecked((int)0x887A0002);
    const uint SoftwareAdapter = 2;

    static List<GpuAdapter>? _cache;

    public static List<GpuAdapter> List()
    {
        if (_cache != null) return _cache;
        var list = new List<GpuAdapter>();
        try
        {
            var iid = typeof(IDXGIFactory1).GUID;
            if (CreateDXGIFactory1(ref iid, out var factory) == 0)
            {
                for (uint i = 0; i < 16; i++)
                {
                    if (factory.EnumAdapters1(i, out var a) == DxgiNotFound) break;
                    if (a == null) break;
                    try
                    {
                        if (a.GetDesc1(out var desc) != 0) continue;
                        if ((desc.Flags & SoftwareAdapter) != 0) continue;   // Microsoft Basic Render Driver
                        list.Add(new GpuAdapter { Name = desc.Description.Trim(), LuidKey = $"luid_0x{desc.LuidHigh:X8}_0x{desc.LuidLow:X8}", DedicatedBytes = (long)desc.DedicatedVideoMemory.ToUInt64() });
                    }
                    finally { Marshal.ReleaseComObject(a); }
                }
                Marshal.ReleaseComObject(factory);
            }
        }
        catch { /* no DXGI is no card; the sampler falls back to the counters alone */ }
        return _cache = list;
    }
}

// ---------------------------------------------------------------- one reading (PDH)

public sealed class VramSample
{
    public DateTime At { get; set; }
    public string LuidKey { get; set; } = "";
    public string Adapter { get; set; } = "";
    public int CardMB { get; set; }
    public int GameMB { get; set; }
    public int SharedMB { get; set; }
    public int TotalMB { get; set; }
}

public static class GpuCounters
{
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQueryW(string? source, IntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr buffer);
    [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr query);
    const uint PdhFmtLarge = 0x400, PdhMoreData = 0x800007D2;

    // The English names, so it works on a French or Japanese Windows too. The instance name carries the adapter LUID
    // and, for the per-process counter, the pid: "pid_1234_luid_0x00000000_0x00DA1F0D_phys_0".
    const string AdapterDedicated = @"\GPU Adapter Memory(*)\Dedicated Usage";
    const string AdapterShared = @"\GPU Adapter Memory(*)\Shared Usage";
    const string ProcessDedicated = @"\GPU Process Memory(*)\Dedicated Usage";

    public static string? LastProblem { get; private set; }

    /// <summary>One reading of the card the game is drawing on. Null when the counters cannot be read on this machine.</summary>
    public static VramSample? Read(int? gamePid)
    {
        var q = IntPtr.Zero;
        try
        {
            if (PdhOpenQueryW(null, IntPtr.Zero, out q) != 0) { LastProblem = "Windows would not open a performance counter query."; return null; }
            if (PdhAddEnglishCounterW(q, AdapterDedicated, IntPtr.Zero, out var cDed) != 0 ||
                PdhAddEnglishCounterW(q, AdapterShared, IntPtr.Zero, out var cSh) != 0 ||
                PdhAddEnglishCounterW(q, ProcessDedicated, IntPtr.Zero, out var cProc) != 0)
            { LastProblem = "The GPU memory performance counters are not available on this PC (they need a WDDM 2 display driver)."; return null; }
            if (PdhCollectQueryData(q) != 0) { LastProblem = "The GPU memory performance counters could not be read."; return null; }

            var ded = Values(cDed); var shared = Values(cSh); var proc = Values(cProc);
            if (ded.Count == 0) { LastProblem = "The GPU memory performance counters report no adapter."; return null; }

            // which card: the one the game has memory on; failing that, the biggest one DXGI knows; failing that, the busiest
            string? luid = null; long gameBytes = 0;
            if (gamePid is { } pid)
            {
                var prefix = $"pid_{pid}_";
                foreach (var (name, v) in proc)
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { gameBytes += v; luid ??= LuidOf(name); }
            }
            var adapters = GpuAdapters.List();
            luid ??= adapters.OrderByDescending(a => a.DedicatedBytes).Select(a => a.LuidKey).FirstOrDefault(k => ded.Any(x => LuidOf(x.name) == k));
            luid ??= ded.OrderByDescending(x => x.value).Select(x => LuidOf(x.name)).FirstOrDefault();
            if (luid == null) { LastProblem = "No graphics adapter could be matched to the counters."; return null; }

            var adapter = adapters.FirstOrDefault(a => a.LuidKey.Equals(luid, StringComparison.OrdinalIgnoreCase));
            var totalMB = adapter?.DedicatedMB ?? 0;
            LastProblem = null;
            return new VramSample
            {
                At = DateTime.Now,
                LuidKey = luid,
                Adapter = adapter?.Name ?? "",
                CardMB = (int)(ded.Where(x => LuidOf(x.name) == luid).Sum(x => x.value) / 1048576),
                SharedMB = (int)(shared.Where(x => LuidOf(x.name) == luid).Sum(x => x.value) / 1048576),
                GameMB = (int)(gameBytes / 1048576),
                TotalMB = totalMB,
            };
        }
        catch (Exception ex) { LastProblem = "Reading the GPU counters failed: " + ex.Message; return null; }
        finally { if (q != IntPtr.Zero) PdhCloseQuery(q); }
    }

    static string? LuidOf(string instance)
    {
        var i = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase); if (i < 0) return null;
        var end = instance.IndexOf("_phys", i, StringComparison.OrdinalIgnoreCase);
        return (end < 0 ? instance[i..] : instance[i..end]).ToUpperInvariant();
    }

    static List<(string name, long value)> Values(IntPtr counter)
    {
        var list = new List<(string, long)>();
        uint size = 0, count = 0;
        var rc = PdhGetFormattedCounterArrayW(counter, PdhFmtLarge, ref size, ref count, IntPtr.Zero);
        if (rc != PdhMoreData || size == 0) return list;
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, PdhFmtLarge, ref size, ref count, buf) != 0) return list;
            // PDH_FMT_COUNTERVALUE_ITEM_W on x64: LPWSTR szName (8) then PDH_FMT_COUNTERVALUE { DWORD CStatus (4), pad (4), LONGLONG (8) }
            const int item = 24;
            for (var i = 0; i < count; i++)
            {
                var p = buf + i * item;
                var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(p)) ?? "";
                var status = Marshal.ReadInt32(p + 8);
                if (status != 0) continue;
                list.Add((name, Marshal.ReadInt64(p + 16)));
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return list;
    }
}

// ---------------------------------------------------------------- recording while the game runs

public sealed class VramRecorder
{
    public const int IntervalSeconds = 5;
    string? _file; DateTime _started; int _pid; int _peakMB; int _samples;
    public VramLive Live { get; } = new();

    /// <summary>Called every few seconds by the window. Starts a file when the game appears, appends while it runs, stops when it goes.</summary>
    /// <returns>true the moment a recording has just ended, so the window can rescan.</returns>
    public bool Tick()
    {
        if (GameLocator.LoadConfig().LogVideoMemory == false) { Stop(); return false; }
        Process? game = null;
        try { game = Process.GetProcessesByName("Cyberpunk2077").OrderBy(p => p.StartTime).FirstOrDefault(); } catch { }
        if (game == null) { var ended = _file != null; Stop(); return ended; }

        var sample = GpuCounters.Read(game.Id);
        if (sample == null) { Live.Recording = false; Live.Problem = GpuCounters.LastProblem; return false; }
        if (_file == null || _pid != game.Id)
        {
            DateTime st; try { st = game.StartTime; } catch { st = DateTime.Now; }
            Start(st, game.Id, sample);
        }
        _samples++; _peakMB = Math.Max(_peakMB, sample.CardMB);
        try
        {
            File.AppendAllText(_file!, $"{sample.At:yyyy-MM-dd HH:mm:ss},{sample.CardMB},{sample.GameMB},{sample.SharedMB}\r\n");
        }
        catch { /* a full disk must not stop the app */ }
        Live.Recording = true; Live.CardMB = sample.CardMB; Live.GameMB = sample.GameMB; Live.TotalMB = sample.TotalMB; Live.PeakMB = _peakMB;
        Live.Minutes = (DateTime.Now - _started).TotalMinutes; Live.Adapter = sample.Adapter; Live.Problem = null;
        return false;
    }

    void Start(DateTime gameStart, int pid, VramSample first)
    {
        Directory.CreateDirectory(VramLogs.Folder);
        _started = gameStart; _pid = pid; _peakMB = 0; _samples = 0;
        _file = Path.Combine(VramLogs.Folder, $"vram-{gameStart:yyyy-MM-dd_HH-mm-ss}.csv");
        if (!File.Exists(_file))
            File.WriteAllText(_file, $"# Crash Doctor {App.Short} video memory log\r\n# started {gameStart:yyyy-MM-dd HH:mm:ss}\r\n# adapter {first.Adapter.Replace("\r", "").Replace("\n", "")}\r\n# total_mb {first.TotalMB}\r\n# interval_s {IntervalSeconds}\r\n# game_pid {pid}\r\ntime,card_mb,game_mb,shared_mb\r\n");
    }

    void Stop() { _file = null; Live.Recording = false; Live.Problem = null; }
}

// ---------------------------------------------------------------- the store, and matching recordings to sessions

public static class VramLogs
{
    public static string Folder => Path.Combine(GamePaths.AppData, "vram-logs");
    const int Keep = 60;
    const int MaxPoints = 120;

    public static VramLogSummary Summary()
    {
        var files = Files();
        return new VramLogSummary
        {
            Enabled = GameLocator.LoadConfig().LogVideoMemory != false,
            Folder = Folder,
            Recordings = files.Count,
            Bytes = files.Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } }),
        };
    }

    static List<string> Files()
    {
        try { return Directory.Exists(Folder) ? Directory.EnumerateFiles(Folder, "vram-*.csv").OrderBy(f => f).ToList() : new(); } catch { return new(); }
    }

    /// <summary>Every recording on disk, summarised. Oldest beyond the cap are removed here, so the folder cannot grow without limit.</summary>
    public static List<VramLogView> ReadAll()
    {
        var files = Files();
        foreach (var old in files.Take(Math.Max(0, files.Count - Keep))) { try { File.Delete(old); } catch { } }
        var list = new List<VramLogView>();
        foreach (var f in files.Skip(Math.Max(0, files.Count - Keep)))
        {
            var v = Read(f); if (v != null && v.Samples > 0) list.Add(v);
        }
        return list;
    }

    public static VramLogView? Read(string file)
    {
        try
        {
            string[] lines;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs)) lines = sr.ReadToEnd().Split('\n');
            var v = new VramLogView { File = file, IntervalSeconds = VramRecorder.IntervalSeconds };
            var card = new List<int>(); var game = 0; var shared = 0; DateTime? first = null, last = null;
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                if (line[0] == '#')
                {
                    if (line.StartsWith("# adapter ")) v.Adapter = line[10..].Trim();
                    else if (line.StartsWith("# total_mb ") && int.TryParse(line[11..].Trim(), out var t)) v.TotalMB = t;
                    else if (line.StartsWith("# interval_s ") && int.TryParse(line[13..].Trim(), out var iv) && iv > 0) v.IntervalSeconds = iv;
                    else if (line.StartsWith("# started ") && DateTime.TryParseExact(line[10..].Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var st)) v.Start = st;
                    continue;
                }
                var p = line.Split(',');
                if (p.Length < 4 || !DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) continue;
                if (!int.TryParse(p[1], out var c)) continue;
                int.TryParse(p[2], out var g); int.TryParse(p[3], out var s);
                card.Add(c); game = Math.Max(game, g); shared = Math.Max(shared, s);
                first ??= at; last = at;
            }
            if (card.Count == 0 || first == null) return v;
            if (v.Start == default) v.Start = first.Value;
            v.End = last!.Value; v.Samples = card.Count;
            v.Minutes = Math.Max((v.End - first.Value).TotalMinutes + v.IntervalSeconds / 60.0, v.IntervalSeconds / 60.0);
            v.PeakMB = card.Max(); v.GamePeakMB = game; v.SharedPeakMB = shared; v.LastMB = card[^1];
            var total = v.TotalMB > 0 ? v.TotalMB : v.PeakMB;
            v.Peak = total > 0 ? (double)v.PeakMB / total : 0;
            var sorted = card.OrderBy(x => x).ToList();
            v.Median = total > 0 ? (double)sorted[sorted.Count / 2] / total : 0;
            v.MinutesAbove90 = total > 0 ? card.Count(x => x >= 0.9 * total) * v.IntervalSeconds / 60.0 : 0;
            v.Live = (DateTime.Now - v.End).TotalSeconds < 3 * v.IntervalSeconds && GameLocator.GameRunning();
            // downsample keeping the maximum in each bucket, so a short spike to the ceiling is never averaged away
            var bucket = Math.Max(1, (int)Math.Ceiling(card.Count / (double)MaxPoints));
            for (var i = 0; i < card.Count; i += bucket)
            {
                var m = 0; for (var j = i; j < Math.Min(card.Count, i + bucket); j++) m = Math.Max(m, card[j]);
                v.Points.Add(total > 0 ? (int)Math.Round(100.0 * m / total) : 0);
            }
            return v;
        }
        catch { return null; }
    }

    /// <summary>Give each session the recording that overlaps it most. Idempotent; safe to run again after history is merged in.</summary>
    public static void Attach(List<VramLogView> recordings, List<Session> sessions)
    {
        if (recordings.Count == 0) return;
        foreach (var s in sessions)
        {
            var start = s.Start.AddMinutes(-2); var end = (s.End ?? DateTime.Now).AddMinutes(2);
            VramLogView? best = null; var bestOverlap = 0.0;
            foreach (var r in recordings)
            {
                var o = (Min(r.End, end) - Max(r.Start, start)).TotalMinutes;
                if (o > bestOverlap) { bestOverlap = o; best = r; }
            }
            if (best != null) s.VramLog = best;
        }
    }
    static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}

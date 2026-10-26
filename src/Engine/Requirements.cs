using System.Diagnostics;
using Microsoft.Win32;

namespace CrashDoctor.Engine;

public static class RequirementsCheck
{
    public const string WebView2Url = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";     // evergreen bootstrapper
    public const string VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

    public static List<Requirement> Check(CollectedData d)
    {
        var g = d.Paths; var inv = d.Mods; var list = new List<Requirement>();
        var red = d.Red4ext.OrderByDescending(x => x.Start).FirstOrDefault();
        string? PluginVer(string name) => red?.PluginsLoaded.FirstOrDefault(p => p.name.Equals(name, StringComparison.OrdinalIgnoreCase)).version;
        List<string> NeededBy(string fw) => inv.Mods.Where(m => m.Status != "removed" && inv.Needs(m, fw)).Select(m => m.Name).ToList();
        // after a game update, a framework whose own log refused the new build is out of date whatever its file says
        FrameworkCheck? Update(string name) => d.FrameworkChecks.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && f.State is "refused" or "errors" or "notStarted" or "old");
        // the release table (KnownReleases.cs): a version known to be behind this game is out of date with or without an update notice
        KnownCheck? Known(string name) => d.KnownChecks.FirstOrDefault(k => k.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        string AfterUpdate(string status, string name) => (Update(name) != null || Known(name) != null) && status == "ok" ? "outdated" : status;
        FrameworkCheck? Any(string name) => d.FrameworkChecks.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && f.State != "ran");
        string Detail(string detail, string name) => Any(name) is { } u ? L.Cat(detail, u.Text) : Known(name) is { } k ? L.Cat(detail, k.Text) : detail;
        // the file's own version stamp is what is installed now; a log only says what ran then
        string? Ver(string? fromLog, string name) => KnownReleases.InstalledVersion(d, name) ?? (string.IsNullOrEmpty(fromLog) ? null : fromLog);

        // RED4ext (all reads go through GamePaths, so a framework installed as a Mod Organizer 2 mod is found too)
        // its own files, not the folder: after a purge red4ext\ still holds the old logs
        var red4 = KnownReleases.Installed(d, "RED4ext");
        var needRed = inv.Mods.Where(m => inv.Needs(m, "red4ext")).Select(m => m.Name).ToList();
        list.Add(Req("red4ext", "RED4ext", red4 ? KnownReleases.InstalledVersion(d, "RED4ext") ?? red?.Red4extVersion : null, AfterUpdate(red4 ? "ok" : needRed.Count > 0 ? "missing" : "optional", "RED4ext"),
            Detail(red4 ? L.P("req.red4ext.ok", inv.Red4extPlugins) : L.T("req.red4ext.missing"), "RED4ext"), needRed, "https://www.nexusmods.com/cyberpunk2077/mods/2380"));
        // redscript
        var rs = g.Exists(@"engine\tools\scc.exe") || g.Exists(@"engine\tools\scc_lib.dll");
        var needRs = NeededBy("redscript");
        list.Add(Req("redscript", "redscript", null, AfterUpdate(rs ? "ok" : needRs.Count > 0 ? "missing" : "optional", "redscript"),
            Detail(rs ? L.Cat(L.P("req.redscript.ok", inv.RedsFolders), d.RedscriptErrors.Count > 0 ? L.P("req.redscript.errors", d.RedscriptErrors.Count) : "") : L.T("req.redscript.missing"), "redscript"),
            needRs, "https://www.nexusmods.com/cyberpunk2077/mods/1511"));
        // CET
        var cet = g.Exists(@"bin\x64\plugins\cyber_engine_tweaks.asi");
        var needCet = NeededBy("cet");
        // CET's log is about the file that wrote it: an asi replaced since its last start has said nothing yet
        var cetStale = d.CetStarts.Count > 0 && KnownReleases.ChangedSince(d, "Cyber Engine Tweaks", d.CetStarts.Max(x => x.at));
        var cetStatus = !cet ? (needCet.Count > 0 ? "missing" : "optional") : cetStale ? "ok" : (!string.IsNullOrEmpty(d.CetGameVersion) && red != null && !string.IsNullOrEmpty(red.FileVersion) && d.CetGameVersion != red.FileVersion && d.CetGameVersion.Count(c => c == '.') == red.FileVersion.Count(c => c == '.') ? "outdated" : "ok");
        list.Add(Req("cet", "Cyber Engine Tweaks", cet ? Ver(d.CetVersion, "Cyber Engine Tweaks") : null, AfterUpdate(cetStatus, "Cyber Engine Tweaks"),
            Detail(cet ? L.Cat(L.P("req.cet.ok", inv.CetMods), cetStatus == "outdated" ? L.T("req.cet.builtFor", d.CetGameVersion, red!.FileVersion) : "") : L.T("req.cet.missing"), "Cyber Engine Tweaks"),
            needCet, "https://www.nexusmods.com/cyberpunk2077/mods/107"));
        // ArchiveXL / TweakXL / Codeware
        foreach (var (id, name, nexus, fw, detail) in new[] {
            ("archivexl", "ArchiveXL", "4198", "archivexl", L.P("req.archivexl", inv.XlFiles)),
            ("tweakxl", "TweakXL", "4197", "tweakxl", L.P("req.tweakxl", inv.TweakFiles)),
        })
        {
            var present = g.FilesIn(Path.Combine(GamePaths.RelRed4extPlugins, name), "*.dll").Count > 0;
            var need = NeededBy(fw);
            var loaded = PluginVer(name);
            var status = !present ? (need.Count > 0 ? "missing" : "optional") : (red != null && red.Incompatible.Any(i => i.StartsWith(name)) ? "outdated" : "ok");
            list.Add(Req(id, name, present ? Ver(loaded, name) : null, AfterUpdate(status, name), Detail(detail, name), need, $"https://www.nexusmods.com/cyberpunk2077/mods/{nexus}"));
        }
        var cw = g.FilesIn(Path.Combine(GamePaths.RelRed4extPlugins, "Codeware"), "*.dll").Count > 0;
        var needCw = inv.Mods.Where(m => m.Files.Any(f => f.EndsWith(".reds", StringComparison.OrdinalIgnoreCase)) && m.Name != "Codeware").Select(m => m.Name).ToList();
        list.Add(Req("codeware", "Codeware", cw ? Ver(PluginVer("Codeware"), "Codeware") : null, AfterUpdate(cw ? "ok" : needCw.Count > 0 ? "unknown" : "optional", "Codeware"),
            Detail(cw ? L.T("req.codeware.ok") : L.T("req.codeware.missing"), "Codeware"), cw ? needCw : new(), "https://www.nexusmods.com/cyberpunk2077/mods/7780"));
        // Equipment-EX if clothing mods present
        var eqx = g.DirExists(Path.Combine(GamePaths.RelScripts, "EquipmentEx")); if (eqx) list.Add(Req("equipmentex", "Equipment-EX", null, "ok", L.T("req.equipmentex"), new(), "https://www.nexusmods.com/cyberpunk2077/mods/6945"));
        // Advanced Crash Reporter (Akiway): optional, never bundled. With it, a crash names the function it died in and
        // whose code was running there (AdvancedCrashReporter.cs).
        var acrReports = d.AcrReports.Count;
        list.Add(new Requirement
        {
            Id = "acr", Name = "Advanced Crash Reporter", Status = d.AcrDir != null ? "ok" : "optional",
            Detail = d.AcrDir != null ? L.Cat(L.T("req.acr.ok"), L.P("req.acr.reports", acrReports)) : L.T("req.acr.missing"),
            Url = AdvancedCrashReporter.NexusUrl,
        });
        // Microsoft runtimes
        var wv = WebView2Version();
        list.Add(new Requirement { Id = "webview2", Name = "Microsoft WebView2 runtime", Version = wv, Status = wv != null ? "ok" : "missing", Detail = L.T("req.webview2"), DirectInstall = true, Url = WebView2Url });
        var vc = VcRedistInstalled();
        list.Add(new Requirement { Id = "vcredist", Name = "Visual C++ 2015-2022 runtime (x64)", Status = vc ? "ok" : "missing", Detail = L.T("req.vcredist"), NeededBy = new List<string> { "RED4ext", "Cyber Engine Tweaks" }.Where(n => list.Any(x => x.Name == n && x.Status != "optional")).ToList(), DirectInstall = true, Url = VcRedistUrl });
        // DSX for DualSense mod
        if (inv.Mods.Any(m => m.Name.Contains("DualSense", StringComparison.OrdinalIgnoreCase)))
        {
            var dsx = Process.GetProcessesByName("DSX").Length > 0 || Directory.Exists(@"C:\Program Files (x86)\Steam\steamapps\common\DSX");
            list.Add(new Requirement { Id = "dsx", Name = "DSX (DualSense on PC)", Status = dsx ? "ok" : "optional", Detail = L.T("req.dsx"), NeededBy = inv.Mods.Where(m => m.Name.Contains("DualSense", StringComparison.OrdinalIgnoreCase)).Select(m => m.Name).ToList(), Url = "https://store.steampowered.com/app/1812620/DSX/" });
        }
        foreach (var q in list) q.NeededByCount = q.NeededBy.Count;
        return list;
    }

    static Requirement Req(string id, string name, string? ver, string status, string detail, List<string> neededBy, string url) => new() { Id = id, Name = name, Version = ver, Status = status, Detail = detail, NeededBy = neededBy, DirectInstall = false, Url = url };

    public static string? WebView2Version()
    {
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Registry32) })
            try { using var k = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"); var v = k?.GetValue("pv") as string; if (!string.IsNullOrEmpty(v) && v != "0.0.0.0") return v; } catch { }
        return null;
    }
    public static bool VcRedistInstalled()
    {
        try { using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"); return k != null && Convert.ToInt32(k.GetValue("Installed") ?? 0) == 1; } catch { return false; }
    }
}

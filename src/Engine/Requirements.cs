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

        // RED4ext
        var red4 = File.Exists(Path.Combine(g.Red4extPlugins, "..", "RED4ext.dll")) || File.Exists(Path.Combine(g.Bin, "winmm.dll")) || Directory.Exists(Path.Combine(g.GameDir, "red4ext"));
        var needRed = inv.Mods.Where(m => inv.Needs(m, "red4ext")).Select(m => m.Name).ToList();
        list.Add(Req("red4ext", "RED4ext", red?.Red4extVersion, red4 ? "ok" : needRed.Count > 0 ? "missing" : "optional", red4 ? $"Loader for native plugins. {inv.Red4extPlugins} plugin folders installed." : "Loader for native plugins (ArchiveXL, TweakXL, Codeware and many more need it).", needRed, "https://www.nexusmods.com/cyberpunk2077/mods/2380"));
        // redscript
        var rs = File.Exists(Path.Combine(g.GameDir, "engine", "tools", "scc.exe")) || File.Exists(Path.Combine(g.GameDir, "engine", "tools", "scc_lib.dll"));
        var needRs = NeededBy("redscript");
        list.Add(Req("redscript", "redscript", null, rs ? "ok" : needRs.Count > 0 ? "missing" : "optional", rs ? $"Compiles .reds script mods. {inv.RedsFolders} script folders installed." + (d.RedscriptErrors.Count > 0 ? $" {d.RedscriptErrors.Count} compile errors in the last run." : "") : "Compiles .reds script mods.", needRs, "https://www.nexusmods.com/cyberpunk2077/mods/1511"));
        // CET
        var cet = File.Exists(Path.Combine(g.Bin, "plugins", "cyber_engine_tweaks.asi"));
        var needCet = NeededBy("cet");
        var cetStatus = !cet ? (needCet.Count > 0 ? "missing" : "optional") : (!string.IsNullOrEmpty(d.CetGameVersion) && red != null && !string.IsNullOrEmpty(red.FileVersion) && d.CetGameVersion != red.FileVersion ? "outdated" : "ok");
        list.Add(Req("cet", "Cyber Engine Tweaks", d.CetVersion, cetStatus, cet ? $"Runs Lua mods. {inv.CetMods} CET mods installed." + (cetStatus == "outdated" ? $" Built for game {d.CetGameVersion}, game is {red!.FileVersion}." : "") : "Runs Lua mods.", needCet, "https://www.nexusmods.com/cyberpunk2077/mods/107"));
        // ArchiveXL / TweakXL / Codeware
        foreach (var (id, name, nexus, fw, detail) in new[] {
            ("archivexl", "ArchiveXL", "4198", "archivexl", $"Loads .xl resource and world patches. {inv.XlFiles} .xl files installed."),
            ("tweakxl", "TweakXL", "4197", "tweakxl", $"Applies .yaml tweak records. {inv.TweakFiles} tweak files installed."),
        })
        {
            var present = Directory.Exists(Path.Combine(g.Red4extPlugins, name)) && Directory.EnumerateFiles(Path.Combine(g.Red4extPlugins, name), "*.dll").Any();
            var need = NeededBy(fw);
            var loaded = PluginVer(name);
            var status = !present ? (need.Count > 0 ? "missing" : "optional") : (red != null && red.Incompatible.Any(i => i.StartsWith(name)) ? "outdated" : "ok");
            list.Add(Req(id, name, loaded, status, detail, need, $"https://www.nexusmods.com/cyberpunk2077/mods/{nexus}"));
        }
        var cw = Directory.Exists(Path.Combine(g.Red4extPlugins, "Codeware"));
        var needCw = inv.Mods.Where(m => m.Files.Any(f => f.EndsWith(".reds", StringComparison.OrdinalIgnoreCase)) && m.Name != "Codeware").Select(m => m.Name).ToList();
        list.Add(Req("codeware", "Codeware", PluginVer("Codeware"), cw ? "ok" : needCw.Count > 0 ? "unknown" : "optional", cw ? "Script library many mods import." : "Script library many mods import. Install it if a mod's page lists it.", cw ? needCw : new(), "https://www.nexusmods.com/cyberpunk2077/mods/7780"));
        // Equipment-EX if clothing mods present
        var eqx = Directory.Exists(Path.Combine(g.Scripts, "EquipmentEx")); if (eqx) list.Add(Req("equipmentex", "Equipment-EX", null, "ok", "Outfit system used by clothing and wardrobe mods.", new(), "https://www.nexusmods.com/cyberpunk2077/mods/6945"));
        // Microsoft runtimes
        var wv = WebView2Version();
        list.Add(new Requirement { Id = "webview2", Name = "Microsoft WebView2 runtime", Version = wv, Status = wv != null ? "ok" : "missing", Detail = "Used by Crash Doctor itself to draw this window.", DirectInstall = true, Url = WebView2Url });
        var vc = VcRedistInstalled();
        list.Add(new Requirement { Id = "vcredist", Name = "Visual C++ 2015-2022 runtime (x64)", Status = vc ? "ok" : "missing", Detail = "Needed by RED4ext, Cyber Engine Tweaks and most native plugins.", NeededBy = new List<string> { "RED4ext", "Cyber Engine Tweaks" }.Where(n => list.Any(x => x.Name == n && x.Status != "optional")).ToList(), DirectInstall = true, Url = VcRedistUrl });
        // DSX for DualSense mod
        if (inv.Mods.Any(m => m.Name.Contains("DualSense", StringComparison.OrdinalIgnoreCase)))
        {
            var dsx = Process.GetProcessesByName("DSX").Length > 0 || Directory.Exists(@"C:\Program Files (x86)\Steam\steamapps\common\DSX");
            list.Add(new Requirement { Id = "dsx", Name = "DSX (DualSense on PC)", Status = dsx ? "ok" : "optional", Detail = "Only needed by Enhanced DualSense Support. Steam app 1812620. Must be running while you play.", NeededBy = inv.Mods.Where(m => m.Name.Contains("DualSense", StringComparison.OrdinalIgnoreCase)).Select(m => m.Name).ToList(), Url = "https://store.steampowered.com/app/1812620/DSX/" });
        }
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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrashDoctor.Engine;

public static class Scanner
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = false, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    public static Report Scan(GamePaths g, int days = 30)
    {
        var since = DateTime.Now.AddDays(-days);
        var timing = Environment.GetEnvironmentVariable("CRASHDOCTOR_TIMING") == "1";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        void Mark(string what) { if (timing) { Console.Error.WriteLine($"  [timing] {sw.ElapsedMilliseconds,7} ms  {what}"); sw.Restart(); } }
        var d = Collectors.CollectAll(g, since);
        Mark("(collect total)");
        var r = new Report();
        var (fv, pv) = GameLocator.ExeVersion(g);
        var latestRed = d.Red4ext.OrderByDescending(x => x.Start).FirstOrDefault();
        r.Game = new GameInfo { Path = g.GameDir, Store = g.Store, FileVersion = fv, Version = !string.IsNullOrEmpty(latestRed?.ProductVersion) ? latestRed!.ProductVersion : GuessPatch(fv), Running = GameLocator.GameRunning() };
        r.System = d.System;
        r.Mods = new ModsSummary { Installed = d.Mods.Mods.Count, Enabled = d.Mods.Mods.Count(m => m.Status == "enabled"), Manager = d.Mods.Manager, Frameworks = Frameworks(d) };
        Analyzer.Analyze(d, r);
        Mark("analyze");
        try { r.Archive = CrashArchive.Preserve(d, r.Sessions); } catch { /* keeping logs must never cost the diagnosis */ }
        Mark("crash log archive");
        var seenUntil = GameLocator.LoadConfig().CrashesSeenUntil;
        if (seenUntil != null)
            r.NewCrashes = r.Sessions.Where(s => s.EndKind is not (EndKind.Clean or EndKind.Running) && s.Start > seenUntil.Value)
                                     .OrderByDescending(s => s.Start).Select(s => s.Id).ToList();
        r.ModsList = d.Mods.Mods.OrderBy(m => m.Name).Select(m => Flagged(d, m, r)).ToList();
        Mark("mod flags");
        r.Knowledge = Engine.Knowledge.Look(r);
        Mark("knowledge");
        r.Requirements = RequirementsCheck.Check(d);
        Mark("requirements");
        r.Settings = PrettySettings(d.Settings);
        RefreshProfiles(r);
        r.Warnings = d.Warnings;

        // A bisect in progress reads its own result out of the logs, so playing and rescanning is the whole loop.
        var plan = Bisect.Load();
        if (plan != null)
        {
            var step = plan.Current;
            // The moment the deployed mods match what the step asked for, the clock starts. No button to forget.
            if (step != null && step.StartedAt == null && Bisect.Verify(plan, r) is (var off, var on) && off.Count == 0 && on.Count == 0)
            {
                step.StartedAt = DateTime.Now;
                Bisect.Save(plan);
            }
            Bisect.Advance(plan, r);
            r.Bisect = Bisect.View(plan, r);
        }
        Mark("bisect");
        return r;
    }

    static string GuessPatch(string fileVersion) => fileVersion switch { var v when v.StartsWith("3.0.80.51928") => "2.31", var v when v.StartsWith("3.0.80.") => "2.3x", _ => fileVersion };

    static List<string> Frameworks(CollectedData d)
    {
        var l = new List<string>(); var red = d.Red4ext.OrderByDescending(x => x.Start).FirstOrDefault();
        if (red != null && !string.IsNullOrEmpty(red.Red4extVersion)) l.Add("RED4ext " + red.Red4extVersion);
        if (!string.IsNullOrEmpty(d.CetVersion)) l.Add("CET " + d.CetVersion);
        if (red != null) foreach (var p in red.PluginsLoaded.Where(p => p.name is "ArchiveXL" or "TweakXL" or "Codeware")) l.Add($"{p.name} {p.version}");
        return l;
    }

    static ModRow Flagged(CollectedData d, ModRow m, Report r)
    {
        foreach (var h in r.Health.Where(h => h.Mod != null && (h.Mod.Equals(m.Name, StringComparison.OrdinalIgnoreCase) || h.Mod.Contains(m.Name, StringComparison.OrdinalIgnoreCase)) && h.Severity != "info"))
            m.Flags.Add(new ModFlag { Label = h.Flag ?? (h.Title.Length > 40 ? h.Title[..40] + "…" : h.Title), Level = h.Severity, Detail = h.Detail });
        // Elimination outranks suspicion. If the clean sessions cleared this mod, that is the whole story: showing
        // "suspect" and "ruled out" side by side would just reproduce the confusion the elimination layer exists to end.
        var newestFirst = r.Sessions.OrderByDescending(x => x.Start).ToList();
        var cleared = newestFirst.SelectMany(x => x.RuledOut.Select(ro => (session: x, ro)))
                                 .FirstOrDefault(t => t.ro.Mod.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
        if (cleared.ro != null)
            m.Flags.Add(new ModFlag { Label = "ruled out", Level = "cleared", Detail = cleared.ro.Why, SessionId = cleared.session.Id, When = cleared.session.Start.ToString("d MMM HH:mm") });
        else
            foreach (var s in newestFirst)
                foreach (var su in s.Suspects)
                    if (su.Mod.Equals(m.Name, StringComparison.OrdinalIgnoreCase) && !m.Flags.Any(f => f.Label == "suspect in a crash"))
                        m.Flags.Add(new ModFlag { Label = "suspect in a crash", Level = su.Level, Detail = su.Why, SessionId = s.Id, When = s.Start.ToString("d MMM HH:mm") });
        var fix = Fixes.FindFor(m.Name); if (fix != null) { var st = fix.State(d.Paths); if (st == "applied") m.Flags.Add(new ModFlag { Label = "fix applied", Level = "low", Detail = fix.Title }); else if (st == "applicable") m.Flags.Add(new ModFlag { Label = "known fix available", Level = "medium", Detail = fix.Title }); }
        return m;
    }

    static readonly Dictionary<string, string> Labels = new() { ["FrameGeneration"] = "Frame generation", ["ResolutionScaling"] = "Upscaler", ["DLSS"] = "DLSS mode", ["DLSS_BackendPreset"] = "DLSS model", ["FSR3"] = "FSR mode", ["XESS"] = "XeSS mode", ["RayTracing"] = "Ray tracing", ["RayTracedLighting"] = "Ray traced lighting", ["RayTracedReflections"] = "Ray traced reflections", ["RayTracedSunShadows"] = "Ray traced sun shadows", ["RayTracedLocalShadows"] = "Ray traced local shadows", ["RayTracedPathTracing"] = "Path tracing", ["TextureQuality"] = "Textures", ["Resolution"] = "Resolution", ["WindowMode"] = "Window mode", ["VSync"] = "VSync", ["ReflexMode"] = "NVIDIA Reflex", ["CrowdDensity"] = "Crowd density", ["ScreenSpaceReflectionsQuality"] = "Screen space reflections", ["VolumetricFogResolution"] = "Volumetric fog", ["CascadedShadowsResolution"] = "Cascaded shadows", ["DistantShadowsResolution"] = "Distant shadows", ["MirrorQuality"] = "Mirror quality", ["ColorPrecision"] = "Color precision", ["AmbientOcclusion"] = "Ambient occlusion", ["MaxDynamicDecals"] = "Max dynamic decals", ["HDRModes"] = "HDR" };
    static Dictionary<string, string> PrettySettings(Dictionary<string, string> s)
    {
        var o = new Dictionary<string, string>();
        foreach (var kv in Labels)
            if (s.TryGetValue(kv.Key, out var v))
            {
                v = v.Replace("UI-Settings-Video-QualitySetting-", "");
                // The settings file writes booleans both ways depending on the key, so "Ray tracing: True" sat in a
                // column of Off / Medium / Ultra until this stopped being case-sensitive.
                if (v.Equals("true", StringComparison.OrdinalIgnoreCase)) v = "On";
                else if (v.Equals("false", StringComparison.OrdinalIgnoreCase)) v = "Off";
                o[kv.Value] = v;
            }
        return o;
    }

    /// <summary>Profiles and the graphics options are cheap to re-read, so the window refreshes them after every profile action without a full scan.</summary>
    public static void RefreshProfiles(Report r)
    {
        try { r.Graphics = GraphicsProfiles.Current(); r.Profiles = GraphicsProfiles.Summaries(r.Graphics); r.SettingsBackup = GraphicsProfiles.LastBackup(); }
        catch { /* a profile problem must never cost the diagnosis */ }
    }

    public static string ToJson(Report r) => JsonSerializer.Serialize(r, Json);
}

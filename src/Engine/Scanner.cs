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
        // a Mod Organizer 2 instance managing this game decides where everything below is read from (ModOrganizer.cs)
        ModOrganizer.Attach(g);
        var d = Collectors.CollectAll(g, since);
        Mark("(collect total)");
        var r = new Report();
        var (fv, pv) = GameLocator.ExeVersion(g);
        var latestRed = d.Red4ext.OrderByDescending(x => x.Start).FirstOrDefault();
        r.Game = new GameInfo { Path = g.GameDir, Store = g.Store, FileVersion = fv, Version = GameUpdate.Version(d, fv, pv), Running = GameLocator.GameRunning() };
        // a game update since the last scan, and what each framework's own log says about the new build (GameUpdate.cs)
        try
        {
            d.GameChange = GameUpdate.Detect(d, r.Game, remember: true);
            if (d.GameChange != null)
            {
                d.FrameworkChecks = GameUpdate.Frameworks(d, d.GameChange);
                r.Game.PreviousVersion = d.GameChange.From; r.Game.UpdatedAt = d.GameChange.At;
                if (string.IsNullOrEmpty(r.Game.Version)) r.Game.Version = d.GameChange.To ?? "";
            }
        }
        catch { /* never worth the diagnosis */ }
        r.System = d.System;
        r.Mods = new ModsSummary { Installed = d.Mods.Mods.Count, Enabled = d.Mods.Mods.Count(m => m.Status == "enabled"), Manager = d.Mods.Manager, Counted = d.Mods.Manager is "Vortex" or ModOrganizer.ManagerName ? "mods" : "pieces", Frameworks = Frameworks(d),
            Mo2Instance = g.Mo2?.BaseDir, Mo2Profile = g.Mo2?.Profile };
        if (d.Mods.AlsoMo2) d.Warnings.Add(L.T("warn.vortexAndMo2", g.Mo2!.BaseDir));
        Analyzer.Analyze(d, r);
        Mark("analyze");
        // what a hunt on each fault would demand of a clean run, shown on the pick list before anyone commits to one
        foreach (var grp in r.CrashGroups) { var (m, note, timed) = Bisect.Threshold(r, grp.Signature); grp.MinutesToBeat = m; grp.ThresholdNote = note; grp.TimedCrashes = timed; }
        try { r.Update = Updates.Status(); } catch { }   // cache only; a scan never touches the network
        try { r.Archive = CrashArchive.Preserve(d, r.Sessions); } catch { /* keeping logs must never cost the diagnosis */ }
        Mark("crash log archive");
        try { r.VramLogs = VramLogs.Summary(); } catch { }
        try { r.GameLogs = LogCleaner.Summary(g); } catch { }
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
            m.Flags.Add(new ModFlag { Label = h.Flag ?? (L.IsToken(h.Title) || h.Title.Length <= 40 ? h.Title : h.Title[..40] + "…"), Level = h.Severity, Detail = h.Detail });
        // Elimination outranks suspicion. If the clean sessions cleared this mod, that is the whole story: showing
        // "suspect" and "ruled out" side by side would just reproduce the confusion the elimination layer exists to end.
        var newestFirst = r.Sessions.OrderByDescending(x => x.Start).ToList();
        var cleared = newestFirst.SelectMany(x => x.RuledOut.Select(ro => (session: x, ro)))
                                 .FirstOrDefault(t => t.ro.Mod.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
        if (cleared.ro != null)
            m.Flags.Add(new ModFlag { Label = L.T("flag.ruledOut"), Level = "cleared", Detail = cleared.ro.Why, SessionId = cleared.session.Id, When = L.Date(cleared.session.Start, "dmt") });
        else
            foreach (var s in newestFirst)
                foreach (var su in s.Suspects)
                    if (su.Mod.Equals(m.Name, StringComparison.OrdinalIgnoreCase) && !m.Flags.Any(f => f.Label == L.T("flag.suspect")))
                        m.Flags.Add(new ModFlag { Label = L.T("flag.suspect"), Level = su.Level, Detail = su.Why, SessionId = s.Id, When = L.Date(s.Start, "dmt") });
        var fix = Fixes.FindFor(m.Name); if (fix != null) { var st = fix.State(d.Paths); if (st == "applied") m.Flags.Add(new ModFlag { Label = L.T("flag.fixApplied"), Level = "low", Detail = fix.Title }); else if (st == "applicable") m.Flags.Add(new ModFlag { Label = L.T("flag.fixAvailable"), Level = "medium", Detail = fix.Title }); }
        return m;
    }

    // the settings quoted on the Settings page and in the copied summary, in the order they are listed
    static readonly string[] Shown = { "FrameGeneration", "ResolutionScaling", "DLSS", "DLSS_BackendPreset", "FSR3", "XESS", "RayTracing", "RayTracedLighting", "RayTracedReflections", "RayTracedSunShadows", "RayTracedLocalShadows", "RayTracedPathTracing", "TextureQuality", "Resolution", "WindowMode", "VSync", "ReflexMode", "CrowdDensity", "ScreenSpaceReflectionsQuality", "VolumetricFogResolution", "CascadedShadowsResolution", "DistantShadowsResolution", "MirrorQuality", "ColorPrecision", "AmbientOcclusion", "MaxDynamicDecals", "HDRModes" };
    static Dictionary<string, string> PrettySettings(Dictionary<string, string> s)
    {
        var o = new Dictionary<string, string>();
        foreach (var key in Shown)
            if (s.TryGetValue(key, out var v))
            {
                v = v.Replace("UI-Settings-Video-QualitySetting-", "");
                // The settings file writes booleans both ways depending on the key, so "Ray tracing: True" sat in a
                // column of Off / Medium / Ultra until this stopped being case-sensitive.
                if (v.Equals("true", StringComparison.OrdinalIgnoreCase)) v = "On";
                else if (v.Equals("false", StringComparison.OrdinalIgnoreCase)) v = "Off";
                o[GraphicsProfiles.LabelToken(key)] = Analyzer.Gfx(v);
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

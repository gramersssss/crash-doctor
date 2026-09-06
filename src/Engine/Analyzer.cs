using System.Text.RegularExpressions;

namespace CrashDoctor.Engine;

// Turns collected facts into sessions, verdicts, suspects and health items.
public static class Analyzer
{
    const int EvidenceWindowSeconds = 120;
    const int DriverWindowSeconds = 30;

    public static void Analyze(CollectedData d, Report r)
    {
        ResolveNames(d);
        var sessions = BuildSessions(d);
        foreach (var s in sessions) Judge(d, s, sessions);
        var merged = History.Merge(sessions, History.Load());
        History.Save(merged);
        r.Sessions = merged;
        r.Latest = merged.Where(s => s.EndKind is not (EndKind.Clean or EndKind.Running)).OrderByDescending(s => s.Start).FirstOrDefault();
        r.Health = Health(d, merged);
        r.SettingsNotes = SettingsNotes(d, merged);
    }

    // Map raw event owners (CET folder, .xl file, dll) to mod display names once, up front.
    static void ResolveNames(CollectedData d)
    {
        foreach (var e in d.Events)
        {
            if (e.Source.StartsWith("CET · ") && e.Mod != null) { var owner = d.Mods.OwnerOfCetMod(e.Mod); if (owner != null) { e.Mod = owner; e.Source = "CET · " + owner; } }
            else if (e.Source == "ArchiveXL" && e.Mod != null && e.Mod.EndsWith(".xl", StringComparison.OrdinalIgnoreCase)) { var owner = d.Mods.OwnerOfXl(e.Mod); if (owner != null) e.Mod = owner; }
            else if (e.Kind == "appcrash" && e.Mod != null) { var owner = OwnerOfDll(d, e.Mod); if (owner != null) e.Mod = owner; }
        }
    }

    // ---------- sessions ----------
    static List<Session> BuildSessions(CollectedData d)
    {
        var list = new List<Session>();
        var red = d.Red4ext.OrderBy(x => x.Start).ToList();
        var gameRunning = GameLocator.GameRunning();
        for (int i = 0; i < red.Count; i++)
        {
            var x = red[i]; var next = i + 1 < red.Count ? red[i + 1].Start : (DateTime?)null;
            var s = new Session { Id = "s" + x.Start.ToString("yyyyMMddHHmmss"), Start = x.Start, Red4extLog = x.File };
            if (x.CleanShutdown != null) { s.EndKind = EndKind.Clean; s.End = x.CleanShutdown; }
            else if (x.CrashAt != null) { s.End = x.CrashAt; s.CrashTime = x.CrashAt; s.EngineMessage = x.CrashMessage; s.CrashFile = x.CrashFile; s.EndKind = IsGpuMessage(x.CrashMessage) ? EndKind.Gpu : EndKind.Engine; }
            else
            {
                var cr = d.CrashReporterTimes.Where(t => t >= x.Start && (next == null || t < next)).OrderBy(t => t).FirstOrDefault();
                if (cr != default) { s.End = cr; s.CrashTime = cr; s.EndKind = EndKind.Unknown; }
                else if (i == red.Count - 1 && gameRunning) { s.EndKind = EndKind.Running; }
                else { s.End = x.LastWrite; s.EndKind = EndKind.Unknown; s.DurationKnown = false; }
            }
            var end = s.End ?? DateTime.Now;
            s.Minutes = Math.Max(0, (end - s.Start).TotalMinutes);
            list.Add(s);
        }
        // crash reports with no session log left: a partial session, time known, length unknown
        foreach (var t in d.CrashReporterTimes)
            if (!list.Any(s => s.Start <= t && (s.End ?? DateTime.Now).AddMinutes(1) >= t))
                list.Add(new Session { Id = "c" + t.ToString("yyyyMMddHHmmss"), Start = t, End = t, CrashTime = t, EndKind = EndKind.Unknown, Minutes = 0, Partial = true, DurationKnown = false });
        return list;
    }

    static bool IsGpuMessage(string? m) => m != null && (m.Contains("Gpu Crash", StringComparison.OrdinalIgnoreCase) || m.Contains("DXGI_ERROR", StringComparison.OrdinalIgnoreCase) || m.Contains("device removed", StringComparison.OrdinalIgnoreCase));

    // ---------- verdict per session ----------
    static void Judge(CollectedData d, Session s, List<Session> all)
    {
        var fg = d.Settings.TryGetValue("FrameGeneration", out var fgv) && fgv != "Off";
        var rt = SettingOn(d, "RayTracing"); d.Settings.TryGetValue("RayTracedLighting", out var rtl);
        var settingsKnown = !(s.CrashTime != null && d.SettingsWritten != null && d.SettingsWritten > s.CrashTime.Value.AddMinutes(1));

        if (s.EndKind == EndKind.Clean) { s.Verdict = $"Closed normally after {Dur(s.Minutes)}."; s.Confidence = 3; return; }
        if (s.EndKind == EndKind.Running) { s.Verdict = "Still running."; s.Confidence = 3; return; }

        var crashAt = s.CrashTime ?? s.End ?? s.Start;
        var window = d.Events.Where(e => e.At <= crashAt.AddSeconds(5) && e.At >= crashAt.AddSeconds(-EvidenceWindowSeconds) && (s.Partial || e.At >= s.Start)).OrderBy(e => e.At).ToList();
        var driver = window.Where(e => e.Kind == "driver" && e.At >= crashAt.AddSeconds(-DriverWindowSeconds)).ToList();
        var appcrash = window.FirstOrDefault(e => e.Kind == "appcrash");
        var power = window.FirstOrDefault(e => e.Kind == "power");
        var sessionErrors = d.Events.Where(e => e.Kind == "error" && e.At >= (s.Partial ? crashAt.AddMinutes(-30) : s.Start) && e.At <= crashAt.AddSeconds(5)).ToList();
        var repeats = sessionErrors.GroupBy(Key).Where(g => g.Count() >= 5).Select(g => g.Key).ToHashSet();
        bool IsNoise(LogEvent e) => repeats.Contains(Key(e));
        var scriptErrs = window.Where(e => e.Kind == "error" && !IsNoise(e)).ToList();
        var noiseErrs = window.Where(e => e.Kind == "error" && IsNoise(e)).GroupBy(e => e.Source).Select(g => g.OrderByDescending(e => e.At).First()).ToList();
        var xlErrs = window.Where(e => e.Kind is "xl-error" or "xl-warning" && (crashAt - e.At).TotalSeconds <= 20 && !e.Text.StartsWith("[WorldStreaming] Some patches have not been applied")).ToList();
        var activity = window.Where(e => e.Kind == "activity").OrderByDescending(e => e.At).FirstOrDefault();

        foreach (var e in driver) s.Evidence.Add(Ev(e, crashAt, true));
        if (appcrash != null) s.Evidence.Add(Ev(appcrash, crashAt, true));
        foreach (var e in scriptErrs.TakeLast(4)) s.Evidence.Add(Ev(e, crashAt, (crashAt - e.At).TotalSeconds <= 30));
        foreach (var e in xlErrs.TakeLast(2)) s.Evidence.Add(Ev(e, crashAt, false));
        foreach (var e in noiseErrs.Take(2)) { var ev = Ev(e, crashAt, false); var n = sessionErrors.Count(x => x.Source == e.Source); ev.Text += $"  (repeats all session, {n} times)"; s.Evidence.Add(ev); }
        if (activity != null) s.Evidence.Add(Ev(activity, crashAt, false));
        if (power != null) s.Evidence.Add(Ev(power, crashAt, true));
        s.Evidence = s.Evidence.OrderByDescending(e => e.TMinus).ToList();
        if (settingsKnown) s.SettingsAtCrash = new Dictionary<string, string>(d.Settings);
        if (d.CrashInfo is { } ci && all.Where(x => x.CrashTime != null).OrderByDescending(x => x.CrashTime).FirstOrDefault() == s)
        {
            if (ci.TryGetProperty("district", out var dist)) s.District = Pretty(dist.GetString());
            if (ci.TryGetProperty("trackedQuest", out var q) && q.TryGetProperty("name", out var qn)) s.Quest = qn.GetString();
        }
        var partialNote = s.Partial ? " Only the crash report survives for this session; the game's own log from that time has been rotated away, so the play time is unknown." : "";

        // ---- rules, in order of certainty ----
        if (s.EndKind == EndKind.Gpu || driver.Count > 0)
        {
            s.EndKind = EndKind.Gpu; s.Confidence = 3;
            if (!settingsKnown) s.Verdict = "The GPU faulted; the driver reported an error and the engine recorded a GPU crash, not a mod error. The settings file has changed since, so the settings that were running are not known.";
            else if (fg && rt) s.Verdict = $"The GPU faulted while frame generation and ray tracing ({rtl}) were both on. The engine reported a GPU crash, not a mod error.";
            else if (fg) s.Verdict = "The GPU faulted while frame generation was on. The engine reported a GPU crash, not a mod error.";
            else if (rt) s.Verdict = $"The GPU faulted with ray tracing on ({rtl}). The engine reported a GPU crash, not a mod error.";
            else s.Verdict = "The GPU faulted with frame generation and ray tracing both off. The engine reported a GPU crash, not a mod error; suspect the driver, temperatures or an overclock.";
            s.Verdict += partialNote;
            if (settingsKnown && fg) s.Suspects.Add(new Suspect { Mod = rt ? $"Frame generation + ray traced lighting {rtl}" : "Frame generation", Level = "high", Why = d.System.VramGB > 0 && d.System.VramGB <= 8 ? $"On an {d.System.VramGB} GB card this runs at the VRAM ceiling; the map and dense districts push it over." : "Frame generation adds its own GPU work and memory on top of everything else.", Actions = { new UiAction("settings", "See the settings") } });
            else if (settingsKnown && !rt) s.Suspects.Add(new Suspect { Mod = "GPU driver / hardware", Level = "medium", Why = "No frame gen or ray tracing was on. Try the previous driver, check temperatures, rule out an overclock.", Actions = { new UiAction("open:driver", "Open Device Manager") } });
            else if (!settingsKnown) s.Suspects.Add(new Suspect { Mod = "Graphics settings at the time", Level = "medium", Why = "GPU faults in this game are nearly always frame generation and ray tracing exceeding VRAM. Check what was on.", Actions = { new UiAction("settings", "See the settings") } });
            return;
        }
        if (s.EndKind == EndKind.Engine && s.EngineMessage != null)
        {
            s.Confidence = 2;
            if (s.EngineMessage.Contains("scripts", StringComparison.OrdinalIgnoreCase))
            {
                s.Verdict = "The engine stopped because the script files could not be loaded. That is a redscript problem: a broken script mod or a compiler that does not match the game patch.";
                foreach (var w in d.RedscriptErrors.Take(3)) s.Evidence.Add(new Evidence { TMinus = 0, Source = "redscript", Text = w, Hit = true });
                s.Suspects.Add(new Suspect { Mod = "redscript / script mods", Level = "high", Why = "The redscript log names the file it choked on.", Actions = { new UiAction("open:redscript", "Open redscript log") } });
            }
            else s.Verdict = $"The engine stopped itself with: \"{s.EngineMessage}\"" + (s.CrashFile != null ? $" (in {Path.GetFileName(s.CrashFile)})." : ".");
            AddScriptSuspects(d, s, scriptErrs, xlErrs, crashAt);
            return;
        }
        if (appcrash != null)
        {
            s.Confidence = 2; var mod = appcrash.Mod ?? "";
            var raw = Regex.Match(appcrash.Text, @"faulted in (\S+)").Groups[1].Value;
            if (Regex.IsMatch(raw, @"^(nvwgf2um|nvlddmkm|nvgpucomp|amdxc64|amdxx64|atidxx|igd)", RegexOptions.IgnoreCase)) { s.EndKind = EndKind.Gpu; s.Confidence = 3; s.Verdict = $"Windows recorded the fault inside the graphics driver ({raw}).{partialNote}"; s.Suspects.Add(new Suspect { Mod = "GPU driver", Level = "high", Why = "The faulting module is the display driver. A clean install of the previous driver version is the usual fix.", Actions = { new UiAction("open:driver", "Open Device Manager") } }); return; }
            if (mod != raw && d.Mods.Find(mod) != null) { s.EndKind = EndKind.Script; s.Confidence = 3; s.Verdict = $"Windows recorded the fault inside {raw}, which belongs to the mod {mod}.{partialNote}"; s.Suspects.Add(SuspectFor(d, mod, "high", "Its native code is where the crash happened.")); return; }
            s.Verdict = $"Windows recorded the fault in {raw}." + (raw.StartsWith("Cyberpunk2077", StringComparison.OrdinalIgnoreCase) ? " That is the game itself, which usually means bad data handed to it by a mod (a broken mesh, appearance or sector patch)." : "") + partialNote;
            AddScriptSuspects(d, s, scriptErrs, xlErrs, crashAt);
            return;
        }
        if (scriptErrs.Count > 0)
        {
            var last = scriptErrs.Last(); var secs = (int)Math.Round((crashAt - last.At).TotalSeconds);
            s.EndKind = EndKind.Script; s.Confidence = secs <= 15 ? 2 : 1;
            var name = last.Mod ?? last.Source.Replace("CET · ", "");
            s.Verdict = $"Script error in {name} {secs} s before the crash: {Short(last.Text)} Not a GPU fault; the engine recorded no error of its own.{partialNote}";
            AddScriptSuspects(d, s, scriptErrs, xlErrs, crashAt);
            return;
        }
        if (xlErrs.Count > 0 && activity != null && activity.Text.Contains("world streaming"))
        {
            var last = xlErrs.Last(); var secs = (int)Math.Round((crashAt - last.At).TotalSeconds);
            s.EndKind = EndKind.Script; s.Confidence = 1;
            s.Verdict = $"Crashed during world streaming, {secs} s after ArchiveXL reported a failed sector patch from {last.Mod ?? "a mod"}. A world-editing mod is the likely cause.{partialNote}";
            AddScriptSuspects(d, s, scriptErrs, xlErrs, crashAt);
            return;
        }
        // nothing specific
        s.Confidence = activity != null ? 1 : 0;
        if (s.Partial) { s.Verdict = "A crash was reported at this time. The session log from then has been rotated away, so there is nothing more to go on."; s.Confidence = 0; }
        else if (activity != null && activity.Text.Contains("NPC loading")) { s.EndKind = EndKind.Script; s.Verdict = "Crashed while an NPC was loading, with no engine or GPU error recorded. That pattern points at an appearance or body mod for that NPC, or a mod that edits the location."; }
        else if (activity != null && activity.Text.Contains("world streaming")) { s.EndKind = EndKind.Script; s.Verdict = "Crashed while a world sector patch was being applied during streaming. That points at a mod that edits the map in that area."; }
        else if (power != null) { s.Verdict = "The whole PC went down, not just the game. That is power, overheating or a hard reset, not a mod."; s.Confidence = 2; }
        else if (s.CrashTime == null) { s.Verdict = "The session ended without any record: no shutdown, no crash report. The game was probably closed by force (Task Manager, Alt+F4 during a hang) or the PC was restarted."; }
        else s.Verdict = "Crashed with nothing logged in the two minutes before. The engine could not describe it, no mod reported an error, and the driver did not fault.";
        var cluster = all.Count(x => x != s && x.District != null && x.District == s.District && x.EndKind != EndKind.Clean);
        if (s.District != null && cluster >= 2) s.Verdict += $" This is the {Ordinal(cluster + 1)} crash in {s.District}; something specific to that area is likely.";
        AddScriptSuspects(d, s, scriptErrs, xlErrs, crashAt);
        if (activity != null && activity.Text.Contains("world streaming")) foreach (var m in WorldPatchMods(d).Take(3)) if (!s.Suspects.Any(x => x.Mod == m)) s.Suspects.Add(SuspectFor(d, m, "medium", "Edits world sectors; failed or overlapping sector patches crash during streaming."));
    }

    static string Key(LogEvent e) => e.Source + "|" + Regex.Replace(e.Text, @"\d+", "#");

    static void AddScriptSuspects(CollectedData d, Session s, List<LogEvent> scriptErrs, List<LogEvent> xlErrs, DateTime crashAt)
    {
        foreach (var grp in scriptErrs.GroupBy(e => e.Mod ?? e.Source.Replace("CET · ", "")).OrderBy(g => (crashAt - g.Max(e => e.At)).TotalSeconds))
        {
            var name = grp.Key; var secs = (int)Math.Round((crashAt - grp.Max(e => e.At)).TotalSeconds);
            if (s.Suspects.Any(x => x.Mod == name)) continue;
            s.Suspects.Add(SuspectFor(d, name, secs <= 15 ? "high" : "medium", $"Its script errored {secs} s before the crash: {Short(grp.Last().Text)}"));
        }
        foreach (var grp in xlErrs.Where(e => e.Mod != null).GroupBy(e => e.Mod!))
        {
            var secs = (int)Math.Round((crashAt - grp.Max(e => e.At)).TotalSeconds);
            if (s.Suspects.Any(x => x.Mod == grp.Key)) continue;
            s.Suspects.Add(SuspectFor(d, grp.Key, "medium", $"ArchiveXL reported a problem with its patch {secs} s before the crash: {Short(grp.Last().Text)}"));
        }
    }

    static Suspect SuspectFor(CollectedData d, string modName, string level, string why)
    {
        var s = new Suspect { Mod = modName, Level = level, Why = why };
        var row = d.Mods.Find(modName);
        if (row?.NexusId != null) s.Actions.Add(new UiAction("nexus:" + row.NexusId, "Open on Nexus"));
        var fix = Fixes.FindFor(modName); if (fix != null) { var st = fix.State(d.Paths); if (st == "applied") s.Actions.Add(new UiAction("fix:" + fix.Id, "Fix applied · revert")); else if (st == "applicable") s.Actions.Add(new UiAction("fix:" + fix.Id, "Apply known fix: " + fix.Title)); }
        return s;
    }

    static string? OwnerOfDll(CollectedData d, string dll)
    {
        if (string.IsNullOrEmpty(dll)) return null;
        var kv = d.Mods.FileOwner.FirstOrDefault(k => k.Key.EndsWith("\\" + dll, StringComparison.OrdinalIgnoreCase));
        return kv.Value;
    }

    static IEnumerable<string> WorldPatchMods(CollectedData d) => d.Mods.Mods.Where(m => m.Type.Contains("ArchiveXL") && m.Files.Any(f => f.EndsWith(".xl", StringComparison.OrdinalIgnoreCase) && XlEditsWorld(d, f))).Select(m => m.Name);
    static readonly Dictionary<string, bool> XlCache = new(StringComparer.OrdinalIgnoreCase);
    static bool XlEditsWorld(CollectedData d, string rel)
    {
        if (XlCache.TryGetValue(rel, out var b)) return b;
        try { var t = Files.ReadAllTextShared(Path.Combine(d.Paths.GameDir, rel)); b = t.Contains("streaming") || t.Contains("nodeRef") || t.Contains("sectors"); } catch { b = false; }
        XlCache[rel] = b; return b;
    }

    static Evidence Ev(LogEvent e, DateTime crashAt, bool hit) => new() { TMinus = Math.Max(0, (int)Math.Round((crashAt - e.At).TotalSeconds)), Source = e.Source, Text = e.Text, Hit = hit, At = e.At, Mod = e.Mod };
    static bool SettingOn(CollectedData d, string key) => d.Settings.TryGetValue(key, out var v) && (v == "true" || v == "True");
    static string Dur(double m) => m < 1 ? "under a minute" : m < 60 ? $"{Math.Round(m)} min" : $"{(int)(m / 60)} h {Math.Round(m % 60)} min";
    static string Short(string t) => t.Length > 110 ? t[..110] + "…" : t;
    static string Ordinal(int n) => n switch { 1 => "first", 2 => "second", 3 => "third", 4 => "fourth", 5 => "fifth", _ => n + "th" };
    static string? Pretty(string? district) => district == null ? null : Regex.Replace(district.Replace("_", " · "), "(?<=[a-z])(?=[A-Z])", " ");

    // ---------- health ----------
    static List<HealthItem> Health(CollectedData d, List<Session> sessions)
    {
        var h = new List<HealthItem>();
        var week = sessions.Where(s => s.Start > DateTime.Now.AddDays(-7)).ToList();
        var full = week.Where(s => !s.Partial && s.EndKind != EndKind.Running).ToList();
        var partial = week.Count(s => s.Partial);
        if (full.Count > 0 || partial > 0)
        {
            var wc = full.Count(s => s.EndKind != EndKind.Clean);
            var crashesFull = full.Where(s => s.EndKind != EndKind.Clean).ToList();
            var med = crashesFull.Count > 0 ? crashesFull.Select(s => s.Minutes).OrderBy(x => x).ElementAt(crashesFull.Count / 2) : 0;
            var sev = full.Count > 0 && wc >= 3 && wc >= full.Count / 2.0 ? "high" : (wc + partial) > 0 ? "medium" : "info";
            var detail = full.Count > 0 ? $"{wc} of {full.Count} logged sessions crashed" + (wc > 0 ? $"; typical time to crash {Dur(med)}" : "") + $". {crashesFull.Count(s => s.EndKind == EndKind.Gpu)} GPU faults, {crashesFull.Count(s => s.EndKind == EndKind.Script)} mod/script, {crashesFull.Count(s => s.EndKind is EndKind.Unknown or EndKind.Engine)} other." : "";
            if (partial > 0) detail += $" Plus {partial} crash report{(partial == 1 ? "" : "s")} from sessions whose logs have been rotated away.";
            h.Add(new HealthItem { Severity = sev, Title = $"{wc + partial} crash{(wc + partial == 1 ? "" : "es")} in the last 7 days", Detail = detail.Trim() });
        }
        var crashes = sessions.Where(s => s.EndKind is not (EndKind.Clean or EndKind.Running)).ToList();
        var fg = d.Settings.TryGetValue("FrameGeneration", out var fgv) && fgv != "Off"; var rt = SettingOn(d, "RayTracing"); d.Settings.TryGetValue("RayTracedLighting", out var rtl);
        if (fg && rt && d.System.VramGB > 0 && d.System.VramGB <= 8) h.Add(new HealthItem { Severity = crashes.Any(c => c.EndKind == EndKind.Gpu) ? "high" : "medium", Title = $"Frame generation with ray tracing on an {d.System.VramGB} GB GPU", Detail = $"Ray traced lighting is {rtl}. This combination sits at the VRAM ceiling on {d.System.VramGB} GB; GPU faults on the map or in dense areas are the usual result. Turn one of them down.", Action = new UiAction("settings", "See settings") });
        if (d.Settings.TryGetValue("RayTracedPathTracing", out var pt) && pt == "true" && d.System.VramGB <= 12) h.Add(new HealthItem { Severity = "medium", Title = "Path tracing on a card with 12 GB or less", Detail = "Path tracing needs more VRAM than any other setting. Expect faults in dense areas.", Action = new UiAction("settings", "See settings") });
        if (d.System.DriverDate is { } dd && dd < DateTime.Now.AddMonths(-9)) h.Add(new HealthItem { Severity = "low", Title = $"Graphics driver is from {dd:MMM yyyy}", Detail = "Not necessarily a problem, but frame generation and ray tracing fixes ship in driver updates." });

        var latestRed = d.Red4ext.OrderByDescending(x => x.Start).FirstOrDefault();
        if (latestRed != null)
            foreach (var inc in latestRed.Incompatible)
            {
                var name = Regex.Match(inc, @"^(.+?) \(version").Groups[1].Value; var owner = d.Mods.OwnerOfPlugin(name.Replace(" ", "")) ?? d.Mods.OwnerOfPlugin(name) ?? name;
                h.Add(new HealthItem { Severity = "medium", Title = $"Native plugin refuses to load on this patch: {name}", Detail = inc + " Whatever this mod does through its native half is silently off, and its script half may error.", Mod = owner, Action = NexusAction(d, owner) });
            }
        if (latestRed != null)
        {
            var startup = d.Events.Where(e => e.Kind is "xl-error" or "xl-warning" && e.At >= latestRed.Start && e.At <= latestRed.Start.AddMinutes(3)).ToList();
            // "Some patches have not been applied" is the consequence of a sector error already listed; drop it when that error is present
            if (startup.Any(e => e.Text.Contains("[WorldStreaming]") && e.Kind == "xl-error")) startup = startup.Where(e => !e.Text.StartsWith("[WorldStreaming] Some patches have not been applied")).ToList();
            // one item per (mod, kind of problem); distinct messages counted inside it
            foreach (var grp in startup.GroupBy(e => (owner: e.Mod ?? GuessOwnerFromText(d, e.Text) ?? "", cat: e.Text.Contains("WorldStreaming") ? "world" : (e.Text.Contains("doesn't exist") || e.Text.Contains("non-existent")) ? "missing" : "other")))
            {
                var msgs = grp.Select(e => e.Text).Distinct().ToList(); var first = grp.First();
                var owner = grp.Key.owner == "" ? null : grp.Key.owner;
                var world = grp.Key.cat == "world"; var missing = grp.Key.cat == "missing";
                var title = world ? "World sector patch fails every launch" : missing ? "Mod points at resources that do not exist" : "ArchiveXL reports a problem";
                if (owner == null) title += " (mod not identified)";
                var detail = msgs.Count == 1 ? msgs[0] : $"{msgs.Count} distinct messages, e.g. {msgs[0]}";
                if (world) detail += " Another mod changed that sector first, or the game patch did; the patch is skipped."; else if (missing) detail += " The mod references files that are not installed or were removed in a game patch; those parts are skipped.";
                h.Add(new HealthItem { Severity = world ? "medium" : first.Kind == "xl-error" ? (owner == null ? "low" : "medium") : "low", Title = title, Detail = detail, Mod = owner, Action = owner != null ? NexusAction(d, owner) : null });
            }
        }
        foreach (var w in d.RedscriptWarnings.Take(6)) h.Add(new HealthItem { Severity = w.Contains("overwrites a previous annotation") ? "low" : "medium", Title = w.Contains("overwrites a previous annotation") ? "Two mods replace the same script method" : "redscript warning", Detail = w, Mod = GuessOwnerFromText(d, w) });
        foreach (var e in d.RedscriptErrors.Take(6)) h.Add(new HealthItem { Severity = "high", Title = "redscript error: a script mod does not compile", Detail = e, Mod = GuessOwnerFromText(d, e) });
        var lastSess = sessions.Where(s => !s.Partial).OrderByDescending(s => s.Start).FirstOrDefault();
        if (lastSess != null)
            foreach (var grp in d.Events.Where(e => e.Kind == "error" && e.Source.StartsWith("CET") && e.At >= lastSess.Start).GroupBy(e => e.Source).Where(g => g.Count() >= 10))
            {
                var owner = grp.First().Mod ?? grp.Key.Replace("CET · ", "");
                h.Add(new HealthItem { Severity = "medium", Title = $"{owner} logs the same error {grp.Count()} times per session", Detail = Short(grp.Last().Text) + " A feature of the mod is not working; check for an update.", Mod = owner, Action = NexusAction(d, owner) });
            }
        if (Directory.Exists(d.Paths.Scripts))
        {
            // same file name in two places is only a conflict when the contents are the same script (two mods bundling one fix)
            var dups = Directory.EnumerateFiles(d.Paths.Scripts, "*.reds", SearchOption.AllDirectories).GroupBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1 && !GenericScriptName(g.Key))
                .Select(g => g.GroupBy(f => { try { return Fixes.Sha(f); } catch { return f; } }).Where(x => x.Count() > 1).ToList()).Where(x => x.Count > 0).ToList();
            foreach (var g in dups.Take(5)) foreach (var same in g) h.Add(new HealthItem { Severity = "low", Title = $"{Path.GetFileName(same.First())} is installed {same.Count()} times", Detail = "Identical copies of one script compile more than once; the compiler warns and the last one wins. Two mods are bundling the same fix: " + string.Join(", ", same.Select(f => d.Mods.FileOwner.FirstOrDefault(kv => kv.Key.EndsWith(Path.GetRelativePath(d.Paths.GameDir, f), StringComparison.OrdinalIgnoreCase)).Value ?? Path.GetRelativePath(d.Paths.Scripts, f))) });
        }
        if (latestRed != null && latestRed.Incompatible.Count == 0 && d.RedscriptErrors.Count == 0) h.Add(new HealthItem { Severity = "info", Title = "Frameworks loaded cleanly", Detail = $"RED4ext {latestRed.Red4extVersion} and {latestRed.PluginsLoaded.Count} native plugins loaded without warnings; scripts compiled." });
        if (d.Warnings.Count > 0) h.Add(new HealthItem { Severity = "info", Title = "Some sources could not be read", Detail = string.Join(" ", d.Warnings) });
        return h;
    }

    // Many mods ship a Config.reds / Utils.reds inside their own folder; that is a naming coincidence, not a conflict.
    static bool GenericScriptName(string name) => Regex.IsMatch(name, @"^(config|utils|util|helpers|helper|classes|main|module|callback|callbacks|events|settings|init|types|globals|data|core)\.reds$", RegexOptions.IgnoreCase);

    static UiAction? NexusAction(CollectedData d, string? modName) { var row = modName == null ? null : d.Mods.Find(modName); return row?.NexusId != null ? new UiAction("nexus:" + row.NexusId, "Open on Nexus") : null; }
    static string? GuessOwnerFromText(CollectedData d, string text)
    {
        var m = Regex.Match(text, @"r6\\scripts\\([^\\]+)\\"); if (m.Success) { var f = d.Mods.FileOwner.FirstOrDefault(kv => kv.Key.StartsWith(@"r6\scripts\" + m.Groups[1].Value + @"\", StringComparison.OrdinalIgnoreCase)); if (f.Value != null) return f.Value; return m.Groups[1].Value; }
        var q = Regex.Match(text, "\"([^\"\\\\]+)\\\\"); // first path segment inside quotes, e.g. "pinkydude\..."
        if (q.Success) { var seg = q.Groups[1].Value; if (seg.Length >= 5 && seg != "base") { var mod = d.Mods.Mods.FirstOrDefault(x => x.Files.Any(f => f.Contains(seg, StringComparison.OrdinalIgnoreCase)) || x.Name.Contains(seg, StringComparison.OrdinalIgnoreCase)); if (mod != null) return mod.Name; } }
        return null;
    }

    static List<SettingsNote> SettingsNotes(CollectedData d, List<Session> sessions)
    {
        var n = new List<SettingsNote>();
        var fg = d.Settings.TryGetValue("FrameGeneration", out var fgv) && fgv != "Off"; var rt = SettingOn(d, "RayTracing");
        var longestClean = sessions.Where(s => s.EndKind == EndKind.Clean && d.SettingsWritten != null && s.Start >= d.SettingsWritten).OrderByDescending(s => s.Minutes).FirstOrDefault();
        if (fg && rt && d.System.VramGB > 0 && d.System.VramGB <= 8) n.Add(new SettingsNote { Level = "high", Title = "Frame generation and ray tracing together", Detail = $"On {d.System.VramGB} GB this is the combination behind GPU faults. Drop ray traced lighting to Medium or turn frame gen off." });
        else if (longestClean != null && longestClean.Minutes >= 60) n.Add(new SettingsNote { Level = "low", Title = "These settings have held up", Detail = $"The longest clean session since this settings file was written ran {Dur(longestClean.Minutes)}." });
        if (d.SettingsWritten != null) n.Add(new SettingsNote { Level = "low", Title = "Settings file last written", Detail = d.SettingsWritten.Value.ToString("f") + ". Verdicts only quote settings for crashes that happened after this time." });
        return n;
    }
}

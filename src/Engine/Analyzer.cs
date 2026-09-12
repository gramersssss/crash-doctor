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
        // Nothing may be ranked until we know what this install does when it is NOT crashing.
        var elimination = Elimination.Build(d, sessions);
        foreach (var s in sessions) Judge(d, s, sessions, elimination);
        foreach (var s in sessions) SayWhenNothingSurvived(s, elimination);
        r.CrashGroups = GroupBySignature(d, sessions);
        foreach (var g in r.CrashGroups) NoteGroupOnSessions(g, sessions);
        var merged = History.Merge(sessions, History.Load());
        History.Save(merged);
        r.Sessions = merged;
        r.Latest = merged.Where(s => s.EndKind is not (EndKind.Clean or EndKind.Running)).OrderByDescending(s => s.Start).FirstOrDefault();
        r.Health = Health(d, merged, elimination, r);
        r.SettingsNotes = SettingsNotes(d, merged);
    }

    // Cluster crashes by the instruction that faulted. Deterministic, needs no interpretation, and it is the only
    // thing here that can say with certainty that two crashes are - or are not - the same problem.
    static List<CrashGroup> GroupBySignature(CollectedData d, List<Session> sessions)
    {
        var lastClean = sessions.Where(x => x.EndKind == EndKind.Clean).Select(x => x.Start).DefaultIfEmpty(DateTime.MinValue).Max();
        var groups = new List<CrashGroup>();
        foreach (var grp in sessions.Where(x => x.Signature != null).GroupBy(x => x.Signature!))
        {
            var list = grp.OrderBy(x => x.Start).ToList();
            var withReport = list.Select(x => d.CrashReports.FirstOrDefault(c => x.CrashTime != null && Math.Abs((c.At - x.CrashTime.Value).TotalSeconds) <= 90)).FirstOrDefault(c => c?.Dump != null);
            groups.Add(new CrashGroup
            {
                Signature = grp.Key,
                Fault = list[0].Fault ?? grp.Key,
                Build = list[0].Build,
                Exception = withReport?.Dump?.ExceptionName ?? "",
                Plain = withReport?.Dump?.Plain ?? "",
                Count = list.Count,
                FirstSeen = list[0].Start,
                LastSeen = list[^1].Start,
                Current = list[^1].Start > lastClean,
                Injector = list.Select(x => x.Injector).FirstOrDefault(x => x != null),
                InjectorSessions = list.Count(x => x.Injector != null),
                SessionIds = list.Select(x => x.Id).ToList(),
            });
        }
        // The same offset under two builds is two groups, because after a game patch it is two different
        // instructions. Say so rather than leaving someone to wonder why one fault is listed twice.
        foreach (var byFault in groups.GroupBy(g => g.Fault))
        {
            var builds = byFault.Select(g => g.Build).Distinct().ToList();
            if (builds.Count < 2) continue;
            foreach (var g in byFault)
            {
                g.OtherBuilds = builds.Count - 1;
                g.BuildNote = $"The same offset also appears under {Join(builds.Where(b => b != g.Build).Select(b => b ?? "an unknown build").ToList())}. A game update moves every address, so those are recorded separately: at this offset they are different instructions, not the same bug seen twice.";
            }
        }
        return groups.OrderByDescending(g => g.Count).ThenByDescending(g => g.LastSeen).ToList();
    }

    // Tell each crash how many others share its exact fault. A repeat is a pattern; a one-off is probably not worth
    // chasing, and saying which is which up front is most of the value.
    static void NoteGroupOnSessions(CrashGroup g, List<Session> sessions)
    {
        if (g.Count < 2) return;
        foreach (var s in sessions.Where(x => g.SessionIds.Contains(x.Id)))
            s.Verdict += $" This exact fault ({g.Fault}{(g.OtherBuilds > 0 && g.Build != null ? " on game build " + g.Build : "")}) has happened {g.Count} times between {g.FirstSeen:d MMM} and {g.LastSeen:d MMM} - it is one repeating problem, not {g.Count} unrelated crashes.";
    }

    // A crash with no surviving suspect is a legitimate outcome, not a hole to paper over. When elimination has
    // cleared everything, the verdict must stop implying a culprit and hand over a concrete next step instead.
    static void SayWhenNothingSurvived(Session s, Elimination el)
    {
        if (s.EndKind is EndKind.Clean or EndKind.Running || s.Suspects.Count > 0 || s.Verdict.Length == 0) return;
        if (s.RuledOut.Count > 0)
        {
            s.Verdict += $" {Join(s.RuledOut.Select(r => r.Mod).Distinct().ToList())} looked like {(s.RuledOut.Count == 1 ? "a lead" : "leads")} and {(s.RuledOut.Count == 1 ? "was" : "were")} ruled out: the same thing happens in sessions that end normally. Nothing else stands out, so there is no named cause for this one yet.";
            if (s.Confidence > 1) s.Confidence = 1;
        }
        else if (el.HasBaseline) s.Verdict += " No mod stands out: nothing was logged near the crash that does not also happen in sessions ending normally.";
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
    static void Judge(CollectedData d, Session s, List<Session> all, Elimination el)
    {
        var fg = d.Settings.TryGetValue("FrameGeneration", out var fgv) && fgv != "Off";
        var rt = SettingOn(d, "RayTracing"); d.Settings.TryGetValue("RayTracedLighting", out var rtl);
        var settingsKnown = !(s.CrashTime != null && d.SettingsWritten != null && d.SettingsWritten > s.CrashTime.Value.AddMinutes(1));

        if (s.EndKind == EndKind.Clean) { s.Verdict = $"Closed normally after {Dur(s.Minutes)}."; s.Confidence = 3; return; }
        if (s.EndKind == EndKind.Running) { s.Verdict = "Still running."; s.Confidence = 3; return; }

        var crashAt = s.CrashTime ?? s.End ?? s.Start;
        var report = s.CrashTime == null ? null : d.CrashReports.FirstOrDefault(c => Math.Abs((c.At - s.CrashTime.Value).TotalSeconds) <= 90);
        if (report != null)
        {
            if (report.VramKnown) { s.VramUsedMB = report.VramUsedMB; s.VramTotalMB = report.VramTotalMB; }
            s.Exception = report.Exception; s.Position = report.Position; s.Screenshot = report.Screenshot;
            if (report.Dump is { } dump)
            {
                s.Signature = dump.Signature;
                s.Fault = dump.Fault;
                s.Build = dump.ModuleVersion;
                s.FaultingModule = dump.FaultingModule;
                s.Injector = MiniDump.InjectorIn(dump.Modules);
                if (dump.Fault != null) s.Exception = dump.ExceptionName + " at " + dump.Fault;
            }
        }
        // The card was full. The engine needs headroom for transient allocations, so failures start well before 100 %:
        // crashes have been observed from about 90 % up. The driver also reports more than the card holds once it has
        // spilled into system memory, which is worse, not better.
        var vramTight = report != null && report.VramKnown && report.VramRatio >= 0.90;
        var vramOver = report != null && report.VramKnown && report.VramUsedMB >= report.VramTotalMB;
        var badRead = report?.Exception != null && report.Exception.Contains("ACCESS_VIOLATION", StringComparison.OrdinalIgnoreCase);
        // Changing graphics settings makes the engine tear down and rebuild its render targets, which is the single
        // largest memory spike a session ever sees. On a card that is already close to full it is a common way to fall over.
        s.AreaMods = AreaMods(d, s, crashAt);
        s.AreaSectors = d.SectorPatches.Where(x => x.At <= crashAt.AddSeconds(2) && x.At >= crashAt.AddSeconds(-60))
                                       .Select(x => x.Sector).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var failingHere = s.AreaMods.Where(a => a.Failing).ToList();
        var settingsChanged = d.SettingsWritten is { } sw && s.CrashTime != null && sw <= s.CrashTime.Value.AddSeconds(5) && sw >= s.CrashTime.Value.AddMinutes(-5) && sw >= s.Start;
        var window = d.Events.Where(e => e.At <= crashAt.AddSeconds(5) && e.At >= crashAt.AddSeconds(-EvidenceWindowSeconds) && (s.Partial || e.At >= s.Start)).OrderBy(e => e.At).ToList();
        var driver = window.Where(e => e.Kind == "driver" && e.At >= crashAt.AddSeconds(-DriverWindowSeconds)).ToList();
        var appcrash = window.FirstOrDefault(e => e.Kind == "appcrash");
        var power = window.FirstOrDefault(e => e.Kind == "power");
        var sessionErrors = d.Events.Where(e => e.Kind == "error" && e.At >= (s.Partial ? crashAt.AddMinutes(-30) : s.Start) && e.At <= crashAt.AddSeconds(5)).ToList();
        var repeats = sessionErrors.GroupBy(Key).Where(g => g.Count() >= 5).Select(g => g.Key).ToHashSet();
        // Two independent noise tests: it repeats endlessly inside this session, or it also happens in sessions that
        // ended cleanly. Either way it cannot be the reason this one died, so it is kept out of the verdict entirely.
        bool IsNoise(LogEvent e) => repeats.Contains(Key(e)) || el.IsNoise(e);
        var scriptErrs = window.Where(e => e.Kind == "error" && !IsNoise(e)).ToList();
        var noiseErrs = window.Where(e => e.Kind == "error" && IsNoise(e)).GroupBy(e => e.Source).Select(g => g.OrderByDescending(e => e.At).First()).ToList();
        // Record what the clean sessions eliminated, so the interface can show what is NOT the cause.
        foreach (var e in noiseErrs)
        {
            var owner = e.Mod ?? e.Source.Replace("CET · ", "");
            var why = el.WhyRuledOut(owner);
            if (why != null && !s.RuledOut.Any(x => x.Mod == owner)) s.RuledOut.Add(new RuledOut { Mod = owner, Why = why });
        }
        var xlErrs = window.Where(e => e.Kind is "xl-error" or "xl-warning" && (crashAt - e.At).TotalSeconds <= 20 && !e.Text.StartsWith("[WorldStreaming] Some patches have not been applied")).ToList();
        var activity = window.Where(e => e.Kind == "activity").OrderByDescending(e => e.At).FirstOrDefault();

        foreach (var e in driver) s.Evidence.Add(Ev(e, crashAt, true));
        if (appcrash != null) s.Evidence.Add(Ev(appcrash, crashAt, true));
        var noisyMods = BrokenPluginMods(d);
        bool Noise(LogEvent e) => el.IsNoise(e) || noisyMods.Contains(e.Mod ?? e.Source.Replace("CET · ", ""));
        foreach (var e in scriptErrs.TakeLast(4)) s.Evidence.Add(Ev(e, crashAt, (crashAt - e.At).TotalSeconds <= 30 && !Noise(e)));
        foreach (var e in xlErrs.TakeLast(2)) s.Evidence.Add(Ev(e, crashAt, false));
        foreach (var e in noiseErrs.Take(2)) { var ev = Ev(e, crashAt, false); var n = sessionErrors.Count(x => x.Source == e.Source); ev.Text += $"  (repeats all session, {n} times)"; s.Evidence.Add(ev); }
        if (activity != null) s.Evidence.Add(Ev(activity, crashAt, false));
        if (power != null) s.Evidence.Add(Ev(power, crashAt, true));
        if (report != null && report.VramKnown) s.Evidence.Add(new Evidence { TMinus = 0, Source = "Crash report · video memory", Text = $"{report.VramUsedMB} MB of the card's {report.VramTotalMB} MB were in use ({Pct(report.VramRatio)})" + (vramOver ? " — over what the card holds; the driver had spilled into system memory" : ""), Hit = vramTight });
        if (settingsChanged) s.Evidence.Add(new Evidence { TMinus = Math.Max(0, (int)Math.Round((crashAt - d.SettingsWritten!.Value).TotalSeconds)), Source = "Graphics settings", Text = "the settings were changed during this session (the game rebuilt its render targets)", Hit = false, At = d.SettingsWritten!.Value });
        if (report?.Exception != null) s.Evidence.Add(new Evidence { TMinus = 0, Source = "Crash report · exception", Text = report.Exception + (report.ExceptionDetail != null ? " — " + report.ExceptionDetail : ""), Hit = false });
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
            if (vramTight) s.Verdict += $" Video memory was {(vramOver ? "over" : "at")} the card's limit at the time ({report!.VramUsedMB} of {report.VramTotalMB} MB), which is the usual reason a GPU faults in this game.";
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
            AddScriptSuspects(d, s, scriptErrs, xlErrs, crashAt, el);
            return;
        }
        if (appcrash != null)
        {
            s.Confidence = 2; var mod = appcrash.Mod ?? "";
            var raw = Regex.Match(appcrash.Text, @"faulted in (\S+)").Groups[1].Value;
            if (Regex.IsMatch(raw, @"^(nvwgf2um|nvlddmkm|nvgpucomp|amdxc64|amdxx64|atidxx|igd)", RegexOptions.IgnoreCase)) { s.EndKind = EndKind.Gpu; s.Confidence = 3; s.Verdict = $"Windows recorded the fault inside the graphics driver ({raw}).{partialNote}"; s.Suspects.Add(new Suspect { Mod = "GPU driver", Level = "high", Why = "The faulting module is the display driver. A clean install of the previous driver version is the usual fix.", Actions = { new UiAction("open:driver", "Open Device Manager") } }); return; }
            if (mod != raw && d.Mods.Find(mod) != null) { s.EndKind = EndKind.Script; s.Confidence = 3; s.Verdict = $"Windows recorded the fault inside {raw}, which belongs to the mod {mod}.{partialNote}"; s.Suspects.Add(SuspectFor(d, mod, "high", "Its native code is where the crash happened.")); return; }
            s.Verdict = $"Windows recorded the fault in {raw}." + (raw.StartsWith("Cyberpunk2077", StringComparison.OrdinalIgnoreCase) ? " That is the game itself, which usually means bad data handed to it by a mod (a broken mesh, appearance or sector patch)." : "") + partialNote;
            AddScriptSuspects(d, s, scriptErrs, xlErrs, crashAt, el);
            return;
        }
        // A full card, with the engine dying on a read of memory that was never handed to it, is the commonest
        // "crash with nothing in the logs". Nothing writes an error for it, so it has to be read out of the crash report.
        if (vramTight && report != null)
        {
            s.EndKind = EndKind.Vram;
            s.Confidence = vramOver ? 3 : report.VramRatio >= 0.95 ? 2 : 1;
            s.Verdict = $"Ran out of video memory. {report.VramUsedMB} MB of the card's {report.VramTotalMB} MB were in use when it died ({Pct(report.VramRatio)})"
                + (vramOver ? ", i.e. past what the card physically holds, so the driver had already spilled into system memory. " : ". ")
                + (badRead ? "The engine then failed to get memory for something and crashed reading a resource that was never created (" + report.Exception + "). " : "")
                + "No driver fault and no mod error was recorded, which is exactly how this looks: the game simply asks for more than the card can hold and falls over."
                + (settingsChanged ? " The graphics settings were also changed during this session, minutes before the crash — rebuilding the render targets is the biggest memory spike a session ever sees, and on an already-full card that alone can end it. Judge the new settings by the next few sessions, not by this one." : "")
                + partialNote;
            var levers = HeavySettings(d);
            s.Suspects.Add(new Suspect
            {
                Mod = "Video memory ceiling",
                Level = "high",
                Why = (levers.Count > 0 ? "The expensive settings right now are " + Join(levers) + ". Turning any one of them down frees more than any single mod would. " : "")
                    + "Textures and crowd density are the two biggest levers; ray traced lighting and reflections are next.",
                Actions = { new UiAction("settings", "See the settings") }
            });
            var heavy = HeaviestTextureMods(d);
            if (heavy.Count > 0) s.Suspects.Add(new Suspect { Mod = "Installed texture weight", Level = "low", Why = $"{Mb(d.Mods.ArchiveBytes)} of mod archives are installed. The heaviest are {Join(heavy)}. Nothing here is broken — it is simply more texture than a {Math.Round(report.VramTotalMB / 1024.0)} GB card can keep resident in a dense district.", Actions = { new UiAction("mods", "See the mod list") } });
            AddScriptSuspects(d, s, scriptErrs, xlErrs, crashAt, el);
            return;
        }
        if (scriptErrs.Count > 0)
        {
            var last = scriptErrs.Last(); var secs = (int)Math.Round((crashAt - last.At).TotalSeconds);
            s.EndKind = EndKind.Script; s.Confidence = secs <= 15 ? 2 : 1;
            var name = last.Mod ?? last.Source.Replace("CET · ", "");
            s.Verdict = $"Script error in {name} {secs} s before the crash: {Short(last.Text)} Not a GPU fault; the engine recorded no error of its own.{partialNote}";
            AddScriptSuspects(d, s, scriptErrs, xlErrs, crashAt, el);
            return;
        }
        if (xlErrs.Count > 0 && activity != null && activity.Text.Contains("world streaming"))
        {
            var last = xlErrs.Last(); var secs = (int)Math.Round((crashAt - last.At).TotalSeconds);
            s.EndKind = EndKind.Script; s.Confidence = failingHere.Count > 0 ? 2 : 1;
            var shared = s.AreaMods.Count;
            s.Verdict = $"Crashed during world streaming, {secs} s after ArchiveXL reported a failed sector patch from {last.Mod ?? "a mod"}."
                + (shared > 1 ? $" {shared} mods were rewriting the map sectors loading around you at the time" + (failingHere.Count > 0 ? $", and {Join(failingHere.Select(a => a.Mod).Distinct().ToList())} " + (failingHere.Count == 1 ? "is the one that " : "are the ones that ") + "no longer fits the sector it edits." : ".") : " A world-editing mod is the likely cause.")
                + partialNote;
            AddAreaSuspects(d, s, el);
            AddScriptSuspects(d, s, scriptErrs, xlErrs, crashAt, el);
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
        AddScriptSuspects(d, s, scriptErrs, xlErrs, crashAt, el);
        AddAreaSuspects(d, s, el);
        if (s.AreaMods.Count == 0 && activity != null && activity.Text.Contains("world streaming")) foreach (var m in WorldPatchMods(d).Take(3)) if (!s.Suspects.Any(x => x.Mod == m)) s.Suspects.Add(SuspectFor(d, m, "medium", "Edits world sectors; failed or overlapping sector patches crash during streaming."));
        AddLocationSuspects(d, s);
    }

    // Which mods were rewriting the map sectors that streamed in around the player in the last minute of the session.
    // ArchiveXL names both the sector and every .xl applied to it, so this is read off the log rather than guessed.
    static List<AreaMod> AreaMods(CollectedData d, Session s, DateTime crashAt)
    {
        var here = d.SectorPatches.Where(p => p.At <= crashAt.AddSeconds(2) && p.At >= crashAt.AddSeconds(-60)).ToList();
        if (here.Count == 0) return new();
        // sector -> the set of .xl files that patched it
        var bySector = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in here)
        {
            if (!bySector.TryGetValue(p.Sector, out var set)) bySector[p.Sector] = set = new(StringComparer.OrdinalIgnoreCase);
            foreach (var x in p.Xls) set.Add(x);
        }
        var failing = d.Events.Where(e => e.Kind is "xl-error" or "xl-warning" && e.At >= s.Start && e.At <= crashAt.AddSeconds(2) && e.Text.Contains("WorldStreaming"))
                              .Select(e => XlNameIn(e.Text)).Where(x => x != null).Select(x => x!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = new List<AreaMod>();
        foreach (var xl in bySector.Values.SelectMany(v => v).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var mine = bySector.Where(kv => kv.Value.Contains(xl)).Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var others = bySector.Where(kv => mine.Contains(kv.Key)).SelectMany(kv => kv.Value).Where(o => !o.Equals(xl, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            rows.Add(new AreaMod { Mod = d.Mods.OwnerOfXl(xl) ?? Path.GetFileNameWithoutExtension(xl), File = xl, Sectors = mine.Count, SharedWith = others, Failing = failing.Contains(xl) });
        }
        foreach (var r in rows)
            r.Note = r.Failing ? "ArchiveXL reported this mod's patch did not fit the sector it was applied to during this session."
                   : r.SharedWith >= 3 ? "Shares these sectors with several other mods."
                   : null;
        return rows.OrderByDescending(r => r.Failing).ThenByDescending(r => r.Sectors).Take(12).ToList();
    }
    static string? XlNameIn(string text) { var m = Regex.Match(text, @"([\w\-. ]+\.xl):"); return m.Success ? m.Groups[1].Value.Trim() : null; }

    // Mods whose ArchiveXL patches name the place or quest where the crash happened. A crash with no error logged, in a
    // place several mods rewrite, is usually one of them.
    static void AddLocationSuspects(CollectedData d, Session s)
    {
        var tokens = new List<string>();
        if (s.Quest != null) { var q = Regex.Match(s.Quest, @"^([a-z]+\d+)"); if (q.Success) tokens.Add(q.Groups[1].Value); }
        if (s.District != null) { var slug = Regex.Replace(s.District.Split('·')[0].Trim(), "(?<=[a-z])(?=[A-Z])", "_").Replace(" ", "_").ToLower(); tokens.Add(slug); tokens.Add(slug.Replace("_", "")); }
        tokens = tokens.Where(t => t.Length >= 4).Distinct().ToList();
        if (tokens.Count == 0) return;
        var hits = new List<(string mod, int n)>();
        foreach (var m in d.Mods.Mods.Where(m => m.Status != "removed"))
        {
            int n = 0;
            foreach (var rel in m.Files.Where(f => f.EndsWith(".xl", StringComparison.OrdinalIgnoreCase)))
            {
                string txt; try { txt = Files.ReadAllTextShared(Path.Combine(d.Paths.GameDir, rel)); } catch { continue; }
                foreach (var t in tokens) n += Regex.Matches(txt, Regex.Escape(t), RegexOptions.IgnoreCase).Count;
            }
            if (n > 0) hits.Add((m.Name, n));
        }
        var where = s.Quest != null && s.District != null ? $"{s.District} during {s.Quest}" : s.District ?? s.Quest ?? "this location";
        foreach (var (mod, n) in hits.OrderByDescending(h => h.n).Take(4))
        {
            if (s.Suspects.Any(x => x.Mod == mod)) continue;
            var row = d.Mods.Find(mod); var wip = row != null && (Regex.IsMatch(row.Version, @"^0\.[01](\.|$)") || Regex.IsMatch(mod, @"\b(WIP|alpha|beta|test)\b", RegexOptions.IgnoreCase));
            s.Suspects.Add(SuspectFor(d, mod, wip ? "high" : n >= 20 ? "medium" : "low", $"Its patches rewrite {where} ({n} references){(wip ? "; it is an unfinished early version" : "")}. With several mods editing one place, one of them is the usual cause of a crash while NPCs or the area load."));
        }
    }

    // Mods whose native plugin RED4ext refused to load on this game patch. Their Lua half then errors on every tick.
    static HashSet<string> BrokenPluginMods(CollectedData d)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var latest = d.Red4ext.OrderByDescending(x => x.Start).FirstOrDefault();
        if (latest == null) return set;
        foreach (var inc in latest.Incompatible)
        {
            var name = Regex.Match(inc, @"^(.+?) \(version").Groups[1].Value;
            if (name.Length == 0) continue;
            set.Add(name);
            var owner = d.Mods.OwnerOfPlugin(name.Replace(" ", "")) ?? d.Mods.OwnerOfPlugin(name);
            if (owner != null) set.Add(owner);
        }
        return set;
    }

    internal static string Pct(double r) => Math.Round(r * 100) + "%";
    static string Mb(long bytes) => bytes >= 1073741824L ? $"{bytes / 1073741824.0:0.#} GB" : $"{bytes / 1048576L} MB";
    static string Join(List<string> x) => x.Count == 1 ? x[0] : string.Join(", ", x.Take(x.Count - 1)) + " and " + x[^1];

    // The graphics settings that cost the most video memory, named only when they are actually turned up.
    static List<string> HeavySettings(CollectedData d)
    {
        var l = new List<string>();
        if (d.Settings.TryGetValue("TextureQuality", out var t) && t is "High" or "Ultra") l.Add($"textures {t}");
        if (d.Settings.TryGetValue("CrowdDensity", out var c) && c is "High" or "Ultra") l.Add($"crowd density {c}");
        if (d.Settings.TryGetValue("RayTracedLighting", out var rl) && rl is "High" or "Ultra" or "Psycho") l.Add($"ray traced lighting {rl}");
        if (SettingOn(d, "RayTracedReflections")) l.Add("ray traced reflections on");
        if (d.Settings.TryGetValue("ScreenSpaceReflectionsQuality", out var ssr) && ssr is "Ultra" or "Psycho") l.Add($"screen space reflections {ssr}");
        if (SettingOn(d, "RayTracedPathTracing")) l.Add("path tracing on");
        return l;
    }

    static List<string> HeaviestTextureMods(CollectedData d) => d.Mods.HeaviestArchives(3);

    static string Key(LogEvent e) => e.Source + "|" + Regex.Replace(e.Text, @"\d+", "#");

    // Suspects drawn from what was actually being rewritten underfoot. A mod whose patch ArchiveXL had to skip is the
    // strongest of these: it proves the sector on disk is not the sector the mod was built against.
    static void AddAreaSuspects(CollectedData d, Session s, Elimination el)
    {
        foreach (var a in s.AreaMods.Where(x => x.Failing || x.SharedWith >= 3).Take(4))
        {
            if (s.Suspects.Any(x => x.Mod == a.Mod)) continue;
            var cleared = el.WhyRuledOut(a.Mod);
            if (cleared != null) { if (!s.RuledOut.Any(x => x.Mod == a.Mod)) s.RuledOut.Add(new RuledOut { Mod = a.Mod, Why = cleared }); continue; }
            var why = a.Failing
                ? $"It rewrites {a.Sectors} of the map sectors that were loading around you, and ArchiveXL had to skip part of its patch this session because the sector no longer matches what the mod expects. {a.SharedWith} other mod{(a.SharedWith == 1 ? "" : "s")} edit the same ground."
                : $"It rewrites {a.Sectors} of the map sectors that were loading around you, shared with {a.SharedWith} other mods. Overlapping sector edits are a common cause of crashes while moving through an area.";
            s.Suspects.Add(SuspectFor(d, a.Mod, a.Failing ? "high" : "low", why));
        }
    }

    static void AddScriptSuspects(CollectedData d, Session s, List<LogEvent> scriptErrs, List<LogEvent> xlErrs, DateTime crashAt, Elimination el)
    {
        var brokenPlugin = BrokenPluginMods(d);

        // Returns true and records the elimination if this mod cannot be the cause. Two independent reasons:
        // it also misbehaves in sessions that end cleanly, or its native half never loads on this game patch at all
        // (in which case its script errors are a permanent condition, not an event that happened at the crash).
        bool Eliminated(string name)
        {
            if (s.RuledOut.Any(x => x.Mod == name)) return true;
            var why = el.WhyRuledOut(name);
            if (why == null && brokenPlugin.Contains(name))
                why = "It errors constantly because its native plugin will not load on this game patch. That is a standing fault, not something that happened at the crash.";
            if (why == null) return false;
            s.RuledOut.Add(new RuledOut { Mod = name, Why = why });
            return true;
        }

        foreach (var grp in scriptErrs.GroupBy(e => e.Mod ?? e.Source.Replace("CET · ", "")).OrderBy(g => (crashAt - g.Max(e => e.At)).TotalSeconds))
        {
            var name = grp.Key;
            if (s.Suspects.Any(x => x.Mod == name) || Eliminated(name)) continue;
            var secs = (int)Math.Round((crashAt - grp.Max(e => e.At)).TotalSeconds);
            // Without a clean baseline nothing has been checked against anything, so no suspect may be called high.
            var level = !el.HasBaseline ? "medium" : secs <= 15 ? "high" : "medium";
            var why = $"Its script errored {secs} s before the crash: {Short(grp.Last().Text)}"
                    + (el.HasBaseline ? $" It does not do this in any of the {el.CleanSessions} sessions that ended cleanly." : NoBaselineNote(el));
            s.Suspects.Add(SuspectFor(d, name, level, why));
        }
        foreach (var grp in xlErrs.Where(e => e.Mod != null).GroupBy(e => e.Mod!))
        {
            var name = grp.Key;
            if (s.Suspects.Any(x => x.Mod == name) || Eliminated(name)) continue;
            var secs = (int)Math.Round((crashAt - grp.Max(e => e.At)).TotalSeconds);
            s.Suspects.Add(SuspectFor(d, name, "medium", $"ArchiveXL reported a problem with its patch {secs} s before the crash: {Short(grp.Last().Text)}"
                    + (el.HasBaseline ? "" : NoBaselineNote(el))));
        }
    }

    // Said out loud rather than hidden: with nothing to compare against, a lead is a guess.
    static string NoBaselineNote(Elimination el) =>
        el.CleanSessions == 0
            ? " No session on record has ended cleanly, so there is nothing to compare this against — treat it as a lead, not a finding."
            : $" Only {el.CleanSessions} session on record ended cleanly, which is too few to rule anything out — treat this as a lead, not a finding.";

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
    internal static bool SettingOn(CollectedData d, string key) => d.Settings.TryGetValue(key, out var v) && (v == "true" || v == "True");
    static string Dur(double m) => m < 1 ? "under a minute" : m < 60 ? $"{Math.Round(m)} min" : $"{(int)(m / 60)} h {Math.Round(m % 60)} min";
    internal static string Short(string t) => t.Length > 110 ? t[..110] + "…" : t;
    static string Ordinal(int n) => n switch { 1 => "first", 2 => "second", 3 => "third", 4 => "fourth", 5 => "fifth", _ => n + "th" };
    static string? Pretty(string? district) => district == null ? null : Regex.Replace(district.Replace("_", " · "), "(?<=[a-z])(?=[A-Z])", " ");

    // ---------- health ----------
    static List<HealthItem> Health(CollectedData d, List<Session> sessions, Elimination el, Report r)
    {
        var h = new List<HealthItem>();
        // How much of a clean baseline exists decides how much anything else here can be trusted. Say it first -
        // but only when there is actually a crash to reason about. On an install that has never crashed, warning
        // that suspects cannot be confirmed is both meaningless and mildly alarming.
        var anyCrash = sessions.Any(s => s.EndKind is not (EndKind.Clean or EndKind.Running));
        if (!anyCrash) { /* nothing to eliminate against, and nothing to eliminate */ }
        else if (!el.HasBaseline)
            h.Add(new HealthItem { Severity = "medium", Title = el.CleanSessions == 0 ? "No session has ended cleanly yet" : "Only one session has ended cleanly",
                Detail = $"Crash Doctor rules a mod out by checking whether it misbehaves in sessions that end normally too. With {(el.CleanSessions == 0 ? "no clean sessions" : "one clean session")} on record there is nothing to compare against, so every suspect below is a lead rather than a finding. Play until you quit the game normally a couple of times and scan again — the diagnosis gets sharper, not vaguer, the more you play." });
        else
            h.Add(new HealthItem { Severity = "info", Title = $"{el.CleanSessions} clean sessions are being used to rule things out",
                Detail = $"Anything that also happens in a session you quit normally cannot be why another one crashed. Of the {el.CleanSessions + el.CrashedSessions} sessions whose logs the game still keeps, {el.CleanSessions} ended cleanly and {el.CrashedSessions} crashed — that comparison is what lets the diagnosis discard background noise instead of blaming it. Older sessions are remembered but their logs have been rotated away, so they cannot be compared." });
        var week = sessions.Where(s => s.Start > DateTime.Now.AddDays(-7)).ToList();
        var full = week.Where(s => !s.Partial && s.EndKind != EndKind.Running).ToList();
        var partial = week.Count(s => s.Partial);
        if (full.Count > 0 || partial > 0)
        {
            var wc = full.Count(s => s.EndKind != EndKind.Clean);
            var crashesFull = full.Where(s => s.EndKind != EndKind.Clean).ToList();
            var med = crashesFull.Count > 0 ? crashesFull.Select(s => s.Minutes).OrderBy(x => x).ElementAt(crashesFull.Count / 2) : 0;
            var sev = full.Count > 0 && wc >= 3 && wc >= full.Count / 2.0 ? "high" : (wc + partial) > 0 ? "medium" : "info";
            var detail = full.Count > 0 ? $"{wc} of {full.Count} logged sessions crashed" + (wc > 0 ? $"; typical time to crash {Dur(med)}" : "") + $". {crashesFull.Count(s => s.EndKind == EndKind.Gpu)} GPU faults, {crashesFull.Count(s => s.EndKind == EndKind.Vram)} out of video memory, {crashesFull.Count(s => s.EndKind == EndKind.Script)} mod/script, {crashesFull.Count(s => s.EndKind is EndKind.Unknown or EndKind.Engine)} other." : "";
            if (partial > 0) detail += $" Plus {partial} crash report{(partial == 1 ? "" : "s")} from sessions whose logs have been rotated away.";
            h.Add(new HealthItem { Severity = sev, Title = $"{wc + partial} crash{(wc + partial == 1 ? "" : "es")} in the last 7 days", Detail = detail.Trim() });
        }
        var crashes = sessions.Where(s => s.EndKind is not (EndKind.Clean or EndKind.Running)).ToList();

        // The distinct-problems count, read straight off the minidumps. This reframes everything below it.
        var groups = GroupBySignature(d, sessions);
        if (groups.Count > 0)
        {
            var repeat = groups.Where(g => g.Count >= 2).ToList();
            var live = groups.Where(g => g.Current).ToList();
            var total = groups.Sum(g => g.Count);
            var detail = $"{total} crashes with a readable dump fall into {groups.Count} distinct fault{(groups.Count == 1 ? "" : "s")} - crashes at the same machine instruction are the same bug. "
                + (repeat.Count > 0 ? $"{repeat.Count} of them repeat: " + Join(repeat.Take(3).Select(g => $"{g.Fault} ({g.Count}x, {g.FirstSeen:d MMM}-{g.LastSeen:d MMM})").ToList()) + ". " : "")
                + (live.Count == 0 ? "None of them has happened since your last clean session, so nothing is currently recurring." : $"{live.Count} {(live.Count == 1 ? "is" : "are")} still happening since the last clean session.");
            // "3 separate problems, not 3 random ones" was the wording when nothing repeated, which says nothing at
            // all. Nothing repeating is its own finding, and the opposite of the usual one.
            var title = groups.Count == 1 ? "All your crashes are one repeating fault"
                      : groups.Count == total ? $"Your {total} crashes are {total} different faults, none of them repeating"
                      : $"Your crashes are {groups.Count} separate problems, not {total} random ones";
            h.Add(new HealthItem { Severity = live.Count > 0 ? "medium" : "info", Title = title, Detail = detail });

            // An injected trainer or cheat tool is worth stating, with the count, and without a conclusion attached.
            var inj = groups.Where(g => g.InjectorSessions > 0).ToList();
            if (inj.Count > 0)
            {
                var withInj = inj.Sum(g => g.InjectorSessions);
                h.Add(new HealthItem { Severity = "low", Title = $"{inj[0].Injector} was running inside the game in {withInj} of these crashes",
                    Detail = $"Tools like this patch game code at fixed addresses, so one built for a different game version writes into the wrong place. That makes it worth knowing about - but {withInj} of {total} is a correlation, not a cause, and it can only be settled by playing without it. Close it before launching if you want to test that." });
            }
        }

        // Everything that used to sit here - the video-memory run, the settings that do not fit the card, the
        // driver age, the plugins RED4ext refused, ArchiveXL's startup complaints, redscript, CET error spam and
        // duplicated scripts - is now in Rules.cs, where each one can be listed, reasoned about and tested on its
        // own. What is left in this method is the part that is not a rule: what the clean sessions allow us to
        // conclude, how much crashed this week, and how many distinct faults that really is.
        var latestRed = d.Red4ext.OrderByDescending(x => x.Start).FirstOrDefault();
        if (latestRed != null && latestRed.Incompatible.Count == 0 && d.RedscriptErrors.Count == 0) h.Add(new HealthItem { Severity = "info", Title = "Frameworks loaded cleanly", Detail = $"RED4ext {latestRed.Red4extVersion} and {latestRed.PluginsLoaded.Count} native plugins loaded without warnings; scripts compiled." });
        if (d.Warnings.Count > 0) h.Add(new HealthItem { Severity = "info", Title = "Some sources could not be read", Detail = string.Join(" ", d.Warnings) });
        // The rule catalogue: recognisable causes that need no crash signature at all. See Rules.cs.
        foreach (var hit in Rules.Run(d, r)) h.Add(new HealthItem { Severity = hit.Severity, Title = hit.Title, Detail = hit.Detail, Mod = hit.Mod, Flag = hit.Flag, Action = hit.Action });
        return h;
    }

    // Many mods ship a Config.reds / Utils.reds inside their own folder; that is a naming coincidence, not a conflict.
    internal static bool GenericScriptName(string name) => Regex.IsMatch(name, @"^(config|utils|util|helpers|helper|classes|main|module|callback|callbacks|events|settings|init|types|globals|data|core)\.reds$", RegexOptions.IgnoreCase);

    internal static UiAction? NexusAction(CollectedData d, string? modName) { var row = modName == null ? null : d.Mods.Find(modName); return row?.NexusId != null ? new UiAction("nexus:" + row.NexusId, "Open on Nexus") : null; }
    internal static string? GuessOwnerFromText(CollectedData d, string text)
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

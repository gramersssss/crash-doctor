using System.Text.RegularExpressions;

namespace CrashDoctor.Engine;

// The game updated. (Patch 2.5, "Edgerunners 2 Update", is due 22 Oct 2026, and this has to read right the morning
// after.)
//
// RED4ext, redscript, Cyber Engine Tweaks, ArchiveXL, TweakXL and Codeware hook into the game's own code, so a game
// update usually breaks them until their authors release a matching build - and every mod built on them breaks
// with them. The night after a patch the honest answer to "why does it crash now" is very often "your frameworks do
// not support this game version yet; wait for their updates", and a tool that instead starts naming mods is doing
// harm.
//
// What this does NOT do is guess which framework versions support which game version. That table would be wrong
// the day a framework ships, and nobody can verify it in advance. Instead it reads what each framework itself wrote
// the first time the game ran after the update: RED4ext's log (and which plugins it refused), CET's log (the game
// build it saw, and whether it said it does not support it), and the redscript log (whether the scripts compiled).
// That is evidence, it is specific to this install, and it is right for any future version as well.
public sealed class GameVersionChange
{
    public string? From { get; set; }            // "2.31"
    public string? FromBuild { get; set; }       // "3.0.80.51928"
    public string? To { get; set; }
    public string? ToBuild { get; set; }
    public DateTime At { get; set; }             // when the exe was rewritten (or first seen changed)
    public bool Newer { get; set; } = true;      // false = the build went back (a Steam beta switch, a rollback)
}

public sealed class FrameworkCheck
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "";      // ran | refused | errors | notStarted | notRun
    public string Text { get; set; } = "";       // token
}

public static class GameUpdate
{
    // How long after an update the notice stays when every framework has already proven itself on the new build.
    const int QuietDays = 7;

    /// <summary>The game version to show, from the most current source that has one.</summary>
    public static string Version(CollectedData d, string fileVersion, string productVersion)
    {
        if (!string.IsNullOrWhiteSpace(productVersion)) return productVersion.Trim();
        // a log only speaks for the build it was written by
        var red = d.Red4ext.OrderByDescending(x => x.Start).FirstOrDefault(x => fileVersion.Length == 0 || x.FileVersion == fileVersion);
        if (!string.IsNullOrEmpty(red?.ProductVersion)) return red!.ProductVersion;
        var patch = d.CrashReports.OrderByDescending(c => c.At).Select(c => c.GamePatch).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (patch != null && fileVersion.Length == 0) return patch;
        return fileVersion switch { var v when v.StartsWith("3.0.80.51928") => "2.31", "" => "", _ => fileVersion };
    }

    /// <summary>
    /// Notice a change of game build: against the build remembered from the last scan, or failing that against the
    /// logs of sessions played before the exe was rewritten. Remembers the current build for next time.
    /// </summary>
    public static GameVersionChange? Detect(CollectedData d, GameInfo game, bool remember)
    {
        var cfg = GameLocator.LoadConfig();
        var exeTime = d.GameExeWritten;
        var nowBuild = game.FileVersion;
        var nowVersion = game.Version;
        // the fixture exe (and a damaged one) has no version: then the newest log written after the exe is the build
        if (nowBuild.Length == 0 && exeTime != null)
        {
            var after = d.Red4ext.Where(x => x.Start >= exeTime && x.FileVersion.Length > 0).OrderByDescending(x => x.Start).FirstOrDefault();
            if (after != null) { nowBuild = after.FileVersion; if (nowVersion.Length == 0 || nowVersion == game.FileVersion) nowVersion = after.ProductVersion; }
        }
        GameVersionChange? change = null;

        if (nowBuild.Length > 0 && cfg.LastGameFileVersion is { Length: > 0 } last && last != nowBuild)
            change = new GameVersionChange { From = cfg.LastGameVersion, FromBuild = last, To = nowVersion, ToBuild = nowBuild, At = exeTime ?? DateTime.Now };
        else if (nowBuild.Length > 0 && cfg.GameVersionChangedAt is { } at && cfg.PreviousGameFileVersion is { Length: > 0 } prev && prev != nowBuild && cfg.LastGameFileVersion == nowBuild)
            change = new GameVersionChange { From = cfg.PreviousGameVersion, FromBuild = prev, To = nowVersion, ToBuild = nowBuild, At = at };
        else if (nowBuild.Length > 0 && exeTime != null)
        {
            // first scan since Crash Doctor was installed or its settings were reset: the sessions before the exe changed
            var before = d.Red4ext.Where(x => x.Start < exeTime && x.FileVersion.Length > 0).OrderByDescending(x => x.Start).FirstOrDefault();
            if (before != null && before.FileVersion != nowBuild)
                change = new GameVersionChange { From = before.ProductVersion, FromBuild = before.FileVersion, To = nowVersion, ToBuild = nowBuild, At = exeTime.Value };
        }
        if (change != null)
        {
            change.Newer = !(System.Version.TryParse(change.FromBuild, out var a) && System.Version.TryParse(change.ToBuild, out var b) && b < a);
            if (string.IsNullOrEmpty(change.To)) change.To = change.ToBuild;
            if (string.IsNullOrEmpty(change.From)) change.From = change.FromBuild;
        }

        if (remember && nowBuild.Length > 0)
        {
            var changed = cfg.LastGameFileVersion is { Length: > 0 } lb && lb != nowBuild;
            if (changed) { cfg.PreviousGameFileVersion = cfg.LastGameFileVersion; cfg.PreviousGameVersion = cfg.LastGameVersion; cfg.GameVersionChangedAt = change?.At ?? DateTime.Now; }
            // a change found in the logs is remembered too, so it is still known after those logs rotate away
            else if (change != null && cfg.PreviousGameFileVersion != change.FromBuild)
            { cfg.PreviousGameFileVersion = change.FromBuild; cfg.PreviousGameVersion = change.From; cfg.GameVersionChangedAt = change.At; changed = true; }
            if (changed || cfg.LastGameFileVersion != nowBuild || cfg.LastGameVersion != nowVersion)
            {
                cfg.LastGameFileVersion = nowBuild; cfg.LastGameVersion = nowVersion;
                try { GameLocator.SaveConfig(cfg); } catch { }
            }
        }
        return change;
    }

    /// <summary>What each framework's own log says about the build it found after the change.</summary>
    public static List<FrameworkCheck> Frameworks(CollectedData d, GameVersionChange c)
    {
        var g = d.Paths; var list = new List<FrameworkCheck>();
        var since = c.At.AddMinutes(-1);
        // did the game itself run since? A crash report, a CET start, or any RED4ext log says so
        var gameRan = d.Red4ext.Any(x => x.Start >= since) || d.CetStarts.Any(x => x.at >= since) || d.CrashReports.Any(x => x.At >= since);
        string Missing(string name) => gameRan ? L.T("update.fw.notStarted", name) : L.T("update.fw.notRun", name);
        var MissingState = gameRan ? "notStarted" : "notRun";

        var red = d.Red4ext.Where(x => x.Start >= since).OrderByDescending(x => x.Start).FirstOrDefault();
        var hasRed = g.Exists(@"red4ext\RED4ext.dll") || g.Exists(@"bin\x64\winmm.dll") || g.DirExists("red4ext");
        if (hasRed)
        {
            if (red == null) list.Add(new FrameworkCheck { Name = "RED4ext", State = MissingState, Text = Missing("RED4ext") });
            else if (red.Errors.Count > 0) list.Add(new FrameworkCheck { Name = "RED4ext", State = "errors", Text = L.T("update.fw.red4extErrors", red.Red4extVersion, L.Date(red.Start, "dmt"), red.Errors[0]) });
            else list.Add(new FrameworkCheck { Name = "RED4ext", State = "ran", Text = L.P("update.fw.red4extRan", red.PluginsLoaded.Count, red.Red4extVersion, red.ProductVersion.Length > 0 ? red.ProductVersion : red.FileVersion) });

            foreach (var plugin in new[] { "ArchiveXL", "TweakXL", "Codeware" })
            {
                if (!g.DirExists(Path.Combine(GamePaths.RelRed4extPlugins, plugin))) continue;
                if (red == null) { list.Add(new FrameworkCheck { Name = plugin, State = MissingState, Text = Missing(plugin) }); continue; }
                var refused = red.Incompatible.FirstOrDefault(i => i.StartsWith(plugin, StringComparison.OrdinalIgnoreCase));
                var loaded = red.PluginsLoaded.FirstOrDefault(p => p.name.Equals(plugin, StringComparison.OrdinalIgnoreCase));
                if (refused != null) list.Add(new FrameworkCheck { Name = plugin, State = "refused", Text = L.T("update.fw.pluginRefused", plugin, refused) });
                else if (loaded.name != null) list.Add(new FrameworkCheck { Name = plugin, State = "ran", Text = L.T("update.fw.pluginLoaded", plugin, loaded.version) });
                else list.Add(new FrameworkCheck { Name = plugin, State = "errors", Text = L.T("update.fw.pluginAbsent", plugin) });
            }
            // every other native plugin RED4ext turned away on the new build, counted in one line
            if (red != null)
            {
                var others = red.Incompatible.Select(i => Regex.Match(i, @"^(.+?) \(version").Groups[1].Value).Where(n => n.Length > 0 && !new[] { "ArchiveXL", "TweakXL", "Codeware" }.Contains(n, StringComparer.OrdinalIgnoreCase)).Distinct().ToList();
                if (others.Count > 0) list.Add(new FrameworkCheck { Name = "plugins", State = "refused", Text = L.P("update.fw.otherPlugins", others.Count, L.List(others.Take(6).ToList())) });
            }
        }

        if (g.Exists(@"bin\x64\plugins\cyber_engine_tweaks.asi") || d.CetLog != null)
        {
            var refused = d.CetRefused.Where(x => x.at >= since).Select(x => Regex.Replace(x.line, @"^(\[[^\]]*\]\s*)+", "")).FirstOrDefault();
            var start = d.CetStarts.Where(x => x.at >= since).OrderByDescending(x => x.at).FirstOrDefault();
            if (refused != null) list.Add(new FrameworkCheck { Name = "Cyber Engine Tweaks", State = "refused", Text = L.T("update.fw.cetRefused", refused) });
            else if (start.gameVersion != null) list.Add(new FrameworkCheck { Name = "Cyber Engine Tweaks", State = "ran", Text = L.T("update.fw.cetRan", d.CetVersion, start.gameVersion) });
            else list.Add(new FrameworkCheck { Name = "Cyber Engine Tweaks", State = MissingState, Text = Missing("Cyber Engine Tweaks") });
        }

        if (g.Exists(@"engine\tools\scc.exe") || g.Exists(@"engine\tools\scc_lib.dll"))
        {
            var written = d.RedscriptLog != null ? File.GetLastWriteTime(d.RedscriptLog) : (DateTime?)null;
            if (written == null || written < since) list.Add(new FrameworkCheck { Name = "redscript", State = MissingState, Text = Missing("redscript") });
            else if (d.RedscriptErrors.Count > 0) list.Add(new FrameworkCheck { Name = "redscript", State = "errors", Text = L.P("update.fw.redscriptErrors", d.RedscriptErrors.Count, Analyzer.Short(d.RedscriptErrors[0])) });
            else list.Add(new FrameworkCheck { Name = "redscript", State = "ran", Text = L.T("update.fw.redscriptRan") });
        }
        return list;
    }

    /// <summary>The Health item for a game update, or null when there is nothing to say any more.</summary>
    public static HealthItem? Health(GameVersionChange c, List<FrameworkCheck> fw)
    {
        var bad = fw.Where(f => f.State is "refused" or "errors" or "notStarted").ToList();
        var waiting = fw.Where(f => f.State == "notRun").ToList();
        if (bad.Count == 0 && waiting.Count == 0 && c.At < DateTime.Now.AddDays(-QuietDays)) return null;
        // a month on, a framework that still refuses is its own standing problem (Rules: NativePluginsRefusingToLoad),
        // not news about the update
        if (c.At < DateTime.Now.AddDays(-30)) return null;
        var title = !c.Newer ? L.T("update.titleChanged", c.To, c.From)
                  : bad.Count > 0 ? L.T("update.titleProblems", c.To)
                  : waiting.Count > 0 ? L.T("update.titleWaiting", c.To)
                  : L.T("update.titleFine", c.To);
        var lead = L.T("update.lead", c.From, c.To, L.Date(c.At, "dm"));
        var why = L.T("update.why", c.To);
        var said = fw.Count == 0 ? L.T("update.noFrameworks") : L.T("update.said", string.Join(" ", fw.Select(f => f.Text)));
        var advice = bad.Count > 0 ? L.T("update.adviceBad", c.To) : waiting.Count > 0 ? L.T("update.adviceWaiting") : L.T("update.adviceFine");
        return new HealthItem
        {
            Severity = bad.Count > 0 ? "high" : waiting.Count > 0 ? "medium" : "info",
            Title = title,
            Detail = lead + " " + why + " " + said + " " + advice + " " + L.T("update.honest"),
            Flag = null,
            Action = new UiAction("req", L.T("act.seeRequirements")),
        };
    }
}

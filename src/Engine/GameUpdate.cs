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
    // Not seen changing on this PC: the release table knows this build came out recently (KnownReleases.cs). A new
    // Crash Doctor user, or one whose 2.31 logs have already rotated away, still has to hear about the update.
    public bool Inferred { get; set; }
    public DateTime? ReleasedAt { get; set; }    // the table's release date for that build
}

public sealed class FrameworkCheck
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "";      // ran | refused | errors | notStarted | notRun
    public string Text { get; set; } = "";       // token
    public string? Known { get; set; }           // what the release table said: old | noRelease (KnownReleases.cs)
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
        // RED4ext writes "2.50" for what the game and CDPR call 2.5
        if (!string.IsNullOrEmpty(red?.ProductVersion)) return KnownReleases.Normalise(red!.ProductVersion);
        var patch = d.CrashReports.OrderByDescending(c => c.At).Select(c => c.GamePatch).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (patch != null && fileVersion.Length == 0) return patch;
        return KnownReleases.GameOfBuild(fileVersion) ?? fileVersion;
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
            if (after != null) { nowBuild = after.FileVersion; if (nowVersion.Length == 0 || nowVersion == game.FileVersion) nowVersion = KnownReleases.Normalise(after.ProductVersion); }
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
                change = new GameVersionChange { From = KnownReleases.Normalise(before.ProductVersion), FromBuild = before.FileVersion, To = nowVersion, ToBuild = nowBuild, At = exeTime.Value };
        }
        if (change == null && nowBuild.Length > 0 && Recent(nowBuild) is { } rel)
            change = new GameVersionChange { To = nowVersion.Length > 0 ? nowVersion : rel.Version, ToBuild = nowBuild, At = exeTime is { } et && et > rel.At ? et : rel.At, Inferred = true, ReleasedAt = rel.At };
        if (change != null && !change.Inferred)
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
            else if (change != null && !change.Inferred && cfg.PreviousGameFileVersion != change.FromBuild)
            { cfg.PreviousGameFileVersion = change.FromBuild; cfg.PreviousGameVersion = change.From; cfg.GameVersionChangedAt = change.At; changed = true; }
            if (changed || cfg.LastGameFileVersion != nowBuild || cfg.LastGameVersion != nowVersion)
            {
                cfg.LastGameFileVersion = nowBuild; cfg.LastGameVersion = nowVersion;
                try { GameLocator.SaveConfig(cfg); } catch { }
            }
        }
        return change;
    }

    /// <summary>A build the release table says came out in the last 30 days, with its version and date.</summary>
    static (string Version, DateTime At)? Recent(string build)
    {
        var g = KnownReleases.Data.Games.FirstOrDefault(x => x.Build.Length > 0 && build.StartsWith(x.Build, StringComparison.Ordinal));
        if (g?.Released == null || !DateTime.TryParse(g.Released, out var at)) return null;
        return at > DateTime.Now.AddDays(-30) && at <= DateTime.Now.AddDays(1) ? (g.Version, at) : null;
    }

    /// <summary>
    /// What each framework's own log says about the build it found after the change, plus what the release table
    /// (KnownReleases.cs) adds for the version installed now. Only frameworks whose own file is installed now are
    /// listed: after a purge the logs of the last modded launch are still on disk, and they are history, not state.
    /// </summary>
    public static List<FrameworkCheck> Frameworks(CollectedData d, GameVersionChange c)
    {
        var g = d.Paths; var list = new List<FrameworkCheck>();
        var since = c.At.AddMinutes(-1);
        // did the game itself run since? A crash report, a CET start, or any RED4ext log says so
        var gameRan = d.Red4ext.Any(x => x.Start >= since) || d.CetStarts.Any(x => x.at >= since) || d.CrashReports.Any(x => x.At >= since);
        string Missing(string name) => gameRan ? L.T("update.fw.notStarted", name) : L.T("update.fw.notRun", name);
        var MissingState = gameRan ? "notStarted" : "notRun";
        var known = d.KnownChecks;
        // the table's word on a framework, added to whatever its log said; "old" outranks a log that saw nothing wrong
        var red = d.Red4ext.Where(x => x.Start >= since).OrderByDescending(x => x.Start).FirstOrDefault();
        // the last time the game ran at all, by the newest log anything wrote since the update
        var lastRun = new[] { red?.LastWrite, d.CetStarts.Where(x => x.at >= since).Select(x => (DateTime?)x.at).DefaultIfEmpty(null).Max() }.Where(x => x != null).DefaultIfEmpty(null).Max();
        void Add(string name, string state, string text)
        {
            var k = known.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (k != null && state is "ran" or "notRun" or "notReached" or "updated") state = "old";
            // installed or updated after the game last ran (25 Oct 2026: ArchiveXL 1.28.0 over the 1.27.5 that took the
            // game down on 22 Oct): what the logs said then is about the old file
            else if (k == null && lastRun is DateTime lr && state != "notRun" && KnownReleases.ChangedSince(d, name, lr))
            {
                var ver = KnownReleases.InstalledVersion(d, name);
                state = "updated"; text = L.T("update.fw.updatedSince", ver != null ? name + " " + ver : name, L.Date(lr, "dmt"));
            }
            list.Add(new FrameworkCheck { Name = name, State = state, Text = k != null ? L.Cat(text, k.Text) : text, Known = k?.Kind });
        }
        // the game went down while RED4ext was still loading plugins: whatever comes later never had its turn
        bool notReached = red?.DiedLoading != null;
        if (KnownReleases.Installed(d, "RED4ext"))
        {
            if (red == null) Add("RED4ext", MissingState, Missing("RED4ext"));
            else if (red.Errors.Count > 0) Add("RED4ext", "errors", L.T("update.fw.red4extErrors", red.Red4extVersion, L.Date(red.Start, "dmt"), red.Errors[0]));
            else Add("RED4ext", "ran", L.P("update.fw.red4extRan", red.PluginsLoaded.Count, red.Red4extVersion, red.ProductVersion.Length > 0 ? red.ProductVersion : red.FileVersion));

            var main = new[] { "ArchiveXL", "TweakXL", "Codeware" };
            foreach (var plugin in main)
            {
                if (!KnownReleases.Installed(d, plugin)) continue;
                if (red == null) { Add(plugin, MissingState, Missing(plugin)); continue; }
                var refused = red.Incompatible.FirstOrDefault(i => i.StartsWith(plugin, StringComparison.OrdinalIgnoreCase));
                var loaded = red.PluginsLoaded.FirstOrDefault(p => p.name.Equals(plugin, StringComparison.OrdinalIgnoreCase));
                if (red.DiedLoading != null && red.DiedLoading.Equals(plugin, StringComparison.OrdinalIgnoreCase)) Add(plugin, "errors", L.T("update.fw.pluginDiedLoading", plugin));
                else if (refused != null) Add(plugin, "refused", L.Cat(L.T("update.fw.pluginRefused", plugin, refused), RefusedPlain(refused)));
                else if (loaded.name != null) Add(plugin, "ran", L.T("update.fw.pluginLoaded", plugin, loaded.version));
                // RED4ext never reached it: the game went down on an earlier plugin
                else if (red.DiedLoading != null) Add(plugin, "notReached", L.T("update.fw.notReached", plugin));
                else Add(plugin, "errors", L.T("update.fw.pluginAbsent", plugin));
            }
            if (red != null)
            {
                // another native plugin the game died loading is the most important line of all
                if (red.DiedLoading != null && !main.Contains(red.DiedLoading, StringComparer.OrdinalIgnoreCase))
                    list.Add(new FrameworkCheck { Name = red.DiedLoading, State = "errors", Text = L.T("update.fw.pluginDiedLoading", red.DiedLoading) });
                // every other native plugin RED4ext turned away on the new build, counted in one line
                // ... still installed as the file RED4ext refused, not replaced or removed since
                var others = red.Incompatible.Select(i => Regex.Match(i, @"^(.+?) \(version").Groups[1].Value).Where(n => n.Length > 0 && !main.Contains(n, StringComparer.OrdinalIgnoreCase))
                                .Where(n => !g.DirExists(Path.Combine(GamePaths.RelRed4extPlugins, n)) || (KnownReleases.FileTime(d, n) is DateTime ft && ft <= red.LastWrite.AddMinutes(1)))
                                .Distinct().ToList();
                if (others.Count > 0)
                {
                    var first = red.Incompatible.First(i => i.StartsWith(others[0], StringComparison.OrdinalIgnoreCase));
                    list.Add(new FrameworkCheck { Name = "plugins", State = "refused", Text = L.Cat(L.P("update.fw.otherPlugins", others.Count, L.List(others.Take(6).ToList())), RefusedPlain(first)) });
                }
            }
            // a table entry for a plugin not covered above (RedFileSystem needs RED4ext 1.31.0, for one)
            foreach (var k in known.Where(k => k.Name != "RED4ext" && k.Name != "Cyber Engine Tweaks" && !list.Any(f => f.Name.Equals(k.Name, StringComparison.OrdinalIgnoreCase))))
                list.Add(new FrameworkCheck { Name = k.Name, State = "old", Text = k.Text, Known = k.Kind });
        }

        if (KnownReleases.Installed(d, "Cyber Engine Tweaks"))
        {
            var refused = d.CetRefused.Where(x => x.at >= since).Select(x => Regex.Replace(x.line, @"^(\[[^\]]*\]\s*)+", "")).FirstOrDefault();
            var start = d.CetStarts.Where(x => x.at >= since).OrderByDescending(x => x.at).FirstOrDefault();
            if (refused != null) Add("Cyber Engine Tweaks", "refused", L.T("update.fw.cetRefused", refused));
            else if (start.gameVersion != null) Add("Cyber Engine Tweaks", "ran", L.T("update.fw.cetRan", d.CetVersion, start.gameVersion));
            else if (notReached) Add("Cyber Engine Tweaks", "notReached", L.T("update.fw.notReached", "Cyber Engine Tweaks"));
            else Add("Cyber Engine Tweaks", MissingState, Missing("Cyber Engine Tweaks"));
        }

        if (g.Exists(@"engine\tools\scc.exe") || g.Exists(@"engine\tools\scc_lib.dll"))
        {
            var written = d.RedscriptLog != null ? File.GetLastWriteTime(d.RedscriptLog) : (DateTime?)null;
            if ((written == null || written < since) && notReached) list.Add(new FrameworkCheck { Name = "redscript", State = "notReached", Text = L.T("update.fw.notReached", "redscript") });
            else if (written == null || written < since) list.Add(new FrameworkCheck { Name = "redscript", State = MissingState, Text = Missing("redscript") });
            else if (d.RedscriptErrors.Count > 0) list.Add(new FrameworkCheck { Name = "redscript", State = "errors", Text = L.P("update.fw.redscriptErrors", d.RedscriptErrors.Count, Analyzer.Short(d.RedscriptErrors[0])) });
            else list.Add(new FrameworkCheck { Name = "redscript", State = "ran", Text = L.T("update.fw.redscriptRan") });
        }
        return list;
    }

    /// <summary>
    /// RED4ext's "is incompatible with the current patch. The requested runtime of the plugin is 3.0.80.51928", in
    /// plain words: the plugin was built for that game build and RED4ext will not start it on another one.
    /// </summary>
    public static string RefusedPlain(string line)
    {
        var m = Regex.Match(line, @"^(.+?) \(version: ([^,)]+)\).*?requested runtime(?: of the plugin is)? ([\d.]+\d)");
        return m.Success ? L.T("plugin.refusedPlain", m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim(), KnownReleases.BuildLabel(m.Groups[3].Value)) : "";
    }

    /// <summary>The Health item for a game update, or null when there is nothing to say any more.</summary>
    public static HealthItem? Health(GameVersionChange c, List<FrameworkCheck> fw)
    {
        // known only from the release table, with no framework installed: nothing worth a line in Health
        if (c.Inferred && fw.Count == 0) return null;
        // the one line counting other native plugins RED4ext refused is not about a framework (26 Oct 2026: Gary's
        // frameworks all on 2.5, only DualSense Support and LizziesBDs refused - "your frameworks do not all support
        // it" would be false); each of those has its own Health item (Rules: NativePluginsRefusingToLoad)
        var bad = fw.Where(f => f.State is "refused" or "errors" or "notStarted" or "old" && f.Name != "plugins").ToList();
        var waiting = fw.Where(f => f.State is "notRun" or "updated" or "notReached").ToList();
        if (bad.Count == 0 && waiting.Count == 0 && c.At < DateTime.Now.AddDays(-QuietDays)) return null;
        // a month on, a framework that still refuses is its own standing problem (Rules: NativePluginsRefusingToLoad),
        // not news about the update
        if (c.At < DateTime.Now.AddDays(-30)) return null;
        var title = !c.Newer ? L.T("update.titleChanged", c.To, c.From)
                  : bad.Count > 0 ? L.T("update.titleProblems", c.To)
                  : waiting.Count > 0 || fw.Count == 0 ? L.T("update.titleWaiting", c.To)
                  : L.T("update.titleFine", c.To);
        var lead = c.Inferred ? L.T("update.leadRecent", c.To, L.Date(c.ReleasedAt ?? c.At, "dm")) : L.T("update.lead", c.From, c.To, L.Date(c.At, "dm"));
        var why = L.T("update.why", c.To);
        var said = fw.Count == 0 ? L.T("update.noFrameworks") : L.T("update.said", string.Join(" ", fw.Select(f => f.Text)));
        // a release for the new version exists for some of them: "update these" comes before "wait for the rest"
        var advice = fw.Any(f => f.Known == "old") ? L.T("update.adviceKnown", c.To)
                   : bad.Count > 0 ? L.T("update.adviceBad", c.To) : waiting.Count > 0 ? L.T("update.adviceWaiting") : fw.Count == 0 ? "" : L.T("update.adviceFine");
        var sources = DateTime.TryParse(KnownReleases.Data.AsOf, out var asOf) ? L.T("update.sources", L.Date(asOf, "dmy")) : "";
        return new HealthItem
        {
            Severity = bad.Count > 0 ? "high" : waiting.Count > 0 ? "medium" : "info",
            Title = title,
            Detail = L.Cat(lead, fw.Count == 0 ? "" : why, said, advice, fw.Count == 0 ? "" : sources),
            Flag = null,
            Action = new UiAction("req", L.T("act.seeRequirements")),
        };
    }
}

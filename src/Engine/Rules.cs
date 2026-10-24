using System.Text.RegularExpressions;

namespace CrashDoctor.Engine;

// The rule catalogue - layer 4 of DESIGN.md, "How it reasons".
//
// This is the answer to "but every crash is different". The fixes differ; the evidence gathering does not. Some
// causes have a crash signature and belong in fixes/catalog.json keyed on it. Far more of them are recognisable
// from a *pattern in the evidence* with no signature at all - a file that should not be there, a plugin built for
// last month's game, an archive that downloaded as zero bytes - and those are what live here.
//
// A rule is deliberately small: look at the collected facts, return what it found. Adding one should be a few
// lines in this file and nothing else, because the value of this layer is in how many of them there are.
//
// Every rule here has been watched firing against tools/make-rules-fixture.py, and watched staying silent against
// both the fresh and healthy fixtures. Add the condition to that fixture in the same change that adds the rule.
//
// Two standing obligations, both from DESIGN.md:
//   - never manufacture concern. A rule that cannot tell the difference between a real problem and a normal install
//     does not belong here, however clever it looks.
//   - state correlation as correlation, with the counts visible.
//
// Rejected, so they are not tried again:
//   - "native plugin is older than the game build". Plugins legitimately predate patches by months and keep
//     working - on the reference machine it flagged nine, including the current release of ArchiveXL. It could not
//     tell a healthy install from a broken one, which is the one thing a rule may not do.
//   - "the game drive is nearly full". Real cause, easy to write, and there is no way to make it fire on demand
//     short of filling a disk - so it could never be shown to work. A rule nobody has watched fire is the same
//     hope as a rule nobody has watched stay quiet; see tools/make-rules-fixture.py.
//   - "a CET mod folder has no init.lua". CET ignores those folders, but mods legitimately ship data-only folders
//     beside their code, so it flags working installs.
public sealed class RuleHit
{
    public string Severity { get; set; } = "medium";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string? Mod { get; set; }
    /// <summary>Two or three words for the mods-table chip. See <see cref="HealthItem.Flag"/>.</summary>
    public string? Flag { get; set; }
    public UiAction? Action { get; set; }
}

public static class Rules
{
    // Returns however many findings it has, which for most rules is one or none - One() and None keep those
    // reading as one expression. A rule returning a list is what let the rules that were still inline in
    // Analyzer.Health move here: several of them report once per plugin, per script error or per duplicated file.
    public delegate IEnumerable<RuleHit> Rule(CollectedData d, Report r);

    static readonly RuleHit[] None = Array.Empty<RuleHit>();
    static IEnumerable<RuleHit> One(RuleHit hit) => new[] { hit };

    public static readonly Rule[] All =
    {
        // things that should not be on disk at all
        BrokenUpscalerOverride,
        EmptyOrTruncatedArchives,
        SavesInCloudStorage,
        DeployedFilesAreMissing,
        Mo2EnabledModsMissing,
        Mo2HiddenCode,
        // what the mod loaders reported this launch
        NativePluginsRefusingToLoad,
        ArchiveXlStartupProblems,
        ScriptsWrittenForTheOldInventoryFilters,
        RedscriptProblems,
        ScriptErrorSpam,
        DuplicatedScripts,
        // what the crashes themselves say
        VideoMemoryAtTheCrashes,
        VideoMemoryWhilePlaying,
        EngineRanOutOfMemory,
        StackOverflowIsRecursion,
        CrashesOnlyAtStartup,
        OverlaysInjectedAtTheCrash,
        // settings that do not fit the hardware
        SettingsOverTheVramBudget,
        AgeingGraphicsDriver,
    };

    public static List<RuleHit> Run(CollectedData d, Report r)
    {
        var hits = new List<RuleHit>();
        foreach (var rule in All)
        {
            try { hits.AddRange(rule(d, r)); }
            catch { /* a rule that throws is a bug in the rule, never a reason to lose the rest of the report */ }
        }
        return hits;
    }

    // ---------------------------------------------------------------- rules

    // DLSS enablers and FSR3 bridges work by dropping replacement DLLs beside the game and impersonating NVIDIA's.
    // On patch 2.3+ the game ships its own FidelityFX and XeSS, so these overwrite files the game now owns. This is
    // the single most destructive thing in common circulation: it survives uninstalling the mod, because removing
    // the mod does not put the game's own DLLs back.
    static readonly string[] EnablerFiles =
    {
        "dlss-enabler.dll", "dlss-enabler-upscaler.dll", "dlssg_to_fsr3_amd_is_better.dll",
        "dlssg_to_fsr3.ini", "fakenvapi.ini", "nvapi64-proxy.dll", "dlss-enabler.log",
    };
    static IEnumerable<RuleHit> BrokenUpscalerOverride(CollectedData d, Report r)
    {
        var found = new List<string>();
        foreach (var dir in new[] { GamePaths.RelBin, "" })
            foreach (var f in EnablerFiles)
                if (d.Paths.Exists(dir.Length == 0 ? f : Path.Combine(dir, f))) found.Add(Path.GetFileName(f));
        if (found.Count == 0) return None;
        return One(new RuleHit
        {
            Severity = "high",
            Title = L.T("rule.enabler.title"),
            Detail = L.T("rule.enabler.detail", L.List(found.Distinct().ToList())),
            Action = new UiAction("open:gamefolder", L.T("act.openGameFolder")),
        });
    }

    // A download that failed part-way leaves a file that is present, named correctly, and unreadable. The loader
    // reports nothing useful; the game simply misbehaves wherever that content was meant to appear.
    static IEnumerable<RuleHit> EmptyOrTruncatedArchives(CollectedData d, Report r)
    {
        var bad = new List<string>();
        foreach (var (f, _) in d.Paths.FilesIn(GamePaths.RelArchiveMods, "*.archive"))
        {
            long len; try { len = new FileInfo(f).Length; } catch { continue; }
            if (len < 1024) bad.Add(len == 0 ? L.T("rule.emptyArchive.empty", Path.GetFileName(f)) : L.T("rule.emptyArchive.bytes", Path.GetFileName(f), len));
        }
        if (bad.Count == 0) return None;
        return One(new RuleHit
        {
            Severity = "high",
            Title = L.P("rule.emptyArchive.title", bad.Count),
            Detail = L.T("rule.emptyArchive.detail", ListMore(bad, 5)),
        });
    }

    // OneDrive and similar sync clients rewrite files underneath the game while it is saving, and can turn a save
    // into a placeholder that is not on disk at all when the game asks for it.
    static IEnumerable<RuleHit> SavesInCloudStorage(CollectedData d, Report r)
    {
        var saves = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "CD Projekt Red", "Cyberpunk 2077");
        string? real = null;
        try { if (Directory.Exists(saves)) real = new DirectoryInfo(saves).LinkTarget ?? saves; } catch { }
        var candidates = new List<string?> { real };
        try
        {
            foreach (var v in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
                if (Environment.GetEnvironmentVariable(v) is { Length: > 0 } od)
                {
                    var p = Path.Combine(od, "Saved Games", "CD Projekt Red", "Cyberpunk 2077");
                    if (Directory.Exists(p)) candidates.Add(p);
                }
        }
        catch { }
        var hit = candidates.FirstOrDefault(c => c != null && Regex.IsMatch(c, @"OneDrive|Dropbox|Google ?Drive|iCloud", RegexOptions.IgnoreCase));
        if (hit == null) return None;
        return One(new RuleHit
        {
            Severity = "medium",
            Title = L.T("rule.cloudSaves.title"),
            Detail = L.T("rule.cloudSaves.detail", hit),
        });
    }

    // Overlays and capture tools inject themselves into the game's rendering. Most are harmless most of the time;
    // some are long-standing and well-documented sources of instability with modded Cyberpunk. Reported with the
    // counts, and without claiming to know that this one caused anything.
    static readonly Dictionary<string, string> Hooks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RTSSHooks64.dll"] = "hook.rtss",
        ["graphics-hook64.dll"] = "hook.obs",
        ["fraps64.dll"] = "hook.fraps",
        ["DiscordHook64.dll"] = "hook.discord",
    };
    static IEnumerable<RuleHit> OverlaysInjectedAtTheCrash(CollectedData d, Report r)
    {
        var dumps = d.CrashReports.Where(c => c.Dump != null).ToList();
        if (dumps.Count == 0) return None;
        var seen = new Dictionary<string, int>();
        foreach (var c in dumps)
            foreach (var m in c.Dump!.Modules.Distinct(StringComparer.OrdinalIgnoreCase))
                if (Hooks.TryGetValue(m, out var who)) seen[who] = seen.GetValueOrDefault(who) + 1;
        if (seen.Count == 0) return None;
        var worst = seen.OrderByDescending(kv => kv.Value).First();
        return One(new RuleHit
        {
            Severity = "low",
            Title = L.T("rule.overlay.title", L.T(worst.Key), worst.Value, dumps.Count),
            Detail = L.Cat(L.T("rule.overlay.detail", worst.Value, dumps.Count),
                           seen.Count > 1 ? L.T("rule.overlay.also", L.List(seen.Keys.Where(k => k != worst.Key).Select(k => L.T(k)).ToList())) : ""),
        });
    }

    // The engine sets its own out-of-memory flag in the crash report when an allocation it asked for came back
    // empty. That is the engine saying so, not us inferring it from a fault address, and it is collected already
    // and was going nowhere. The distinction worth drawing is whether the graphics card was also full at the time:
    // if it was, this is the video-memory story and is already told; if it was not, something *other* than the card
    // ran out, and turning graphics down will do nothing at all.
    static IEnumerable<RuleHit> EngineRanOutOfMemory(CollectedData d, Report r)
    {
        var all = d.CrashReports;
        var oom = all.Where(c => c.EngineOom).ToList();
        if (oom.Count == 0) return None;
        var measured = oom.Where(c => c.VramKnown).ToList();
        var cardFull = measured.Count(c => c.VramRatio >= 0.95);
        var cardFine = measured.Count - cardFull;
        var newest = oom.Max(c => c.At);

        var lead = L.Cat(L.P("rule.oom.lead", all.Count, oom.Count, L.Date(newest)), L.T("rule.oom.meaning"));
        string rest;
        if (measured.Count == 0) rest = L.T("rule.oom.unmeasured");
        else if (cardFine == 0) rest = L.T("rule.oom.cardFull", cardFull);
        else
        {
            var lowest = measured.Where(c => c.VramRatio < 0.95).Min(c => c.VramRatio);
            rest = L.Cat(L.T("rule.oom.cardFine", cardFine, L.Pct(lowest)), d.System.RamGB > 0 ? L.T("rule.oom.ram", d.System.RamGB) : L.T("rule.oom.ramUnknown"));
        }

        return One(new RuleHit
        {
            Severity = cardFine > 0 ? "high" : "medium",
            Title = L.P("rule.oom.title", oom.Count),
            Detail = L.Cat(lead, rest),
            Action = new UiAction(cardFine > 0 ? "mods" : "settings", cardFine > 0 ? L.T("act.seeModList") : L.T("act.seeSettings")),
        });
    }

    // A stack overflow is the one exception code that names its own cause. The stack only runs out when something
    // calls itself without ever returning, which is a logic fault in code, never hardware and never video memory.
    // In this game it is nearly always scripts: two mods wrapping the same method so each ends up calling the
    // other. Worth saying loudly, because every generic "lower your settings" answer is wrong for it.
    static IEnumerable<RuleHit> StackOverflowIsRecursion(CollectedData d, Report r)
    {
        var dumps = d.CrashReports.Where(c => c.Dump != null).ToList();
        var so = dumps.Where(c => c.Dump!.ExceptionCode == 0xC00000FD).ToList();
        if (so.Count == 0) return None;
        var where = so.Select(c => c.Dump!.FaultingModule).Where(m => m != null).Distinct(StringComparer.OrdinalIgnoreCase).Select(m => m!).ToList();
        var wrapped = d.RedscriptWarnings.Count(w => w.Contains("overwrites a previous annotation"));

        return One(new RuleHit
        {
            Severity = "high",
            Title = so.Count == dumps.Count && dumps.Count > 1
                ? L.T("rule.stack.titleAll", dumps.Count)
                : L.P("rule.stack.title", so.Count, dumps.Count),
            Detail = L.Cat(where.Count > 0 ? L.T("rule.stack.leadWhere", L.List(where)) : L.T("rule.stack.lead"),
                           L.T("rule.stack.meaning"),
                           wrapped > 0 ? L.P("rule.stack.wrapped", wrapped) : L.T("rule.stack.noWrapped")),
        });
    }

    // Crashes that always arrive before the player is properly in the game are a different animal from crashes that
    // happen somewhere: they are a loading problem, and - far more usefully - they reproduce on demand. That makes
    // them the easiest kind to find, which is worth telling someone who thinks a fast crash is a worse one.
    const double StartupMinutes = 2.0;
    static IEnumerable<RuleHit> CrashesOnlyAtStartup(CollectedData d, Report r)
    {
        var recent = r.Sessions.Where(s => s.Start > DateTime.Now.AddDays(-14) && !s.Partial && s.DurationKnown).ToList();
        var crashes = recent.Where(s => s.EndKind is not (EndKind.Clean or EndKind.Running)).ToList();
        if (crashes.Count < 3) return None;
        if (crashes.Any(s => s.Minutes > StartupMinutes)) return None;
        var longest = crashes.Max(s => s.Minutes);
        var played = recent.Count(s => s.EndKind == EndKind.Clean && s.Minutes > StartupMinutes);

        return One(new RuleHit
        {
            Severity = "high",
            Title = L.P("rule.startup.title", crashes.Count, StartupMinutes),
            Detail = L.Cat(L.T("rule.startup.longest", longest < 1 ? L.P("word.seconds", Math.Round(longest * 60)) : L.P("word.minutes", Math.Round(longest, 1))),
                           L.T("rule.startup.meaning"),
                           played > 0 ? L.P("rule.startup.played", played) : "",
                           L.T("rule.startup.easy")),
            Action = new UiAction("bisect", L.T("act.findIt")),
        });
    }

    // Vortex keeps a manifest of every file it put in the game folder. When the game is verified through Steam, GOG
    // or Epic, the store deletes files it does not recognise - which is all of them - and the manifest still says
    // they are there. Antivirus quarantine and an interrupted deploy do the same. The mod is installed as far as the
    // manager is concerned and absent as far as the game is concerned, and nothing reports an error either way.
    static IEnumerable<RuleHit> DeployedFilesAreMissing(CollectedData d, Report r)
    {
        if (!File.Exists(d.Paths.VortexManifest)) return None;
        // Parsed here rather than taken from the inventory, because the inventory drops each entry's deployment
        // target and a file with a target does not necessarily live at <game>\<relPath>.
        //
        // Entries carrying a target are skipped outright rather than guessed at. Every entry on the only machine
        // this has been run against has an empty target, so which way Vortex stores the path for a REDmod-style
        // deployment - relPath already including the target, or not - has never been observed. Guess it wrong and
        // this rule reports *every* file as missing and tells someone with a perfectly healthy install to purge
        // and redeploy, at high severity. Losing coverage of those entries is the cheaper mistake by a distance.
        // Remove the skip once a manifest with a non-empty target has actually been looked at.
        List<(string mod, string path)> entries = new();
        var skipped = 0;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(Files.ReadAllTextShared(d.Paths.VortexManifest));
            if (!doc.RootElement.TryGetProperty("files", out var files)) return None;
            foreach (var f in files.EnumerateArray())
            {
                var rel = f.TryGetProperty("relPath", out var p) ? p.GetString() : null;
                if (string.IsNullOrEmpty(rel)) continue;
                var target = f.TryGetProperty("target", out var t) ? t.GetString() ?? "" : "";
                if (target.Length > 0) { skipped++; continue; }
                var src = f.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "";
                entries.Add((ModInventory.FromFolder(src).Name, Path.Combine(d.Paths.GameDir, rel)));
                if (entries.Count >= 20000) break;   // a manifest this size is already pathological; do not stall the scan
            }
        }
        catch { return None; }
        if (entries.Count == 0) return None;

        var missing = entries.Where(e => !File.Exists(e.path)).ToList();
        if (missing.Count == 0) return None;
        var mods = missing.Select(e => e.mod).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var all = missing.Count == entries.Count;

        return One(new RuleHit
        {
            Severity = "high",
            Title = mods.Count == 1
                ? L.T("rule.vortexMissing.titleOne", mods[0])
                : L.P("rule.vortexMissing.titleMany", missing.Count),
            Detail = L.Cat(all
                    ? L.P("rule.vortexMissing.none", entries.Count)
                    : L.P("rule.vortexMissing.some", entries.Count, missing.Count, L.P("word.nMods", mods.Count), ListMore(mods, 4)),
                L.T("rule.vortexMissing.why"),
                L.T("rule.vortexMissing.fix"),
                // Said out loud, not swallowed: it stops the count reading as the whole picture, and it is how
                // anyone posting a report tells us these entries exist in the wild at all.
                skipped > 0 ? L.P("rule.vortexMissing.skipped", skipped) : ""),
            Mod = mods.Count == 1 ? mods[0] : null,
            Flag = mods.Count == 1 ? L.T("flag.filesMissing") : null,
        });
    }

    // Mod Organizer 2's version of the same trap: the profile enables a mod whose folder is gone (deleted or renamed
    // outside MO2, or the mods folder moved). MO2 marks it in its own list, but nothing tells the game, and the scan
    // would otherwise just see a mod with no files.
    static IEnumerable<RuleHit> Mo2EnabledModsMissing(CollectedData d, Report r)
    {
        var m = d.Paths.Mo2; var missing = d.Mods.Mo2Missing;
        if (m == null || missing.Count == 0 || d.Mods.Manager != ModOrganizer.ManagerName) return None;
        return One(new RuleHit
        {
            Severity = "high",
            Title = missing.Count == 1 ? L.T("rule.mo2Missing.titleOne", missing[0]) : L.P("rule.mo2Missing.titleMany", missing.Count),
            Detail = L.T("rule.mo2Missing.detail", m.Profile, ListMore(missing, 4), m.ModsDir),
            Mod = missing.Count == 1 ? missing[0] : null,
            Flag = missing.Count == 1 ? L.T("flag.folderMissing") : null,
        });
    }

    // In MO2 two mods that ship the same file do not both load: the one lower in the list wins and the other's copy
    // is hidden from the game. Usually that is the point (a patch, an update installed as its own mod). For code -
    // scripts, plugins, tweaks, archives - it is also how a mod ends up "doing nothing", so the pairs are listed, low
    // and with the files named. Data files (textures, configs) are left out: those overlaps are routine.
    static readonly string[] CodeExt = { ".reds", ".lua", ".dll", ".asi", ".xl", ".yaml", ".yml", ".tweak", ".archive" };
    static IEnumerable<RuleHit> Mo2HiddenCode(CollectedData d, Report r)
    {
        if (d.Mods.Manager != ModOrganizer.ManagerName || d.Mods.Mo2Conflicts.Count == 0) return None;
        var hits = new List<RuleHit>();
        var code = d.Mods.Mo2Conflicts.Where(c => CodeExt.Any(e => c.rel.EndsWith(e, StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var pair in code.SelectMany(c => c.losers.Select(l => (c.winner, loser: l, c.rel))).GroupBy(x => (x.winner, x.loser)).OrderByDescending(g => g.Count()).Take(3))
        {
            var files = pair.Select(x => Path.GetFileName(x.rel)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            hits.Add(new RuleHit
            {
                Severity = "low",
                Title = L.P("rule.mo2Hidden.title", files.Count, pair.Key.winner, pair.Key.loser),
                Detail = L.T("rule.mo2Hidden.detail", ListMore(files, 3), pair.Key.winner, pair.Key.loser),
                Mod = pair.Key.loser,
                Flag = L.T("flag.filesHidden"),
            });
        }
        return hits;
    }

    // ------------------------------------------------- migrated out of Analyzer.Health
    //
    // These predate the catalogue and lived inline in Analyzer.Health, where they were indistinguishable from the
    // parts of that method that are not rules at all - the clean-session baseline, the crash count, the fault
    // grouping - and could not be run, listed or reasoned about separately. The wording is carried over unchanged;
    // this move is about where they live, not what they say.

    // RED4ext refuses a native plugin built against a different patch, and says so in its log every launch. The
    // script half of the same mod usually keeps running and calling functions that are no longer there.
    static IEnumerable<RuleHit> NativePluginsRefusingToLoad(CollectedData d, Report r)
    {
        var latestRed = d.Red4ext.OrderByDescending(x => x.Start).FirstOrDefault();
        // RED4ext itself gone (a purge leaves its logs behind): what it refused then is history, not this install
        if (latestRed == null || !KnownReleases.Installed(d, "RED4ext")) return None;
        var hits = new List<RuleHit>();
        foreach (var inc in latestRed.Incompatible)
        {
            var name = Regex.Match(inc, @"^(.+?) \(version").Groups[1].Value;
            var owner = d.Mods.OwnerOfPlugin(name.Replace(" ", "")) ?? d.Mods.OwnerOfPlugin(name) ?? name;
            hits.Add(new RuleHit
            {
                Severity = "medium",
                Title = L.T("rule.plugin.title", name),
                Detail = L.Cat(L.T("rule.plugin.detail", inc), GameUpdate.RefusedPlain(inc)),
                Mod = owner,
                Flag = L.T("flag.pluginNotLoading"),
                Action = Analyzer.NexusAction(d, owner),
            });
        }
        return hits;
    }

    // ArchiveXL's own complaints from the first three minutes of the last launch: world sector patches that could
    // not be applied, and patches pointing at resources that are not installed. One item per mod per kind of
    // problem, with the distinct messages counted inside it rather than one item per line.
    static IEnumerable<RuleHit> ArchiveXlStartupProblems(CollectedData d, Report r)
    {
        var latestRed = d.Red4ext.OrderByDescending(x => x.Start).FirstOrDefault();
        if (latestRed == null) return None;
        var startup = d.Events.Where(e => e.Kind is "xl-error" or "xl-warning" && e.At >= latestRed.Start && e.At <= latestRed.Start.AddMinutes(3)).ToList();
        // "Some patches have not been applied" is the consequence of a sector error already listed; drop it when that error is present
        if (startup.Any(e => e.Text.Contains("[WorldStreaming]") && e.Kind == "xl-error"))
            startup = startup.Where(e => !e.Text.StartsWith("[WorldStreaming] Some patches have not been applied")).ToList();

        var hits = new List<RuleHit>();
        foreach (var grp in startup.GroupBy(e => (owner: e.Mod ?? Analyzer.GuessOwnerFromText(d, e.Text) ?? "",
                                                  cat: e.Text.Contains("WorldStreaming") ? "world" : (e.Text.Contains("doesn't exist") || e.Text.Contains("non-existent")) ? "missing" : "other")))
        {
            var msgs = grp.Select(e => e.Text).Distinct().ToList(); var first = grp.First();
            var owner = grp.Key.owner == "" ? null : grp.Key.owner;
            var world = grp.Key.cat == "world"; var missing = grp.Key.cat == "missing";
            var title = world ? L.T("rule.xl.titleWorld") : missing ? L.T("rule.xl.titleMissing") : L.T("rule.xl.titleOther");
            if (owner == null) title = L.T("rule.xl.notIdentified", title);
            var detail = L.Cat(msgs.Count == 1 ? msgs[0] : L.P("rule.xl.distinct", msgs.Count, msgs[0]),
                               world ? L.T("rule.xl.world") : missing ? L.T("rule.xl.missing") : "");
            hits.Add(new RuleHit
            {
                Severity = world ? "medium" : first.Kind == "xl-error" ? (owner == null ? "low" : "medium") : "low",
                Title = title,
                Detail = detail,
                Mod = owner,
                Flag = world ? L.T("flag.worldPatch") : missing ? L.T("flag.missingResources") : L.T("flag.xlProblem"),
                Action = owner != null ? Analyzer.NexusAction(d, owner) : null,
            });
        }
        return hits;
    }

    // Game update 2.5 (22 Oct 2026) gave InventoryItemModeLogicController.UpdateAvailableItems and RefreshAvailableItems
    // an optional Bool parameter. A mod that wraps or replaces the old no-argument versions (More Weapon Filters, for
    // one) no longer compiles - and while one script fails, redscript loads none of them, so every script mod is off.
    // It fires only on a redscript error that is about those methods: the error text names them, or the line it points
    // at is the annotation on one of them in that very file. The mod is named from the file's path.
    static readonly Regex OldFilterHook = new(@"@(?:wrapMethod|replaceMethod)\(\s*InventoryItemModeLogicController\s*\)[^{;]*?func\s+(UpdateAvailableItems|RefreshAvailableItems)\s*\(\s*\)", RegexOptions.Singleline);
    internal static List<(string mod, string method, string line)> OldInventoryFilterHooks(CollectedData d)
    {
        var list = new List<(string, string, string)>();
        foreach (var (file, text) in d.RedscriptErrorBlocks)
        {
            string? method = null;
            if (text.Contains("InventoryItemModeLogicController", StringComparison.Ordinal))
                method = Regex.Match(text, @"\b(UpdateAvailableItems|RefreshAvailableItems)\b") is { Success: true } mm ? mm.Groups[1].Value : null;
            if (method == null && file != null && File.Exists(file) && new FileInfo(file).Length < 1_000_000
                && Regex.Match(text.Split('\n')[0], @"\.reds:(\d+)") is { Success: true } lm)
            {
                var errLine = int.Parse(lm.Groups[1].Value);
                var src = Files.ReadAllTextShared(file);
                foreach (Match m in OldFilterHook.Matches(src))
                {
                    var at = src[..m.Index].Count(c => c == '\n') + 1;                       // the annotation's line
                    var funcLine = src[..(m.Index + m.Length)].Count(c => c == '\n') + 1;   // the func line
                    if (errLine >= at - 1 && errLine <= funcLine + 1) { method = m.Groups[1].Value; break; }
                }
            }
            if (method == null) continue;
            var owner = (file != null ? Analyzer.GuessOwnerFromText(d, file) : null) ?? Analyzer.GuessOwnerFromText(d, text)
                        ?? (file != null ? Path.GetFileNameWithoutExtension(file) : "a script mod");
            var first = text.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            var shown = string.Join(" ", first.Take(4)).Replace(d.Paths.GameDir, "");
            list.Add((owner, method, shown.Length > 300 ? shown[..300] + "…" : shown));
        }
        return list.GroupBy(x => x.Item1).Select(g => g.First()).ToList();
    }
    static IEnumerable<RuleHit> ScriptsWrittenForTheOldInventoryFilters(CollectedData d, Report r) =>
        OldInventoryFilterHooks(d).Select(x => new RuleHit
        {
            Severity = "high",
            Title = L.T("rule.rs25.title", x.mod),
            Detail = L.Cat(L.T("rule.rs25.detail", x.method, x.mod), x.line),
            Mod = x.mod,
            Flag = L.T("flag.doesNotCompile"),
            Action = Analyzer.NexusAction(d, x.mod),
        });

    // redscript compiles every .reds file at launch. An error means a script mod is not running at all; a warning
    // about a replaced annotation means two mods are fighting over the same method, which is the shape behind a
    // good share of script crashes.
    static IEnumerable<RuleHit> RedscriptProblems(CollectedData d, Report r)
    {
        var hits = new List<RuleHit>();
        foreach (var w in d.RedscriptWarnings.Take(6))
        {
            var overwrite = w.Contains("overwrites a previous annotation");
            hits.Add(new RuleHit
            {
                Severity = overwrite ? "low" : "medium",
                Title = overwrite ? L.T("rule.rs.overwriteTitle") : L.T("rule.rs.warningTitle"),
                Detail = w,
                Mod = Analyzer.GuessOwnerFromText(d, w),
                Flag = overwrite ? L.T("flag.scriptOverridden") : L.T("flag.redscriptWarning"),
            });
        }
        var explained = OldInventoryFilterHooks(d).Select(x => x.mod).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var e in d.RedscriptErrors.Where(e => !(Analyzer.GuessOwnerFromText(d, e) is { } o && explained.Contains(o))).Take(6))
            hits.Add(new RuleHit
            {
                Severity = "high",
                Title = L.T("rule.rs.errorTitle"),
                Detail = e,
                Mod = Analyzer.GuessOwnerFromText(d, e),
                Flag = L.T("flag.doesNotCompile"),
            });
        return hits;
    }

    // A CET mod erroring ten or more times in one session is a feature of that mod that is simply not working. It
    // is rarely the crash - it happens in sessions that end fine too - but it is worth knowing and worth fixing.
    static IEnumerable<RuleHit> ScriptErrorSpam(CollectedData d, Report r)
    {
        var lastSess = r.Sessions.Where(s => !s.Partial).OrderByDescending(s => s.Start).FirstOrDefault();
        if (lastSess == null) return None;
        var hits = new List<RuleHit>();
        foreach (var grp in d.Events.Where(e => e.Kind == "error" && e.Source.StartsWith("CET") && e.At >= lastSess.Start).GroupBy(e => e.Source).Where(g => g.Count() >= 10))
        {
            var owner = grp.First().Mod ?? grp.Key.Replace("CET · ", "");
            hits.Add(new RuleHit
            {
                Severity = "medium",
                Title = L.P("rule.spam.title", grp.Count(), owner),
                Detail = L.T("rule.spam.detail", Analyzer.Short(grp.Last().Text)),
                Mod = owner,
                Flag = L.T("flag.errorSpam"),
                Action = Analyzer.NexusAction(d, owner),
            });
        }
        return hits;
    }

    // The same file name in two places is only a conflict when the contents are the same script - two mods bundling
    // one shared fix. Different files that happen to share a common name are not a problem and must not be listed.
    // Read through GamePaths, so under Mod Organizer 2 it is the merged set the game compiles.
    static IEnumerable<RuleHit> DuplicatedScripts(CollectedData d, Report r)
    {
        var all = d.Paths.FilesIn(GamePaths.RelScripts, "*.reds", recurse: true);
        if (all.Count == 0) return None;
        var dups = all
            .GroupBy(f => Path.GetFileName(f.full), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1 && !Analyzer.GenericScriptName(g.Key))
            .Select(g => g.GroupBy(f => { try { return Fixes.Sha(f.full); } catch { return f.full; } }).Where(x => x.Count() > 1).ToList())
            .Where(x => x.Count > 0).ToList();

        var hits = new List<RuleHit>();
        foreach (var g in dups.Take(5))
            foreach (var same in g)
                hits.Add(new RuleHit
                {
                    Severity = "low",
                    Title = L.P("rule.dupScript.title", same.Count(), Path.GetFileName(same.First().full)),
                    Detail = L.T("rule.dupScript.detail", string.Join(", ", same.Select(f => d.Mods.OwnerOf(f.rel) ?? Path.GetRelativePath(GamePaths.RelScripts, f.rel)))),
                });
        return hits;
    }

    // The run of video-memory readings across the recent crashes. The engine records this only in the crash report
    // and never in a log, so this is the one place it can be seen at all - and the run of numbers is more use than
    // any single verdict, which is why it is reported even when none of them is near the ceiling.
    static IEnumerable<RuleHit> VideoMemoryAtTheCrashes(CollectedData d, Report r)
    {
        var withVram = r.Sessions.Where(s => s.VramUsedMB > 0 && s.VramTotalMB > 0).OrderByDescending(s => s.Start).Take(12).ToList();
        if (withVram.Count == 0) return None;
        var total = withVram[0].VramTotalMB!.Value;
        var atCeiling = withVram.Where(s => (double)s.VramUsedMB!.Value / s.VramTotalMB!.Value >= 0.95).ToList();
        var lines = withVram.Select(s => L.T("rule.vramCrash.item", L.Date(s.Start, "dmt"), s.VramUsedMB, L.Pct((double)s.VramUsedMB!.Value / s.VramTotalMB!.Value)));
        var lead = atCeiling.Count == 0
            ? L.P("rule.vramCrash.none", withVram.Count)
            : L.P("rule.vramCrash.some", withVram.Count, atCeiling.Count, total);
        return One(new RuleHit
        {
            Severity = atCeiling.Count >= 3 ? "high" : atCeiling.Count > 0 ? "medium" : "info",
            Title = L.P("rule.vramCrash.title", withVram.Count),
            Detail = L.Cat(lead, L.T("rule.newestFirst", string.Join(" · ", lines)), L.T("rule.vramCrash.card", total),
                           atCeiling.Count > 0 ? L.T("rule.vram.levers") : ""),
            Action = new UiAction("settings", L.T("act.seeSettings")),
        });
    }

    // Video memory through whole sessions, recorded by Crash Doctor while its window was open (VramMonitor.cs). The
    // crash reports only ever say how full the card was at the instant of death; this says how it ran the rest of the
    // time, which is what decides whether a settings change was safe. Reported even when everything is fine, because
    // "the card never got near full" is the answer people change their settings to get.
    static IEnumerable<RuleHit> VideoMemoryWhilePlaying(CollectedData d, Report r)
    {
        var recorded = r.Sessions.Where(s => s.EndKind != EndKind.Running && s.VramLog is { TotalMB: > 0, Minutes: >= 2 })
                                 .OrderByDescending(s => s.Start).Take(12).ToList();
        if (recorded.Count == 0) return None;
        var total = recorded[0].VramLog!.TotalMB;
        var hot = recorded.Where(s => s.VramLog!.Peak >= 0.95 || s.VramLog!.MinutesAbove90 >= 2).ToList();
        var above = Math.Round(recorded.Sum(s => s.VramLog!.MinutesAbove90));
        var lines = recorded.Select(s => s.VramLog!.MinutesAbove90 >= 1
            ? L.T("rule.vramPlay.itemAbove", L.Date(s.Start, "dmt"), L.Pct(s.VramLog!.Peak), L.Pct(s.VramLog!.Median), Math.Round(s.VramLog!.MinutesAbove90))
            : L.T("rule.vramPlay.item", L.Date(s.Start, "dmt"), L.Pct(s.VramLog!.Peak), L.Pct(s.VramLog!.Median)));
        var n = recorded.Count;
        var lead = hot.Count == 0
            ? L.P("rule.vramPlay.none", n, total)
            : above >= 1 ? L.P("rule.vramPlay.hotAbove", n, hot.Count, total, above) : L.P("rule.vramPlay.hot", n, hot.Count, total);
        return One(new RuleHit
        {
            Severity = hot.Count >= 3 ? "high" : hot.Count > 0 ? "medium" : "info",
            Title = L.P("rule.vramPlay.title", n),
            Detail = L.Cat(lead, L.T("rule.newestFirst", string.Join(" · ", lines)), hot.Count > 0 ? L.T("rule.vram.levers") : ""),
            Action = new UiAction("sessions", L.T("act.seeSessions")),
        });
    }

    // Settings whose cost is known in advance to exceed the card. Graphics advice is usually worthless because it
    // is generic; these two are not, because the combination and the card size are both read from the machine.
    static IEnumerable<RuleHit> SettingsOverTheVramBudget(CollectedData d, Report r)
    {
        var hits = new List<RuleHit>();
        var fg = d.Settings.TryGetValue("FrameGeneration", out var fgv) && fgv != "Off";
        var rt = Analyzer.SettingOn(d, "RayTracing");
        d.Settings.TryGetValue("RayTracedLighting", out var rtl);
        if (fg && rt && d.System.VramGB > 0 && d.System.VramGB <= 8)
            hits.Add(new RuleHit
            {
                Severity = r.Sessions.Any(s => s.EndKind == EndKind.Gpu) ? "high" : "medium",
                Title = L.T("rule.fgRt.title", d.System.VramGB),
                Detail = L.T("rule.fgRt.detail", Analyzer.Gfx(rtl), d.System.VramGB),
                Action = new UiAction("settings", L.T("act.seeSettings")),
            });
        if (d.Settings.TryGetValue("RayTracedPathTracing", out var pt) && pt == "true" && d.System.VramGB <= 12)
            hits.Add(new RuleHit
            {
                Severity = "medium",
                Title = L.T("rule.pt.title"),
                Detail = L.T("rule.pt.detail"),
                Action = new UiAction("settings", L.T("act.seeSettings")),
            });
        return hits;
    }

    // Not a fault, and deliberately low: driver age is worth a line because frame generation and ray tracing fixes
    // ship in driver updates, and worth no more than a line because most old drivers are fine.
    static IEnumerable<RuleHit> AgeingGraphicsDriver(CollectedData d, Report r)
    {
        if (d.System.DriverDate is not { } dd || dd >= DateTime.Now.AddMonths(-9)) return None;
        return One(new RuleHit
        {
            Severity = "low",
            Title = L.T("rule.driver.title", L.Date(dd, "my")),
            Detail = L.T("rule.driver.detail"),
        });
    }

    /// <summary>"a, b, c and 4 more" - the first few named, the rest counted.</summary>
    internal static string ListMore(List<string> items, int show) =>
        items.Count <= show ? L.List(items) : L.List(items.Take(show).Append(L.P("word.nMore", items.Count - show)).ToList());
}

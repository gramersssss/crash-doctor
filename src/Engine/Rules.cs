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
        // what the mod loaders reported this launch
        NativePluginsRefusingToLoad,
        ArchiveXlStartupProblems,
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
        foreach (var dir in new[] { d.Paths.Bin, d.Paths.GameDir })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in EnablerFiles)
                if (File.Exists(Path.Combine(dir, f))) found.Add(Path.GetFileName(f));
        }
        if (found.Count == 0) return None;
        return One(new RuleHit
        {
            Severity = "high",
            Title = "A DLSS/FSR enabler has replaced files the game now owns",
            Detail = $"Found {Join(found.Distinct().ToList())} in the game folder. Since patch 2.3 the game ships its own FidelityFX and XeSS, and these tools overwrite those DLLs with their own copies. "
                   + "That combination is a common cause of GPU faults and of the game refusing to start after an update. Removing the mod does not undo it, because the game's original files were replaced: verify the game files through Steam, GOG or Epic to put them back.",
            Action = new UiAction("open:gamefolder", "Open the game folder"),
        });
    }

    // A download that failed part-way leaves a file that is present, named correctly, and unreadable. The loader
    // reports nothing useful; the game simply misbehaves wherever that content was meant to appear.
    static IEnumerable<RuleHit> EmptyOrTruncatedArchives(CollectedData d, Report r)
    {
        if (!Directory.Exists(d.Paths.ArchiveMods)) return None;
        var bad = new List<string>();
        foreach (var f in Directory.EnumerateFiles(d.Paths.ArchiveMods, "*.archive"))
        {
            long len; try { len = new FileInfo(f).Length; } catch { continue; }
            if (len < 1024) bad.Add(Path.GetFileName(f) + (len == 0 ? " (empty)" : $" ({len} bytes)"));
        }
        if (bad.Count == 0) return None;
        return One(new RuleHit
        {
            Severity = "high",
            Title = bad.Count == 1 ? "One mod archive is empty or truncated" : $"{bad.Count} mod archives are empty or truncated",
            Detail = $"{Join(bad.Take(5).ToList())}{(bad.Count > 5 ? $" and {bad.Count - 5} more" : "")}. A real archive is never this small - these are interrupted downloads. "
                   + "Nothing will report an error; the content simply is not there, and the game can fault where it was expected. Re-download and reinstall these mods.",
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
            Title = "Your saves are inside a cloud-synced folder",
            Detail = $"Saves are at {hit}. Sync clients rewrite files while the game is writing them, and can leave a save as an online-only placeholder that is not really on disk. "
                   + "That shows up as corrupted saves and as crashes on load. Move the Cyberpunk 2077 save folder out of the synced area, or exclude it from syncing.",
        });
    }

    // Overlays and capture tools inject themselves into the game's rendering. Most are harmless most of the time;
    // some are long-standing and well-documented sources of instability with modded Cyberpunk. Reported with the
    // counts, and without claiming to know that this one caused anything.
    static readonly Dictionary<string, string> Hooks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RTSSHooks64.dll"] = "RivaTuner Statistics Server (MSI Afterburner)",
        ["graphics-hook64.dll"] = "OBS game capture",
        ["fraps64.dll"] = "Fraps",
        ["DiscordHook64.dll"] = "the Discord overlay",
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
            Title = $"{worst.Key} was injected into the game in {worst.Value} of {dumps.Count} crashes",
            Detail = $"Tools that hook the game's rendering sit between it and the driver, and this one is a documented source of instability with heavily modded Cyberpunk. "
                   + $"{worst.Value} of {dumps.Count} is a correlation and not a cause - the way to settle it is to close it and play. "
                   + (seen.Count > 1 ? "Also seen: " + Join(seen.Keys.Where(k => k != worst.Key).ToList()) + "." : ""),
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

        var detail = $"The crash report carries the engine's own out-of-memory flag in {oom.Count} of {all.Count} crash{(all.Count == 1 ? "" : "es")}, most recently on {newest:d MMM}. "
                   + "That is the engine recording that it asked the system for memory and did not get it. ";
        if (measured.Count == 0)
            detail += "None of those crashes recorded how much video memory was in use, so there is no way to tell from here whether the card was the thing that ran out.";
        else if (cardFine == 0)
            detail += $"In all {cardFull} of them the graphics card was also at 95 % or more, so this is the same problem the video-memory reading already describes: the card is the constraint, and the settings that free the most are textures and crowd density.";
        else
        {
            var lowest = measured.Where(c => c.VramRatio < 0.95).Min(c => c.VramRatio);
            detail += $"In {cardFine} of them the card was below its ceiling - as low as {lowest * 100:0} % - so the card is not what ran out there. Something else is: the Windows page file (a small or disabled one is the usual cause on a machine with plenty of RAM), other programs holding memory, or simply the number of mod archives the engine has to keep indexed. "
                    + $"{(d.System.RamGB > 0 ? $"This machine has {d.System.RamGB} GB of RAM, which " : "Having a lot of RAM ")}does not help if the page file is capped - Windows still refuses the allocation.";
        }

        return One(new RuleHit
        {
            Severity = cardFine > 0 ? "high" : "medium",
            Title = $"The engine ran out of memory in {oom.Count} crash{(oom.Count == 1 ? "" : "es")}",
            Detail = detail,
            Action = new UiAction(cardFine > 0 ? "mods" : "settings", cardFine > 0 ? "See the mod list" : "See the settings"),
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
                ? $"All {dumps.Count} of your crashes are stack overflows"
                : $"{so.Count} of {dumps.Count} crashes {(so.Count == 1 ? "is a stack overflow" : "are stack overflows")}",
            Detail = $"The stack ran out of room{(where.Count > 0 ? ", faulting in " + Join(where) : "")}. A stack only runs out when something calls itself without ever returning, so this is a loop in code - not the graphics card, not video memory, and not something a settings change will alter. "
                   + "In this game it is nearly always scripts: two mods replacing the same method so that each one ends up calling the other. "
                   + (wrapped > 0
                        ? $"redscript reported {wrapped} method{(wrapped == 1 ? "" : "s")} being replaced more than once this session, which is exactly the shape that produces it - those warnings name the methods, and the mods replacing them are where to look first."
                        : "Start with whatever was installed or updated most recently, and look for two mods that change the same thing."),
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
            Title = $"All {crashes.Count} recent crashes happened in the first {StartupMinutes:0} minutes",
            Detail = $"The longest of them lasted {(longest < 1 ? $"{longest * 60:0} seconds" : $"{longest:0.#} minutes")}. A crash that always arrives before you are properly in the game is a loading problem - a plugin, a script that fails to compile, or a save that cannot be read - rather than anything about where you were or what you were doing. "
                   + (played > 0 ? $"{played} session{(played == 1 ? "" : "s")} in the same period got past that point and ended normally, so the game is not broken outright. " : "")
                   + "It is also the easiest kind of crash to find, because it happens every time instead of eventually: Find it will narrow the mod list down in a handful of short runs rather than a week of playing. Start with whatever you installed or updated most recently.",
            Action = new UiAction("bisect", "Find it"),
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
                ? $"Vortex thinks {mods[0]} is installed, but its files are not in the game folder"
                : $"{missing.Count} files Vortex deployed are missing from the game folder",
            Detail = (all
                    ? $"None of the {entries.Count} files in Vortex's deployment record is actually on disk. "
                    : $"{missing.Count} of the {entries.Count} files in Vortex's deployment record are not on disk, across {mods.Count} mod{(mods.Count == 1 ? "" : "s")}: {Join(mods.Take(4).ToList())}{(mods.Count > 4 ? $" and {mods.Count - 4} more" : "")}. ")
                + "Verifying the game files through Steam, GOG or Epic does this - the store deletes everything it does not recognise, and the manager is never told. Antivirus quarantine and a deployment that was interrupted have the same effect. "
                + "Vortex still lists these mods as installed and the game never sees them, which is the state behind \"the mod does nothing\" and ArchiveXL complaining that a file does not exist. Open Vortex and deploy again - purge first if it says everything is already deployed."
                // Said out loud, not swallowed: it stops the count reading as the whole picture, and it is how
                // anyone posting a report tells us these entries exist in the wild at all.
                + (skipped > 0 ? $" ({skipped} further files deploy to their own target folder and were not checked.)" : ""),
            Mod = mods.Count == 1 ? mods[0] : null,
            Flag = mods.Count == 1 ? "files missing" : null,
        });
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
        if (latestRed == null) return None;
        var hits = new List<RuleHit>();
        foreach (var inc in latestRed.Incompatible)
        {
            var name = Regex.Match(inc, @"^(.+?) \(version").Groups[1].Value;
            var owner = d.Mods.OwnerOfPlugin(name.Replace(" ", "")) ?? d.Mods.OwnerOfPlugin(name) ?? name;
            hits.Add(new RuleHit
            {
                Severity = "medium",
                Title = $"Native plugin refuses to load on this patch: {name}",
                Detail = inc + " Whatever this mod does through its native half is silently off, and its script half may error.",
                Mod = owner,
                Flag = "plugin not loading",
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
            var title = world ? "World sector patch fails every launch" : missing ? "Mod points at resources that do not exist" : "ArchiveXL reports a problem";
            if (owner == null) title += " (mod not identified)";
            var detail = msgs.Count == 1 ? msgs[0] : $"{msgs.Count} distinct messages, e.g. {msgs[0]}";
            if (world) detail += " Another mod changed that sector first, or the game patch did; the patch is skipped.";
            else if (missing) detail += " The mod references files that are not installed or were removed in a game patch; those parts are skipped.";
            hits.Add(new RuleHit
            {
                Severity = world ? "medium" : first.Kind == "xl-error" ? (owner == null ? "low" : "medium") : "low",
                Title = title,
                Detail = detail,
                Mod = owner,
                Flag = world ? "world patch failing" : missing ? "missing resources" : "ArchiveXL problem",
                Action = owner != null ? Analyzer.NexusAction(d, owner) : null,
            });
        }
        return hits;
    }

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
                Title = overwrite ? "Two mods replace the same script method" : "redscript warning",
                Detail = w,
                Mod = Analyzer.GuessOwnerFromText(d, w),
                Flag = overwrite ? "script overridden" : "redscript warning",
            });
        }
        foreach (var e in d.RedscriptErrors.Take(6))
            hits.Add(new RuleHit
            {
                Severity = "high",
                Title = "redscript error: a script mod does not compile",
                Detail = e,
                Mod = Analyzer.GuessOwnerFromText(d, e),
                Flag = "does not compile",
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
                Title = $"{owner} logs the same error {grp.Count()} times per session",
                Detail = Analyzer.Short(grp.Last().Text) + " A feature of the mod is not working; check for an update.",
                Mod = owner,
                Flag = "error spam",
                Action = Analyzer.NexusAction(d, owner),
            });
        }
        return hits;
    }

    // The same file name in two places is only a conflict when the contents are the same script - two mods bundling
    // one shared fix. Different files that happen to share a common name are not a problem and must not be listed.
    static IEnumerable<RuleHit> DuplicatedScripts(CollectedData d, Report r)
    {
        if (!Directory.Exists(d.Paths.Scripts)) return None;
        var dups = Directory.EnumerateFiles(d.Paths.Scripts, "*.reds", SearchOption.AllDirectories)
            .GroupBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1 && !Analyzer.GenericScriptName(g.Key))
            .Select(g => g.GroupBy(f => { try { return Fixes.Sha(f); } catch { return f; } }).Where(x => x.Count() > 1).ToList())
            .Where(x => x.Count > 0).ToList();

        var hits = new List<RuleHit>();
        foreach (var g in dups.Take(5))
            foreach (var same in g)
                hits.Add(new RuleHit
                {
                    Severity = "low",
                    Title = $"{Path.GetFileName(same.First())} is installed {same.Count()} times",
                    Detail = "Identical copies of one script compile more than once; the compiler warns and the last one wins. Two mods are bundling the same fix: "
                           + string.Join(", ", same.Select(f => d.Mods.FileOwner.FirstOrDefault(kv => kv.Key.EndsWith(Path.GetRelativePath(d.Paths.GameDir, f), StringComparison.OrdinalIgnoreCase)).Value ?? Path.GetRelativePath(d.Paths.Scripts, f))),
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
        var lines = withVram.Select(s => $"{s.Start:d MMM HH:mm} {s.VramUsedMB} MB ({Analyzer.Pct((double)s.VramUsedMB!.Value / s.VramTotalMB!.Value)})");
        var lead = atCeiling.Count == 0
            ? $"None of the last {withVram.Count} crashes was short of video memory, so the card is not the constraint."
            : $"{atCeiling.Count} of the last {withVram.Count} crashes happened with the {total} MB card at 95 % or more. That is where allocations start failing, and a failed allocation crashes the engine with nothing written to any log.";
        return One(new RuleHit
        {
            Severity = atCeiling.Count >= 3 ? "high" : atCeiling.Count > 0 ? "medium" : "info",
            Title = $"Video memory at the last {withVram.Count} crash{(withVram.Count == 1 ? "" : "es")}",
            Detail = lead + "  Newest first: " + string.Join(" · ", lines) + $".  Card total {total} MB."
                   + (atCeiling.Count > 0 ? "  Textures and crowd density free the most; ray traced lighting and reflections are next." : ""),
            Action = new UiAction("settings", "See settings"),
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
        var lines = recorded.Select(s => $"{s.Start:d MMM HH:mm} peak {Analyzer.Pct(s.VramLog!.Peak)}, median {Analyzer.Pct(s.VramLog!.Median)}"
                                       + (s.VramLog!.MinutesAbove90 >= 1 ? $", {Math.Round(s.VramLog!.MinutesAbove90)} min above 90%" : ""));
        var n = recorded.Count; var ses = n == 1 ? "session" : "sessions";
        var lead = hot.Count == 0
            ? $"In the last {n} recorded {ses} the {total} MB card never sat near its ceiling, so video memory is not the constraint with the current settings."
            : $"In {hot.Count} of the last {n} recorded {ses} the {total} MB card ran close to full" + (above >= 1 ? $", {above} min above 90 % in all" : ", touching 95 % or more") + ". That is the range where the engine's next texture or mesh allocation can fail, and a failed allocation crashes it with nothing written to any log.";
        return One(new RuleHit
        {
            Severity = hot.Count >= 3 ? "high" : hot.Count > 0 ? "medium" : "info",
            Title = $"Video memory while you played, last {n} {ses}",
            Detail = lead + "  Newest first: " + string.Join(" · ", lines) + "."
                   + (hot.Count > 0 ? "  Textures and crowd density free the most; ray traced lighting and reflections are next." : ""),
            Action = new UiAction("sessions", "See sessions"),
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
                Title = $"Frame generation with ray tracing on an {d.System.VramGB} GB GPU",
                Detail = $"Ray traced lighting is {rtl}. This combination sits at the VRAM ceiling on {d.System.VramGB} GB; GPU faults on the map or in dense areas are the usual result. Turn one of them down.",
                Action = new UiAction("settings", "See settings"),
            });
        if (d.Settings.TryGetValue("RayTracedPathTracing", out var pt) && pt == "true" && d.System.VramGB <= 12)
            hits.Add(new RuleHit
            {
                Severity = "medium",
                Title = "Path tracing on a card with 12 GB or less",
                Detail = "Path tracing needs more VRAM than any other setting. Expect faults in dense areas.",
                Action = new UiAction("settings", "See settings"),
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
            Title = $"Graphics driver is from {dd:MMM yyyy}",
            Detail = "Not necessarily a problem, but frame generation and ray tracing fixes ship in driver updates.",
        });
    }

    static string Join(List<string> x) => x.Count == 0 ? "" : x.Count == 1 ? x[0] : string.Join(", ", x.Take(x.Count - 1)) + " and " + x[^1];
}

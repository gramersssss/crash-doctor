using System.Text.RegularExpressions;

namespace CrashDoctor.Engine;

// The rule catalogue - layer 4 of DESIGN.md, "How it reasons".
//
// This is the answer to "but every crash is different". The fixes differ; the evidence gathering does not. Some
// causes have a crash signature and belong in fixes/catalog.json keyed on it. Far more of them are recognisable
// from a *pattern in the evidence* with no signature at all - a file that should not be there, a plugin built for
// last month's game, an archive that downloaded as zero bytes - and those are what live here.
//
// A rule is deliberately small: look at the collected facts, return one finding or nothing. Adding one should be a
// few lines in this file and nothing else, because the value of this layer is in how many of them there are.
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
//
// Rules still living inline in Analyzer.Health (frame generation on a small card, incompatible native plugins,
// redscript failures, duplicated scripts, the video-memory readout) predate this file and should migrate here.
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
    public delegate RuleHit? Rule(CollectedData d, Report r);

    public static readonly Rule[] All =
    {
        BrokenUpscalerOverride,
        EmptyOrTruncatedArchives,
        SavesInCloudStorage,
        OverlaysInjectedAtTheCrash,
        EngineRanOutOfMemory,
        StackOverflowIsRecursion,
        CrashesOnlyAtStartup,
        DeployedFilesAreMissing,
    };

    public static List<RuleHit> Run(CollectedData d, Report r)
    {
        var hits = new List<RuleHit>();
        foreach (var rule in All)
        {
            try { if (rule(d, r) is { } hit) hits.Add(hit); }
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
    static RuleHit? BrokenUpscalerOverride(CollectedData d, Report r)
    {
        var found = new List<string>();
        foreach (var dir in new[] { d.Paths.Bin, d.Paths.GameDir })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in EnablerFiles)
                if (File.Exists(Path.Combine(dir, f))) found.Add(Path.GetFileName(f));
        }
        if (found.Count == 0) return null;
        return new RuleHit
        {
            Severity = "high",
            Title = "A DLSS/FSR enabler has replaced files the game now owns",
            Detail = $"Found {Join(found.Distinct().ToList())} in the game folder. Since patch 2.3 the game ships its own FidelityFX and XeSS, and these tools overwrite those DLLs with their own copies. "
                   + "That combination is a common cause of GPU faults and of the game refusing to start after an update. Removing the mod does not undo it, because the game's original files were replaced: verify the game files through Steam, GOG or Epic to put them back.",
            Action = new UiAction("open:gamefolder", "Open the game folder"),
        };
    }

    // A download that failed part-way leaves a file that is present, named correctly, and unreadable. The loader
    // reports nothing useful; the game simply misbehaves wherever that content was meant to appear.
    static RuleHit? EmptyOrTruncatedArchives(CollectedData d, Report r)
    {
        if (!Directory.Exists(d.Paths.ArchiveMods)) return null;
        var bad = new List<string>();
        foreach (var f in Directory.EnumerateFiles(d.Paths.ArchiveMods, "*.archive"))
        {
            long len; try { len = new FileInfo(f).Length; } catch { continue; }
            if (len < 1024) bad.Add(Path.GetFileName(f) + (len == 0 ? " (empty)" : $" ({len} bytes)"));
        }
        if (bad.Count == 0) return null;
        return new RuleHit
        {
            Severity = "high",
            Title = bad.Count == 1 ? "One mod archive is empty or truncated" : $"{bad.Count} mod archives are empty or truncated",
            Detail = $"{Join(bad.Take(5).ToList())}{(bad.Count > 5 ? $" and {bad.Count - 5} more" : "")}. A real archive is never this small - these are interrupted downloads. "
                   + "Nothing will report an error; the content simply is not there, and the game can fault where it was expected. Re-download and reinstall these mods.",
        };
    }

    // OneDrive and similar sync clients rewrite files underneath the game while it is saving, and can turn a save
    // into a placeholder that is not on disk at all when the game asks for it.
    static RuleHit? SavesInCloudStorage(CollectedData d, Report r)
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
        if (hit == null) return null;
        return new RuleHit
        {
            Severity = "medium",
            Title = "Your saves are inside a cloud-synced folder",
            Detail = $"Saves are at {hit}. Sync clients rewrite files while the game is writing them, and can leave a save as an online-only placeholder that is not really on disk. "
                   + "That shows up as corrupted saves and as crashes on load. Move the Cyberpunk 2077 save folder out of the synced area, or exclude it from syncing.",
        };
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
    static RuleHit? OverlaysInjectedAtTheCrash(CollectedData d, Report r)
    {
        var dumps = d.CrashReports.Where(c => c.Dump != null).ToList();
        if (dumps.Count == 0) return null;
        var seen = new Dictionary<string, int>();
        foreach (var c in dumps)
            foreach (var m in c.Dump!.Modules.Distinct(StringComparer.OrdinalIgnoreCase))
                if (Hooks.TryGetValue(m, out var who)) seen[who] = seen.GetValueOrDefault(who) + 1;
        if (seen.Count == 0) return null;
        var worst = seen.OrderByDescending(kv => kv.Value).First();
        return new RuleHit
        {
            Severity = "low",
            Title = $"{worst.Key} was injected into the game in {worst.Value} of {dumps.Count} crashes",
            Detail = $"Tools that hook the game's rendering sit between it and the driver, and this one is a documented source of instability with heavily modded Cyberpunk. "
                   + $"{worst.Value} of {dumps.Count} is a correlation and not a cause - the way to settle it is to close it and play. "
                   + (seen.Count > 1 ? "Also seen: " + Join(seen.Keys.Where(k => k != worst.Key).ToList()) + "." : ""),
        };
    }

    // The engine sets its own out-of-memory flag in the crash report when an allocation it asked for came back
    // empty. That is the engine saying so, not us inferring it from a fault address, and it is collected already
    // and was going nowhere. The distinction worth drawing is whether the graphics card was also full at the time:
    // if it was, this is the video-memory story and is already told; if it was not, something *other* than the card
    // ran out, and turning graphics down will do nothing at all.
    static RuleHit? EngineRanOutOfMemory(CollectedData d, Report r)
    {
        var all = d.CrashReports;
        var oom = all.Where(c => c.EngineOom).ToList();
        if (oom.Count == 0) return null;
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

        return new RuleHit
        {
            Severity = cardFine > 0 ? "high" : "medium",
            Title = $"The engine ran out of memory in {oom.Count} crash{(oom.Count == 1 ? "" : "es")}",
            Detail = detail,
            Action = new UiAction(cardFine > 0 ? "mods" : "settings", cardFine > 0 ? "See the mod list" : "See the settings"),
        };
    }

    // A stack overflow is the one exception code that names its own cause. The stack only runs out when something
    // calls itself without ever returning, which is a logic fault in code, never hardware and never video memory.
    // In this game it is nearly always scripts: two mods wrapping the same method so each ends up calling the
    // other. Worth saying loudly, because every generic "lower your settings" answer is wrong for it.
    static RuleHit? StackOverflowIsRecursion(CollectedData d, Report r)
    {
        var dumps = d.CrashReports.Where(c => c.Dump != null).ToList();
        var so = dumps.Where(c => c.Dump!.ExceptionCode == 0xC00000FD).ToList();
        if (so.Count == 0) return null;
        var where = so.Select(c => c.Dump!.FaultingModule).Where(m => m != null).Distinct(StringComparer.OrdinalIgnoreCase).Select(m => m!).ToList();
        var wrapped = d.RedscriptWarnings.Count(w => w.Contains("overwrites a previous annotation"));

        return new RuleHit
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
        };
    }

    // Crashes that always arrive before the player is properly in the game are a different animal from crashes that
    // happen somewhere: they are a loading problem, and - far more usefully - they reproduce on demand. That makes
    // them the easiest kind to find, which is worth telling someone who thinks a fast crash is a worse one.
    const double StartupMinutes = 2.0;
    static RuleHit? CrashesOnlyAtStartup(CollectedData d, Report r)
    {
        var recent = r.Sessions.Where(s => s.Start > DateTime.Now.AddDays(-14) && !s.Partial && s.DurationKnown).ToList();
        var crashes = recent.Where(s => s.EndKind is not (EndKind.Clean or EndKind.Running)).ToList();
        if (crashes.Count < 3) return null;
        if (crashes.Any(s => s.Minutes > StartupMinutes)) return null;
        var longest = crashes.Max(s => s.Minutes);
        var played = recent.Count(s => s.EndKind == EndKind.Clean && s.Minutes > StartupMinutes);

        return new RuleHit
        {
            Severity = "high",
            Title = $"All {crashes.Count} recent crashes happened in the first {StartupMinutes:0} minutes",
            Detail = $"The longest of them lasted {(longest < 1 ? $"{longest * 60:0} seconds" : $"{longest:0.#} minutes")}. A crash that always arrives before you are properly in the game is a loading problem - a plugin, a script that fails to compile, or a save that cannot be read - rather than anything about where you were or what you were doing. "
                   + (played > 0 ? $"{played} session{(played == 1 ? "" : "s")} in the same period got past that point and ended normally, so the game is not broken outright. " : "")
                   + "It is also the easiest kind of crash to find, because it happens every time instead of eventually: Find it will narrow the mod list down in a handful of short runs rather than a week of playing. Start with whatever you installed or updated most recently.",
            Action = new UiAction("bisect", "Find it"),
        };
    }

    // Vortex keeps a manifest of every file it put in the game folder. When the game is verified through Steam, GOG
    // or Epic, the store deletes files it does not recognise - which is all of them - and the manifest still says
    // they are there. Antivirus quarantine and an interrupted deploy do the same. The mod is installed as far as the
    // manager is concerned and absent as far as the game is concerned, and nothing reports an error either way.
    static RuleHit? DeployedFilesAreMissing(CollectedData d, Report r)
    {
        if (!File.Exists(d.Paths.VortexManifest)) return null;
        // Parsed here rather than taken from the inventory because the inventory drops each entry's deployment
        // target, and a file with a target does not live at <game>\<relPath>. Getting that wrong would report every
        // REDmod-deployed file as missing.
        List<(string mod, string path)> entries = new();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(Files.ReadAllTextShared(d.Paths.VortexManifest));
            if (!doc.RootElement.TryGetProperty("files", out var files)) return null;
            foreach (var f in files.EnumerateArray())
            {
                var rel = f.TryGetProperty("relPath", out var p) ? p.GetString() : null;
                if (string.IsNullOrEmpty(rel)) continue;
                var target = f.TryGetProperty("target", out var t) ? t.GetString() ?? "" : "";
                var src = f.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "";
                entries.Add((ModInventory.FromFolder(src).Name, Path.Combine(d.Paths.GameDir, target, rel)));
                if (entries.Count >= 20000) break;   // a manifest this size is already pathological; do not stall the scan
            }
        }
        catch { return null; }
        if (entries.Count == 0) return null;

        var missing = entries.Where(e => !File.Exists(e.path)).ToList();
        if (missing.Count == 0) return null;
        var mods = missing.Select(e => e.mod).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var all = missing.Count == entries.Count;

        return new RuleHit
        {
            Severity = "high",
            Title = mods.Count == 1
                ? $"Vortex thinks {mods[0]} is installed, but its files are not in the game folder"
                : $"{missing.Count} files Vortex deployed are missing from the game folder",
            Detail = (all
                    ? $"None of the {entries.Count} files in Vortex's deployment record is actually on disk. "
                    : $"{missing.Count} of the {entries.Count} files in Vortex's deployment record are not on disk, across {mods.Count} mod{(mods.Count == 1 ? "" : "s")}: {Join(mods.Take(4).ToList())}{(mods.Count > 4 ? $" and {mods.Count - 4} more" : "")}. ")
                + "Verifying the game files through Steam, GOG or Epic does this - the store deletes everything it does not recognise, and the manager is never told. Antivirus quarantine and a deployment that was interrupted have the same effect. "
                + "Vortex still lists these mods as installed and the game never sees them, which is the state behind \"the mod does nothing\" and ArchiveXL complaining that a file does not exist. Open Vortex and deploy again - purge first if it says everything is already deployed.",
            Mod = mods.Count == 1 ? mods[0] : null,
            Flag = mods.Count == 1 ? "files missing" : null,
        };
    }

    static string Join(List<string> x) => x.Count == 0 ? "" : x.Count == 1 ? x[0] : string.Join(", ", x.Take(x.Count - 1)) + " and " + x[^1];
}

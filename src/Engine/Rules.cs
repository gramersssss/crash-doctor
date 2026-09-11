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
// Rejected, so it is not tried again: "native plugin is older than the game build". Plugins legitimately predate
// patches by months and keep working - on the reference machine it flagged nine, including the current release of
// ArchiveXL. It could not tell a healthy install from a broken one, which is the one thing a rule may not do.
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

    static string Join(List<string> x) => x.Count == 0 ? "" : x.Count == 1 ? x[0] : string.Join(", ", x.Take(x.Count - 1)) + " and " + x[^1];
}

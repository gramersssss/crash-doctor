using System.Text.Json;

namespace CrashDoctor.Engine;

// The guided bisect. See DESIGN.md, "How it reasons".
//
// When the logs name nothing and elimination has cleared everything, there is still one method that always works:
// halve the mod list, play, see whether the same fault comes back, and repeat. It is mechanical rather than clever,
// and it finds causes nobody has ever seen - including ones that never write a single line to any log.
//
// Two rules shape the implementation:
//
// 1. Crash Doctor never enables or disables anything. It says exactly what to switch off, and then VERIFIES that
//    what it asked for is what is actually deployed before it will accept a result. Mistrusting the toggle is the
//    point: "I disabled that mod" and "that mod is disabled" are different statements, and a bisect built on the
//    first one silently produces the wrong answer.
//
// 2. "It didn't crash" is not proof. These crashes are intermittent, so a clean run only counts when it lasts
//    meaningfully longer than this fault's own typical time-to-crash. The threshold is derived from the crash
//    history rather than invented, and it is shown to the user so they can judge it themselves.
public sealed class BisectStep
{
    public int Index { get; set; }
    public List<string> Off { get; set; } = new();     // must be disabled for this step
    public int CandidatesBefore { get; set; }
    public DateTime? StartedAt { get; set; }           // when the toggles were verified and play could begin
    public DateTime? DecidedAt { get; set; }
    public string Outcome { get; set; } = "pending";   // pending | recurred | survived
    public string? Note { get; set; }
}

public sealed class BisectPlan
{
    public string Id { get; set; } = "";
    public DateTime Started { get; set; }
    public string Signature { get; set; } = "";        // the exact fault being hunted
    public int MinutesToBeat { get; set; } = 15;       // a clean run must last at least this long to count
    public string? ThresholdNote { get; set; }         // where that number came from, in words, so it can be judged
    public List<string> Candidates { get; set; } = new();
    public List<string> Cleared { get; set; } = new();
    public List<BisectStep> Steps { get; set; } = new();
    public string Status { get; set; } = "running";    // running | found | exhausted | stopped | stale
    public string? Culprit { get; set; }
    public string? GameBuild { get; set; }             // the build every round of this run was measured against

    public BisectStep? Current => Steps.LastOrDefault(s => s.Outcome == "pending");
}

public static class Bisect
{
    static string PathFile => Path.Combine(GamePaths.AppData, "bisect.json");

    // Frameworks and loaders. Turning these off does not test anything, it just stops half the mod list working, so
    // they are never offered as candidates.
    static readonly string[] NeverTouch =
    {
        "red4ext", "cyber engine tweaks", "cet ", "archivexl", "tweakxl", "codeware", "redscript",
        "mod settings", "mod_settings", "native settings", "input loader", "input_loader", "cybercmd",
        "equipment-ex", "cyberware-ex", "virtual atelier", "audioware", "deceptious quest core",
        "native interactions framework", "browserextensionframework",
    };

    // How long a clean run has to last before it says anything: twice this fault's typical time-to-crash, never under
    // 15 minutes, never over 2 hours. The cap exists because one 2.5-hour crash produced a 5-hour demand on the
    // reference machine, which nobody will play to, and a threshold nobody meets is the same as no bisect. With fewer
    // than three timed crashes the number is a guess and the note says so; the page shows the note beside the number.
    public const int MinThreshold = 15, MaxThreshold = 120, EnoughTimed = 3;
    public static (int minutes, string note, int timed) Threshold(Report r, string signature)
    {
        var timed = r.Sessions.Where(s => s.Signature == signature && s.DurationKnown && s.Minutes > 0).Select(s => s.Minutes).OrderBy(x => x).ToList();
        if (timed.Count == 0)
            return (MinThreshold, $"No crash of this fault has a known length (their session logs were gone before they were scanned), so {MinThreshold} minutes is a floor rather than a measurement. Treat a clean run as weak evidence until a timed crash exists.", 0);
        var median = timed[timed.Count / 2];
        var raw = (int)Math.Ceiling(median * 2);
        var minutes = Math.Clamp(raw, MinThreshold, MaxThreshold);
        var basis = timed.Count >= EnoughTimed
            ? $"twice the typical {Math.Round(median)} min this fault takes to appear, from {timed.Count} timed crashes"
            : $"twice the {Math.Round(median)} min {(timed.Count == 1 ? "the only timed crash of this fault" : "the two timed crashes of this fault")} took, so treat it as rough";
        var note = raw > MaxThreshold
            ? $"Capped at {MaxThreshold} min. Uncapped it would be {raw} min ({basis}). A fault this slow to appear makes a clean run weaker evidence than usual, so give the step more than one session where you can."
            : raw < MinThreshold
                ? $"{MinThreshold} min is the floor; {basis} would give only {raw}."
                : $"{basis.Substring(0, 1).ToUpperInvariant()}{basis.Substring(1)}.";
        return (minutes, note, timed.Count);
    }

    public static BisectPlan? Load()
    {
        try { if (File.Exists(PathFile)) return JsonSerializer.Deserialize<BisectPlan>(File.ReadAllText(PathFile), Scanner.Json); } catch { }
        return null;
    }

    public static void Save(BisectPlan? p)
    {
        try
        {
            Directory.CreateDirectory(GamePaths.AppData);
            if (p == null) { if (File.Exists(PathFile)) File.Delete(PathFile); return; }
            File.WriteAllText(PathFile, JsonSerializer.Serialize(p, Scanner.Json));
        }
        catch { }
    }

    /// <summary>Begin hunting one specific fault. Candidates are every enabled mod that is not a framework.</summary>
    public static BisectPlan Start(CollectedData d, Report r, string signature)
    {
        var group = r.CrashGroups.FirstOrDefault(g => g.Signature == signature);
        var (threshold, thresholdNote, _) = Threshold(r, signature);

        // Mods that were ever suspected of this fault go first, so the earliest step tests the best guesses and can
        // clear all of them at once.
        var suspected = r.Sessions.Where(s => s.Signature == signature).SelectMany(s => s.Suspects.Select(x => x.Mod)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = r.ModsList
            .Where(m => m.Status == "enabled" && !IsFramework(m.Name))
            .OrderByDescending(m => suspected.Contains(m.Name))
            .ThenByDescending(m => (m.Type ?? "").Contains("ArchiveXL"))
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(m => m.Name).ToList();

        var plan = new BisectPlan
        {
            Id = "b" + DateTime.Now.ToString("yyyyMMddHHmmss"),
            Started = DateTime.Now,
            Signature = signature,
            MinutesToBeat = threshold,
            ThresholdNote = thresholdNote,
            Candidates = candidates,
            GameBuild = r.Game.FileVersion,
        };
        NextStep(plan);
        Save(plan);
        return plan;
    }

    public static bool IsFramework(string name)
    {
        var n = name.ToLowerInvariant();
        return NeverTouch.Any(x => n.Contains(x));
    }

    static void NextStep(BisectPlan p)
    {
        if (p.Candidates.Count <= 1)
        {
            p.Status = p.Candidates.Count == 1 ? "found" : "exhausted";
            p.Culprit = p.Candidates.FirstOrDefault();
            return;
        }
        var half = p.Candidates.Take((p.Candidates.Count + 1) / 2).ToList();
        p.Steps.Add(new BisectStep { Index = p.Steps.Count + 1, Off = half, CandidatesBefore = p.Candidates.Count });
    }

    /// <summary>
    /// Advance the run using whatever has happened since the current step began. Called on every scan, so the user
    /// only has to play and rescan - there is no "submit result" button to get wrong.
    /// </summary>
    public static void Advance(BisectPlan p, Report r)
    {
        if (p.Status != "running") return;

        // A game update moves every address, so the fault being hunted can no longer occur under the signature this
        // run is watching for. Left alone, the next long clean session would be read as "it survived" and would
        // blame whichever mods happened to be switched off. Stop instead and say why.
        if (p.GameBuild != null && r.Game.FileVersion.Length > 0 && r.Game.FileVersion != p.GameBuild)
        {
            p.Status = "stale";
            Save(p);
            return;
        }

        var step = p.Current;
        if (step?.StartedAt == null) return;

        var since = r.Sessions.Where(s => s.Start >= step.StartedAt.Value.AddMinutes(-1)).ToList();
        var recurred = since.FirstOrDefault(s => s.Signature == p.Signature);
        var survived = since.FirstOrDefault(s => s.EndKind == EndKind.Clean && s.Minutes >= p.MinutesToBeat);

        if (recurred != null)
        {
            // The fault came back with those mods off, so none of them causes it.
            step.Outcome = "recurred";
            step.Note = $"The same fault happened again on {recurred.Start:d MMM HH:mm} with those {step.Off.Count} mods off, so none of them is the cause.";
            p.Cleared.AddRange(step.Off);
            p.Candidates = p.Candidates.Where(c => !step.Off.Contains(c)).ToList();
        }
        else if (survived != null)
        {
            // A run long enough to be meaningful, with those mods off. The cause is very likely among them.
            step.Outcome = "survived";
            step.Note = $"You played {Math.Round(survived.Minutes)} minutes cleanly on {survived.Start:d MMM HH:mm} with those mods off - longer than this fault normally takes. It is most likely one of them.";
            p.Cleared.AddRange(p.Candidates.Where(c => !step.Off.Contains(c)));
            p.Candidates = step.Off.ToList();
        }
        else return;   // nothing decisive yet: keep playing

        step.DecidedAt = DateTime.Now;
        NextStep(p);
        Save(p);
    }

    /// <summary>What the user still has to change before the current step is a valid experiment.</summary>
    public static (List<string> turnOff, List<string> turnOn) Verify(BisectPlan p, Report r)
    {
        var step = p.Current;
        if (step == null) return (new(), new());
        var byName = r.ModsList.ToDictionary(m => m.Name, m => m.Status, StringComparer.OrdinalIgnoreCase);
        string? Status(string n) => byName.TryGetValue(n, out var s) ? s : null;

        var turnOff = step.Off.Where(n => Status(n) == "enabled").ToList();
        // Anything still in the running must be ON, or the test proves nothing about it.
        var turnOn = p.Candidates.Where(n => !step.Off.Contains(n) && Status(n) == "disabled").ToList();
        return (turnOff, turnOn);
    }

    /// <summary>Build the view the interface renders. All the wording lives here so the engine owns the reasoning.</summary>
    public static BisectView? View(BisectPlan? p, Report r)
    {
        if (p == null) return null;
        var v = new BisectView
        {
            Id = p.Id,
            Signature = p.Signature,
            Status = p.Status,
            MinutesToBeat = p.MinutesToBeat,
            ThresholdNote = p.ThresholdNote,
            Candidates = p.Candidates.Count,
            Cleared = p.Cleared.Count,
            StepNumber = p.Steps.Count,
            StepsLeft = p.Candidates.Count > 1 ? (int)Math.Ceiling(Math.Log2(p.Candidates.Count)) : 0,
            Culprit = p.Culprit,
            History = p.Steps.Where(s => s.Outcome != "pending").Select(s => $"Step {s.Index}: {s.Off.Count} mods off → {s.Note}").ToList(),
        };

        if (p.Status == "found")
        {
            v.Headline = $"It is {p.Culprit}.";
            v.Detail = $"Narrowed from {p.Cleared.Count + 1} mods in {p.Steps.Count(s => s.Outcome != "pending")} tests, by watching whether {p.Signature} came back each time. "
                     + $"Now turn all the other mods back on in Vortex and leave only {p.Culprit} off - if the fault stays away with everything else running, that is your answer. If it comes back, the cause was a combination rather than one mod, and the run has to start again with that one excluded.";
            return v;
        }
        if (p.Status == "exhausted")
        {
            v.Headline = "The cause is not among the mods that were tested.";
            v.Detail = "Every candidate was cleared, so this fault comes from something else: the game itself, a driver, hardware, or a tool running alongside the game. Check the Health page for anything injected into the process. Turn your mods back on in Vortex - none of them was responsible.";
            return v;
        }
        if (p.Status == "stale")
        {
            v.Headline = "The game updated, so this hunt cannot continue.";
            v.Detail = $"Every round so far was measured against game build {p.GameBuild}, and an update moves every address - the fault being hunted ({p.Signature.Split('@')[0]}) cannot happen at that address any more. "
                     + $"Nothing learned in {p.Steps.Count(x => x.Outcome != "pending")} round(s) is wrong, but it cannot be carried forward. Turn your mods back on, play until the crash reappears, then start a new hunt against whatever fault the new build reports.";
            return v;
        }
        if (p.Status == "stopped") { v.Headline = "Bisect stopped."; return v; }

        var step = p.Current;
        if (step == null) return v;
        v.Off = step.Off;
        var (turnOff, turnOn) = Verify(p, r);
        v.TurnOff = turnOff; v.TurnOn = turnOn;
        v.Ready = turnOff.Count == 0 && turnOn.Count == 0;
        v.Playing = step.StartedAt != null;

        if (!v.Ready)
        {
            v.Headline = $"Step {step.Index}: turn off {step.Off.Count} of the {step.CandidatesBefore} mods still in the running.";
            v.Detail = (turnOff.Count > 0 ? $"{turnOff.Count} still need turning off. " : "")
                     + (turnOn.Count > 0 ? $"{turnOn.Count} that must stay in the test are currently disabled and need turning back on. " : "")
                     + "Do it in Vortex, deploy, then scan again - Crash Doctor checks what is actually deployed rather than taking your word for it.";
        }
        else if (!v.Playing)
        {
            v.Headline = $"Step {step.Index} is set up correctly. Go and play.";
            v.Detail = $"Play the way you normally do, in the place this fault usually happens. Either it comes back - which clears all {step.Off.Count} of these - or you get a clean run of at least {p.MinutesToBeat} minutes, which points at them. Then scan again; the result is read from your logs automatically.";
        }
        else
        {
            v.Headline = $"Step {step.Index} is running. Play, then scan.";
            v.Detail = $"Nothing decisive yet. A crash with {p.Signature} clears these {step.Off.Count} mods; a clean run of {p.MinutesToBeat}+ minutes points at them. Anything shorter is not conclusive either way, so keep playing.";
        }
        return v;
    }
}

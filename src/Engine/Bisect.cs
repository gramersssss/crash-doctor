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
            return (MinThreshold, L.T("bisect.noTimed", MinThreshold), 0);
        var median = timed[timed.Count / 2];
        var raw = (int)Math.Ceiling(median * 2);
        var minutes = Math.Clamp(raw, MinThreshold, MaxThreshold);
        var basis = timed.Count >= EnoughTimed
            ? L.P("bisect.basis", timed.Count, Math.Round(median))
            : L.P("bisect.basisRough", timed.Count, Math.Round(median));
        var note = raw > MaxThreshold
            ? L.T("bisect.capped", MaxThreshold, raw, basis)
            : raw < MinThreshold
                ? L.T("bisect.floor", MinThreshold, basis, raw)
                : L.T("bisect.basisSentence", basis);
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
            .Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

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
            step.Note = L.P("bisect.recurred", step.Off.Count, L.Date(recurred.Start, "dmt"));
            p.Cleared.AddRange(step.Off);
            p.Candidates = p.Candidates.Where(c => !step.Off.Contains(c)).ToList();
        }
        else if (survived != null)
        {
            // A run long enough to be meaningful, with those mods off. The cause is very likely among them.
            step.Outcome = "survived";
            step.Note = L.P("bisect.survived", Math.Round(survived.Minutes), L.Date(survived.Start, "dmt"));
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
        // Two mods can share a name (e.g. an archive and a folder both called "Additional Stuff 01" - Nuttyboy812, 2 Oct):
        // ToDictionary threw "An item with the same key has already been added" and the whole scan failed.
        var byName = r.ModsList.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Any(m => m.Status == "enabled") ? "enabled" : g.First().Status, StringComparer.OrdinalIgnoreCase);
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
            History = p.Steps.Where(s => s.Outcome != "pending").Select(s => L.P("bisect.history", s.Off.Count, s.Index, s.Note)).ToList(),
        };
        var mgr = Manager(r);

        if (p.Status == "found")
        {
            v.Headline = L.T("bisect.found", p.Culprit);
            v.Detail = L.Cat(L.P("bisect.foundHow", p.Steps.Count(s => s.Outcome != "pending"), p.Cleared.Count + 1, p.Signature),
                             L.T("bisect.foundNext", mgr, p.Culprit));
            return v;
        }
        if (p.Status == "exhausted")
        {
            v.Headline = L.T("bisect.exhausted");
            v.Detail = L.T("bisect.exhaustedDetail", mgr);
            return v;
        }
        if (p.Status == "stale")
        {
            v.Headline = L.T("bisect.stale");
            v.Detail = L.Cat(L.T("bisect.staleWhy", p.GameBuild, p.Signature.Split('@')[0]), L.P("bisect.staleNext", p.Steps.Count(x => x.Outcome != "pending")));
            return v;
        }
        if (p.Status == "stopped") { v.Headline = L.T("bisect.stopped"); return v; }

        var step = p.Current;
        if (step == null) return v;
        v.Off = step.Off;
        var (turnOff, turnOn) = Verify(p, r);
        v.TurnOff = turnOff; v.TurnOn = turnOn;
        v.Ready = turnOff.Count == 0 && turnOn.Count == 0;
        v.Playing = step.StartedAt != null;

        if (!v.Ready)
        {
            v.Headline = L.P("bisect.setUp", step.CandidatesBefore, step.Index, step.Off.Count);
            v.Detail = L.Cat(turnOff.Count > 0 ? L.P("bisect.stillOn", turnOff.Count) : "",
                             turnOn.Count > 0 ? L.P("bisect.stillOff", turnOn.Count) : "",
                             r.Mods.Manager == ModOrganizer.ManagerName ? L.T("bisect.doItMo2") : L.T("bisect.doIt", mgr));
        }
        else if (!v.Playing)
        {
            v.Headline = L.T("bisect.ready", step.Index);
            v.Detail = L.P("bisect.readyDetail", step.Off.Count, p.MinutesToBeat);
        }
        else
        {
            v.Headline = L.T("bisect.running", step.Index);
            v.Detail = L.P("bisect.runningDetail", step.Off.Count, p.Signature, p.MinutesToBeat);
        }
        return v;
    }

    /// <summary>The mod manager by name, for "turn these off in ..." - or the reader's words for "your mod manager".</summary>
    public static string Manager(Report r) => r.Mods.Manager switch { "Vortex" => "Vortex", ModOrganizer.ManagerName => ModOrganizer.ManagerName, _ => L.T("mgr.manual") };
}

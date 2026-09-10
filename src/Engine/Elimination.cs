using System.Text.RegularExpressions;

namespace CrashDoctor.Engine;

// The elimination layer. See DESIGN.md, "How it reasons".
//
// The single most common mistake in crash diagnosis — and the one this engine made repeatedly before this existed —
// is treating whatever was logged shortly before a crash as its cause. Most of what a modded game logs is constant
// background noise: a mod whose native plugin will not load errors every twelve seconds forever, in sessions that
// end cleanly as well as sessions that die.
//
// So before any suspect is ranked, the same evidence is looked for in sessions that ended with a clean shutdown.
// Anything that also happens when nothing goes wrong cannot be why something went wrong. It gets demoted and
// labelled, never promoted.
//
// This needs a baseline. With fewer than two clean sessions on record nothing may be eliminated, and the engine
// must say so rather than quietly assume innocence.
public sealed class Elimination
{
    public const int MinimumCleanSessions = 2;

    public int CleanSessions { get; private set; }
    public int CrashedSessions { get; private set; }

    // evidence key -> how many clean / crashed sessions it appeared in
    readonly Dictionary<string, (int clean, int crashed)> _byEvidence = new();
    readonly Dictionary<string, (int clean, int crashed)> _byMod = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether there is enough of a clean baseline to rule anything out at all.</summary>
    public bool HasBaseline => CleanSessions >= MinimumCleanSessions;

    public (int clean, int crashed) EvidenceCounts(LogEvent e) => _byEvidence.TryGetValue(KeyOf(e), out var v) ? v : (0, 0);
    public (int clean, int crashed) ModCounts(string mod) => _byMod.TryGetValue(mod, out var v) ? v : (0, 0);

    /// <summary>This exact message also occurs in sessions that ended cleanly, so it is background noise.</summary>
    public bool IsNoise(LogEvent e) => HasBaseline && EvidenceCounts(e).clean > 0;

    /// <summary>Everything this mod logs also occurs in sessions that ended cleanly.</summary>
    public bool IsNoisyMod(string? mod) => mod != null && HasBaseline && ModCounts(mod).clean > 0;

    /// <summary>Plain-language reason a suspect was demoted, or null if it was not.</summary>
    public string? WhyRuledOut(string? mod)
    {
        if (!IsNoisyMod(mod)) return null;
        var (clean, crashed) = ModCounts(mod!);
        return $"It logs the same thing in {clean} session{(clean == 1 ? "" : "s")} that ended perfectly normally"
             + (crashed > 0 ? $", as well as {crashed} that crashed" : "")
             + ". Something that happens when nothing goes wrong cannot be why something went wrong.";
    }

    // Numbers vary between occurrences of the same message (line numbers stay, ids and counts do not), so they are
    // flattened before comparing. Quoted paths are kept: a different file genuinely is a different message.
    static string KeyOf(LogEvent e) => e.Source + "|" + Regex.Replace(e.Text, @"\d+", "#");

    public static Elimination Build(CollectedData d, List<Session> sessions)
    {
        var el = new Elimination();
        // A session with no end time cannot own an event; a running one has not finished proving anything yet.
        var windows = sessions
            .Where(s => s.EndKind is EndKind.Clean or EndKind.Gpu or EndKind.Vram or EndKind.Script or EndKind.Engine or EndKind.Unknown)
            .Where(s => !s.Partial && s.End != null)
            .Select(s => (start: s.Start, end: s.End!.Value, clean: s.EndKind == EndKind.Clean))
            .OrderBy(w => w.start)
            .ToList();
        el.CleanSessions = windows.Count(w => w.clean);
        el.CrashedSessions = windows.Count(w => !w.clean);
        if (windows.Count == 0) return el;

        // Which session does each event belong to? Sessions do not overlap, so a linear sweep in time order is enough.
        var events = d.Events.Where(e => e.Kind is "error" or "xl-error" or "xl-warning").OrderBy(e => e.At).ToList();
        var seenEvidence = new HashSet<(string key, int session)>();
        var seenMod = new HashSet<(string mod, int session)>();
        int w = 0;
        foreach (var e in events)
        {
            while (w < windows.Count && e.At > windows[w].end) w++;
            if (w >= windows.Count) break;
            if (e.At < windows[w].start) continue;              // between sessions; belongs to none
            var clean = windows[w].clean;
            // count each distinct message once per session, so a 12-second repeat does not outvote a one-off
            if (seenEvidence.Add((KeyOf(e), w))) Bump(el._byEvidence, KeyOf(e), clean);
            if (e.Mod != null && seenMod.Add((e.Mod, w))) Bump(el._byMod, e.Mod, clean);
        }
        return el;
    }

    static void Bump(Dictionary<string, (int clean, int crashed)> map, string key, bool clean)
    {
        map.TryGetValue(key, out var v);
        map[key] = clean ? (v.clean + 1, v.crashed) : (v.clean, v.crashed + 1);
    }
}

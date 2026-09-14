using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrashDoctor.Engine;

// The knowledge base: things that are known about what the logs say, kept as data rather than as code.
//
// Rules.cs recognises causes from *structured* facts - a file that should not exist, a number over a threshold, a
// shape in the session history - and each one is a few lines of C#. This is the other half: the far larger set of
// things where the engine or a mod loader has written a specific sentence into a log, and somebody knows what that
// sentence means. There is no logic in those, only explanation, so they live in knowledge\*.json next to the exe
// and can be added to, corrected and shipped without rebuilding anything.
//
// Two hard limits, both from DESIGN.md and both the reason this is a lookup table rather than anything cleverer:
//
//   - An entry explains what a message means. It may not name a mod as the cause. If the evidence names a mod, the
//     evidence has already said so; if it does not, neither does this.
//   - Every entry carries where the knowledge came from, and says whether it is established or merely commonly
//     reported. "Commonly reported" is worth printing and worth labelling as exactly that.
//
// Matching is deliberately narrow: only against the evidence attached to a crash and that crash's own fault and
// exception. Matching against every line in every log would fire on healthy installs, which is the one thing
// nothing in this app is allowed to do.
public sealed class KnowledgeEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Says { get; set; } = "";            // what the message means, in plain language
    public string? Do { get; set; }                    // what to do about it, when there is something to do
    public string Source { get; set; } = "";           // where this knowledge came from - always stated
    public string Confidence { get; set; } = "likely"; // established | likely
    public KnowledgeMatch Match { get; set; } = new();
}

public sealed class KnowledgeMatch
{
    public List<string> Log { get; set; } = new();        // regex, matched against evidence text
    public List<string> Fault { get; set; } = new();      // "Cyberpunk2077.exe+0x2A41E06", substring
    public List<string> Exception { get; set; } = new();  // "EXCEPTION_STACK_OVERFLOW", substring
}

public sealed class KnowledgeHit
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Says { get; set; } = "";
    public string? Do { get; set; }
    public string Source { get; set; } = "";
    public string Confidence { get; set; } = "likely";
    public string Matched { get; set; } = "";   // the line that brought it up, so the reader can judge it
    public int Crashes { get; set; }            // how many crashes it matched
}

public static class Knowledge
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "knowledge");

    static List<KnowledgeEntry>? _all;
    static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Every entry from every file in knowledge\. A broken file is skipped, never fatal.</summary>
    public static List<KnowledgeEntry> All()
    {
        if (_all != null) return _all;
        var list = new List<KnowledgeEntry>();
        try
        {
            if (Directory.Exists(Dir))
                foreach (var f in Directory.EnumerateFiles(Dir, "*.json").OrderBy(x => x))
                {
                    try { list.AddRange(JsonSerializer.Deserialize<List<KnowledgeEntry>>(Files.ReadAllTextShared(f), Opts) ?? new()); }
                    catch { /* one malformed file must not cost the rest */ }
                }
        }
        catch { }
        return _all = list.Where(e => e.Id.Length > 0 && e.Says.Length > 0).ToList();
    }

    public static List<KnowledgeHit> Look(Report r)
    {
        var entries = All();
        if (entries.Count == 0) return new();
        var crashes = r.Sessions.Where(s => s.EndKind is not (EndKind.Clean or EndKind.Running)).ToList();
        if (crashes.Count == 0) return new();

        var hits = new Dictionary<string, KnowledgeHit>();
        foreach (var e in entries)
        {
            var regexes = new List<Regex>();
            foreach (var p in e.Match.Log)
            {
                try { regexes.Add(new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)); }
                catch { /* a bad pattern in a data file is a typo, not a crash */ }
            }

            foreach (var s in crashes)
            {
                string? matched = null;
                if (e.Match.Fault.Count > 0 && s.Fault != null && e.Match.Fault.Any(f => s.Fault.Contains(f, StringComparison.OrdinalIgnoreCase)))
                    matched = s.Fault;
                if (matched == null && e.Match.Exception.Count > 0 && s.Exception != null && e.Match.Exception.Any(x => s.Exception.Contains(x, StringComparison.OrdinalIgnoreCase)))
                    matched = s.Exception;
                if (matched == null && regexes.Count > 0)
                    matched = s.Evidence.Select(ev => ev.Text).FirstOrDefault(t => regexes.Any(rx => rx.IsMatch(t)));
                if (matched == null) continue;

                if (hits.TryGetValue(e.Id, out var h)) h.Crashes++;
                else hits[e.Id] = new KnowledgeHit
                {
                    Id = e.Id, Title = e.Title, Says = e.Says, Do = e.Do, Source = e.Source,
                    Confidence = e.Confidence is "established" ? "established" : "likely",
                    Matched = matched.Length > 160 ? matched[..160] + "…" : matched,
                    Crashes = 1,
                };
            }
        }
        // most-seen first; an entry that matches ten crashes is more use than one that matches a single old one
        return hits.Values.OrderByDescending(h => h.Crashes).ThenBy(h => h.Title).ToList();
    }
}

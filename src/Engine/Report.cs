using System.Text.Json.Serialization;

namespace CrashDoctor.Engine;

// The data contract between the engine and the interface (ui/index.html). Property names are camelCase on the wire.

public sealed class Report
{
    /// <summary>
    /// Which build produced this report, e.g. "0.5.1+745f2d4...". The support model is "post the saved report",
    /// and without this there is no way to tell whether a report came from the current version or one from three
    /// releases ago. It cannot be added retroactively: every report made before this existed is unattributable.
    /// </summary>
    public string AppVersion { get; set; } = App.Version;
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public GameInfo Game { get; set; } = new();
    public SystemInfo System { get; set; } = new();
    public ModsSummary Mods { get; set; } = new();
    public Session? Latest { get; set; }
    public List<Session> Sessions { get; set; } = new();
    public List<HealthItem> Health { get; set; } = new();
    public List<CrashGroup> CrashGroups { get; set; } = new();   // the distinct bugs behind all the crashes
    public BisectView? Bisect { get; set; }                      // the guided hunt, when one is running
    public List<ModRow> ModsList { get; set; } = new();
    public List<Requirement> Requirements { get; set; } = new();
    public Dictionary<string, string> Settings { get; set; } = new();
    public List<SettingsNote> SettingsNotes { get; set; } = new();
    public List<string> Warnings { get; set; } = new();   // things the scan could not read
    public string? Privacy { get; set; }                 // what was removed from a saved copy, shown in the page
}

// All the crashes that faulted at one identical instruction. This is what turns a pile of crashes into a small
// number of separate problems, each of which can be chased, fixed or ruled out on its own.
public sealed class CrashGroup
{
    public string Signature { get; set; } = "";   // includes the build; two builds are two groups, deliberately
    public string Fault { get; set; } = "";       // the readable half
    public string? Build { get; set; }
    public int OtherBuilds { get; set; }          // same offset seen under this many other builds
    public string? BuildNote { get; set; }
    public string Exception { get; set; } = "";
    public string Plain { get; set; } = "";
    public int Count { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public bool Current { get; set; }             // seen since the most recent clean session; still happening
    public string? Injector { get; set; }
    public int InjectorSessions { get; set; }     // how many of Count had it, so the correlation is visible
    public List<string> SessionIds { get; set; } = new();
}

// Everything the interface needs to render a bisect run. All the wording is decided in the engine so the reasoning
// and the words describing it cannot drift apart.
public sealed class BisectView
{
    public string Id { get; set; } = "";
    public string Signature { get; set; } = "";
    public string Status { get; set; } = "running";
    public string Headline { get; set; } = "";
    public string Detail { get; set; } = "";
    public int MinutesToBeat { get; set; }
    public int Candidates { get; set; }
    public int Cleared { get; set; }
    public int StepNumber { get; set; }
    public int StepsLeft { get; set; }
    public bool Ready { get; set; }        // what is deployed matches what the step asked for
    public bool Playing { get; set; }      // the step is set up and waiting on a play session
    public string? Culprit { get; set; }
    public List<string> Off { get; set; } = new();       // the mods this step wants disabled
    public List<string> TurnOff { get; set; } = new();   // of those, still enabled
    public List<string> TurnOn { get; set; } = new();    // wrongly disabled, would spoil the test
    public List<string> History { get; set; } = new();
}

public sealed class GameInfo
{
    public string Name { get; set; } = "Cyberpunk 2077";
    public string Version { get; set; } = "";
    public string Store { get; set; } = "";
    public string Path { get; set; } = "";
    public string FileVersion { get; set; } = "";
    public bool Running { get; set; }
}

public sealed class SystemInfo
{
    public string Gpu { get; set; } = "";
    public int VramGB { get; set; }
    public string Driver { get; set; } = "";
    public DateTime? DriverDate { get; set; }
    public int RamGB { get; set; }
    public string Os { get; set; } = "";
    public string Machine { get; set; } = "";
    public bool Laptop { get; set; }
}

public sealed class ModsSummary
{
    public int Installed { get; set; }
    public int Enabled { get; set; }
    public string Manager { get; set; } = "manual";
    public List<string> Frameworks { get; set; } = new();
}

public enum EndKind { Clean, Gpu, Vram, Script, Engine, Unknown, Running }

public sealed class Session
{
    public string Id { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime? End { get; set; }
    public double Minutes { get; set; }
    public EndKind EndKind { get; set; }   // serialized camelCase by Scanner.Json: clean | gpu | vram | script | engine | unknown | running
    public string Verdict { get; set; } = "";
    public int Confidence { get; set; }          // 0..3
    public bool Partial { get; set; }            // only a crash report exists; the session log has been rotated away
    public bool DurationKnown { get; set; } = true;
    public string? District { get; set; }
    public string? Quest { get; set; }
    public List<Evidence> Evidence { get; set; } = new();
    public List<Suspect> Suspects { get; set; } = new();
    public Dictionary<string, string> SettingsAtCrash { get; set; } = new();
    // From the crash report folder the engine writes next to every crash (see CrashReports.cs).
    public int? VramUsedMB { get; set; }
    public int? VramTotalMB { get; set; }
    public string? Exception { get; set; }        // e.g. "EXCEPTION_ACCESS_VIOLATION (0xC0000005)"
    // From the minidump. Same signature means the same bug, however different the logs around it look.
    public string? Signature { get; set; }        // identity, build included: "Cyberpunk2077.exe+0x2A41E06@3.0.80.51928"
    public string? Fault { get; set; }            // the readable half: "Cyberpunk2077.exe+0x2A41E06"
    public string? Build { get; set; }            // build of the faulting module the offset was measured in
    public string? FaultingModule { get; set; }
    public string? Injector { get; set; }         // a trainer / cheat tool found inside the game process
    public string? Position { get; set; }         // where the player was standing
    public string? Screenshot { get; set; }       // the frame the game was showing when it died
    public List<AreaMod> AreaMods { get; set; } = new();  // mods rewriting the ground the player was on
    public int AreaSectors { get; set; }                  // distinct map sectors that streamed in around them
    public List<RuledOut> RuledOut { get; set; } = new();  // looked suspicious, eliminated by the clean sessions
    [JsonIgnore] public string? Red4extLog { get; set; }
    [JsonIgnore] public DateTime? CrashTime { get; set; }
    [JsonIgnore] public string? EngineMessage { get; set; }
    [JsonIgnore] public string? CrashFile { get; set; }
}

// Something that looked like a lead and was eliminated because it also happens in sessions that end cleanly.
// Showing this is half the value: knowing what is NOT the cause is what stops people disabling mods at random.
public sealed class RuledOut
{
    public string Mod { get; set; } = "";
    public string Why { get; set; } = "";
}

// One mod that was patching the map sectors streaming in around the player when the game died.
public sealed class AreaMod
{
    public string Mod { get; set; } = "";
    public string File { get; set; } = "";        // the .xl doing the patching
    public int Sectors { get; set; }              // how many of the sectors here it rewrites
    public int SharedWith { get; set; }           // how many other mods edit at least one of the same sectors
    public bool Failing { get; set; }             // ArchiveXL reported its patch did not fit this session
    public string? Note { get; set; }
}

public sealed class Evidence
{
    public int TMinus { get; set; }              // seconds before the crash (0 = at the crash)
    public string Source { get; set; } = "";     // e.g. "CET · DualSense Support"
    public string Text { get; set; } = "";
    public bool Hit { get; set; }                // highlighted as the key clue
    [JsonIgnore] public DateTime At { get; set; }
    [JsonIgnore] public string? Mod { get; set; }
}

public sealed class Suspect
{
    public string Mod { get; set; } = "";
    public string Level { get; set; } = "medium"; // high | medium | low
    public string Why { get; set; } = "";
    public List<UiAction> Actions { get; set; } = new();
}

public sealed class UiAction
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public UiAction() { }
    public UiAction(string id, string label) { Id = id; Label = label; }
}

public sealed class HealthItem
{
    public string Severity { get; set; } = "info"; // high | medium | low | info
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string? Mod { get; set; }
    /// <summary>
    /// Two or three words for the chip in the mods table, e.g. "plugin not loading". The title is a sentence and
    /// belongs in the Health list; squeezed into a table cell it truncates to nonsense. Any item that names a Mod
    /// should set this; without it the table falls back to a cut-off title.
    /// </summary>
    public string? Flag { get; set; }
    public UiAction? Action { get; set; }
}

public sealed class ModRow
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Type { get; set; } = "";
    public string Status { get; set; } = "enabled";
    public string? NexusId { get; set; }
    public List<ModFlag> Flags { get; set; } = new();
    [JsonIgnore] public string Folder { get; set; } = "";
    [JsonIgnore] public List<string> Files { get; set; } = new();
}

public sealed class ModFlag
{
    public string Label { get; set; } = "";
    public string Level { get; set; } = "low";
    public string? Detail { get; set; }
    public string? SessionId { get; set; }   // the crash this came from, so the flag can be opened
    public string? When { get; set; }        // human date of that crash
}

public sealed class Requirement
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Version { get; set; }
    public string Status { get; set; } = "unknown"; // ok | missing | outdated | optional | unknown
    public string Detail { get; set; } = "";
    public List<string> NeededBy { get; set; } = new();
    public int NeededByCount { get; set; }        // kept when the names are withheld from a saved copy
    public bool DirectInstall { get; set; }
    public string? Url { get; set; }
}

public sealed class SettingsNote
{
    public string Level { get; set; } = "low";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
}

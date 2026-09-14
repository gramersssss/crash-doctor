using System.Linq;
using CrashDoctor.Engine;

namespace CrashDoctor;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Headless mode for testing and scripting:
        //   CrashDoctor.exe --json report.json [--html report.html] [--game "C:\path"] [--no-mods]   (any order)
        // --no-mods leaves the mod list out of the saved HTML, the same choice the window offers when saving.
        // This used to require --json or --html to be the FIRST argument; putting --game first silently opened the
        // window instead, which looks exactly like the scan hanging and writing nothing.
        // Graphics profiles from the command line, so a launcher can switch quality before starting the game:
        //   CrashDoctor.exe --profiles | --profile-save "Name" | --profile-apply "Name" | --profile-undo
        // Applying and undoing refuse while the game is running, back up first, and read the result back.
        if (args.Any(a => a.StartsWith("--profile")))
        {
            string? Arg(string flag) { for (int i = 0; i < args.Length - 1; i++) if (args[i] == flag) return args[i + 1]; return null; }
            ProfileResult? res = null;
            if (args.Contains("--profiles"))
            {
                var cur = GraphicsProfiles.Current();
                foreach (var p in GraphicsProfiles.Summaries(cur))
                    Console.WriteLine($"{p.Name}{(p.MatchesGame ? "  (active)" : "")}  -  " + string.Join(", ", p.Headline.Select(kv => $"{kv.Key} {kv.Value}")));
                Console.WriteLine($"{cur.Count} graphics options in the game's settings file, {cur.Count(o => o.Editable)} editable.");
                Environment.Exit(0);
            }
            if (Arg("--profile-save") is { } save) res = GraphicsProfiles.SaveCurrent(save);
            else if (Arg("--profile-apply") is { } apply) res = GraphicsProfiles.Apply(apply);
            else if (args.Contains("--profile-undo")) res = GraphicsProfiles.Undo();
            else if (Array.IndexOf(args, "--profile-set") is var si and >= 0 && si + 3 < args.Length)
            {
                // --profile-set "Name" TextureQuality High   (setting by its short name; value as the game spells it)
                var key = GraphicsProfiles.Current().FirstOrDefault(o => o.Key.EndsWith("/" + args[si + 2], StringComparison.OrdinalIgnoreCase))?.Key ?? args[si + 2];
                System.Text.Json.Nodes.JsonNode? val;
                try { val = System.Text.Json.Nodes.JsonNode.Parse(args[si + 3]); } catch { val = System.Text.Json.Nodes.JsonValue.Create(args[si + 3]); }
                res = GraphicsProfiles.Update(args[si + 1], new() { [key] = val });
            }
            if (res == null) { Console.Error.WriteLine("Use --profiles, --profile-save \"Name\", --profile-set \"Name\" Setting Value, --profile-apply \"Name\" or --profile-undo."); return 2; }
            (res.Ok ? Console.Out : Console.Error).WriteLine(res.Message);
            Console.Out.Flush();
            Environment.Exit(res.Ok ? 0 : 1);
        }

        // One reading of the graphics card's memory, the way the recorder takes it. This is what to ask someone with an
        // AMD or Intel card to run and paste: it says which adapter was picked, how big it is, and whether the counters
        // answered at all, without anyone having to play first.
        if (args.Contains("--vram-probe"))
        {
            foreach (var a in GpuAdapters.List()) Console.WriteLine($"adapter  {a.Name}  {a.DedicatedMB} MB  {a.LuidKey}");
            int? pid = null; try { pid = System.Diagnostics.Process.GetProcessesByName("Cyberpunk2077").FirstOrDefault()?.Id; } catch { }
            var s = GpuCounters.Read(pid);
            if (s == null) { Console.Error.WriteLine("counters: " + GpuCounters.LastProblem); Console.Error.Flush(); Environment.Exit(1); }
            Console.WriteLine($"reading  {s.Adapter}: {s.CardMB} of {s.TotalMB} MB in use, {s.SharedMB} MB shared" + (pid != null ? $", the game {s.GameMB} MB (pid {pid})" : ", game not running"));
            Console.Out.Flush();
            Environment.Exit(0);
        }

        if (args.Any(a => a == "--json" || a == "--html"))
        {
            try
            {
                string? Value(string flag) { for (int i = 0; i < args.Length - 1; i++) if (args[i] == flag) return args[i + 1]; return null; }
                var gamePath = Value("--game");
                if (gamePath != null && !GameLocator.IsGameDir(gamePath)) { Console.Error.WriteLine("--game " + gamePath + " does not contain bin" + Path.DirectorySeparatorChar + "x64" + Path.DirectorySeparatorChar + "Cyberpunk2077.exe."); return 2; }
                var g = gamePath != null ? new GamePaths { GameDir = gamePath, Store = "manual" } : GameLocator.Locate();
                if (g == null) { Console.Error.WriteLine("Cyberpunk 2077 not found. Use --game <folder>."); return 2; }
                var dbg = Environment.GetEnvironmentVariable("CRASHDOCTOR_TIMING") == "1";
                void Say(string m) { if (dbg) Console.Error.WriteLine("  [cli] " + m); }
                Say("scan starting");
                var r = Scanner.Scan(g);
                Say("scan returned");
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == "--json") { Say("serialising"); var json = System.Text.Json.JsonSerializer.Serialize(r, new System.Text.Json.JsonSerializerOptions(Scanner.Json) { WriteIndented = true }); Say($"serialised {json.Length} chars, writing"); File.WriteAllText(args[i + 1], json); Say("written"); }
                    if (args[i] == "--html") { Say("exporting html"); HtmlExporter.Save(r, args[i + 1], !args.Contains("--no-mods")); Say("html written"); }
                }
                Console.WriteLine($"Scanned {g.GameDir}: {r.Sessions.Count} sessions, latest verdict: {r.Latest?.Verdict ?? "(no crash)"}");
                Console.Out.Flush();
                // A command-line run has finished the moment its output is written. Returning normally waits on
                // whatever non-background threads the WMI and event-log readers left behind, which can idle for
                // minutes; nothing useful happens in that time, so leave immediately.
                Say("done, exiting");
                Environment.Exit(0);
                return 0;
            }
            catch (Exception ex)
            {
                try { Directory.CreateDirectory(GamePaths.AppData); File.WriteAllText(Path.Combine(GamePaths.AppData, "last-cli-error.log"), ex.ToString()); } catch { }
                Console.Error.WriteLine(ex); return 1;
            }
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}

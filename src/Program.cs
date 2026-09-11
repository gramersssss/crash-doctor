using System.Linq;
using CrashDoctor.Engine;

namespace CrashDoctor;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Headless mode for testing and scripting:
        //   CrashDoctor.exe --json report.json [--html report.html] [--game "C:\path"]   (any order)
        // This used to require --json or --html to be the FIRST argument; putting --game first silently opened the
        // window instead, which looks exactly like the scan hanging and writing nothing.
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
                    if (args[i] == "--html") { Say("exporting html"); HtmlExporter.Save(r, args[i + 1]); Say("html written"); }
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

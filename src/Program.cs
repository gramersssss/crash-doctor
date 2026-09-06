using CrashDoctor.Engine;

namespace CrashDoctor;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // headless mode for testing and scripting:  CrashDoctor.exe --json report.json [--game "C:\path"] [--html report.html]
        if (args.Length > 0 && (args[0] == "--json" || args[0] == "--html"))
        {
            try
            {
                string? gamePath = null; for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--game") gamePath = args[i + 1];
                var g = gamePath != null ? new GamePaths { GameDir = gamePath, Store = "manual" } : GameLocator.Locate();
                if (g == null) { Console.Error.WriteLine("Cyberpunk 2077 not found. Use --game <folder>."); return 2; }
                var r = Scanner.Scan(g);
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == "--json") File.WriteAllText(args[i + 1], System.Text.Json.JsonSerializer.Serialize(r, new System.Text.Json.JsonSerializerOptions(Scanner.Json) { WriteIndented = true }));
                    if (args[i] == "--html") HtmlExporter.Save(r, args[i + 1]);
                }
                Console.WriteLine($"Scanned {g.GameDir}: {r.Sessions.Count} sessions, latest verdict: {r.Latest?.Verdict ?? "(no crash)"}");
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

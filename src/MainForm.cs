using System.Diagnostics;
using System.Text.Json;
using CrashDoctor.Engine;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CrashDoctor;

public sealed class MainForm : Form
{
    readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    GamePaths? _game;
    Report? _report;
    bool _scanning;

    public MainForm()
    {
        Text = "Crash Doctor for Cyberpunk 2077";
        Width = 1280; Height = 820; MinimumSize = new Size(900, 600); StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(0xED, 0xF0, 0xF3);
        try { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "app.ico")); } catch { }
        Controls.Add(_web);
        Load += async (_, _) => await InitAsync();
    }

    async Task InitAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(GamePaths.AppData, "webview"));
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = Environment.GetEnvironmentVariable("CRASHDOCTOR_DEVTOOLS") == "1";
            core.SetVirtualHostNameToFolderMapping("app.crashdoctor", HtmlExporter.UiDir, CoreWebView2HostResourceAccessKind.Allow);
            core.WebMessageReceived += OnMessage;
            core.NewWindowRequested += (_, e) => { e.Handled = true; OpenUrl(e.Uri); };
            core.Navigate("https://app.crashdoctor/index.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Crash Doctor needs the Microsoft WebView2 runtime to draw its window.\n\n" + ex.Message + "\n\nInstall it from the Microsoft site and start Crash Doctor again.", "Crash Doctor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            OpenUrl(RequirementsCheck.WebView2Url);
        }
    }

    void Post(object o) => _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(o, Scanner.Json));

    async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonDocument doc; try { doc = JsonDocument.Parse(e.WebMessageAsJson); } catch { return; }
        var cmd = doc.RootElement.TryGetProperty("cmd", out var c) ? c.GetString() : null;
        switch (cmd)
        {
            case "ready": await ScanAsync(); break;
            case "scan": await ScanAsync(); break;
            case "export": Export(); break;
            case "action": await ActionAsync(doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() ?? "" : ""); break;
        }
    }

    async Task ScanAsync()
    {
        if (_scanning) return; _scanning = true;
        try
        {
            _game ??= GameLocator.Locate();
            if (_game == null)
            {
                Post(new { cmd = "error", text = "Cyberpunk 2077 was not found in Steam, GOG or Epic. Pick the game folder (the one that contains bin\\x64\\Cyberpunk2077.exe)." });
                if (!PickGameFolder()) { Post(new { cmd = "noscan" }); return; }
            }
            var g = _game!;
            _report = await Task.Run(() => Scanner.Scan(g));
            Post(new { cmd = "report", report = _report });
        }
        catch (Exception ex) { Post(new { cmd = "error", text = "The scan failed: " + ex.Message }); }
        finally { _scanning = false; }
    }

    bool PickGameFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Select the Cyberpunk 2077 game folder", UseDescriptionForTitle = true, ShowNewFolderButton = false };
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        if (!GameLocator.IsGameDir(dlg.SelectedPath)) { MessageBox.Show(this, "That folder does not contain bin\\x64\\Cyberpunk2077.exe.", "Crash Doctor"); return false; }
        _game = new GamePaths { GameDir = dlg.SelectedPath, Store = "manual" };
        GameLocator.SaveConfig(new GameLocator.Config { GamePath = dlg.SelectedPath });
        return true;
    }

    void Export()
    {
        if (_report == null) return;
        using var dlg = new SaveFileDialog { Filter = "HTML report|*.html", FileName = $"CrashDoctor-report-{DateTime.Now:yyyyMMdd-HHmm}.html", Title = "Save the full report" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { HtmlExporter.Save(_report, dlg.FileName); Post(new { cmd = "toast", text = "Report saved." }); OpenUrl(dlg.FileName); }
        catch (Exception ex) { Post(new { cmd = "error", text = "Could not save the report: " + ex.Message }); }
    }

    async Task ActionAsync(string id)
    {
        var g = _game;
        if (id.StartsWith("nexus:")) { OpenUrl("https://www.nexusmods.com/cyberpunk2077/mods/" + id[6..]); return; }
        if (id == "settings") { Post(new { cmd = "go", view = "settings" }); return; }
        if (id == "open:driver") { Run("devmgmt.msc"); return; }
        if (id == "open:redscript" && g != null) { OpenUrl(Path.Combine(g.RedscriptLogs, "redscript_rCURRENT.log")); return; }
        if (id == "open:gamefolder" && g != null) { OpenUrl(g.GameDir); return; }
        if (id == "pickgame") { if (PickGameFolder()) await ScanAsync(); return; }
        if (id.StartsWith("fix:") && g != null)
        {
            var fix = Fixes.ById(id[4..]); if (fix == null) return;
            var applied = fix.Applied(g);
            var q = applied ? $"Revert the fix \"{fix.Title}\"?\n\nThe mod's original file will be put back." : $"Apply the fix \"{fix.Title}\" to {fix.Mod}?\n\n{fix.Why}\n\nThe original file is backed up first and can be reverted with one click.";
            if (MessageBox.Show(this, q, "Crash Doctor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var msg = applied ? Fixes.Revert(fix, g) : Fixes.Apply(fix, g);
            Post(new { cmd = "toast", text = msg });
            await ScanAsync();
            return;
        }
        if (id.StartsWith("req:") && _report != null)
        {
            var reqs = _report.Requirements;
            if (id == "req:open-missing") { foreach (var r in reqs.Where(r => r.Status is "missing" or "outdated" && !r.DirectInstall && r.Url != null)) OpenUrl(r.Url!); return; }
            if (id == "req:install-missing") { foreach (var r in reqs.Where(r => r.Status is "missing" or "outdated" && r.DirectInstall && r.Url != null)) await InstallAsync(r); await ScanAsync(); return; }
            if (id.StartsWith("req:open:")) { var r = reqs.FirstOrDefault(x => x.Id == id[9..]); if (r?.Url != null) OpenUrl(r.Url); return; }
            if (id.StartsWith("req:install:")) { var r = reqs.FirstOrDefault(x => x.Id == id[12..]); if (r != null) { await InstallAsync(r); await ScanAsync(); } return; }
        }
    }

    async Task InstallAsync(Requirement r)
    {
        if (r.Url == null) return;
        if (MessageBox.Show(this, $"Download and run the installer for {r.Name} from Microsoft?\n\n{r.Url}", "Crash Doctor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            Post(new { cmd = "toast", text = $"Downloading {r.Name}…" });
            var tmp = Path.Combine(Path.GetTempPath(), $"crashdoctor-{r.Id}.exe");
            using (var http = new HttpClient()) { var bytes = await http.GetByteArrayAsync(r.Url); await File.WriteAllBytesAsync(tmp, bytes); }
            var p = Process.Start(new ProcessStartInfo(tmp) { UseShellExecute = true });
            if (p != null) await p.WaitForExitAsync();
            Post(new { cmd = "toast", text = $"{r.Name} installer finished." });
        }
        catch (Exception ex) { Post(new { cmd = "error", text = $"Could not install {r.Name}: {ex.Message}. You can install it by hand from {r.Url}" }); }
    }

    static void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
    static void Run(string cmd) { try { Process.Start(new ProcessStartInfo(cmd) { UseShellExecute = true }); } catch { } }
}

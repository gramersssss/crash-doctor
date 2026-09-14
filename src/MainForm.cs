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
        // Take the icon out of our own exe, where ApplicationIcon has already embedded it. Reading app.ico from
        // beside the exe used to be the only attempt, and it never worked: that file is not part of the build
        // output or of what build.ps1 ships, so the window quietly wore the default WinForms icon.
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath); } catch { }
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
            case "theme": ApplyWindowTheme(doc.RootElement); break;
            case "profile": ProfileAction(doc.RootElement); break;
            case "archive":
                // switching crash-log keeping on or off; the next scan starts or stops saving
                var cfg = GameLocator.LoadConfig();
                cfg.KeepCrashLogs = doc.RootElement.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True;
                GameLocator.SaveConfig(cfg);
                if (_report != null) _report.Archive = CrashArchive.Summary();
                Post(new { cmd = "archive", archive = _report?.Archive ?? CrashArchive.Summary() });
                break;
        }
    }

    // Graphics profiles. Saving, editing and deleting only touch Crash Doctor's own files; applying and undoing write
    // the game's settings file, so those two ask first - the page already told the user what will happen, but a
    // write to something the game owns gets a second, native confirmation that cannot be clicked through by accident.
    void ProfileAction(JsonElement m)
    {
        string S(string k) => m.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        var op = S("op"); var name = S("name");
        ProfileResult res;
        try
        {
            switch (op)
            {
                case "save": res = GraphicsProfiles.SaveCurrent(name); break;
                case "delete":
                    if (MessageBox.Show(this, $"Delete the profile \"{name}\"?\n\nThe game's current settings are not affected.", "Crash Doctor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    res = GraphicsProfiles.Delete(name); break;
                case "update":
                    var edits = new Dictionary<string, System.Text.Json.Nodes.JsonNode?>();
                    if (m.TryGetProperty("edits", out var e) && e.ValueKind == JsonValueKind.Object)
                        foreach (var p in e.EnumerateObject()) edits[p.Name] = System.Text.Json.Nodes.JsonNode.Parse(p.Value.GetRawText());
                    res = GraphicsProfiles.Update(name, edits, S("newName"), m.TryGetProperty("notes", out var n) ? n.GetString() : null); break;
                case "apply":
                    if (MessageBox.Show(this, $"Put the \"{name}\" graphics settings into the game?\n\nYour current settings are backed up first, and Undo puts them back. Only graphics and display settings change; controls, audio and key bindings are left alone.", "Crash Doctor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    res = GraphicsProfiles.Apply(name); break;
                case "undo":
                    if (MessageBox.Show(this, "Put back the game's graphics settings from before the last profile was applied?", "Crash Doctor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    res = GraphicsProfiles.Undo(); break;
                default: return;
            }
        }
        catch (Exception ex) { res = new ProfileResult { Ok = false, Message = "That did not work: " + ex.Message }; }

        if (_report != null) Scanner.RefreshProfiles(_report);
        Post(new { cmd = "profiles", ok = res.Ok, message = res.Message, graphics = _report?.Graphics, profiles = _report?.Profiles, settingsBackup = _report?.SettingsBackup });
    }

    // The page picks the theme; the window around it has to follow, or a dark theme sits inside a white title bar
    // and flashes the light background while resizing. Windows 11 lets the caption take the header's own colour;
    // Windows 10 only offers dark or light, and ignores the colour call, which is fine.
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    const int DwmUseImmersiveDarkMode = 20, DwmCaptionColor = 35;

    void ApplyWindowTheme(JsonElement m)
    {
        try
        {
            var dark = m.TryGetProperty("dark", out var d) && d.ValueKind == JsonValueKind.True;
            if (m.TryGetProperty("paper", out var p) && TryColor(p.GetString(), out var paper)) BackColor = paper;
            var on = dark ? 1 : 0;
            DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkMode, ref on, sizeof(int));
            if (m.TryGetProperty("caption", out var cap) && TryColor(cap.GetString(), out var cc))
            {
                var colorref = cc.R | (cc.G << 8) | (cc.B << 16);   // COLORREF is 0x00BBGGRR
                DwmSetWindowAttribute(Handle, DwmCaptionColor, ref colorref, sizeof(int));
            }
        }
        catch { /* cosmetic; never worth an error */ }
    }

    static bool TryColor(string? hex, out Color c)
    {
        c = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v)) return false;
        c = Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
        return true;
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
        var keep = GameLocator.LoadConfig(); keep.GamePath = dlg.SelectedPath; GameLocator.SaveConfig(keep);
        return true;
    }

    void Export()
    {
        if (_report == null) return;
        // Saved reports get pasted into forum threads, so whether the mod list travels with it is the author's call.
        // Stated plainly and without characterising what is in the list.
        var includeMods = true;
        if (_report.ModsList.Count > 0)
        {
            var q = MessageBox.Show(this,
                $"Include your mod list in the saved report?" + Environment.NewLine + Environment.NewLine +
                $"It names all {_report.ModsList.Count} of your mods. That is usually what makes a report worth reading, but saved reports often get posted publicly, so it is worth deciding on purpose." + Environment.NewLine + Environment.NewLine +
                "Leaving it out also removes mod names from the requirements and area panels. The handful of mods the diagnosis actually names still appear, because naming them is the diagnosis." + Environment.NewLine + Environment.NewLine +
                "Yes - include the mod list.   No - leave it out.   Cancel - do not save.",
                "Save report", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (q == DialogResult.Cancel) return;
            includeMods = q == DialogResult.Yes;
        }
        using var dlg = new SaveFileDialog { Filter = "HTML report|*.html", FileName = $"CrashDoctor-report-{DateTime.Now:yyyyMMdd-HHmm}.html", Title = "Save the full report" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { HtmlExporter.Save(_report, dlg.FileName, includeMods); Post(new { cmd = "toast", text = includeMods ? "Report saved." : "Report saved without the mod list." }); OpenUrl(dlg.FileName); }
        catch (Exception ex) { Post(new { cmd = "error", text = "Could not save the report: " + ex.Message }); }
    }

    async Task ActionAsync(string id)
    {
        var g = _game;
        if (id.StartsWith("nexus:")) { OpenUrl("https://www.nexusmods.com/cyberpunk2077/mods/" + id[6..]); return; }
        // Plain view jumps. "mods" was being emitted as an action id before anything handled it, so "See the mod
        // list" was a button that did nothing; listing the views here means a new rule can point at any of them.
        if (id is "settings" or "mods" or "health" or "sessions" or "bisect" or "req") { Post(new { cmd = "go", view = id }); return; }
        if (id == "open:driver") { Run("devmgmt.msc"); return; }
        if (id == "open:redscript" && g != null) { OpenUrl(Path.Combine(g.RedscriptLogs, "redscript_rCURRENT.log")); return; }
        if (id == "open:gamefolder" && g != null) { OpenUrl(g.GameDir); return; }
        if (id == "open:crashlogs") { Directory.CreateDirectory(CrashArchive.Folder); OpenUrl(CrashArchive.Folder); return; }
        if (id.StartsWith("open:crashlog:"))
        {
            var e = CrashArchive.Index().FirstOrDefault(x => x.SessionId == id["open:crashlog:".Length..]);
            var zip = e == null ? null : Path.Combine(CrashArchive.Folder, e.Zip);
            if (zip != null && File.Exists(zip)) Run("explorer.exe", "/select,\"" + zip + "\""); else OpenUrl(CrashArchive.Folder);
            return;
        }
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
        if (id.StartsWith("bisect:") && _report != null)
        {
            if (id.StartsWith("bisect:start:"))
            {
                var sig = id["bisect:start:".Length..];
                var n = _report.ModsList.Count(m => m.Status == "enabled" && !Bisect.IsFramework(m.Name));
                var steps = n > 1 ? (int)Math.Ceiling(Math.Log2(n)) : 1;
                if (MessageBox.Show(this,
                        $"Hunt {sig} by bisection?" + Environment.NewLine + Environment.NewLine + "Crash Doctor will ask you to switch groups of mods off in Vortex, play, and scan again. It never changes anything itself - it only checks what is actually deployed and reads the result from your logs." + Environment.NewLine + Environment.NewLine + "{n} mods are in the running, so expect about {steps} rounds of play.",
                        "Crash Doctor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                var d = Collectors.CollectAll(_game!, DateTime.Now.AddDays(-30));
                Bisect.Start(d, _report, sig);
                await ScanAsync();
                Post(new { cmd = "go", view = "bisect" });
                return;
            }
            if (id == "bisect:stop")
            {
                if (MessageBox.Show(this, "Stop the bisect and forget its progress?" + Environment.NewLine + Environment.NewLine + "Your mods are left exactly as they are now - turn the ones you switched off back on in Vortex when you are ready.", "Crash Doctor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Bisect.Save(null);
                await ScanAsync();
                return;
            }
            if (id == "bisect:copy")
            {
                var v = _report.Bisect;
                if (v != null && v.Off.Count > 0) { try { Clipboard.SetText(string.Join(Environment.NewLine, v.Off)); Post(new { cmd = "toast", text = $"{v.Off.Count} mod names copied - paste into Vortex's search to find them." }); } catch { } }
                return;
            }
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
    static void Run(string cmd, string? args = null) { try { Process.Start(new ProcessStartInfo(cmd) { UseShellExecute = true, Arguments = args ?? "" }); } catch { } }
}

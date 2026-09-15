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

    // Video memory is recorded only while this window exists (see VramMonitor.cs): a timer looks for the game every
    // few seconds, appends a reading while it runs, and when it goes away waits for the crash reporter to finish
    // writing and then rescans, so the session that just ended is on screen without anyone pressing anything.
    readonly VramRecorder _vram = new();
    readonly System.Windows.Forms.Timer _vramTimer = new() { Interval = VramRecorder.IntervalSeconds * 1000 };
    bool _vramBusy;
    DateTime? _gameEndedAt;
    Launcher.Play? _play;        // a game started by "play with this profile", whose settings go back when it closes
    const int RescanDelaySeconds = 25;

    public MainForm()
    {
        _vramTimer.Tick += async (_, _) => await VramTickAsync();
        _vramTimer.Start();
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
            case "seen":
                // the user dismissed the new-crash notice or opened the crash: everything up to the newest crash is seen
                if (_report != null && NewestCrash(_report) is { } newest)
                {
                    var cs = GameLocator.LoadConfig();
                    if (cs.CrashesSeenUntil == null || newest > cs.CrashesSeenUntil) { cs.CrashesSeenUntil = newest; GameLocator.SaveConfig(cs); }
                    _report.NewCrashes = new();
                }
                break;
            case "archive":
                // switching crash-log keeping on or off; the next scan starts or stops saving
                var cfg = GameLocator.LoadConfig();
                cfg.KeepCrashLogs = doc.RootElement.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True;
                GameLocator.SaveConfig(cfg);
                if (_report != null) _report.Archive = CrashArchive.Summary();
                Post(new { cmd = "archive", archive = _report?.Archive ?? CrashArchive.Summary() });
                break;
            case "update":
                // the opt-in update check: a switch, and a "check now" that ignores the once-a-day pacing
                var uc = GameLocator.LoadConfig();
                if (doc.RootElement.TryGetProperty("enabled", out var uen)) { uc.CheckForUpdates = uen.ValueKind == JsonValueKind.True; GameLocator.SaveConfig(uc); }
                var checkNow = doc.RootElement.TryGetProperty("check", out var ck) && ck.ValueKind == JsonValueKind.True;
                await UpdateCheckAsync(force: checkNow || (uc.CheckForUpdates == true && Updates.Due()));
                break;
            case "vramlog":
                // switching video memory recording on or off; the recorder checks the setting on its next tick
                var vc = GameLocator.LoadConfig();
                vc.LogVideoMemory = doc.RootElement.TryGetProperty("enabled", out var ven) && ven.ValueKind == JsonValueKind.True;
                GameLocator.SaveConfig(vc);
                if (_report != null) _report.VramLogs = VramLogs.Summary();
                Post(new { cmd = "vramlog", vramLogs = _report?.VramLogs ?? VramLogs.Summary() });
                break;
        }
    }

    async Task VramTickAsync()
    {
        if (_vramBusy) return; _vramBusy = true;
        try
        {
            var ended = await Task.Run(() => _vram.Tick());
            Post(new { cmd = "vram", live = _vram.Live });
            if (ended) _gameEndedAt = DateTime.Now;
            // a play started from a profile: when the game goes away, put the previous settings back
            if (_play != null)
            {
                var play = _play;
                var done = await Task.Run(() => Launcher.Watch(play));
                if (done != null)
                {
                    _play = null;
                    if (_report != null) Scanner.RefreshProfiles(_report);
                    Post(new { cmd = "toast", text = done });
                    Post(new { cmd = "profiles", ok = true, message = (string?)null, graphics = _report?.Graphics, profiles = _report?.Profiles, settingsBackup = _report?.SettingsBackup });
                }
                Post(new { cmd = "play", play = _play == null ? null : new { profile = _play.Profile, restore = _play.Restore, running = _play.SeenRunning } });
            }
            if (_gameEndedAt != null && (DateTime.Now - _gameEndedAt.Value).TotalSeconds >= RescanDelaySeconds && !_scanning)
            {
                _gameEndedAt = null;
                await ScanAsync();
            }
        }
        catch { /* the recorder must never take the window down */ }
        finally { _vramBusy = false; }
    }

    static DateTime? NewestCrash(Report r) =>
        r.Sessions.Where(s => s.EndKind is not (EndKind.Clean or EndKind.Running)).Select(s => (DateTime?)s.Start).DefaultIfEmpty(null).Max();

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
                case "play":
                    // apply, start the game, and put the previous settings back when it closes (Launcher.cs)
                    if (_game == null) { res = new ProfileResult { Ok = false, Message = "The game folder is not known yet. Scan first." }; break; }
                    if (_play != null) { res = new ProfileResult { Ok = false, Message = $"A play with \"{_play.Profile}\" is already in progress." }; break; }
                    if (MessageBox.Show(this, $"Play with the \"{name}\" settings?\n\nYour current graphics settings are backed up, the profile is put into the game, and the game is started{(_game.Store.Equals("Steam", StringComparison.OrdinalIgnoreCase) ? " through Steam" : "")}. When the game closes, your previous settings are put back automatically. Only graphics and display settings change; controls, audio and key bindings are left alone.", "Crash Doctor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    var (pr, play) = Launcher.PlayWith(_game, name);
                    res = pr; _play = play;
                    Post(new { cmd = "play", play = play == null ? null : new { profile = play.Profile, restore = play.Restore, running = false } });
                    break;
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
            // The first time the window runs there is no "seen" marker yet. Set it to the newest crash on record rather
            // than greeting someone with every crash they have ever had as "new".
            var cfgSeen = GameLocator.LoadConfig();
            if (cfgSeen.CrashesSeenUntil == null)
            {
                cfgSeen.CrashesSeenUntil = NewestCrash(_report) ?? DateTime.Now;
                GameLocator.SaveConfig(cfgSeen);
                _report.NewCrashes = new();
            }
            Post(new { cmd = "report", report = _report });
        }
        catch (Exception ex) { Post(new { cmd = "error", text = "The scan failed: " + ex.Message }); }
        finally { _scanning = false; }
        // once a day, and only if the user turned it on: ask the version file whether a newer build exists
        if (Updates.Due()) await UpdateCheckAsync(force: true);
    }

    async Task UpdateCheckAsync(bool force)
    {
        try
        {
            var enabled = GameLocator.LoadConfig().CheckForUpdates == true;
            var info = force && enabled ? await Updates.CheckAsync() : Updates.Status();
            if (_report != null) _report.Update = info;
            Post(new { cmd = "update", update = info });
        }
        catch { /* a failed check is a line of text on the settings page, never an error */ }
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
        if (id == "open:vramlogs") { Directory.CreateDirectory(VramLogs.Folder); OpenUrl(VramLogs.Folder); return; }
        if (id.StartsWith("open:vramlog:"))
        {
            var f = _report?.Sessions.FirstOrDefault(x => x.Id == id["open:vramlog:".Length..])?.VramLog?.File;
            if (f != null && File.Exists(f)) Run("explorer.exe", "/select,\"" + f + "\""); else OpenUrl(VramLogs.Folder);
            return;
        }
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

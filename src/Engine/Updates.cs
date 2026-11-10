using System.Net.Http;
using System.Text.Json;

namespace CrashDoctor.Engine;

// The update check, the only thing in the app that ever touches the network.
//
// On by default since 0.8.2 (Gary, 10 Nov 2026: people kept running old builds). Existing users who never chose are on
// too: CheckForUpdates is null until the Settings switch is used, and only an explicit false keeps it off. It does
// exactly one thing: fetch a small version file (version.json on GitHub; one GET, no identifiers sent, nothing about
// the install in the request) once each time the window starts, and compare the version in it with this build's. It never downloads or installs anything; a newer version is a line of text and a button that
// opens the release page in the browser. The result is cached in config.json so the page can show it without
// asking again, and the headless scan never checks at all.
//
// The file it asks for is JSON: {"version":"0.7.0","url":"https://www.nexusmods.com/cyberpunk2077/mods/...","notes":"..."}
public sealed class UpdateInfo
{
    public bool Enabled { get; set; }
    public bool Configured { get; set; }          // there is an address to ask
    public DateTime? Checked { get; set; }        // when it last asked
    public string Current { get; set; } = App.Short;
    public string? Latest { get; set; }
    public string? Url { get; set; }
    public string? Notes { get; set; }
    public bool Newer { get; set; }               // Latest is newer than Current
    public string? Problem { get; set; }          // why the last check did not answer, in plain words
}

public static class Updates
{
    // Where the version file lives: version.json at the root of the source repository, served raw by GitHub. Bump the
    // version there when a release goes up on Nexus and every copy with the check on learns of it within a day.
    // config.json's UpdateUrl overrides it, and so does CRASHDOCTOR_UPDATE_URL, which is how the check is tested
    // against a local file server.
    public const string DefaultVersionUrl = "https://raw.githubusercontent.com/gramersssss/crash-doctor/main/version.json";
    public static readonly TimeSpan Every = TimeSpan.FromHours(20);

    public static string? VersionUrl()
    {
        var env = Environment.GetEnvironmentVariable("CRASHDOCTOR_UPDATE_URL");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        var cfg = GameLocator.LoadConfig().UpdateUrl;
        if (!string.IsNullOrWhiteSpace(cfg)) return cfg;
        return string.IsNullOrWhiteSpace(DefaultVersionUrl) ? null : DefaultVersionUrl;
    }

    /// <summary>What the page shows, from the cache alone. Never touches the network.</summary>
    public static UpdateInfo Status()
    {
        var c = GameLocator.LoadConfig();
        var info = new UpdateInfo
        {
            Enabled = On(c),
            Configured = VersionUrl() != null,
            Checked = c.LastUpdateCheck,
            Latest = c.LatestVersion,
            Url = c.LatestVersionUrl,
            Notes = c.LatestVersionNotes,
            Problem = c.LastUpdateProblem,
        };
        info.Newer = IsNewer(info.Latest, info.Current);
        return info;
    }

    /// <summary>On unless the user switched it off (null = never chose = on, since 0.8.2).</summary>
    public static bool On(GameLocator.Config c) => c.CheckForUpdates != false;

    /// <summary>Whether a check may run: switched on and there is an address. The window asks once per start.</summary>
    public static bool Due()
    {
        var c = GameLocator.LoadConfig();
        return On(c) && VersionUrl() != null;
    }

    /// <summary>One fetch of the version file. Only the window calls this, once per start, and never when the user has switched the check off.</summary>
    public static async Task<UpdateInfo> CheckAsync()
    {
        var c = GameLocator.LoadConfig();
        var url = VersionUrl();
        c.LastUpdateCheck = DateTime.Now;
        if (url == null) { c.LastUpdateProblem = L.T("upd.noAddress"); GameLocator.SaveConfig(c); return Status(); }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CrashDoctor/" + App.Short);
            var json = await http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var version = root.TryGetProperty("version", out var v) ? v.GetString() : null;
            if (string.IsNullOrWhiteSpace(version) || !Version.TryParse(Clean(version), out _)) throw new FormatException("the version file did not contain a version number");
            c.LatestVersion = version.Trim();
            c.LatestVersionUrl = root.TryGetProperty("url", out var u) ? u.GetString() : null;
            c.LatestVersionNotes = root.TryGetProperty("notes", out var n) ? n.GetString() : null;
            c.LastUpdateProblem = null;
        }
        catch (Exception ex)
        {
            c.LastUpdateProblem = L.T("upd.noAnswer", ex.Message);
        }
        GameLocator.SaveConfig(c);
        return Status();
    }

    static string Clean(string v) { var s = v.Trim().TrimStart('v', 'V'); var plus = s.IndexOf('+'); return plus > 0 ? s[..plus] : s; }

    public static bool IsNewer(string? latest, string current)
    {
        if (string.IsNullOrWhiteSpace(latest)) return false;
        return Version.TryParse(Clean(latest), out var l) && Version.TryParse(Clean(current), out var c) && l > c;
    }
}

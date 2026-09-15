using System.Net.Http;
using System.Text.Json;

namespace CrashDoctor.Engine;

// The update check. Opt-in, and the only thing in the app that ever touches the network.
//
// "Nothing leaves your PC" is a promise the Nexus page makes and the app has to keep, so this is off until the user
// turns it on under Settings, and when it is on it does exactly one thing: fetch a small version file (one GET,
// no identifiers sent, nothing about the install in the request) at most once a day, and compare the version in it
// with this build's. It never downloads or installs anything; a newer version is a line of text and a button that
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
    // Where the version file lives. Empty until the release page exists; config.json's UpdateUrl overrides it, and so
    // does CRASHDOCTOR_UPDATE_URL, which is how the check is tested against a local file server.
    public const string DefaultVersionUrl = "";
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
            Enabled = c.CheckForUpdates == true,
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

    public static bool Due()
    {
        var c = GameLocator.LoadConfig();
        return c.CheckForUpdates == true && VersionUrl() != null && (c.LastUpdateCheck == null || DateTime.Now - c.LastUpdateCheck.Value > Every);
    }

    /// <summary>One fetch of the version file. Only the window calls this, and only when the user has turned the check on.</summary>
    public static async Task<UpdateInfo> CheckAsync()
    {
        var c = GameLocator.LoadConfig();
        var url = VersionUrl();
        c.LastUpdateCheck = DateTime.Now;
        if (url == null) { c.LastUpdateProblem = "There is no address to ask yet."; GameLocator.SaveConfig(c); return Status(); }
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
            c.LastUpdateProblem = "The check did not get an answer: " + ex.Message;
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

using System.Diagnostics;

namespace CrashDoctor.Engine;

// Starting the game, for "play with this profile".
//
// A profile is only useful if you then play with it, and the point of a one-off profile (a lighter one for a dense
// district, say) is that your usual settings come back afterwards. So: back up, apply, start the game the way the
// store would, and when the game goes away put the backup back. The window does the watching with the same timer
// that records video memory; the command line waits in the foreground.
//
// Steam installs are started through Steam, so its overlay, cloud saves and DRM behave exactly as if the play button
// had been pressed. Everything else runs the exe directly from its own folder.
public static class Launcher
{
    public const string SteamAppId = "1091500";

    /// <summary>The state of a play started by Crash Doctor: what to put back and whether the game has been seen yet.</summary>
    public sealed class Play
    {
        public string Profile { get; set; } = "";
        public DateTime LaunchedAt { get; set; }
        public bool Restore { get; set; }          // a backup was made by this launch's Apply and should go back afterwards
        public bool SeenRunning { get; set; }
        public static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(5);
    }

    public static ProfileResult Start(GamePaths g)
    {
        try
        {
            if (g.Store.Equals("Steam", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo("steam://rungameid/" + SteamAppId) { UseShellExecute = true });
                return new ProfileResult { Ok = true, Message = "Asked Steam to start Cyberpunk 2077." };
            }
            if (!File.Exists(g.Exe)) return new ProfileResult { Ok = false, Message = "The game's exe was not found at " + g.Exe + "." };
            Process.Start(new ProcessStartInfo(g.Exe) { UseShellExecute = true, WorkingDirectory = g.Bin });
            return new ProfileResult { Ok = true, Message = "Started Cyberpunk 2077." };
        }
        catch (Exception ex) { return new ProfileResult { Ok = false, Message = "Could not start the game: " + ex.Message }; }
    }

    /// <summary>
    /// Apply a profile and start the game. Restore is true when the apply changed the file (so a backup exists to put
    /// back); a profile the game already had makes no backup, and putting back an older one would be wrong.
    /// </summary>
    public static (ProfileResult result, Play? play) PlayWith(GamePaths g, string profile)
    {
        var before = GraphicsProfiles.LastBackup();
        var applied = GraphicsProfiles.Apply(profile);
        if (!applied.Ok) return (applied, null);
        var madeBackup = GraphicsProfiles.LastBackup() is { } after && after != before;
        var started = Start(g);
        if (!started.Ok)
        {
            if (madeBackup) { var undo = GraphicsProfiles.Undo(); started.Message += " Your settings were put back" + (undo.Ok ? "." : ", or tried to be: " + undo.Message); }
            return (started, null);
        }
        var play = new Play { Profile = profile, LaunchedAt = DateTime.Now, Restore = madeBackup };
        var msg = $"Playing with \"{profile}\". " + (madeBackup ? "Your previous settings go back when the game closes." : "The game already had these settings, so there is nothing to put back afterwards.");
        return (new ProfileResult { Ok = true, Message = msg }, play);
    }

    /// <summary>Called every few seconds while a play is in progress. Returns a message when it has finished (settings restored, or the game never appeared), else null.</summary>
    public static string? Watch(Play play)
    {
        var running = GameLocator.GameRunning();
        if (running) { play.SeenRunning = true; return null; }
        if (!play.SeenRunning)
        {
            if (DateTime.Now - play.LaunchedAt < Play.StartTimeout) return null;
            return Finish(play, "The game did not start within 5 minutes.");
        }
        return Finish(play, "The game has closed.");
    }

    static string Finish(Play play, string what)
    {
        if (!play.Restore) return what + $" The \"{play.Profile}\" settings stay in the game.";
        var undo = GraphicsProfiles.Undo();
        return what + (undo.Ok ? " Your previous settings are back." : " Your previous settings could not be put back: " + undo.Message + " Undo under Settings will try again.");
    }
}

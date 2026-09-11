using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrashDoctor.Engine;

// A saved report is the same interface with the data baked in, so it opens anywhere without the app.
//
// It is also the thing people paste into forum threads, which makes it the one output that leaves the machine.
// Two things therefore never travel in it:
//
//   - Absolute paths under the user's profile. They carry the Windows account name, which is often a real name,
//     and they are useless to a reader anyway: the files they point at are not in the report.
//   - Optionally the mod list, because naming every mod someone has installed is a more personal disclosure than
//     it looks, and the person sharing it should be the one deciding.
//
// The live window is unaffected - this only shapes the saved copy.
public static class HtmlExporter
{
    public static string UiDir => Path.Combine(AppContext.BaseDirectory, "ui");

    public static void Save(Report r, string path, bool includeModList = true)
    {
        var safe = Sanitise(r, includeModList);
        var json = Scanner.ToJson(safe).Replace("</script", "<" + @"\" + "/script");
        json = ScrubPaths(json);
        var inject = "<script>window.EXPORTED=" + json + ";</script>\n</body>";
        var html = File.ReadAllText(Path.Combine(UiDir, "index.html")).Replace("</body>", inject);
        File.WriteAllText(path, html);
    }

    /// <summary>Round-trip through JSON to get a copy, so shaping the saved report cannot disturb the live one.</summary>
    static Report Sanitise(Report r, bool includeModList)
    {
        var clone = JsonSerializer.Deserialize<Report>(Scanner.ToJson(r), Scanner.Json) ?? r;
        var removed = new List<string>();

        var shots = clone.Sessions.Count(s => s.Screenshot != null);
        foreach (var s in clone.Sessions) s.Screenshot = null;
        if (shots > 0) removed.Add($"{shots} crash screenshot path{(shots == 1 ? "" : "s")}");

        if (!includeModList)
        {
            // The table is the obvious place, but most of the names actually escape through the requirements page,
            // which lists every mod needing each framework, and through the area panel. Withholding the list has to
            // mean withholding the names, or the option is a false promise.
            var n = clone.ModsList.Count;
            clone.ModsList = new List<ModRow>();
            foreach (var q in clone.Requirements) q.NeededBy = new List<string>();
            foreach (var s2 in clone.Sessions) s2.AreaMods = new List<AreaMod>();
            if (n > 0) removed.Add($"the list of {n} mod names, and mod names from the requirements and area panels");
        }

        clone.Privacy = removed.Count > 0
            ? "Saved copy. Removed before saving: " + string.Join("; ", removed) + ". Personal folder paths are replaced with %USERPROFILE%."
                + (includeModList ? "" : " Mods named in the diagnosis itself are still shown - naming them is the diagnosis.")
            : "Saved copy. Personal folder paths are replaced with %USERPROFILE%.";
        return clone;
    }

    // A catch-all rather than a list of known fields: anything that ends up in the report later is covered without
    // anyone having to remember this file exists.
    static string ScrubPaths(string json)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 3)
        {
            var back = profile;                                   // C:\Users\name
            var fwd = profile.Replace('\\', '/');                 // C:/Users/name
            var escaped = back.Replace(@"\", @"\\");              // as it appears inside JSON
            foreach (var form in new[] { escaped, fwd, back })
                json = json.Replace(form, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }
        // any other account's profile path too, in case the game or a mod lives under one
        json = Regex.Replace(json, @"[A-Za-z]:(\\\\|/)Users\1[^\\/""]+", "%USERPROFILE%", RegexOptions.IgnoreCase);
        return json;
    }
}

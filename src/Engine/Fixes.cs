using System.Security.Cryptography;
using System.Text.Json;

namespace CrashDoctor.Engine;

// Known fixes: small, reviewed edits to a specific mod file, applied only when the file on disk is byte-for-byte the
// version the fix was written for. Always backed up, always revertible. Catalogue lives in fixes\catalog.json next to the exe.
public sealed class KnownFix
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Mod { get; set; } = "";              // display name to match (case-insensitive contains)
    public string Target { get; set; } = "";           // path relative to the game folder
    public string OriginalSha256 { get; set; } = "";
    public string PatchedFile { get; set; } = "";      // relative to fixes\
    public string PatchedSha256 { get; set; } = "";
    public string Why { get; set; } = "";
    public string? NexusId { get; set; }

    public string TargetPath(GamePaths g) => Path.Combine(g.GameDir, Target);
    public string State(GamePaths g)
    {
        var p = TargetPath(g); if (!File.Exists(p)) return "missing";
        var h = Fixes.Sha(p); return h == PatchedSha256 ? "applied" : h == OriginalSha256 ? "applicable" : "different-version";
    }
    public bool Applied(GamePaths g) => State(g) == "applied";
}

public static class Fixes
{
    static List<KnownFix>? _all;
    public static string FixesDir => Path.Combine(AppContext.BaseDirectory, "fixes");
    public static string BackupDir => Path.Combine(GamePaths.AppData, "backups");

    public static List<KnownFix> All()
    {
        if (_all != null) return _all;
        try { var f = Path.Combine(FixesDir, "catalog.json"); _all = File.Exists(f) ? JsonSerializer.Deserialize<List<KnownFix>>(File.ReadAllText(f), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new() : new(); }
        catch { _all = new(); }
        return _all;
    }
    public static KnownFix? FindFor(string modName) => All().FirstOrDefault(f => modName.Contains(f.Mod, StringComparison.OrdinalIgnoreCase) || f.Mod.Contains(modName, StringComparison.OrdinalIgnoreCase));
    public static KnownFix? ById(string id) => All().FirstOrDefault(f => f.Id == id);

    public static string Sha(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)); }

    public static string Apply(KnownFix fix, GamePaths g)
    {
        var target = fix.TargetPath(g);
        var state = fix.State(g);
        if (state == "missing") return "The mod file is not installed, nothing to fix.";
        if (state == "applied") return "Already applied.";
        if (state == "different-version") return "The file on disk is a different version than this fix was written for (the mod was probably updated). Not touching it.";
        var patched = Path.Combine(FixesDir, fix.PatchedFile);
        if (!File.Exists(patched) || Sha(patched) != fix.PatchedSha256) return "The fix file shipped with Crash Doctor is missing or damaged.";
        var bdir = Path.Combine(BackupDir, fix.Id); Directory.CreateDirectory(bdir);
        File.Copy(target, Path.Combine(bdir, "original" + Path.GetExtension(target)), true);
        File.Copy(patched, target, true);
        return "Applied. The original is backed up and one click reverts it.";
    }

    public static string Revert(KnownFix fix, GamePaths g)
    {
        var target = fix.TargetPath(g);
        var backup = Path.Combine(BackupDir, fix.Id, "original" + Path.GetExtension(target));
        if (!File.Exists(backup)) return "No backup found for this fix, so nothing was changed. Reinstall the mod to get the original back.";
        if (!File.Exists(Path.GetDirectoryName(target)!)) { /* dir check below */ }
        if (!Directory.Exists(Path.GetDirectoryName(target))) return "The mod folder is gone (mod uninstalled?). Nothing to revert.";
        File.Copy(backup, target, true);
        return "Reverted. The original file is back.";
    }
}

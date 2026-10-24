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

    // through GamePaths, so under Mod Organizer 2 this is the copy in the mod's own folder that the game loads
    public string TargetPath(GamePaths g) => g.Find(Target) ?? Path.Combine(g.GameDir, Target);
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
        if (state == "missing") return L.T("fix.missing");
        if (state == "applied") return L.T("fix.already");
        if (state == "different-version") return L.T("fix.otherVersion");
        var patched = Path.Combine(FixesDir, fix.PatchedFile);
        if (!File.Exists(patched) || Sha(patched) != fix.PatchedSha256) return L.T("fix.damaged");
        var bdir = Path.Combine(BackupDir, fix.Id); Directory.CreateDirectory(bdir);
        File.Copy(target, Path.Combine(bdir, "original" + Path.GetExtension(target)), true);
        File.Copy(patched, target, true);
        return L.T("fix.applied");
    }

    public static string Revert(KnownFix fix, GamePaths g)
    {
        var target = fix.TargetPath(g);
        var backup = Path.Combine(BackupDir, fix.Id, "original" + Path.GetExtension(target));
        if (!File.Exists(backup)) return L.T("fix.noBackup");
        if (!File.Exists(Path.GetDirectoryName(target)!)) { /* dir check below */ }
        if (!Directory.Exists(Path.GetDirectoryName(target))) return L.T("fix.folderGone");
        File.Copy(backup, target, true);
        return L.T("fix.reverted");
    }
}

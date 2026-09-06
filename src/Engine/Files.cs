using System.Text;

namespace CrashDoctor.Engine;

// The game and its mod loaders keep log files open while running. Read them with sharing so a scan works mid-session.
public static class Files
{
    public static string ReadAllTextShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs, Encoding.UTF8, true);
        return sr.ReadToEnd();
    }
    public static string[] ReadAllLinesShared(string path)
    {
        var lines = new List<string>();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs, Encoding.UTF8, true);
        string? l; while ((l = sr.ReadLine()) != null) lines.Add(l);
        return lines.ToArray();
    }
    public static IEnumerable<string> ReadLinesShared(string path) => ReadAllLinesShared(path);
}

using System.Reflection;

namespace CrashDoctor.Engine;

/// <summary>Who we are, for anything that has to identify this build to a human.</summary>
public static class App
{
    /// <summary>
    /// The informational version, which build.ps1 stamps as "x.y.z+&lt;commit sha&gt;" - the version for the reader
    /// and the sha for whoever has to reproduce what they are looking at. Falls back to the plain assembly version
    /// if the attribute is missing, and to "unknown" rather than throwing, because a version string is never worth
    /// failing a scan over.
    /// </summary>
    public static readonly string Version = Read();

    /// <summary>Just the "x.y.z" part, for places where the sha is noise.</summary>
    public static string Short => Version.Split('+')[0];

    static string Read()
    {
        try
        {
            var asm = typeof(App).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info)) return info;
            return asm.GetName().Version?.ToString() ?? "unknown";
        }
        catch { return "unknown"; }
    }
}

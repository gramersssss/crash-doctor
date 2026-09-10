using System.Text;

namespace CrashDoctor.Engine;

// Reads the Cyberpunk2077.dmp that the game writes beside every crash report.
//
// This is the most useful file on the machine and nothing else in this ecosystem opens it. It records the exact
// machine instruction that faulted. Two crashes that fault at the same instruction are the same bug; two that fault
// at different ones are different bugs, however similar their logs look. That turns "the game crashes sometimes"
// into "you have three distinct problems", which is a completely different conversation.
//
// Only the header, the module list and the exception record are read - a few kilobytes out of a ~32 MB file - so
// this stays cheap across dozens of crashes.
public sealed class DumpInfo
{
    public uint ExceptionCode { get; set; }
    public ulong ExceptionAddress { get; set; }
    public string? FaultingModule { get; set; }      // e.g. "Cyberpunk2077.exe"
    public ulong FaultingOffset { get; set; }        // offset into that module
    public string? AccessKind { get; set; }          // read | write | execute
    public ulong AccessedAddress { get; set; }
    public List<string> Modules { get; set; } = new();

    /// <summary>Stable identity of this crash: same signature means the same bug.</summary>
    public string? Signature => FaultingModule == null ? null : FaultingModule + "+0x" + FaultingOffset.ToString("X");

    public string ExceptionName => ExceptionCode switch
    {
        0xC0000005 => "EXCEPTION_ACCESS_VIOLATION",
        0x80000003 => "EXCEPTION_BREAKPOINT",
        0xC00000FD => "EXCEPTION_STACK_OVERFLOW",
        0xC0000094 => "EXCEPTION_INT_DIVIDE_BY_ZERO",
        0xC0000006 => "EXCEPTION_IN_PAGE_ERROR",
        0xC000001D => "EXCEPTION_ILLEGAL_INSTRUCTION",
        0xE06D7363 => "C++ exception",
        _ => "0x" + ExceptionCode.ToString("X8")
    };

    /// <summary>One plain sentence about what the processor was doing. The mechanism, not the blame.</summary>
    public string Plain => ExceptionCode switch
    {
        0xC0000005 when AccessKind == "read" => "The game tried to read memory that was not there any more, typically a resource that failed to load or was freed while still in use.",
        0xC0000005 when AccessKind == "write" => "The game tried to write to memory it did not own.",
        0xC0000005 => "The game touched memory that was not valid.",
        0x80000003 => "The engine stopped itself deliberately: it hit a condition it treats as fatal rather than carrying on.",
        0xC00000FD => "The stack ran out of room, usually endless recursion in a script or a mod calling itself.",
        0xC0000006 => "A page of memory could not be read from disk. Suspect the drive or a damaged game file.",
        _ => ""
    };
}

public static class MiniDump
{
    const uint DumpSignature = 0x504D444D;   // MDMP
    const uint ModuleListStream = 4, ExceptionStream = 6;

    public static DumpInfo? Read(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new BinaryReader(fs);
            if (fs.Length < 32 || r.ReadUInt32() != DumpSignature) return null;
            r.ReadUInt32();                                  // version
            var streamCount = r.ReadUInt32();
            var dirRva = r.ReadUInt32();
            if (streamCount > 4096) return null;

            var streams = new Dictionary<uint, (uint size, uint rva)>();
            fs.Position = dirRva;
            for (uint i = 0; i < streamCount; i++)
            {
                if (fs.Position + 12 > fs.Length) break;
                var type = r.ReadUInt32(); var size = r.ReadUInt32(); var rva = r.ReadUInt32();
                streams[type] = (size, rva);
            }
            if (!streams.TryGetValue(ExceptionStream, out var exc)) return null;

            var info = new DumpInfo();
            fs.Position = exc.rva;
            r.ReadUInt32();                                  // thread id
            r.ReadUInt32();                                  // alignment
            info.ExceptionCode = r.ReadUInt32();
            r.ReadUInt32();                                  // flags
            r.ReadUInt64();                                  // nested exception record
            info.ExceptionAddress = r.ReadUInt64();
            var paramCount = r.ReadUInt32();
            r.ReadUInt32();                                  // alignment
            if (paramCount >= 2 && paramCount <= 15)
            {
                var kind = r.ReadUInt64();
                info.AccessedAddress = r.ReadUInt64();
                info.AccessKind = kind switch { 0 => "read", 1 => "write", 8 => "execute", _ => null };
            }

            // Which loaded module contains the faulting address?
            if (streams.TryGetValue(ModuleListStream, out var mods))
            {
                fs.Position = mods.rva;
                var count = r.ReadUInt32();
                if (count <= 8192)
                {
                    var entries = new List<(ulong baseAddr, uint size, uint nameRva)>((int)count);
                    for (uint i = 0; i < count; i++)
                    {
                        if (fs.Position + 108 > fs.Length) break;
                        var start = fs.Position;
                        var baseAddr = r.ReadUInt64();
                        var size = r.ReadUInt32();
                        r.ReadUInt32(); r.ReadUInt32();       // checksum, timestamp
                        var nameRva = r.ReadUInt32();
                        entries.Add((baseAddr, size, nameRva));
                        fs.Position = start + 108;            // skip VersionInfo, CV/Misc records, reserved
                    }
                    foreach (var e in entries)
                    {
                        var name = ReadName(fs, r, e.nameRva);
                        if (name == null) continue;
                        info.Modules.Add(name);
                        if (info.ExceptionAddress >= e.baseAddr && info.ExceptionAddress < e.baseAddr + e.size)
                        {
                            info.FaultingModule = name;
                            info.FaultingOffset = info.ExceptionAddress - e.baseAddr;
                        }
                    }
                }
            }
            return info;
        }
        catch { return null; }
    }

    static string? ReadName(FileStream fs, BinaryReader r, uint rva)
    {
        try
        {
            if (rva == 0 || rva + 4 > fs.Length) return null;
            fs.Position = rva;
            var bytes = r.ReadUInt32();
            if (bytes > 4096 || fs.Position + bytes > fs.Length) return null;
            var full = Encoding.Unicode.GetString(r.ReadBytes((int)bytes)).TrimEnd('\0');
            var slash = full.LastIndexOfAny(new[] { '\\', '/' });
            return slash >= 0 ? full[(slash + 1)..] : full;
        }
        catch { return null; }
    }

    // Tools that inject themselves into the game process. Worth reporting plainly, because they patch game code at
    // fixed addresses and one built for a different game version writes into the wrong place. Reported as an
    // observation with counts, never as a verdict - see DESIGN.md.
    static readonly Dictionary<string, string> Injectors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TrSpeedHack_x64.dll"] = "a FLiNG trainer",
        ["TrSpeedHack.dll"] = "a FLiNG trainer",
        ["TrMonoServer.dll"] = "a FLiNG trainer",
        ["CheatEngine.dll"] = "Cheat Engine",
        ["vehdebug-x86_64.dll"] = "Cheat Engine",
        ["WeMod.dll"] = "WeMod",
    };

    public static string? InjectorIn(IEnumerable<string> modules)
    {
        foreach (var m in modules) if (Injectors.TryGetValue(m, out var who)) return who;
        return null;
    }
}

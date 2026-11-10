using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrashDoctor.Engine;

// The Mox Gorilla Arms save repair (0.8.2). A port of the prototype in glamdocs-unlock\tools\savefix (mox_savefix.py,
// SPEC.md), which Gary tested in the game on 10 Nov 2026: the repaired save loaded, the holstered first-person Mox arms
// were visible and the ripperdoc chair went Mox -> Barghest -> Mox again.
//
// The problem: male V with Phantom Liberty and the Legendary Gorilla Arms in the Mox style. The save keeps V's arm look
// as "parts" in the node CharacetrCustomization_Appearances (sic), and in the broken state the group
// holstered_strong_fpp has lost its Mox part. From then on the game never changes the stored Gorilla parts again: every
// style keeps the Mox shape, and Mox/Barghest are invisible in first person. The repair puts back the exact 62-byte
// part a healthy save holds, sets the group's count 3 -> 4, and rebuilds every size and offset that depends on it.
//
// Rules this follows (DESIGN.md): it only ever writes a NEW save folder, never the original; it asks first; Undo
// removes the copy again; and it refuses everything the prototype refuses - female V (not checked yet), a style other
// than Mox, a layout it does not know, a save that is already healthy. Byte-exact with the prototype, including its
// LZ4 compressor, so the fixtures' md5s can be compared directly (tools\test-savefix.py).
public static class MoxSave
{
    const int Chunk = 262144, TableCap = 512, First = 33 + 12 * TableCap;   // first chunk always at 6177
    public const string NodeName = "CharacetrCustomization_Appearances";
    static readonly byte[] GTpp = Encoding.ASCII.GetBytes("holstered_strong_tpp"), GFpp = Encoding.ASCII.GetBytes("holstered_strong_fpp"), GUnh = Encoding.ASCII.GetBytes("unholstered_strong");
    const string MoxTpp = "h_strong_arms_mox_colors_cyberware01_tpp", MoxFpp = "h_strong_arms_mox_colors_cyberware01_fpp", MoxUnh = "u_strong_arms_mox_colors_cyberware01_fpp";
    // The missing part, byte for byte from a healthy save (Gary's ManualSave-1615, male V, game 2.5): u64 FNV1a64 of the
    // Mox holstered .app path, packed string "male", packed string MoxFpp, 8 zero bytes. Nothing in it is character-specific.
    public static readonly byte[] MoxPart = Convert.FromHexString("2af9a8c34829e9fb" + "846d616c65" + "a8" + Convert.ToHexString(Encoding.ASCII.GetBytes(MoxFpp)) + "0000000000000000");

    public sealed class Refuse : Exception
    {
        public string Token { get; }   // a Lang token, rendered when shown
        public Refuse(string token) : base(L.REn(token)) { Token = token; }
    }
    static Refuse Layout(string detail) => new(L.T("save.refuse.layout", detail));

    // ------------------------------------------------------------------ LZ4 block format (no frame)
    public static byte[] Lz4Decompress(ReadOnlySpan<byte> src, int dsz)
    {
        var dst = new byte[dsz]; int o = 0, i = 0, n = src.Length;
        try
        {
            while (true)
            {
                int tok = src[i++]; int lit = tok >> 4;
                if (lit == 15) { int x; do { x = src[i++]; lit += x; } while (x == 255); }
                if (lit > n - i || lit > dsz - o) throw Layout("LZ4 literals run past the chunk");
                src.Slice(i, lit).CopyTo(dst.AsSpan(o)); i += lit; o += lit;
                if (i >= n) break;
                int off = src[i] | (src[i + 1] << 8); i += 2;
                if (off == 0 || off > o) throw Layout("LZ4 data is damaged (bad offset)");
                int ml = tok & 15;
                if (ml == 15) { int x; do { x = src[i++]; ml += x; } while (x == 255); }
                ml += 4;
                if (ml > dsz - o) throw Layout("LZ4 match runs past the chunk");
                int s = o - off;
                for (int k = 0; k < ml; k++) dst[o++] = dst[s + k];
            }
        }
        catch (IndexOutOfRangeException) { throw Layout("LZ4 data ends early"); }
        if (o != dsz) throw Layout($"LZ4 chunk size mismatch {o} != {dsz}");
        return dst;
    }

    static void Lz4Len(List<byte> o, int v) { while (v >= 255) { o.Add(255); v -= 255; } o.Add((byte)v); }

    /// <summary>The prototype's deterministic greedy compressor, byte for byte (SPEC.md section 5), so a repaired save
    /// can be compared with the fixtures by md5. Any correct LZ4 block compressor would load in the game.</summary>
    public static byte[] Lz4Compress(byte[] src)
    {
        int n = src.Length; var o = new List<byte>(n / 2 + 16);
        var table = new Dictionary<uint, int>();
        int anchor = 0, i = 0;
        while (i <= n - 12)
        {
            uint key = BitConverter.ToUInt32(src, i);
            bool had = table.TryGetValue(key, out var c);
            table[key] = i;
            if (had && i - c <= 65535)
            {
                int m = i + 4, k = c + 4;
                while (m < n - 5 && src[m] == src[k]) { m++; k++; }
                int lit = i - anchor, ml = m - i;
                o.Add((byte)((Math.Min(lit, 15) << 4) | Math.Min(ml - 4, 15)));
                if (lit >= 15) Lz4Len(o, lit - 15);
                for (int x = anchor; x < i; x++) o.Add(src[x]);
                o.Add((byte)((i - c) & 255)); o.Add((byte)((i - c) >> 8));
                if (ml - 4 >= 15) Lz4Len(o, ml - 4 - 15);
                i = anchor = m;
            }
            else i++;
        }
        int last = n - anchor;
        o.Add((byte)(Math.Min(last, 15) << 4));
        if (last >= 15) Lz4Len(o, last - 15);
        for (int x = anchor; x < n; x++) o.Add(src[x]);
        return o.ToArray();
    }

    // ------------------------------------------------------------------ sav.dat container
    public sealed class Node { public int I; public string Name = ""; public int Next, Child, Off, Size, FPos; public Node Copy() => (Node)MemberwiseClone(); }

    static (int v, int i) ReadPacked(byte[] b, int i)
    {
        int x = b[i++]; bool neg = (x & 0x80) != 0; int v = x & 0x3F, sh = 6; bool more = (x & 0x40) != 0;
        while (more) { x = b[i++]; v |= (x & 0x7F) << sh; sh += 7; more = (x & 0x80) != 0; }
        return (neg ? -v : v, i);
    }

    static List<Node> ReadNodes(byte[] raw, int edonAt, int tableEnd, out int end)
    {
        // raw holds the bytes from edonAt (index 0 = "EDON") up to tableEnd
        var (cnt, p) = ReadPacked(raw, 4);
        if (cnt < 1 || cnt > 1_000_000) throw Layout("node table has an unknown layout");
        var nodes = new List<Node>(cnt);
        for (int k = 0; k < cnt; k++)
        {
            var (ln, q) = ReadPacked(raw, p); p = q; ln = Math.Abs(ln);
            var name = Encoding.UTF8.GetString(raw, p, ln); p += ln;
            nodes.Add(new Node { I = k, Name = name, Next = BitConverter.ToInt32(raw, p), Child = BitConverter.ToInt32(raw, p + 4), Off = BitConverter.ToInt32(raw, p + 8), Size = BitConverter.ToInt32(raw, p + 12), FPos = p });
            p += 16;
        }
        end = p;
        return nodes;
    }

    public sealed class SavFile
    {
        public byte[] Raw = Array.Empty<byte>();
        public List<(int off, int csz, int dsz)> Ents = new();
        public byte[] Blob = Array.Empty<byte>();
        public int Edon;
        public List<Node> Nodes = new();
        public int Base => First;

        /// <summary>Full read with every check the prototype makes (SPEC.md section 2). Throws Refuse.</summary>
        public static SavFile Parse(byte[] raw)
        {
            try { return ParseInner(raw); }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException) { throw Layout("the file ends early or is damaged"); }
        }

        static SavFile ParseInner(byte[] raw)
        {
            var s = new SavFile { Raw = raw };
            if (raw.Length < First + 16 || Encoding.ASCII.GetString(raw, 0, 4) != "VASC") throw new Refuse(L.T("save.refuse.notSave"));
            if (Encoding.ASCII.GetString(raw, 25, 4) != "FZLC" || IndexOf(raw, "FZLC"u8, 0) != 25) throw Layout("unknown header layout");
            int n = BitConverter.ToInt32(raw, 29);
            if (n < 1 || n > TableCap) throw Layout($"unknown chunk table ({n} chunks)");
            for (int k = 0; k < n; k++) s.Ents.Add((BitConverter.ToInt32(raw, 33 + 12 * k), BitConverter.ToInt32(raw, 37 + 12 * k), BitConverter.ToInt32(raw, 41 + 12 * k)));
            if (s.Ents[0].off != First) throw Layout("unknown chunk table layout");
            for (int x = 33 + 12 * n; x < First; x++) if (raw[x] != 0) throw Layout("unknown chunk table layout");
            var blob = new MemoryStream();
            for (int k = 0; k < n; k++)
            {
                var (off, csz, dsz) = s.Ents[k];
                if (off < 0 || csz < 8 || dsz < 0 || off + csz > raw.Length || Encoding.ASCII.GetString(raw, off, 4) != "4ZLX" || BitConverter.ToUInt32(raw, off + 4) != (uint)dsz)
                    throw Layout($"chunk {k} has an unknown header");
                if (k + 1 < n && (off + csz != s.Ents[k + 1].off || dsz != Chunk)) throw Layout("chunks are not laid out as expected");
                blob.Write(Lz4Decompress(raw.AsSpan(off + 8, csz - 8), dsz));
            }
            s.Blob = blob.ToArray();
            s.Edon = s.Ents[^1].off + s.Ents[^1].csz;
            if (s.Edon + 4 > raw.Length || Encoding.ASCII.GetString(raw, s.Edon, 4) != "EDON" || Encoding.ASCII.GetString(raw, raw.Length - 4, 4) != "ENOD") throw Layout("node table not found");
            var tail = raw.AsSpan(s.Edon).ToArray();
            s.Nodes = ReadNodes(tail, 0, tail.Length, out var end);
            if (end != tail.Length - 8) throw Layout("node table has an unknown layout");
            if (BitConverter.ToUInt32(raw, raw.Length - 8) != (uint)s.Edon) throw Layout("file footer has an unknown layout");
            var last = s.Nodes[^1];
            if ((long)last.Off + last.Size != First + s.Blob.Length) throw Layout("node data does not end at the end of the data");
            foreach (var nd in s.Nodes)
            {
                if (nd.Off < First || nd.Size < 0 || (long)nd.Off + nd.Size > First + s.Blob.Length) throw Layout($"node {nd.I} lies outside the data");
                if (nd.Size >= 4 && BitConverter.ToUInt32(s.Blob, nd.Off - First) != (uint)nd.I) throw Layout($"node {nd.I} does not start with its own index");
            }
            return s;
        }

        public Node Node(string name)
        {
            var hits = Nodes.Where(n => n.Name == name).ToList();
            if (hits.Count != 1) throw Layout($"expected one {name} node, found {hits.Count}");
            return hits[0];
        }

        public byte[] Data(Node nd) => Blob.AsSpan(nd.Off - First, nd.Size).ToArray();
    }

    /// <summary>
    /// The quick read a scan uses: the chunk table, the node table at the end of the file, and only the chunks that hold
    /// the arm node - about 150 KB of a 6 MB save. The full checks run again before any repair.
    /// </summary>
    public static (Node node, byte[] data) QuickNode(string savPath)
    {
        try
        {
            using var fs = new FileStream(savPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long len = fs.Length;
            if (len < First + 16 || len > int.MaxValue) throw Layout("unexpected file size");
            var head = new byte[First]; fs.ReadExactly(head);
            if (Encoding.ASCII.GetString(head, 0, 4) != "VASC") throw new Refuse(L.T("save.refuse.notSave"));
            if (Encoding.ASCII.GetString(head, 25, 4) != "FZLC") throw Layout("unknown header layout");
            int n = BitConverter.ToInt32(head, 29);
            if (n < 1 || n > TableCap) throw Layout($"unknown chunk table ({n} chunks)");
            var ents = Enumerable.Range(0, n).Select(k => (off: BitConverter.ToInt32(head, 33 + 12 * k), csz: BitConverter.ToInt32(head, 37 + 12 * k), dsz: BitConverter.ToInt32(head, 41 + 12 * k))).ToList();
            if (ents[0].off != First) throw Layout("unknown chunk table layout");
            var foot = new byte[8]; fs.Position = len - 8; fs.ReadExactly(foot);
            if (Encoding.ASCII.GetString(foot, 4, 4) != "ENOD") throw Layout("node table not found");
            int edon = BitConverter.ToInt32(foot, 0);
            if (edon != ents[^1].off + ents[^1].csz || edon < First || edon >= len - 8) throw Layout("node table not found");
            var tail = new byte[len - edon]; fs.Position = edon; fs.ReadExactly(tail);
            if (Encoding.ASCII.GetString(tail, 0, 4) != "EDON") throw Layout("node table not found");
            var nodes = ReadNodes(tail, 0, tail.Length, out var end);
            if (end != tail.Length - 8) throw Layout("node table has an unknown layout");
            var hits = nodes.Where(x => x.Name == NodeName).ToList();
            if (hits.Count != 1) throw Layout($"expected one {NodeName} node, found {hits.Count}");
            var nd = hits[0];
            long v0 = nd.Off - First, v1 = v0 + nd.Size;
            if (v0 < 0 || nd.Size < 4) throw Layout("the arm node lies outside the data");
            // virtual position of each chunk = sum of the decompressed sizes before it
            var data = new byte[nd.Size]; long vpos = 0; int got = 0;
            foreach (var (off, csz, dsz) in ents)
            {
                long c0 = vpos, c1 = vpos + dsz; vpos = c1;
                if (c1 <= v0 || c0 >= v1) continue;
                if (csz < 8 || off + (long)csz > len) throw Layout("chunk has an unknown header");
                var comp = new byte[csz]; fs.Position = off; fs.ReadExactly(comp);
                if (Encoding.ASCII.GetString(comp, 0, 4) != "4ZLX" || BitConverter.ToUInt32(comp, 4) != (uint)dsz) throw Layout("chunk has an unknown header");
                var plain = Lz4Decompress(comp.AsSpan(8), dsz);
                long a = Math.Max(c0, v0), b = Math.Min(c1, v1);
                Array.Copy(plain, a - c0, data, a - v0, b - a); got += (int)(b - a);
            }
            if (got != nd.Size) throw Layout("the arm node lies outside the data");
            if (BitConverter.ToUInt32(data, 0) != (uint)nd.I) throw Layout($"node {nd.I} does not start with its own index");
            return (nd, data);
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or EndOfStreamException) { throw Layout("the file ends early or is damaged"); }
    }

    // ------------------------------------------------------------------ the arm parts
    public sealed class Part { public int Start, End; public string App = "", Opt = ""; }

    static int IndexOf(byte[] d, ReadOnlySpan<byte> pat, int from)
    {
        var i = d.AsSpan(from).IndexOf(pat);
        return i < 0 ? -1 : i + from;
    }

    static (int cntPos, List<Part> parts)? TryGroup(byte[] d, int p)
    {
        if (p + 4 > d.Length) return null;
        uint cnt = BitConverter.ToUInt32(d, p); int cntPos = p; p += 4;
        if (cnt > 64) return null;
        var parts = new List<Part>();
        for (int x = 0; x < cnt; x++)
        {
            int start = p;
            if (p + 8 > d.Length) return null;
            p += 8;
            var strs = new string[2];
            for (int s = 0; s < 2; s++)
            {
                if (p >= d.Length) return null;
                int ln = d[p];
                if ((ln & 0x80) == 0 || (ln & 0x40) != 0) return null;
                ln &= 0x3F;
                if (p + 1 + ln > d.Length) return null;   // the prototype's slice would come up short and fail below
                for (int k = 0; k < ln; k++) if (d[p + 1 + k] > 127) return null;
                strs[s] = Encoding.ASCII.GetString(d, p + 1, ln);
                p += 1 + ln;
            }
            p += 8;
            if (p > d.Length) return null;
            parts.Add(new Part { Start = start, End = p, App = strs[0], Opt = strs[1] });
        }
        return (cntPos, parts);
    }

    // The group name also appears once more in a plain name list near the end of the node (followed by another string,
    // not by a parts list). Exactly one occurrence must be followed by a valid parts list.
    static (int cntPos, List<Part> parts) Group(byte[] d, byte[] name)
    {
        var tag = new byte[name.Length + 1]; tag[0] = (byte)(0x80 | name.Length); name.CopyTo(tag, 1);
        var found = new List<(int, List<Part>)>();
        for (int at = IndexOf(d, tag, 0); at >= 0; at = IndexOf(d, tag, at + 1))
            if (TryGroup(d, at + tag.Length) is { } g) found.Add(g);
        if (found.Count != 1) throw Layout($"group {Encoding.ASCII.GetString(name)}: found {found.Count} parts lists (expected 1)");
        return found[0];
    }

    public sealed class Diagnosis
    {
        public string State = "";          // broken | healthy | other
        public int CntPos;
        public List<Part> Fpp = new();
    }

    /// <summary>broken (repairable), healthy, or other (not this problem); throws Refuse for anything unsure.</summary>
    public static Diagnosis Diagnose(byte[] d, int child)
    {
        if (child != -1) throw Layout($"{NodeName} has child nodes");
        var (_, tpp) = Group(d, GTpp);
        var (cntPos, fpp) = Group(d, GFpp);
        var (_, unh) = Group(d, GUnh);
        var moxT = tpp.Where(x => x.Opt == MoxTpp).ToList();
        var moxU = unh.Where(x => x.Opt == MoxUnh).ToList();
        var res = new Diagnosis { CntPos = cntPos, Fpp = fpp };
        if (fpp.Any(x => x.Opt.Contains("_mox_"))) { res.State = "healthy"; return res; }
        if (moxT.Count == 0 || moxU.Count == 0) { res.State = "other"; return res; }
        var apps = moxT.Concat(moxU).Select(x => x.App).Distinct().ToList();
        if (apps.Count != 1 || apps[0] != "male")
        {
            if (apps.Any(a => a.Contains("female"))) throw new Refuse(L.T("save.refuse.female"));
            throw Layout("Mox parts use appearance " + string.Join(", ", apps.OrderBy(a => a, StringComparer.Ordinal)) + " (expected male)");
        }
        var opts = fpp.Select(x => x.Opt).ToList();
        if (fpp.Count != 3 || opts[0] != "holstered_data" || opts[1] != "h_strong_arms_colors_base_fpp" || !opts[2].Contains("personal_link"))
            throw Layout("first-person holstered parts are not the known broken layout: " + string.Join(", ", opts));
        if (fpp[0].App != "holstered_arms_data_male") throw Layout("first-person holstered data part is not the male one: " + fpp[0].App);
        res.State = "broken";
        return res;
    }

    // ------------------------------------------------------------------ metadata.9.json
    sealed class Meta { public string Text = ""; public string Name = ""; public long? FileSize; public string? Gender; }

    static Meta ReadMeta(string dir)
    {
        var bytes = File.ReadAllBytes(Path.Combine(dir, "metadata.9.json"));
        var text = new UTF8Encoding(false).GetString(bytes);
        using var doc = JsonDocument.Parse(text.TrimStart('﻿'));
        var m = doc.RootElement.GetProperty("Data").GetProperty("metadata");
        return new Meta
        {
            Text = text,
            Name = m.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "",
            FileSize = m.TryGetProperty("fileSize", out var f) && f.TryGetInt64(out var fl) ? fl : null,
            Gender = m.TryGetProperty("bodyGender", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : null,
        };
    }

    // ------------------------------------------------------------------ checking a save
    public sealed class Check
    {
        public string Folder = "";
        public string Name = "";
        public DateTime When;
        public string State = "";           // broken | healthy | other | refused
        public string Message = "";         // token
        public bool LooksLikeIt;            // refused, but the Mox look is stored without its first-person part
    }

    /// <summary>The state of one save folder. A scan uses the quick read; Repair does the full one.</summary>
    public static Check CheckFolder(string dir, bool full = false)
    {
        var c = new Check { Folder = dir, Name = Path.GetFileName(dir.TrimEnd('\\', '/')) };
        var sav = Path.Combine(dir, "sav.dat");
        try
        {
            c.When = File.GetLastWriteTime(sav);
            Node nd; byte[] data;
            if (full) { var s = SavFile.Parse(File.ReadAllBytes(sav)); nd = s.Node(NodeName); data = s.Data(nd); }
            else (nd, data) = QuickNode(sav);
            Diagnosis dx;
            try { dx = Diagnose(data, nd.Child); }
            catch (Refuse r)
            {
                // A Mox look with no first-person Mox part, which this repair is not sure about: say so, but offer nothing.
                c.LooksLikeIt = LooksLikeIt(data);
                throw new Refuse(r.Token);
            }
            if (dx.State != "broken") { c.State = dx.State; c.Message = L.T(dx.State == "healthy" ? "save.state.healthy" : "save.state.other"); return c; }
            c.LooksLikeIt = true;
            var meta = ReadMeta(dir);
            if (meta.FileSize != new FileInfo(sav).Length) throw new Refuse(L.T("save.refuse.metaSize"));
            if (meta.Gender != "Male") throw new Refuse(L.T("save.refuse.female"));
            c.State = "broken"; c.Message = L.T("save.state.broken");
        }
        catch (Refuse r) { c.State = "refused"; c.Message = r.Token; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        { c.State = "refused"; c.Message = L.T("save.refuse.unreadable", e.Message); }
        return c;
    }

    static bool LooksLikeIt(byte[] d)
    {
        try
        {
            var (_, tpp) = Group(d, GTpp); var (_, fpp) = Group(d, GFpp); var (_, unh) = Group(d, GUnh);
            return tpp.Any(x => x.Opt == MoxTpp) && unh.Any(x => x.Opt == MoxUnh) && !fpp.Any(x => x.Opt.Contains("_mox_"));
        }
        catch (Refuse) { return false; }
    }

    /// <summary>Every save folder in the game's save folder, newest first (a folder with a sav.dat).</summary>
    public static List<string> SaveFolders(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return new();
            return Directory.EnumerateDirectories(root).Where(d => File.Exists(Path.Combine(d, "sav.dat")) && File.Exists(Path.Combine(d, "metadata.9.json")))
                .OrderByDescending(d => File.GetLastWriteTime(Path.Combine(d, "sav.dat"))).ToList();
        }
        catch { return new(); }
    }

    // ------------------------------------------------------------------ the repair
    public sealed class Result
    {
        public bool Ok;
        public string Message = "";        // token
        public string? OutDir;
        public string? SrcMd5, OutMd5;
        public long OldSize, NewSize;
        public long InsertAt;
        public string Chunks = "";
    }

    /// <summary>Repair <paramref name="srcDir"/> into the NEW folder <paramref name="outDir"/>. Never writes the original.</summary>
    public static Result Repair(string srcDir, string outDir)
    {
        srcDir = Path.GetFullPath(srcDir); outDir = Path.GetFullPath(outDir);
        if (string.Equals(srcDir.TrimEnd('\\'), outDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new Refuse(L.T("save.refuse.sameFolder"));
        if (Directory.Exists(outDir) || File.Exists(outDir)) throw new Refuse(L.T("save.refuse.exists", Path.GetFileName(outDir)));
        var raw = File.ReadAllBytes(Path.Combine(srcDir, "sav.dat"));
        var meta = ReadMeta(srcDir);
        if (meta.FileSize != raw.Length) throw new Refuse(L.T("save.refuse.metaSize"));
        if (meta.Gender != "Male") throw new Refuse(L.T("save.refuse.female"));
        var sav = SavFile.Parse(raw);
        var cc = sav.Node(NodeName);
        var d = sav.Data(cc);
        var dx = Diagnose(d, cc.Child);
        if (dx.State != "broken") throw new Refuse(L.T(dx.State == "healthy" ? "save.state.healthy" : "save.state.other"));

        int ins = dx.Fpp[2].Start;                                  // before the personal link part, where healthy saves have it
        var newD = new byte[d.Length + MoxPart.Length];
        Array.Copy(d, 0, newD, 0, ins); MoxPart.CopyTo(newD, ins); Array.Copy(d, ins, newD, ins + MoxPart.Length, d.Length - ins);
        BitConverter.TryWriteBytes(newD.AsSpan(dx.CntPos, 4), 4u);
        long P = cc.Off + ins;                                      // absolute (virtual) insert position
        int rel = cc.Off - sav.Base, L5 = MoxPart.Length;
        var blob = new byte[sav.Blob.Length + L5];
        Array.Copy(sav.Blob, 0, blob, 0, rel); newD.CopyTo(blob, rel);
        Array.Copy(sav.Blob, rel + cc.Size, blob, rel + newD.Length, sav.Blob.Length - rel - cc.Size);
        var nodes = sav.Nodes.Select(nd =>
        {
            var n2 = nd.Copy();
            if (nd.Off >= P) n2.Off += L5;
            else if ((long)nd.Off + nd.Size > P) n2.Size += L5;    // the node itself (and any container) grows
            return n2;
        }).ToList();
        var newRaw = BuildSav(sav, blob, nodes);

        // metadata: only "name" (= the new folder name) and "fileSize", by text replacement so the layout stays
        var name = Path.GetFileName(outDir);
        var r1 = new Regex("(\"fileSize\":\\s*)" + raw.Length + "\\b");
        var r2 = new Regex("(\"name\":\\s*)\"" + Regex.Escape(meta.Name) + "\"");
        if (r1.Matches(meta.Text).Count != 1 || r2.Matches(meta.Text).Count != 1) throw Layout("metadata layout not understood");
        var newMeta = r1.Replace(meta.Text, m => m.Groups[1].Value + newRaw.Length);
        newMeta = r2.Replace(newMeta, m => m.Groups[1].Value + JsonSerializer.Serialize(name));
        using (var jd = JsonDocument.Parse(newMeta.TrimStart('﻿')))
        {
            var jm = jd.RootElement.GetProperty("Data").GetProperty("metadata");
            if (jm.GetProperty("fileSize").GetInt64() != newRaw.Length || jm.GetProperty("name").GetString() != name) throw Layout("metadata rewrite failed");
        }

        Verify(sav, newRaw, P, L5);                                 // before anything is written
        Directory.CreateDirectory(outDir);
        try
        {
            File.WriteAllBytes(Path.Combine(outDir, "sav.dat"), newRaw);
            File.WriteAllBytes(Path.Combine(outDir, "metadata.9.json"), new UTF8Encoding(false).GetBytes(newMeta));
            var shot = Path.Combine(srcDir, "screenshot.png");
            if (File.Exists(shot)) File.Copy(shot, Path.Combine(outDir, "screenshot.png"));
            if (!File.ReadAllBytes(Path.Combine(outDir, "sav.dat")).AsSpan().SequenceEqual(newRaw)) throw Layout("the written file does not read back the same");
        }
        catch
        {
            // a half-written copy must not be left for the game to list; this folder was created a moment ago by us
            try { Directory.Delete(outDir, true); } catch { }
            throw;
        }
        return new Result
        {
            Ok = true, OutDir = outDir, SrcMd5 = Md5(raw), OutMd5 = Md5(newRaw), OldSize = raw.Length, NewSize = newRaw.Length, InsertAt = P,
            Chunks = $"{sav.Ents.Count} -> {SavFile.Parse(newRaw).Ents.Count}",
            Message = L.T("save.repaired", Path.GetFileName(srcDir), name),
        };
    }

    /// <summary>Rebuild sav.dat: header unchanged, chunks re-cut at 262144 bytes, unchanged chunks keep their old
    /// compressed bytes, the node table patched in place (only the offset/size fields).</summary>
    static byte[] BuildSav(SavFile old, byte[] blob, List<Node> nodes)
    {
        int pieces = (blob.Length + Chunk - 1) / Chunk;
        if (pieces > TableCap) throw Layout("save too large for the chunk table");
        var head = new byte[First];
        Array.Copy(old.Raw, 0, head, 0, 25);
        "FZLC"u8.CopyTo(head.AsSpan(25)); BitConverter.TryWriteBytes(head.AsSpan(29), pieces);
        var bodies = new List<byte[]>(); int pos = First;
        for (int k = 0; k < pieces; k++)
        {
            int a = k * Chunk, len = Math.Min(Chunk, blob.Length - a);
            byte[] body;
            if (k < old.Ents.Count - 1 && old.Blob.AsSpan(k * Chunk, Math.Min(Chunk, old.Blob.Length - k * Chunk)).SequenceEqual(blob.AsSpan(a, len)))
            {
                var (o, c, _) = old.Ents[k];
                body = old.Raw.AsSpan(o, c).ToArray();
            }
            else
            {
                var pc = blob.AsSpan(a, len).ToArray();
                var comp = Lz4Compress(pc);
                body = new byte[8 + comp.Length];
                "4ZLX"u8.CopyTo(body); BitConverter.TryWriteBytes(body.AsSpan(4), (uint)len); comp.CopyTo(body, 8);
            }
            BitConverter.TryWriteBytes(head.AsSpan(33 + 12 * k), pos);
            BitConverter.TryWriteBytes(head.AsSpan(37 + 12 * k), body.Length);
            BitConverter.TryWriteBytes(head.AsSpan(41 + 12 * k), len);
            bodies.Add(body); pos += body.Length;
        }
        var outS = new MemoryStream();
        outS.Write(head);
        foreach (var b in bodies) outS.Write(b);
        int edon = (int)outS.Length;
        var tail = old.Raw.AsSpan(old.Edon, old.Raw.Length - 8 - old.Edon).ToArray();
        foreach (var nd in nodes) { BitConverter.TryWriteBytes(tail.AsSpan(nd.FPos + 8), nd.Off); BitConverter.TryWriteBytes(tail.AsSpan(nd.FPos + 12), nd.Size); }
        outS.Write(tail);
        outS.Write(BitConverter.GetBytes(edon)); outS.Write("ENOD"u8);
        return outS.ToArray();
    }

    /// <summary>Re-read the new file and prove nothing but the one node changed (SPEC.md section 4, step 7).</summary>
    static void Verify(SavFile old, byte[] newRaw, long P, int len)
    {
        Refuse Fail(string why) => Layout("verification failed: " + why);
        var nw = SavFile.Parse(newRaw);
        var ncc = nw.Node(NodeName);
        if (Diagnose(nw.Data(ncc), ncc.Child).State != "healthy") throw Fail("repaired save is not healthy");
        if (nw.Nodes.Count != old.Nodes.Count) throw Fail("node count changed");
        for (int i = 0; i < old.Nodes.Count; i++)
        {
            var a = old.Nodes[i]; var b = nw.Nodes[i];
            if (a.Name != b.Name || a.Next != b.Next || a.Child != b.Child) throw Fail($"node tree changed at {a.I}");
            if (a.Name == NodeName)
            {
                var da = old.Data(a); var db = nw.Data(b); int k = (int)(P - a.Off);
                if (b.Size != a.Size + len || !db.AsSpan(k, len).SequenceEqual(MoxPart)) throw Fail($"{NodeName} not as expected");
                var fix = db.Take(k).Concat(db.Skip(k + len)).ToArray();
                var diff = Enumerable.Range(0, da.Length).Where(x => da[x] != fix[x]).ToList();
                if (diff.Count != 1 || da[diff[0]] != 3 || fix[diff[0]] != 4) throw Fail($"unexpected byte changes in {NodeName}");
            }
            else if (!old.Data(a).AsSpan().SequenceEqual(nw.Data(b))) throw Fail($"node {a.I} {a.Name} changed");
        }
        // bytes outside any node too: the whole data with the insert taken out = the old data with the count set to 4
        var expect = (byte[])old.Blob.Clone();
        var occ = old.Node(NodeName);
        var (cntPos, _) = Group(old.Data(occ), GFpp);
        BitConverter.TryWriteBytes(expect.AsSpan(occ.Off - old.Base + cntPos), 4u);
        int pv = (int)(P - nw.Base);
        if (nw.Blob.Length != expect.Length + len || !nw.Blob.AsSpan(0, pv).SequenceEqual(expect.AsSpan(0, pv)) || !nw.Blob.AsSpan(pv + len).SequenceEqual(expect.AsSpan(pv)))
            throw Fail("data outside the node changed");
    }

    public static string Md5(byte[] b) => Convert.ToHexString(MD5.HashData(b)).ToLowerInvariant();

    // ------------------------------------------------------------------ in the app: the save folder, the record, Undo
    public sealed class RepairRecord
    {
        public string Source { get; set; } = "";      // folder name of the broken save
        public string SourceMd5 { get; set; } = "";
        public string Created { get; set; } = "";     // folder name of the repaired copy
        public string CreatedMd5 { get; set; } = "";
        public DateTime At { get; set; }
        public bool Undone { get; set; }
    }

    static string RecordFile => Path.Combine(GamePaths.AppData, "save-repairs.json");
    public static List<RepairRecord> Records()
    {
        try { if (File.Exists(RecordFile)) return JsonSerializer.Deserialize<List<RepairRecord>>(File.ReadAllText(RecordFile), Scanner.Json) ?? new(); } catch { }
        return new();
    }
    static void SaveRecords(List<RepairRecord> l)
    {
        Directory.CreateDirectory(GamePaths.AppData);
        File.WriteAllText(RecordFile, JsonSerializer.Serialize(l, Scanner.Json));
    }

    /// <summary>The next free "ManualSave-N", above the highest number in the folder, so the game lists it as a manual save.</summary>
    public static string NextName(string root)
    {
        int max = 0;
        try
        {
            foreach (var d in Directory.EnumerateDirectories(root))
            {
                var m = Regex.Match(Path.GetFileName(d), @"^ManualSave-(\d+)$", RegexOptions.IgnoreCase);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var v)) max = Math.Max(max, v);
            }
        }
        catch { }
        return "ManualSave-" + (max + 1);
    }

    /// <summary>The Repair button: a repaired copy of save folder <paramref name="name"/> under the next free name.</summary>
    public static Result RepairInSaveFolder(string name)
    {
        try
        {
            if (GameLocator.GameRunning()) return new Result { Message = L.T("save.refuse.gameRunning") };
            var root = GamePaths.SavedGames;
            var src = Path.Combine(root, name);
            if (Path.GetFileName(name) != name || !Directory.Exists(src)) return new Result { Message = L.T("save.refuse.gone", name) };
            var outName = NextName(root);
            var res = Repair(src, Path.Combine(root, outName));
            var recs = Records();
            recs.Add(new RepairRecord { Source = name, SourceMd5 = res.SrcMd5 ?? "", Created = outName, CreatedMd5 = res.OutMd5 ?? "", At = DateTime.Now });
            try { SaveRecords(recs); } catch { }
            return res;
        }
        catch (Refuse r) { return new Result { Message = L.T("save.notRepaired", r.Token) }; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        { return new Result { Message = L.T("save.notRepaired", L.T("save.refuse.unreadable", e.Message)) }; }
    }

    /// <summary>Undo: removes the copy Crash Doctor made - only while it is still exactly what was written.</summary>
    public static Result Undo(string created)
    {
        var recs = Records();
        var rec = recs.LastOrDefault(r => r.Created.Equals(created, StringComparison.OrdinalIgnoreCase) && !r.Undone);
        if (rec == null) return new Result { Message = L.T("save.undo.unknown", created) };
        if (GameLocator.GameRunning()) return new Result { Message = L.T("save.refuse.gameRunning") };
        var dir = Path.Combine(GamePaths.SavedGames, rec.Created);
        try
        {
            if (Directory.Exists(dir))
            {
                var sav = Path.Combine(dir, "sav.dat");
                if (!File.Exists(sav) || Md5(File.ReadAllBytes(sav)) != rec.CreatedMd5) return new Result { Message = L.T("save.undo.changed", rec.Created) };
                var allowed = new[] { "sav.dat", "metadata.9.json", "screenshot.png" };
                if (Directory.EnumerateFileSystemEntries(dir).Any(f => !allowed.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))) return new Result { Message = L.T("save.undo.changed", rec.Created) };
                Directory.Delete(dir, true);
            }
            rec.Undone = true; SaveRecords(recs);
            return new Result { Ok = true, Message = L.T("save.undo.done", rec.Created, rec.Source) };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new Result { Message = L.T("save.undo.failed", rec.Created, e.Message) }; }
    }

    /// <summary>What a scan shows: one Health item per broken save (newest first, at most five), the copies made, and
    /// saves that look the same but are not repairable yet.</summary>
    public static List<HealthItem> Health(int max = 400)
    {
        var items = new List<HealthItem>();
        var root = GamePaths.SavedGames;
        var folders = SaveFolders(root).Take(max).ToList();
        if (folders.Count == 0) return items;
        var recs = Records().Where(r => !r.Undone).ToList();
        var checks = folders.Select(f => CheckFolder(f)).ToList();
        var broken = checks.Where(c => c.State == "broken").ToList();
        foreach (var c in broken.Take(5))
        {
            string? srcMd5 = null;
            var done = recs.LastOrDefault(r => r.Source.Equals(c.Name, StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(root, r.Created))
                                              && r.SourceMd5 == (srcMd5 ??= SafeMd5(Path.Combine(c.Folder, "sav.dat"))));
            if (done != null)
                items.Add(new HealthItem
                {
                    Severity = "info", Title = L.T("save.health.doneTitle", c.Name, done.Created),
                    Detail = L.T("save.health.doneDetail", done.Created, L.Date(done.At, "dmt"), c.Name),
                    Action = new UiAction("savefix:undo:" + done.Created, L.T("save.action.undo")),
                });
            else
                items.Add(new HealthItem
                {
                    Severity = "medium", Title = L.T("save.health.title", c.Name, L.Date(c.When, "dmt")),
                    Detail = L.Cat(L.T("save.health.detail"), L.T("save.health.repair", NextName(root), c.Name)),
                    Action = new UiAction("savefix:repair:" + c.Name, L.T("save.action.repair")),
                });
        }
        if (broken.Count > 5)
            items.Add(new HealthItem { Severity = "info", Title = L.P("save.health.more", broken.Count - 5), Detail = L.T("save.health.moreDetail", L.List(broken.Skip(5).Select(c => c.Name))) });
        var unsure = checks.Where(c => c.State == "refused" && c.LooksLikeIt).ToList();
        if (unsure.Count > 0)
            items.Add(new HealthItem
            {
                Severity = "info", Title = L.P("save.health.unsureTitle", unsure.Count),
                Detail = L.Cat(L.T("save.health.unsureDetail"), L.List(unsure.Take(5).Select(c => c.Name + " - " + c.Message).ToList()))
            });
        return items;
    }

    static string? SafeMd5(string f) { try { return Md5(File.ReadAllBytes(f)); } catch { return null; } }
}

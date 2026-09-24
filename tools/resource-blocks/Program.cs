// Lists and swaps blocks of compiled Source 2 resources (*_c files).
//   dotnet run -- list <file>...
//   dotnet run -- swap <target> <donor> <out> <BLOCK>
// Used to give the jersey models the stock ragdoll joints, see
// docs/jerseys/2026-09-24-ragdoll-joints.md.
using System.Text;
static (ushort hv, ushort ver, List<(string Type, byte[] Data)> blocks, byte[] raw) Read(string path)
{
    var b = File.ReadAllBytes(path);
    var fileSize = BitConverter.ToUInt32(b, 0);
    var hv = BitConverter.ToUInt16(b, 4); var ver = BitConverter.ToUInt16(b, 6);
    var blockOffsetPos = 8; var blockOffset = BitConverter.ToUInt32(b, 8); var count = BitConverter.ToUInt32(b, 12);
    var table = blockOffsetPos + (int)blockOffset;
    var blocks = new List<(string, byte[])>();
    for (var i = 0; i < count; i++)
    {
        var e = table + i * 12;
        var type = Encoding.ASCII.GetString(b, e, 4);
        var off = e + 4 + (int)BitConverter.ToUInt32(b, e + 4);
        var size = (int)BitConverter.ToUInt32(b, e + 8);
        blocks.Add((type, b[off..(off + size)]));
    }
    return (hv, ver, blocks, b);
}
if (args[0] == "list")
{
    foreach (var p in args.Skip(1))
    {
        var r = Read(p);
        Console.WriteLine($"{Path.GetFileName(p)}: size={r.raw.Length} headerVersion={r.hv} version={r.ver} blockOffset={BitConverter.ToUInt32(r.raw, 8)} blocks={string.Join(" ", r.blocks.Select(x => x.Type + ":" + x.Data.Length))}");
    }
}
if (args[0] == "swap")
{
    // swap <target> <donor> <out> <blockType>: replace one block of target with donor bytes.
    var t = File.ReadAllBytes(args[1]); var d = Read(args[2]); var type = args[4];
    var donor = d.blocks.Single(x => x.Type == type).Data;
    var count = (int)BitConverter.ToUInt32(t, 12); var table = 8 + (int)BitConverter.ToUInt32(t, 8);
    var entries = new List<(int Index, string Type, int Offset, int Size)>();
    for (var i = 0; i < count; i++)
    {
        var e = table + i * 12;
        entries.Add((i, Encoding.ASCII.GetString(t, e, 4), e + 4 + (int)BitConverter.ToUInt32(t, e + 4), (int)BitConverter.ToUInt32(t, e + 8)));
    }
    var dataStart = entries.Min(x => x.Offset);
    using var ms = new MemoryStream();
    ms.Write(t, 0, dataStart); // header + block table (offsets patched below), kept as-is
    var newPos = new Dictionary<int, (int Offset, int Size)>();
    foreach (var e in entries.OrderBy(x => x.Offset))
    {
        while (ms.Position % 16 != 0) ms.WriteByte(0);
        var bytes = e.Type == type ? donor : t[e.Offset..(e.Offset + e.Size)];
        newPos[e.Index] = ((int)ms.Position, bytes.Length);
        ms.Write(bytes);
    }
    var o = ms.ToArray();
    BitConverter.GetBytes((uint)o.Length).CopyTo(o, 0);
    foreach (var e in entries)
    {
        var at = table + e.Index * 12;
        BitConverter.GetBytes((uint)(newPos[e.Index].Offset - (at + 4))).CopyTo(o, at + 4);
        BitConverter.GetBytes((uint)newPos[e.Index].Size).CopyTo(o, at + 8);
    }
    File.WriteAllBytes(args[3], o);
    // Check: every other block is byte-identical, the swapped one is the donor.
    var check = Read(args[3]); var orig = Read(args[1]);
    var same = check.blocks.Zip(orig.blocks).All(p => p.First.Type == type ? p.First.Data.SequenceEqual(donor) : p.First.Data.SequenceEqual(p.Second.Data));
    Console.WriteLine($"{Path.GetFileName(args[3])}: {o.Length} bytes, other blocks identical and {type} = donor: {same}");
}

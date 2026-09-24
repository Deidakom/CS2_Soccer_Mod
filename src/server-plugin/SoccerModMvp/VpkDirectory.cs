#nullable enable
using System.Text;

namespace SoccerModMvp;

// Reads the file list of a Source 2 VPK (version 1 or 2) from its directory
// tree, e.g. "models/soccermod/kits/kit_home.vmdl_c". Used to check that a
// Workshop addon the server mounted really contains the kit models before a
// player is switched to one: a missing player model leaves that player on a
// black screen.
internal static class VpkDirectory
{
    private const uint Signature = 0x55AA1234;

    public static HashSet<string> ReadEntries(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != Signature)
        {
            throw new InvalidDataException("not a VPK directory file");
        }

        var version = reader.ReadUInt32();
        var treeSize = reader.ReadUInt32();
        if (version == 2)
        {
            reader.ReadBytes(16); // file data, archive MD5, other MD5 and signature section sizes
        }
        else if (version != 1)
        {
            throw new InvalidDataException($"unsupported VPK version {version}");
        }

        var treeEnd = stream.Position + treeSize;
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (ReadString(reader, treeEnd) is { Length: > 0 } extension)
        {
            while (ReadString(reader, treeEnd) is { Length: > 0 } directory)
            {
                while (ReadString(reader, treeEnd) is { Length: > 0 } name)
                {
                    reader.ReadUInt32(); // CRC
                    var preloadBytes = reader.ReadUInt16();
                    reader.ReadBytes(2 + 4 + 4); // archive index, entry offset, entry length
                    if (reader.ReadUInt16() != 0xFFFF)
                    {
                        throw new InvalidDataException("VPK entry terminator missing");
                    }

                    stream.Seek(preloadBytes, SeekOrigin.Current);
                    var folder = directory == " " ? "" : directory + "/";
                    entries.Add(folder + name + (extension == " " ? "" : "." + extension));
                }
            }
        }

        return entries;
    }

    public static bool TryReadEntries(string path, out HashSet<string> entries)
    {
        try
        {
            using var stream = File.OpenRead(path);
            entries = ReadEntries(stream);
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or EndOfStreamException)
        {
            entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return false;
        }
    }

    private static string? ReadString(BinaryReader reader, long end)
    {
        var bytes = new List<byte>();
        while (reader.BaseStream.Position < end)
        {
            var value = reader.ReadByte();
            if (value == 0)
            {
                return Encoding.UTF8.GetString(bytes.ToArray());
            }

            bytes.Add(value);
        }

        throw new InvalidDataException("VPK directory tree is truncated");
    }
}

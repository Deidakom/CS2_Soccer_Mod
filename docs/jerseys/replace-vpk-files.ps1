# Replace selected entries in a VPK while preserving every other entry's raw
# bytes. This is used for texture-only Workshop revisions so a fresh resource
# compiler cannot accidentally rewrite approved model/material resources.

param(
    [Parameter(Mandatory = $true)] [string]$BaseVpk,
    [Parameter(Mandatory = $true)] [string]$OutVpk,
    [Parameter(Mandatory = $true)] [string[]]$Replacement
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $BaseVpk -PathType Leaf)) {
    throw "Base VPK not found: $BaseVpk"
}

foreach ($spec in $Replacement) {
    $separator = $spec.IndexOf('|')
    if ($separator -le 0 -or $separator -ge ($spec.Length - 1)) {
        throw "Replacement must use relative-vpk-path|source-file: $spec"
    }
}

if (-not ("VpkEntryReplacer" -as [type])) {
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public static class VpkEntryReplacer
{
    private sealed class Entry
    {
        public string Extension = "";
        public string Directory = "";
        public string Name = "";
        public uint Crc;
        public ushort ArchiveIndex;
        public byte[] Preload = Array.Empty<byte>();
        public byte[] Payload = Array.Empty<byte>();
        public uint Offset;
        public uint Length;

        public string Path => (Directory == " " ? "" : Directory + "/") + Name + "." + Extension;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }

    private static uint Crc32(byte[] bytes)
    {
        var value = 0xFFFFFFFFu;
        foreach (var item in bytes)
        {
            value = CrcTable[(value ^ item) & 0xFF] ^ (value >> 8);
        }

        return value ^ 0xFFFFFFFFu;
    }

    private static string ReadCString(byte[] tree, ref int position)
    {
        var start = position;
        while (position < tree.Length && tree[position] != 0)
        {
            position++;
        }

        if (position >= tree.Length)
        {
            throw new InvalidDataException("VPK directory tree has an unterminated string.");
        }

        var value = Encoding.ASCII.GetString(tree, start, position - start);
        position++;
        return value;
    }

    private static byte[] ReadBytes(BinaryReader reader, int count)
    {
        if (count < 0)
        {
            throw new InvalidDataException("Negative VPK section size.");
        }

        var bytes = reader.ReadBytes(count);
        if (bytes.Length != count)
        {
            throw new EndOfStreamException("VPK ended before a complete section was read.");
        }

        return bytes;
    }

    private static Dictionary<string, byte[]> LoadReplacements(string[] specifications)
    {
        var replacements = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var specification in specifications)
        {
            var separator = specification.IndexOf('|');
            var path = specification.Substring(0, separator).Replace('\\', '/');
            var source = specification.Substring(separator + 1);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("Replacement file not found.", source);
            }

            if (!replacements.TryAdd(path, File.ReadAllBytes(source)))
            {
                throw new InvalidDataException("Duplicate VPK replacement path: " + path);
            }
        }

        return replacements;
    }

    private static void WriteCString(BinaryWriter writer, string value)
    {
        writer.Write(Encoding.ASCII.GetBytes(value));
        writer.Write((byte)0);
    }

    private static void WriteEntry(BinaryWriter writer, Entry entry)
    {
        WriteCString(writer, entry.Name);
        writer.Write(entry.Crc);
        writer.Write((ushort)entry.Preload.Length);
        writer.Write(entry.ArchiveIndex);
        writer.Write(entry.Offset);
        writer.Write(entry.Length);
        writer.Write((ushort)0xFFFF);
        writer.Write(entry.Preload);
    }

    private static byte[] BuildTree(IReadOnlyList<Entry> entries)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        string currentExtension = null;
        string currentDirectory = null;

        foreach (var entry in entries)
        {
            if (!String.Equals(currentExtension, entry.Extension, StringComparison.Ordinal))
            {
                if (currentDirectory != null)
                {
                    writer.Write((byte)0);
                }

                if (currentExtension != null)
                {
                    writer.Write((byte)0);
                }

                WriteCString(writer, entry.Extension);
                currentExtension = entry.Extension;
                currentDirectory = null;
            }

            if (!String.Equals(currentDirectory, entry.Directory, StringComparison.Ordinal))
            {
                if (currentDirectory != null)
                {
                    writer.Write((byte)0);
                }

                WriteCString(writer, entry.Directory);
                currentDirectory = entry.Directory;
            }

            WriteEntry(writer, entry);
        }

        if (currentDirectory != null)
        {
            writer.Write((byte)0);
        }

        if (currentExtension != null)
        {
            writer.Write((byte)0);
        }

        writer.Write((byte)0);
        writer.Flush();
        return stream.ToArray();
    }

    public static string Replace(string basePath, string outputPath, string[] specifications)
    {
        var replacements = LoadReplacements(specifications);
        var entries = new List<Entry>();
        byte[] archiveMd5Section;
        byte[] signatureSection;
        uint version;
        uint otherMd5Size;

        using (var input = File.OpenRead(basePath))
        using (var reader = new BinaryReader(input, Encoding.ASCII, leaveOpen: false))
        {
            var signature = reader.ReadUInt32();
            version = reader.ReadUInt32();
            var treeSize = reader.ReadUInt32();
            var dataSize = reader.ReadUInt32();
            var archiveMd5Size = reader.ReadUInt32();
            otherMd5Size = reader.ReadUInt32();
            var signatureSize = reader.ReadUInt32();
            if (signature != 0x55AA1234u || version != 2u)
            {
                throw new InvalidDataException("Only VPK v2 files written by the jersey packer are supported.");
            }

            if (otherMd5Size != 48u)
            {
                throw new InvalidDataException("Expected a 48-byte VPK other-MD5 section.");
            }

            var tree = ReadBytes(reader, checked((int)treeSize));
            var data = ReadBytes(reader, checked((int)dataSize));
            archiveMd5Section = ReadBytes(reader, checked((int)archiveMd5Size));
            _ = ReadBytes(reader, checked((int)otherMd5Size));
            signatureSection = ReadBytes(reader, checked((int)signatureSize));

            var position = 0;
            while (position < tree.Length)
            {
                var extension = ReadCString(tree, ref position);
                if (extension.Length == 0)
                {
                    break;
                }

                while (true)
                {
                    var directory = ReadCString(tree, ref position);
                    if (directory.Length == 0)
                    {
                        break;
                    }

                    while (true)
                    {
                        var name = ReadCString(tree, ref position);
                        if (name.Length == 0)
                        {
                            break;
                        }

                        if (position + 18 > tree.Length)
                        {
                            throw new InvalidDataException("VPK entry metadata is truncated.");
                        }

                        var entry = new Entry
                        {
                            Extension = extension,
                            Directory = directory,
                            Name = name,
                            Crc = BitConverter.ToUInt32(tree, position),
                        };
                        position += 4;
                        var preloadLength = BitConverter.ToUInt16(tree, position);
                        position += 2;
                        entry.ArchiveIndex = BitConverter.ToUInt16(tree, position);
                        position += 2;
                        entry.Offset = BitConverter.ToUInt32(tree, position);
                        position += 4;
                        entry.Length = BitConverter.ToUInt32(tree, position);
                        position += 4;
                        var terminator = BitConverter.ToUInt16(tree, position);
                        position += 2;
                        if (terminator != 0xFFFF)
                        {
                            throw new InvalidDataException("VPK entry metadata terminator is invalid.");
                        }

                        if (entry.ArchiveIndex != 0x7FFF)
                        {
                            throw new InvalidDataException("Archive-split VPK entries are not supported: " + entry.Path);
                        }

                        if (position + preloadLength > tree.Length)
                        {
                            throw new InvalidDataException("VPK preload data is truncated.");
                        }

                        entry.Preload = tree.Skip(position).Take(preloadLength).ToArray();
                        position += preloadLength;
                        if ((ulong)entry.Offset + entry.Length > (ulong)data.Length)
                        {
                            throw new InvalidDataException("VPK entry points outside its data section: " + entry.Path);
                        }

                        entry.Payload = data.Skip(checked((int)entry.Offset)).Take(checked((int)entry.Length)).ToArray();
                        entries.Add(entry);
                    }
                }
            }
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        using var newData = new MemoryStream();
        foreach (var entry in entries)
        {
            if (replacements.TryGetValue(entry.Path, out var replacement))
            {
                entry.Payload = replacement;
                found.Add(entry.Path);
            }

            entry.Offset = checked((uint)newData.Position);
            entry.Length = checked((uint)entry.Payload.Length);
            entry.Crc = Crc32(entry.Payload);
            newData.Write(entry.Payload, 0, entry.Payload.Length);
        }

        var missing = replacements.Keys.Where(path => !found.Contains(path)).ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidDataException("Replacement paths were not found in the base VPK: " + String.Join(", ", missing));
        }

        var newTree = BuildTree(entries);
        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(0x55AA1234u);
            writer.Write(version);
            writer.Write(checked((uint)newTree.Length));
            writer.Write(checked((uint)newData.Length));
            writer.Write(checked((uint)archiveMd5Section.Length));
            writer.Write(otherMd5Size);
            writer.Write(checked((uint)signatureSection.Length));
            writer.Write(newTree);
            writer.Write(newData.ToArray());
            writer.Write(archiveMd5Section);

            using var md5 = MD5.Create();
            var treeHash = md5.ComputeHash(newTree);
            var archiveHash = md5.ComputeHash(archiveMd5Section);
            writer.Write(treeHash);
            writer.Write(archiveHash);
            writer.Flush();
            var wholeHash = md5.ComputeHash(output.ToArray());
            writer.Write(wholeHash);
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(outputPath)));
        File.WriteAllBytes(outputPath, output.ToArray());
        return $"replaced {found.Count} entries; preserved {entries.Count - found.Count} entries";
    }
}
"@
}

$result = [VpkEntryReplacer]::Replace($BaseVpk, $OutVpk, $Replacement)
Write-Host $result
Write-Host ("VPK: {0} ({1:N0} bytes)" -f $OutVpk, (Get-Item -LiteralPath $OutVpk).Length)
Write-Host ("SHA-256: " + (Get-FileHash -LiteralPath $OutVpk -Algorithm SHA256).Hash)

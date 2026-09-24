using System.Text;
using SoccerModMvp;

internal static class VpkDirectoryChecks
{
    internal static void Run()
    {
        static void Check(bool condition, string why) { if (!condition) throw new Exception(why); }

        // A jersey addon as Workshop Tools packs it: VPK 2, grouped by extension
        // and folder, one entry with preload bytes, one file in the root.
        byte[] Vpk(uint version, bool truncate = false)
        {
            var tree = new MemoryStream();
            void Text(string value) { var bytes = Encoding.UTF8.GetBytes(value); tree.Write(bytes); tree.WriteByte(0); }
            void Entry(ushort preload)
            {
                tree.Write(BitConverter.GetBytes(0x12345678u));
                tree.Write(BitConverter.GetBytes(preload));
                tree.Write(BitConverter.GetBytes((ushort)0x7FFF));
                tree.Write(BitConverter.GetBytes(0u));
                tree.Write(BitConverter.GetBytes(64u));
                tree.Write(BitConverter.GetBytes((ushort)0xFFFF));
                tree.Write(new byte[preload]);
            }
            Text("vmdl_c");
            Text("models/soccermod/kits"); Text("kit_home"); Entry(0); Text("kit_gkhome"); Entry(5); Text("");
            Text("");
            Text("txt");
            Text(" "); Text("addoninfo"); Entry(0); Text("");
            Text("");
            Text("");
            var body = tree.ToArray();
            if (truncate) body = body[..^6];
            var file = new MemoryStream();
            file.Write(BitConverter.GetBytes(0x55AA1234u));
            file.Write(BitConverter.GetBytes(version));
            file.Write(BitConverter.GetBytes((uint)body.Length));
            if (version == 2) file.Write(new byte[16]);
            file.Write(body);
            file.Write(new byte[32]); // embedded file data follows the tree
            return file.ToArray();
        }

        foreach (var version in new[] { 1u, 2u })
        {
            var entries = VpkDirectory.ReadEntries(new MemoryStream(Vpk(version)));
            Check(entries.SetEquals(new[] { "models/soccermod/kits/kit_home.vmdl_c", "models/soccermod/kits/kit_gkhome.vmdl_c", "addoninfo.txt" }),
                $"VPK {version}: every entry is listed with its folder and extension, preload bytes skipped.");
            Check(entries.Contains("MODELS/SoccerMod/kits/KIT_HOME.vmdl_c"), "Resource paths compare case-insensitively.");
        }

        var failures = 0;
        foreach (var damaged in new[] { Vpk(2, truncate: true), new byte[] { 1, 2, 3, 4, 2, 0, 0, 0 }, Array.Empty<byte>() })
        {
            try { VpkDirectory.ReadEntries(new MemoryStream(damaged)); }
            catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException) { failures++; }
        }
        Check(failures == 3, "Truncated, foreign or empty files are rejected, never half-read.");
        Check(!VpkDirectory.TryReadEntries("/nonexistent/3797479770_dir.vpk", out var none) && none.Count == 0,
            "A missing addon file means no kit models.");

        Console.WriteLine("VPK directory checks passed (5 scenarios).");
    }
}

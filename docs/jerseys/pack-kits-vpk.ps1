# Packs the compiled soccermod_jerseys addon into a single-file VPK v2 that
# MultiAddonManager / the CS2 client can mount, bypassing the Workshop Tools
# publisher (which silently drops every content root outside its whitelist -
# verified 2026-09-07: characters/ and scripts/ never reach the published
# VPK; only maps/, panorama/, materials/, models/, ... do).
#
#   powershell -ExecutionPolicy Bypass -File docs\jerseys\pack-kits-vpk.ps1 -ItemId 3797479770
#
# Output: <OutDir>\<ItemId>_dir.vpk  (name MAM expects:
#         steamapps/workshop/content/730/<id>/<id>_dir.vpk)
# Validate afterwards with Source2Viewer-CLI:  -i <vpk> --vpk_verify  and  --vpk_dir

param(
    [Parameter(Mandatory = $true)] [ValidatePattern('^[1-9][0-9]*$')] [string]$ItemId,
    [string]$AddonDir = "E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\csgo_addons\soccermod_jerseys",
    [string]$OutDir = "$PSScriptRoot\..\..\artifacts\kits-upload",
    [ValidateSet("CustomModels", "LegacyMaterials")]
    [string]$Route = "CustomModels"
)

$ErrorActionPreference = "Stop"
$required = @("addoninfo.txt")
foreach ($variant in @("a", "b", "c", "d")) {
    foreach ($part in @("body", "lower_body")) {
        $required += if ($Route -eq "CustomModels") { "materials/soccermod/kits/kit_${variant}_${part}.vmat_c" }
                    else { "characters/models/tm_leet/materials/tm_leet_v2_${part}_variant${variant}.vmat_c" }
    }
}
if ($Route -eq "CustomModels") {
    foreach ($kit in @("home", "away", "gkhome", "gkaway")) { $required += "models/soccermod/kits/kit_${kit}.vmdl_c" }
    if ($AddonDir -match "soccermod_jerseys_gearless([\\/]|$)") {
        foreach ($kit in @("gkhome", "gkaway")) {
            $required += "materials/soccermod/kits/kit_${kit}_body.vmat_c"
            $required += "materials/soccermod/kits/kit_${kit}_lower_body.vmat_c"
            $required += "materials/soccermod/kits/kit_${kit}_gloves.vmat_c"
        }
    }
} else {
    Write-Warning "Packing failed Route 1 for historical diagnosis only. This does not deliver custom jersey models."
}
foreach ($relative in $required) {
    $file = Join-Path $AddonDir $relative
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
        throw "Cannot pack ${Route}: required nonempty resource missing: $relative"
    }
}

if (-not ("VpkWriter" -as [type])) {
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public static class VpkWriter
{
    static uint[] _crc;
    static uint Crc32(byte[] d)
    {
        if (_crc == null) { _crc = new uint[256]; for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1); _crc[i] = c; } }
        uint x = 0xFFFFFFFFu; foreach (byte b in d) x = _crc[(x ^ b) & 0xFF] ^ (x >> 8); return x ^ 0xFFFFFFFFu;
    }
    static void CStr(BinaryWriter w, string s) { w.Write(Encoding.ASCII.GetBytes(s)); w.Write((byte)0); }

    // Returns a human-readable manifest of what was packed.
    public static string Pack(string root, string outPath, bool customModels, out int fileCount)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        // Only compiled resources (*_c) plus addoninfo.txt. The Workshop Tools
        // leave tools_asset_info.bin, tools_thumbnail_cache.sqlite3,
        // ServerConfig.vdf and _bakeresourcecache/ in the game folder - none
        // of that belongs in a shipped addon.
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !f.Substring(root.Length).Replace('\\', '/').StartsWith("_bakeresourcecache/", StringComparison.OrdinalIgnoreCase))
            .Where(f => f.EndsWith("_c", StringComparison.OrdinalIgnoreCase)
                     || Path.GetFileName(f).Equals("addoninfo.txt", StringComparison.OrdinalIgnoreCase))
            // The compiler can also emit stock dependencies such as
            // materials/default/default_mask. Ship only our kit namespace;
            // the client resolves stock resources from its own game VPKs.
            .Where(f => !customModels || f.Substring(root.Length).Replace('\\', '/').StartsWith("models/soccermod/kits/", StringComparison.Ordinal)
                || f.Substring(root.Length).Replace('\\', '/').StartsWith("materials/soccermod/kits/", StringComparison.Ordinal)
                || f.Substring(root.Length).Equals("addoninfo.txt", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();

        // ext -> dir -> name -> (data)
        var tree = new SortedDictionary<string, SortedDictionary<string, SortedDictionary<string, byte[]>>>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            string rel = f.Substring(root.Length).Replace('\\', '/');
            string dir = rel.Contains("/") ? rel.Substring(0, rel.LastIndexOf('/')) : " ";   // VPK: root dir is a single space
            string fn = Path.GetFileName(rel);
            int dot = fn.LastIndexOf('.');
            string ext = dot >= 0 ? fn.Substring(dot + 1) : " ";
            string name = dot >= 0 ? fn.Substring(0, dot) : fn;
            if (!tree.ContainsKey(ext)) tree[ext] = new SortedDictionary<string, SortedDictionary<string, byte[]>>(StringComparer.Ordinal);
            if (!tree[ext].ContainsKey(dir)) tree[ext][dir] = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            tree[ext][dir][name] = File.ReadAllBytes(f);
        }

        // Lay out the data section first so entry offsets are known.
        var data = new MemoryStream();
        var offsets = new Dictionary<byte[], uint>();
        var manifest = new StringBuilder();
        fileCount = 0;
        foreach (var ext in tree) foreach (var dir in ext.Value) foreach (var file in dir.Value)
        {
            offsets[file.Value] = (uint)data.Length;
            data.Write(file.Value, 0, file.Value.Length);
            manifest.AppendLine(string.Format("{0,9}  {1}/{2}.{3}", file.Value.Length, dir.Key == " " ? "" : dir.Key, file.Key, ext.Key).Replace("  /", "  "));
            fileCount++;
        }

        var treeMs = new MemoryStream();
        using (var w = new BinaryWriter(treeMs, Encoding.ASCII, true))
        {
            foreach (var ext in tree)
            {
                CStr(w, ext.Key);
                foreach (var dir in ext.Value)
                {
                    CStr(w, dir.Key);
                    foreach (var file in dir.Value)
                    {
                        CStr(w, file.Key);
                        w.Write(Crc32(file.Value));          // CRC
                        w.Write((ushort)0);                  // preload bytes
                        w.Write((ushort)0x7FFF);             // archive index: data lives in this file
                        w.Write(offsets[file.Value]);        // entry offset (relative to data section)
                        w.Write((uint)file.Value.Length);    // entry length
                        w.Write((ushort)0xFFFF);             // terminator
                    }
                    w.Write((byte)0);
                }
                w.Write((byte)0);
            }
            w.Write((byte)0);
        }
        byte[] treeBytes = treeMs.ToArray();
        byte[] dataBytes = data.ToArray();
        byte[] archiveMd5Section = new byte[0];

        var outMs = new MemoryStream();
        using (var w = new BinaryWriter(outMs, Encoding.ASCII, true))
        {
            w.Write(0x55AA1234u);                 // signature
            w.Write(2u);                          // version
            w.Write((uint)treeBytes.Length);      // tree size
            w.Write((uint)dataBytes.Length);      // file data section size
            w.Write((uint)archiveMd5Section.Length);
            w.Write(48u);                         // other MD5 section size
            w.Write(0u);                          // signature section size
            w.Write(treeBytes);
            w.Write(dataBytes);
            w.Write(archiveMd5Section);
        }
        using (var md5 = MD5.Create())
        {
            byte[] treeSum = md5.ComputeHash(treeBytes);
            byte[] archiveSum = md5.ComputeHash(archiveMd5Section);
            outMs.Write(treeSum, 0, 16);
            outMs.Write(archiveSum, 0, 16);
            byte[] soFar = outMs.ToArray();            // whole-file checksum covers everything before itself
            byte[] whole = md5.ComputeHash(soFar);
            outMs.Write(whole, 0, 16);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        File.WriteAllBytes(outPath, outMs.ToArray());
        return manifest.ToString();
    }
}
"@
}

$out = Join-Path (Resolve-Path (New-Item -ItemType Directory -Force -Path $OutDir)) "${ItemId}_dir.vpk"
$count = 0
$manifest = [VpkWriter]::Pack($AddonDir, $out, ($Route -eq "CustomModels"), [ref]$count)
$manifest | Set-Content -LiteralPath ($out + ".manifest.txt") -Encoding utf8
Write-Host $manifest
Write-Host ("packed {0} files -> {1}  ({2:N0} bytes)" -f $count, $out, (Get-Item $out).Length) -ForegroundColor Green
Write-Host ("SHA-256: " + (Get-FileHash -LiteralPath $out -Algorithm SHA256).Hash)
Write-Host "Local VPK only. Copying it onto the server does not update Workshop or clients; publish and verify the downloaded package before enabling kits."

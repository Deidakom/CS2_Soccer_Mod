"""Exercise the shipping scripts with a fake compiler, never Valve assets.

These check failure handling and VPK contents, not actual model compilation.
Set SOCCER_PWSH to a portable PowerShell executable if it is not on PATH.
"""
import hashlib
import os
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import unittest
import zlib

ROOT = Path(__file__).resolve().parents[1]
PWSH = os.environ.get("SOCCER_PWSH") or shutil.which("pwsh")
MATERIALS = [f"materials/soccermod/kits/kit_{v}_{p}.vmat"
             for v in "abcd" for p in ("body", "lower_body")]
MODELS = [f"models/soccermod/kits/kit_{k}.vmdl"
          for k in ("home", "away", "gkhome", "gkaway")]


@unittest.skipUnless(PWSH, "PowerShell is required for kit tool integration tests")
class KitToolsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="soccer_kit_test_")
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name)
        self.content = self.base / "content/csgo_addons/soccermod_jerseys"
        self.game = self.base / "game/csgo_addons/soccermod_jerseys"
        self.compiler = self.base / "fake-compiler.ps1"
        self.compiler.write_text(r'''
$i = $args[[Array]::IndexOf($args, "-i") + 1]
$mode = (Get-Content -LiteralPath $i -Raw).Trim()
$global:LASTEXITCODE = 0
if ($mode -eq "fail") { $global:LASTEXITCODE = 9; Write-Output "Error: fixture failure"; return }
if ($mode -eq "false-success") { Write-Output "OK: 0 compiled, 1 failed"; return }
if ($mode -eq "missing-output") { Write-Output "OK: 0 compiled, 0 failed"; return }
$from = [IO.Path]::Combine("content", "csgo_addons")
$to = [IO.Path]::Combine("game", "csgo_addons")
$target = $i.Replace($from, $to) + "_c"
New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
[IO.File]::WriteAllText($target, "fixture output: " + $mode)
Write-Output "OK: 1 compiled, 0 failed"
''')
        for resource in MATERIALS + MODELS:
            self.write(self.content / resource, "ok")

    @staticmethod
    def write(path, value):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(value)

    def run_script(self, name, *args, success=True):
        result = subprocess.run([PWSH, "-NoProfile", "-NonInteractive", "-File",
                                 str(ROOT / "docs/jerseys" / name), *map(str, args)],
                                capture_output=True, text=True, timeout=60)
        if success:
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        return result

    def compile(self, **kwargs):
        return self.run_script("compile-kits.ps1", "-CsRoot", self.base,
                               "-CompilerPath", self.compiler, **kwargs)

    def pack(self, **kwargs):
        return self.run_script("pack-kits-vpk.ps1", "-ItemId", "1234567890",
                               "-AddonDir", self.game, "-OutDir", self.base / "out", **kwargs)

    def test_custom_route_compiles_all_twelve_exact_resources(self):
        self.compile()
        for resource in MATERIALS + MODELS:
            self.assertGreater((self.game / (resource + "_c")).stat().st_size, 0)

    def test_missing_model_source_fails_before_compilation(self):
        (self.content / MODELS[-1]).unlink()
        result = self.compile(success=False)
        self.assertIn("source missing", result.stderr)
        self.assertFalse(self.game.exists())

    def test_stale_outputs_cannot_hide_compiler_errors(self):
        self.compile()
        for mode in ("fail", "false-success"):
            with self.subTest(mode=mode):
                self.write(self.content / MATERIALS[0], mode)
                result = self.compile(success=False)
                self.assertIn("Compilation failed", result.stderr)

    def test_old_route_outputs_cannot_hide_a_missing_custom_output(self):
        for index in range(20):
            self.write(self.game / f"characters/old{index}.vmat_c", "stale")
        self.write(self.content / MODELS[-1], "missing-output")
        result = self.compile(success=False)
        self.assertIn("no nonempty output", result.stderr)

    def test_pack_requires_every_model_and_preserves_last_good_package(self):
        self.compile()
        self.pack()
        package = self.base / "out/1234567890_dir.vpk"
        previous = package.read_bytes()
        (self.game / (MODELS[-1] + "_c")).unlink()
        result = self.pack(success=False)
        self.assertIn("resource missing", result.stderr)
        self.assertEqual(package.read_bytes(), previous)

    def test_custom_vpk_contents_checksums_and_determinism(self):
        self.compile()
        self.write(self.game / "characters/old_override.vmat_c", "must not ship")
        self.write(self.game / "materials/soccermod/kits/color.vtex_c", "texture fixture")
        self.pack()
        package = self.base / "out/1234567890_dir.vpk"
        data = package.read_bytes()
        magic, version, tree_size, data_size, archive_md5_size, other_size, signature_size = struct.unpack_from("<7I", data)
        self.assertEqual((magic, version, archive_md5_size, other_size, signature_size), (0x55AA1234, 2, 0, 48, 0))
        cursor = 28
        entries = {}

        def word():
            nonlocal cursor
            end = data.index(b"\0", cursor)
            value = data[cursor:end].decode("ascii")
            cursor = end + 1
            return value

        while ext := word():
            while folder := word():
                while name := word():
                    crc, preload, archive, offset, length, terminator = struct.unpack_from("<IHHIIH", data, cursor)
                    cursor += 18
                    self.assertEqual((preload, archive, terminator), (0, 0x7FFF, 0xFFFF))
                    path = (folder + "/" if folder != " " else "") + name + "." + ext
                    payload = data[28 + tree_size + offset:28 + tree_size + offset + length]
                    self.assertEqual(zlib.crc32(payload), crc, path)
                    self.assertEqual(payload, (self.game / path).read_bytes(), path)
                    entries[path] = payload
        expected = {resource + "_c" for resource in MATERIALS + MODELS}
        expected |= {"addoninfo.txt", "materials/soccermod/kits/color.vtex_c"}
        self.assertEqual(set(entries), expected)
        self.assertEqual(cursor, 28 + tree_size)
        sums = 28 + tree_size + data_size
        self.assertEqual(data[sums:sums + 16], hashlib.md5(data[28:28 + tree_size]).digest())
        self.assertEqual(data[sums + 16:sums + 32], hashlib.md5(b"").digest())
        self.assertEqual(data[-16:], hashlib.md5(data[:-16]).digest())
        self.pack()
        self.assertEqual(package.read_bytes(), data)

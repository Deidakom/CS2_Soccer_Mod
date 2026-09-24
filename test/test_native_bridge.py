import pathlib
import shutil
import subprocess
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
PAYLOAD = ROOT / "deploy/release/payload/game/csgo/addons/soccermod_native/bin/linuxsteamrt64/soccermod_native.so"


class NativeBridgeTests(unittest.TestCase):
    def test_signature_scanner_and_gamedata_reader(self):
        compiler = shutil.which("c++") or shutil.which("g++") or shutil.which("clang++")
        if not compiler:
            self.skipTest("no C++ compiler")
        with tempfile.TemporaryDirectory() as directory:
            binary = pathlib.Path(directory, "checks")
            subprocess.run([compiler, "-std=c++17", "-Wall", "-Werror", "-O1", "-o", str(binary),
                            str(ROOT / "test/native/native_bridge_checks.cpp")], check=True)
            result = subprocess.run([str(binary)], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_payload_binary_is_built_from_this_source(self):
        # The committed .so must read CounterStrikeSharp's gamedata and carry the
        # 1.41.8.2 fallback; a stale binary would silently lose ball spin.
        data = PAYLOAD.read_bytes()
        for needle in (b"/addons/counterstrikesharp/gamedata/gamedata.json",
                       b"55 48 89 E5 41 57 49 89 FF 41 56 48 8D BD ? ? ? ? 41 55 4C 8D AD"):
            self.assertTrue(needle in data, f"{PAYLOAD.name} lacks {needle.decode()!r}; rebuild it with build-linux.sh")

if __name__ == "__main__":
    unittest.main()

import importlib.util
import os
import pathlib
import socket
import stat
import struct
import tempfile
import threading
import unittest
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("serverctl", ROOT / "deploy" / "testserver" / "serverctl.py")
serverctl = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(serverctl)

GAMEINFO = (
    '"GameInfo"\n{\n\tFileSystem\n\t{\n\t\tSearchPaths\n\t\t{\n'
    "\t\t\tGame_LowViolence\tcsgo_lv // Perfect World content override\n\n"
    "\t\t\tGame\tcsgo\n\t\t\tGame\tcsgo_imported\n\t\t\tGame\tcore\n\n"
    "\t\t\tMod\tcsgo\n\t\t}\n\t}\n}\n"
)


def info_reply(players, bots, map_name="soccer_cssl_stadium_v8"):
    body = b"\xff\xff\xff\xffI\x11" + b"KA Soccer\x00" + map_name.encode() + b"\x00csgo\x00Counter-Strike 2\x00"
    return body + struct.pack("<h", 730) + bytes([players, 16, bots]) + b"d" + b"l" + b"\x00\x01"


class FakeA2S(threading.Thread):
    def __init__(self, players, bots):
        super().__init__(daemon=True)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind(("127.0.0.1", 0))
        self.port = self.sock.getsockname()[1]
        self.players, self.bots = players, bots

    def run(self):
        for _ in range(2):
            data, address = self.sock.recvfrom(4096)
            if data == serverctl.A2S_INFO_REQUEST:
                self.sock.sendto(b"\xff\xff\xff\xffA\x01\x02\x03\x04", address)
            elif data == serverctl.A2S_INFO_REQUEST + b"\x01\x02\x03\x04":
                self.sock.sendto(info_reply(self.players, self.bots), address)
        self.sock.close()


class FakeRcon(threading.Thread):
    def __init__(self, password, outputs, echo_marker=True):
        super().__init__(daemon=True)
        self.listener = socket.socket()
        self.listener.bind(("127.0.0.1", 0))
        self.listener.listen(1)
        self.port = self.listener.getsockname()[1]
        self.password, self.outputs, self.echo_marker = password, outputs, echo_marker

    @staticmethod
    def packet(request_id, kind, body):
        payload = struct.pack("<ii", request_id, kind) + body.encode() + b"\x00\x00"
        return struct.pack("<i", len(payload)) + payload

    def run(self):
        try:
            self.serve()
        finally:
            self.listener.close()

    def serve(self):
        connection, _ = self.listener.accept()
        with connection:
            def read():
                size = struct.unpack("<i", connection.recv(4))[0]
                data = b""
                while len(data) < size:
                    data += connection.recv(size - len(data))
                request_id, kind = struct.unpack("<ii", data[:8])
                return request_id, kind, data[8:-2].decode()

            request_id, _, body = read()
            ok = body == self.password
            connection.sendall(self.packet(request_id, 0, "") + self.packet(request_id if ok else -1, 2, ""))
            if not ok:
                return
            while True:
                try:
                    request_id, kind, body = read()
                except (struct.error, OSError):
                    return
                if kind == 2:
                    text = self.outputs.get(body, "")
                    half = len(text) // 2
                    connection.sendall(self.packet(request_id, 0, text[:half]) + self.packet(request_id, 0, text[half:]))
                elif self.echo_marker:
                    connection.sendall(self.packet(request_id, 0, "") + self.packet(request_id, 0, "\x00\x01"))


class ServerCtlTests(unittest.TestCase):
    def test_a2s_answers_the_challenge_and_reports_humans_and_bots(self):
        fake = FakeA2S(players=5, bots=2)
        fake.start()
        info = serverctl.a2s_info("127.0.0.1", fake.port)
        self.assertEqual((info["players"], info["bots"], info["max"], info["map"]), (5, 2, 16, "soccer_cssl_stadium_v8"))

    def test_rcon_joins_split_replies_and_keeps_commands_apart(self):
        outputs = {"meta list": "Listing 2 plugins:\n [01] CounterStrikeSharp\n [02] SoccerMod Native Physics Bridge\n",
                   "css_plugins list": '[#1:LOADED]: "CS2 SoccerMod" (1.0 Beta)\n'}
        fake = FakeRcon("secret", outputs)
        fake.start()
        result = serverctl.rcon("127.0.0.1", fake.port, "secret", ["meta list", "css_plugins list"])
        self.assertEqual(result, [outputs["meta list"], outputs["css_plugins list"]])

    def test_rcon_without_marker_echo_ends_on_timeout(self):
        fake = FakeRcon("secret", {"status": "map: soccer"}, echo_marker=False)
        fake.start()
        self.assertEqual(serverctl.rcon("127.0.0.1", fake.port, "secret", ["status"], timeout=0.5), ["map: soccer"])

    def test_rcon_rejects_a_wrong_password(self):
        fake = FakeRcon("secret", {})
        fake.start()
        with self.assertRaises(PermissionError):
            serverctl.rcon("127.0.0.1", fake.port, "wrong", ["status"])

    def test_env_password_is_read_without_quotes_or_comments(self):
        with tempfile.TemporaryDirectory() as directory:
            env = pathlib.Path(directory, "cs2.env")
            env.write_text("# comment\nCS2_GSLT=abc\nCS2_RCON_PASSWORD='p:a|s#s'\n", encoding="utf-8")
            self.assertEqual(serverctl.read_env_value(env, "CS2_RCON_PASSWORD"), "p:a|s#s")
            with self.assertRaises(KeyError):
                serverctl.read_env_value(env, "MISSING")

    def test_gameinfo_gets_metamod_once_after_low_violence_for_lf_and_crlf(self):
        for newline in ("\n", "\r\n"):
            with tempfile.TemporaryDirectory() as directory:
                path = pathlib.Path(directory, "gameinfo.gi")
                path.write_bytes(GAMEINFO.replace("\n", newline).encode())
                os.chmod(path, 0o640)
                self.assertEqual(serverctl.ensure_metamod_search_path(path), "added")
                self.assertEqual(serverctl.ensure_metamod_search_path(path), "present")
                lines = path.read_bytes().decode().split(newline)
                index = next(i for i, line in enumerate(lines) if "Game_LowViolence" in line)
                self.assertEqual(lines[index + 1], "\t\t\tGame\tcsgo/addons/metamod")
                self.assertEqual(path.read_bytes().count(b"csgo/addons/metamod"), 1)
                self.assertEqual(stat.S_IMODE(path.stat().st_mode), 0o640)
                if newline == "\r\n":
                    self.assertNotIn(b"\r\r", path.read_bytes())

    def test_gameinfo_without_low_violence_inserts_before_the_csgo_path(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory, "gameinfo.gi")
            path.write_text(GAMEINFO.replace("\t\t\tGame_LowViolence\tcsgo_lv // Perfect World content override\n", ""), encoding="utf-8")
            self.assertEqual(serverctl.ensure_metamod_search_path(path), "added")
            text = path.read_text(encoding="utf-8")
            self.assertLess(text.index("csgo/addons/metamod"), text.index("\tGame\tcsgo\n"))

    def test_commented_or_unknown_layouts_are_not_mistaken_for_success(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory, "gameinfo.gi")
            path.write_text(GAMEINFO.replace("Game_LowViolence", "// Game\tcsgo/addons/metamod\n\t\t\tGame_LowViolence"), encoding="utf-8")
            self.assertEqual(serverctl.ensure_metamod_search_path(path), "added")
            path.write_text('"GameInfo" { }\n', encoding="utf-8")
            with self.assertRaises(ValueError):
                serverctl.ensure_metamod_search_path(path)

    def test_build_ids_come_from_the_public_branch_and_the_manifest(self):
        info = '"730" { "depots" { "branches" { "public" { "buildid" "20456789" "timeupdated" "1" } "beta" { "buildid" "3" } } } }'
        self.assertEqual(serverctl.latest_buildid(info), "20456789")
        self.assertIsNone(serverctl.latest_buildid('"branches" { }'))
        self.assertEqual(serverctl.installed_buildid('"AppState" { "appid" "730" "buildid" "20400000" }'), "20400000")

    def test_unzip_keeps_permissions_and_links_and_rejects_escapes(self):
        with tempfile.TemporaryDirectory() as directory:
            archive_path = pathlib.Path(directory, "package.zip")
            with zipfile.ZipFile(archive_path, "w") as archive:
                executable = zipfile.ZipInfo("addons/counterstrikesharp/dotnet/dotnet")
                executable.external_attr = (stat.S_IFREG | 0o755) << 16
                archive.writestr(executable, b"\x7fELF")
                link = zipfile.ZipInfo("addons/counterstrikesharp/dotnet/current")
                link.external_attr = (stat.S_IFLNK | 0o777) << 16
                archive.writestr(link, "dotnet")
                archive.writestr("addons/metamod/counterstrikesharp.vdf", "vdf")
            destination = pathlib.Path(directory, "out")
            serverctl.safe_unzip(archive_path, destination)
            binary = destination / "addons/counterstrikesharp/dotnet/dotnet"
            self.assertTrue(os.access(binary, os.X_OK))
            self.assertEqual(os.readlink(destination / "addons/counterstrikesharp/dotnet/current"), "dotnet")
            self.assertEqual((destination / "addons/metamod/counterstrikesharp.vdf").read_text(), "vdf")

            for name, target in (("../escape.txt", None), ("addons/evil", "../../../etc/passwd")):
                hostile = pathlib.Path(directory, "hostile.zip")
                with zipfile.ZipFile(hostile, "w") as archive:
                    entry = zipfile.ZipInfo(name)
                    if target:
                        entry.external_attr = (stat.S_IFLNK | 0o777) << 16
                    archive.writestr(entry, target or "x")
                with self.assertRaises(ValueError):
                    serverctl.safe_unzip(hostile, pathlib.Path(directory, "hostile-out"))

    def test_release_asset_selection_requires_every_word(self):
        release = {"tag_name": "v1.0.374", "assets": [
            {"name": "counterstrikesharp-build-374-linux-abc.zip", "browser_download_url": "https://example/linux"},
            {"name": "counterstrikesharp-with-runtime-build-374-windows-abc.zip", "browser_download_url": "https://example/win"},
            {"name": "counterstrikesharp-with-runtime-build-374-linux-abc.zip", "browser_download_url": "https://example/runtime"},
        ]}
        self.assertEqual(serverctl.github_asset(release, ["with-runtime", "linux"]), ("v1.0.374", "https://example/runtime"))
        with self.assertRaises(LookupError):
            serverctl.github_asset(release, ["macos"])

    def test_workshop_status_flags_items_other_players_cannot_download(self):
        public = {"publishedfileid": "3361075564", "result": 1, "visibility": 0, "banned": 0,
                  "file_size": "41710454", "title": "CSF Football Stadium"}
        self.assertEqual(serverctl.workshop_status(public)[1], True)
        self.assertEqual(serverctl.workshop_status({"publishedfileid": "3796041025", "result": 9})[1], False)
        for change in ({"visibility": 2}, {"banned": 1}, {"file_size": "0"}):
            self.assertEqual(serverctl.workshop_status({**public, **change})[1], False, change)


if __name__ == "__main__":
    unittest.main()

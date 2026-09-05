import importlib.util
import io
import json
import os
from pathlib import Path
import sqlite3
import struct
import tempfile
import time
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
os.environ.setdefault("SESSION_SECRET", "unit-test-session-secret-" * 2)
os.environ.setdefault("RCON_HELPER_SECRET", "unit-test-helper-secret-" * 2)


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


auth = load_module("kickoff_auth", "kickoff/auth/auth_server.py")
rcon = load_module("kickoff_rcon", "kickoff/rcon/rcon_helper.py")


class FragmentedSocket:
    def __init__(self, payload):
        self.payload = bytearray(payload)

    def recv(self, size):
        result = self.payload[:min(size, 1)]
        del self.payload[:len(result)]
        return bytes(result)

    def __enter__(self):
        return self

    def __exit__(self, *args):
        pass

    def settimeout(self, seconds):
        pass

    def sendall(self, data):
        pass


class KickoffTests(unittest.TestCase):
    def test_database_context_closes_and_commits(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(auth, "DATABASE_PATH", str(Path(folder) / "test.db")):
            with auth.database() as connection:
                connection.execute("CREATE TABLE sample (value INTEGER)")
                connection.execute("INSERT INTO sample VALUES (7)")
            with self.assertRaises(sqlite3.ProgrammingError):
                connection.execute("SELECT 1")
            with auth.database() as reopened:
                self.assertEqual(reopened.execute("SELECT value FROM sample").fetchone()[0], 7)

    def test_database_context_rolls_back_and_closes_on_error(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(auth, "DATABASE_PATH", str(Path(folder) / "test.db")):
            with auth.database() as connection:
                connection.execute("CREATE TABLE sample (value INTEGER)")
            with self.assertRaisesRegex(RuntimeError, "abort"):
                with auth.database() as connection:
                    connection.execute("INSERT INTO sample VALUES (7)")
                    raise RuntimeError("abort")
            with self.assertRaises(sqlite3.ProgrammingError):
                connection.execute("SELECT 1")
            with auth.database() as reopened:
                self.assertEqual(reopened.execute("SELECT COUNT(*) FROM sample").fetchone()[0], 0)

    def test_database_initialization_is_idempotent(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(auth, "DATA_DIR", folder), patch.object(auth, "DATABASE_PATH", str(Path(folder) / "test.db")):
            auth.initialize_database()
            auth.initialize_database()
            with auth.database() as connection:
                self.assertEqual(connection.execute("PRAGMA journal_mode").fetchone()[0], "wal")

    def test_invalid_session_tokens_fail_closed(self):
        for token in (None, "", ".", "a.b", "é.test", "body.é", "x" * 5000 + ".sig"):
            with self.subTest(token=str(token)[:20]):
                self.assertIsNone(auth.read_payload(token))
        for expiry in (None, [], {}, "tomorrow", True, 1, int(time.time())):
            self.assertIsNone(auth.read_payload(auth.sign_payload({"exp": expiry})))
        payload = {"exp": int(time.time()) + 60, "steamid": "1" * 17}
        self.assertEqual(auth.read_payload(auth.sign_payload(payload)), payload)

    def test_login_redirects_cannot_inject_headers_or_escape_origin(self):
        for destination in ("https://example.com", "//example.com", "/\\example.com", "/\r\nX-Test: yes", "/\t/example.com", "/\x00", "/\x7f"):
            self.assertEqual(auth.safe_return_to(destination), "/")
        self.assertEqual(auth.safe_return_to("/community.html?tab=caps"), "/community.html?tab=caps")

    def test_prepare_rejects_invalid_half_lengths_without_type_errors(self):
        for value in ([], {}, True, None, "600", 600.0, -1):
            body = json.dumps({"game": "cs2", "halfSeconds": value}).encode()
            handler = type("Request", (), {"headers": {"Content-Length": str(len(body))}, "rfile": io.BytesIO(body)})()
            with self.subTest(value=value), self.assertRaises(ValueError):
                rcon.read_prepare_request(handler)

    def test_assignments_reject_duplicates_and_non_ascii_ids(self):
        player = {"id": "1" * 17, "team": "home", "role": "GK"}
        self.assertEqual(rcon.normalize_assignments([player]), [player])
        for players in ([player, player], [{**player, "id": "١" * 17}], [{**player, "role": "GK;quit"}]):
            with self.assertRaises(ValueError):
                rcon.normalize_assignments(players)

    def test_password_parser_preserves_quoted_slashes_and_ignores_comments(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "server.cfg"
            path.write_text('// ignored\nrcon_password "test//value" // unmatched " comment\n')
            with patch.object(rcon, "CSS_RCON_PASSWORD", ""), patch.object(rcon, "CSS_RCON_CONFIG", str(path)):
                self.assertEqual(rcon.read_rcon_password("css"), "test//value")
            with patch.object(rcon, "CSS_RCON_PASSWORD", "from-environment"):
                self.assertEqual(rcon.read_rcon_password("css"), "from-environment")

    def test_rcon_receives_fragmented_headers_and_payloads(self):
        body = struct.pack("<ii", 42, 0) + b"ok\0\0"
        self.assertEqual(rcon.receive_packet(FragmentedSocket(struct.pack("<i", len(body)) + body)), (42, 0, "ok"))

    def test_rcon_rejects_malformed_packets(self):
        for size in (-1, 0, 9, 4 * 1024 * 1024 + 1):
            with self.assertRaises(ValueError):
                rcon.receive_packet(FragmentedSocket(struct.pack("<i", size)))
        with self.assertRaises(ConnectionError):
            rcon.receive_packet(FragmentedSocket(b"\x0a\0"))
        with self.assertRaises(ValueError):
            rcon.receive_packet(FragmentedSocket(struct.pack("<iii", 10, 1, 0) + b"xx"))

    def test_rcon_batch_collects_all_chunks_and_consumes_previous_fence(self):
        def packet(request_id, kind, body):
            data = struct.pack("<ii", request_id, kind) + body.encode() + b"\0\0"
            return struct.pack("<i", len(data)) + data

        wire = b"".join([
            packet(1, 0, ""), packet(1, 2, ""),
            packet(2, 0, "first "), packet(2, 0, "response"),
            packet(3, 0, ""), packet(3, 0, "\0\0\0\x01\0\0\0\0"),
            packet(4, 0, "second response"), packet(5, 0, ""),
        ])
        with patch.object(rcon.secrets, "randbelow", return_value=0), patch.object(rcon, "read_rcon_password", return_value="test"), patch.object(rcon.socket, "create_connection", return_value=FragmentedSocket(wire)):
            self.assertEqual(rcon.run_rcon("cs2", ["status", "css_match status"]), ["first response", "second response"])


if __name__ == "__main__":
    unittest.main()

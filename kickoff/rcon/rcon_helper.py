#!/usr/bin/env python3
"""Private host-side bridge for the allowlisted CS:S and CS2 cap-start actions."""

from __future__ import annotations

import hmac
import json
import os
import re
import secrets
import shlex
import socket
import struct
import threading
import time
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


BIND_HOST = os.environ.get("RCON_HELPER_BIND", "172.30.20.1")
BIND_PORT = int(os.environ.get("RCON_HELPER_PORT", "8099"))
CSS_RCON_HOST = os.environ.get("CSS_RCON_HOST", os.environ.get("RCON_HOST", "127.0.1.1"))
CSS_RCON_PORT = int(os.environ.get("CSS_RCON_PORT", os.environ.get("RCON_PORT", "27015")))
CSS_RCON_CONFIG = os.environ.get(
    "CSS_RCON_CONFIG", os.environ.get("RCON_CONFIG", "/home/gameserver/css/cstrike/cfg/server.cfg")
)
CSS_RCON_PASSWORD = os.environ.get("CSS_RCON_PASSWORD", "")
CS2_RCON_HOST = os.environ.get("CS2_RCON_HOST", "127.0.0.1")
CS2_RCON_PORT = int(os.environ.get("CS2_RCON_PORT", "27017"))
CS2_RCON_PASSWORD = os.environ.get("CS2_RCON_PASSWORD", "")
SHARED_SECRET = os.environ.get("RCON_HELPER_SECRET", "")
CAP_MESSAGE = "A New cap will be played"
ALLOWED_ROLES = {"GK", "DEF", "MID", "WING"}
ALLOWED_TEAMS = {"home", "away"}
ALLOWED_GAMES = {"css", "cs2"}
ALLOWED_HALF_SECONDS = {450, 600, 900}
GAME_LOCKS = {game: threading.Lock() for game in ALLOWED_GAMES}

if len(SHARED_SECRET) < 32:
    raise SystemExit("RCON_HELPER_SECRET must contain at least 32 characters")


def read_rcon_password(game: str) -> str:
    if game == "cs2":
        if not CS2_RCON_PASSWORD:
            raise RuntimeError("CS2 rcon password is not configured")
        return CS2_RCON_PASSWORD
    if CSS_RCON_PASSWORD:
        return CSS_RCON_PASSWORD
    with open(CSS_RCON_CONFIG, encoding="utf-8", errors="replace") as config:
        for raw_line in config:
            line = raw_line.strip()
            if not line:
                continue
            try:
                # Read only the command and its value: // inside a quoted
                # password is data, and trailing comments need not be parsed.
                lexer = shlex.shlex(line, posix=True)
                lexer.whitespace_split = True
                lexer.commenters = ""
                parts = [next(lexer, ""), next(lexer, "")]
            except ValueError:
                continue
            if len(parts) >= 2 and parts[0].lower() == "rcon_password" and parts[1]:
                return parts[1]
    raise RuntimeError("rcon_password is not configured")


def recv_exact(connection: socket.socket, length: int) -> bytes:
    chunks = bytearray()
    while len(chunks) < length:
        chunk = connection.recv(length - len(chunks))
        if not chunk:
            raise ConnectionError("RCON connection closed")
        chunks.extend(chunk)
    return bytes(chunks)


def send_packet(connection: socket.socket, request_id: int, packet_type: int, body: str) -> None:
    payload = struct.pack("<ii", request_id, packet_type) + body.encode("utf-8") + b"\x00\x00"
    connection.sendall(struct.pack("<i", len(payload)) + payload)


def receive_packet(connection: socket.socket) -> tuple[int, int, str]:
    size = struct.unpack("<i", recv_exact(connection, 4))[0]
    if size < 10 or size > 4 * 1024 * 1024:
        raise ValueError("Invalid RCON packet size")
    payload = recv_exact(connection, size)
    if payload[-2:] != b"\x00\x00":
        raise ValueError("Invalid RCON packet terminator")
    request_id, packet_type = struct.unpack("<ii", payload[:8])
    return request_id, packet_type, payload[8:-2].decode("utf-8", "replace")


def run_rcon(game: str, commands: list[str]) -> list[str]:
    if game not in ALLOWED_GAMES:
        raise ValueError("Unsupported game")
    # begin/assign/commit operates on one shared staging roster per server.
    # Serialize batches so concurrent HTTP requests cannot mix two rosters.
    with GAME_LOCKS[game]:
        return _run_rcon(game, commands)


def _run_rcon(game: str, commands: list[str]) -> list[str]:
    password = read_rcon_password(game)
    host = CS2_RCON_HOST if game == "cs2" else CSS_RCON_HOST
    port = CS2_RCON_PORT if game == "cs2" else CSS_RCON_PORT
    auth_id = secrets.randbelow(2_000_000_000) + 1
    with socket.create_connection((host, port), timeout=4) as connection:
        connection.settimeout(4)
        send_packet(connection, auth_id, 3, password)
        authenticated = False
        for _ in range(3):
            response_id, packet_type, _ = receive_packet(connection)
            if response_id == -1:
                raise PermissionError("RCON authentication failed")
            if response_id == auth_id and packet_type == 2:
                authenticated = True
                break
        if not authenticated:
            raise PermissionError("RCON authentication response missing")
        responses = []
        previous_fence_id = None
        for offset, command in enumerate(commands):
            request_id = auth_id + 1 + offset * 2
            fence_id = request_id + 1
            send_packet(connection, request_id, 2, command)
            # Source mirrors this empty RESPONSE_VALUE after all chunks of
            # the command response. A second fence packet may follow it.
            send_packet(connection, fence_id, 0, "")
            chunks = []
            response_size = 0
            for _ in range(4096):
                response_id, packet_type, chunk = receive_packet(connection)
                if packet_type != 0:
                    raise RuntimeError("Unexpected RCON response type")
                if response_id == fence_id:
                    break
                if response_id == previous_fence_id:
                    continue
                if response_id != request_id:
                    raise RuntimeError("Unexpected RCON response")
                response_size += len(chunk)
                if response_size > 4 * 1024 * 1024:
                    raise RuntimeError("RCON response is too large")
                chunks.append(chunk)
            else:
                raise RuntimeError("RCON response terminator missing")
            previous_fence_id = fence_id
            response = "".join(chunks)
            if "unknown command" in response.lower():
                raise RuntimeError(f"Server rejected RCON command: {command.split()[0]}")
            responses.append(response)
        return responses


def read_request_payload(handler: BaseHTTPRequestHandler) -> dict:
    try:
        length = int(handler.headers.get("Content-Length", "0"))
    except ValueError as error:
        raise ValueError("Invalid content length") from error
    if length < 2 or length > 8192:
        raise ValueError("Invalid request size")
    try:
        payload = json.loads(handler.rfile.read(length))
    except (json.JSONDecodeError, UnicodeDecodeError) as error:
        raise ValueError("Invalid JSON") from error
    if not isinstance(payload, dict):
        raise ValueError("Invalid payload")
    return payload


def normalize_assignments(value: object) -> list[dict]:
    if not isinstance(value, list) or not 1 <= len(value) <= 12:
        raise ValueError("Invalid assignments")
    assignments = []
    seen = set()
    for item in value:
        if not isinstance(item, dict):
            raise ValueError("Invalid assignment")
        steamid = str(item.get("id", ""))
        role = str(item.get("role", "")).upper()
        team = str(item.get("team", "")).lower()
        if (
            not re.fullmatch(r"[0-9]{17}", steamid)
            or steamid in seen
            or role not in ALLOWED_ROLES
            or team not in ALLOWED_TEAMS
        ):
            raise ValueError("Invalid assignment")
        seen.add(steamid)
        assignments.append({"id": steamid, "role": role, "team": team})
    return assignments


def read_assignments(handler: BaseHTTPRequestHandler) -> list[dict]:
    return normalize_assignments(read_request_payload(handler).get("assignments"))


def read_prepare_request(handler: BaseHTTPRequestHandler) -> tuple[str, list[dict], int]:
    payload = read_request_payload(handler)
    game = str(payload.get("game", "")).lower()
    if game not in ALLOWED_GAMES:
        raise ValueError("Unsupported game")
    half_seconds = payload.get("halfSeconds")
    if type(half_seconds) is not int or half_seconds not in ALLOWED_HALF_SECONDS:
        raise ValueError("Unsupported half length")
    return game, normalize_assignments(payload.get("assignments")), int(half_seconds)


def read_game_request(handler: BaseHTTPRequestHandler) -> str:
    game = str(read_request_payload(handler).get("game", "")).lower()
    if game not in ALLOWED_GAMES:
        raise ValueError("Unsupported game")
    return game


def prepare_commands(game: str, assignments: list[dict], half_seconds: int) -> tuple[list[str], list[str]]:
    if type(half_seconds) is not int or half_seconds not in ALLOWED_HALF_SECONDS:
        raise ValueError("Unsupported half length")
    if game == "css":
        duration, begin, assign, commit, evict = (
            f"soccer_mod_match_period_length {half_seconds}",
            "sm_kickoff_webcap_begin",
            "sm_kickoff_webcap_assign",
            "sm_kickoff_webcap_commit",
            f'sm_kick @humans "{CAP_MESSAGE}"',
        )
    elif game == "cs2":
        duration, begin, assign, commit, evict = (
            f"css_sm2webcap_reference {half_seconds}",
            "css_sm2webcap_begin",
            "css_sm2webcap_assign",
            "css_sm2webcap_commit",
            # The CS2 plugin now moves non-cap players to spectators itself;
            # nobody needs to reconnect when a website CAP is committed.
            None,
        )
    else:
        raise ValueError("Unsupported game")
    commands = [begin, duration]
    commands.extend(f'{assign} {player["id"]} {player["team"]} {player["role"]}' for player in assignments)
    commands.extend([commit, f'say "{CAP_MESSAGE}"'])
    return commands, [evict] if evict else []


def clear_commands(game: str) -> list[str]:
    if game == "css":
        # The legacy bridge clears its active assignment when a new import begins.
        return ["sm_kickoff_webcap_begin"]
    if game == "cs2":
        return ["css_sm2webcap_clear"]
    raise ValueError("Unsupported game")


def match_status_commands(game: str) -> list[str]:
    """Return the fixed command used for website match lifecycle polling."""
    if game == "cs2":
        return ["css_match status"]
    raise ValueError("Match status is not available for this game")


def stop_match_commands(game: str) -> list[str]:
    """Stop only the CS2 SoccerMod match when its cap creator cancels."""
    if game == "cs2":
        return ["css_match stop"]
    raise ValueError("Match stop is not available for this game")


class Handler(BaseHTTPRequestHandler):
    server_version = "KICKOFF-RCON"
    sys_version = ""

    def log_message(self, fmt: str, *args) -> None:
        print(f'{self.address_string()} - "{self.command} {self.path}"', flush=True)

    def authorized(self) -> bool:
        expected = f"Bearer {SHARED_SECRET}"
        return hmac.compare_digest(self.headers.get("Authorization", "").encode("utf-8"), expected.encode("utf-8"))

    def reply(self, status: HTTPStatus, payload: dict) -> None:
        body = json.dumps(payload, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self) -> None:
        if self.path.startswith("/match-status/"):
            requested_game = self.path.removeprefix("/match-status/")
            if requested_game not in ALLOWED_GAMES or not self.authorized():
                self.reply(HTTPStatus.NOT_FOUND, {"ok": False})
                return
            try:
                responses = run_rcon(requested_game, match_status_commands(requested_game))
            except ValueError:
                self.reply(HTTPStatus.NOT_FOUND, {"ok": False})
                return
            except (OSError, RuntimeError, PermissionError):
                self.reply(HTTPStatus.BAD_GATEWAY, {"ok": False})
                return
            self.reply(HTTPStatus.OK, {"ok": True, "game": requested_game, "responses": responses})
            return
        requested_game = self.path.removeprefix("/health/") if self.path.startswith("/health/") else ""
        games = [requested_game] if requested_game in ALLOWED_GAMES else sorted(ALLOWED_GAMES)
        if self.path not in ("/health", "/health/css", "/health/cs2") or not self.authorized():
            self.reply(HTTPStatus.NOT_FOUND, {"ok": False})
            return
        try:
            for game in games:
                run_rcon(game, ["status"])
        except (OSError, ValueError, RuntimeError, PermissionError):
            self.reply(HTTPStatus.SERVICE_UNAVAILABLE, {"ok": False})
            return
        self.reply(HTTPStatus.OK, {"ok": True, "games": games})

    def do_POST(self) -> None:
        if self.path not in ("/prepare", "/clear", "/stop") or not self.authorized():
            self.reply(HTTPStatus.NOT_FOUND, {"ok": False})
            return
        try:
            if self.path == "/prepare":
                game, assignments, half_seconds = read_prepare_request(self)
                commands, evict_commands = prepare_commands(game, assignments, half_seconds)
                run_rcon(game, commands)
                if evict_commands:
                    time.sleep(1.25)
                    run_rcon(game, evict_commands)
                response = {
                    "ok": True, "game": game, "assignments": len(assignments),
                    "halfSeconds": half_seconds,
                }
            elif self.path == "/clear":
                game = read_game_request(self)
                run_rcon(game, clear_commands(game))
                response = {"ok": True, "game": game, "cleared": True}
            else:
                game = read_game_request(self)
                run_rcon(game, stop_match_commands(game))
                response = {"ok": True, "game": game, "stopped": True}
        except ValueError:
            self.reply(HTTPStatus.BAD_REQUEST, {"ok": False})
            return
        except (OSError, RuntimeError, PermissionError):
            self.reply(HTTPStatus.BAD_GATEWAY, {"ok": False})
            return
        self.reply(HTTPStatus.OK, response)


if __name__ == "__main__":
    server = ThreadingHTTPServer((BIND_HOST, BIND_PORT), Handler)
    print(f"KICKOFF RCON helper listening on {BIND_HOST}:{BIND_PORT}", flush=True)
    server.serve_forever()

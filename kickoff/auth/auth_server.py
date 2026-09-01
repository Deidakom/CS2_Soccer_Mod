#!/usr/bin/env python3
"""Steam OpenID, user roles, and the persistent Soccer Mod cap queue."""

from __future__ import annotations

import base64
import hashlib
import hmac
import ipaddress
import json
import os
import re
import secrets
import sqlite3
import time
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from http import HTTPStatus
from http.cookies import SimpleCookie
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


ORIGIN = os.environ.get("KICKOFF_ORIGIN", "https://kickoff.212-87-212-58.sslip.io").rstrip("/")
PORT = int(os.environ.get("PORT", "8080"))
SECRET = os.environ.get("SESSION_SECRET", "").encode("utf-8")
DATA_DIR = os.environ.get("DATA_DIR", "/tmp/kickoff-data")
DATABASE_PATH = os.path.join(DATA_DIR, "kickoff.sqlite3")
SOCCERMOD_DATABASE_PATH = os.environ.get("SOCCERMOD_DATABASE_PATH", "").strip()
OWNER_STEAM_ID = os.environ.get("KICKOFF_OWNER_STEAM_ID", "").strip()
STEAM_WEB_API_KEY = os.environ.get("STEAM_WEB_API_KEY", "").strip()
RCON_HELPER_URL = os.environ.get("RCON_HELPER_URL", "").strip()
RCON_HELPER_SECRET = os.environ.get("RCON_HELPER_SECRET", "").strip()
STEAM_OPENID = "https://steamcommunity.com/openid/login"
OPENID_NS = "http://specs.openid.net/auth/2.0"
IDENTIFIER_SELECT = f"{OPENID_NS}/identifier_select"
STEAM_ID_PATTERN = re.compile(r"^https?://steamcommunity\.com/openid/id/(\d{17})/?$")
SESSION_COOKIE = "__Host-kickoff_session"
STATE_COOKIE = "kickoff_openid_state"
ALLOWED_ROLES = ("GK", "DEF", "MID", "WING")
ROLE_CAPACITY = {"GK": 2, "DEF": 4, "MID": 2, "WING": 4}
SUPPORTED_CAP_GAMES = {
    "css": {"maps": ("Titan Club 2026",)},
    "cs2": {"maps": ("soccer_cssl_stadium_v8",)},
}
QUEUE_ACTIVITY_INTERVAL = 600
QUEUE_ACTIVITY_GRACE = 60
DURATION_VOTE_SECONDS = 10
DEFAULT_HALF_SECONDS = 600
ALLOWED_HALF_SECONDS = (450, 600, 900)
PROFILE_CACHE: dict[str, tuple[str, float]] = {}

if len(SECRET) < 32:
    raise SystemExit("SESSION_SECRET must contain at least 32 characters")
if not ORIGIN.startswith("https://"):
    raise SystemExit("KICKOFF_ORIGIN must use HTTPS")
if OWNER_STEAM_ID and not re.fullmatch(r"\d{17}", OWNER_STEAM_ID):
    raise SystemExit("KICKOFF_OWNER_STEAM_ID must be a 17-digit SteamID64")


def database() -> sqlite3.Connection:
    connection = sqlite3.connect(DATABASE_PATH, timeout=8)
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA foreign_keys = ON")
    return connection


def initialize_database() -> None:
    os.makedirs(DATA_DIR, mode=0o700, exist_ok=True)
    with database() as connection:
        connection.execute("PRAGMA journal_mode = WAL")
        connection.executescript(
            """
            CREATE TABLE IF NOT EXISTS users (
                steamid TEXT PRIMARY KEY CHECK(length(steamid) = 17),
                display_name TEXT NOT NULL,
                role TEXT NOT NULL DEFAULT 'user' CHECK(role IN ('user', 'admin')),
                account_status TEXT NOT NULL DEFAULT 'active' CHECK(account_status IN ('active', 'suspended', 'banned')),
                created_at INTEGER NOT NULL,
                last_login INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS queue_members (
                steamid TEXT PRIMARY KEY REFERENCES users(steamid) ON DELETE CASCADE,
                display_name TEXT NOT NULL,
                roles_json TEXT NOT NULL,
                main_role TEXT,
                joined_at INTEGER NOT NULL,
                activity_due_at INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS match_starts (
                signature TEXT PRIMARY KEY,
                requested_by TEXT NOT NULL,
                started_at INTEGER NOT NULL,
                prepared_at INTEGER
            );
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS player_preferences (
                steamid TEXT PRIMARY KEY REFERENCES users(steamid) ON DELETE CASCADE,
                roles_json TEXT NOT NULL,
                main_role TEXT NOT NULL CHECK(main_role IN ('GK', 'DEF', 'MID', 'WING')),
                updated_at INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS user_profiles (
                steamid TEXT PRIMARY KEY REFERENCES users(steamid) ON DELETE CASCADE,
                country TEXT NOT NULL DEFAULT '',
                bio TEXT NOT NULL DEFAULT '',
                favorite_game TEXT NOT NULL DEFAULT 'css' CHECK(favorite_game IN ('css', 'cs2')),
                availability TEXT NOT NULL DEFAULT 'flexible' CHECK(availability IN ('flexible', 'weekday_evenings', 'weekends', 'late_night')),
                updated_at INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS caps (
                signature TEXT PRIMARY KEY,
                game TEXT NOT NULL,
                map_name TEXT NOT NULL,
                started_at INTEGER NOT NULL,
                player_count INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS cap_players (
                cap_signature TEXT NOT NULL REFERENCES caps(signature) ON DELETE CASCADE,
                steamid TEXT NOT NULL,
                display_name TEXT NOT NULL,
                assigned_role TEXT NOT NULL CHECK(assigned_role IN ('GK', 'DEF', 'MID', 'WING')),
                team TEXT NOT NULL CHECK(team IN ('home', 'away')),
                PRIMARY KEY (cap_signature, steamid)
            );
            CREATE TABLE IF NOT EXISTS cap_chat_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                steamid TEXT NOT NULL REFERENCES users(steamid) ON DELETE CASCADE,
                display_name TEXT NOT NULL,
                message TEXT NOT NULL,
                created_at INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS duration_vote_sessions (
                signature TEXT PRIMARY KEY,
                game TEXT NOT NULL CHECK(game IN ('css', 'cs2')),
                map_name TEXT NOT NULL,
                started_at INTEGER NOT NULL,
                deadline INTEGER NOT NULL,
                resolved_half_seconds INTEGER CHECK(resolved_half_seconds IN (450, 600, 900))
            );
            CREATE TABLE IF NOT EXISTS duration_votes (
                signature TEXT NOT NULL REFERENCES duration_vote_sessions(signature) ON DELETE CASCADE,
                steamid TEXT NOT NULL REFERENCES users(steamid) ON DELETE CASCADE,
                half_seconds INTEGER NOT NULL CHECK(half_seconds IN (450, 600, 900)),
                voted_at INTEGER NOT NULL,
                PRIMARY KEY (signature, steamid)
            );
            CREATE INDEX IF NOT EXISTS idx_caps_started_at ON caps(started_at DESC);
            CREATE INDEX IF NOT EXISTS idx_cap_players_role ON cap_players(assigned_role);
            CREATE INDEX IF NOT EXISTS idx_cap_chat_messages_created_at ON cap_chat_messages(created_at, id);
            CREATE INDEX IF NOT EXISTS idx_duration_votes_signature ON duration_votes(signature);
            INSERT OR IGNORE INTO settings (key, value) VALUES ('test_mode', '0');
            INSERT OR IGNORE INTO settings (key, value) VALUES ('cap_active', '0');
            INSERT OR IGNORE INTO settings (key, value) VALUES ('cap_creator', '');
            INSERT OR IGNORE INTO settings (key, value) VALUES ('cap_type', 'standard');
            INSERT OR IGNORE INTO settings (key, value) VALUES ('cap_name', 'Soccer Mod');
            INSERT OR IGNORE INTO settings (key, value) VALUES ('cap_game', 'css');
            INSERT OR IGNORE INTO settings (key, value) VALUES ('cap_map', 'Titan Club 2026');
            INSERT OR IGNORE INTO settings (key, value) VALUES ('cap_nonce', '');
            """
        )
        columns = {row[1] for row in connection.execute("PRAGMA table_info(queue_members)")}
        if "main_role" not in columns:
            connection.execute("ALTER TABLE queue_members ADD COLUMN main_role TEXT")
        if "activity_due_at" not in columns:
            connection.execute("ALTER TABLE queue_members ADD COLUMN activity_due_at INTEGER")
        match_start_columns = {row[1] for row in connection.execute("PRAGMA table_info(match_starts)")}
        if "prepared_at" not in match_start_columns:
            connection.execute("ALTER TABLE match_starts ADD COLUMN prepared_at INTEGER")
        for column, definition in (
            ("last_phase", "TEXT"),
            ("last_period", "INTEGER"),
            ("last_periods", "INTEGER"),
            ("last_score_ct", "INTEGER"),
            ("last_score_t", "INTEGER"),
            ("observed_started", "INTEGER NOT NULL DEFAULT 0"),
            ("ended_at", "INTEGER"),
            ("end_reason", "TEXT"),
        ):
            if column not in match_start_columns:
                connection.execute(f"ALTER TABLE match_starts ADD COLUMN {column} {definition}")
                match_start_columns.add(column)
        cap_columns = {row[1] for row in connection.execute("PRAGMA table_info(caps)")}
        for column, definition in (
            ("score_ct", "INTEGER"),
            ("score_t", "INTEGER"),
            ("ended_at", "INTEGER"),
            ("end_reason", "TEXT"),
        ):
            if column not in cap_columns:
                connection.execute(f"ALTER TABLE caps ADD COLUMN {column} {definition}")
                cap_columns.add(column)
        connection.execute(
            "UPDATE queue_members SET activity_due_at = ? WHERE activity_due_at IS NULL",
            (int(time.time()) + QUEUE_ACTIVITY_INTERVAL,),
        )
        rows = connection.execute("SELECT steamid, roles_json FROM queue_members WHERE main_role IS NULL").fetchall()
        for row in rows:
            try:
                roles = json.loads(row["roles_json"])
            except (TypeError, ValueError, json.JSONDecodeError):
                roles = []
            if roles:
                connection.execute("UPDATE queue_members SET main_role = ? WHERE steamid = ?", (roles[0], row["steamid"]))
        user_columns = {row[1] for row in connection.execute("PRAGMA table_info(users)")}
        if "account_status" not in user_columns:
            connection.execute("ALTER TABLE users ADD COLUMN account_status TEXT NOT NULL DEFAULT 'active'")
        connection.execute("PRAGMA optimize")


def _b64encode(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).rstrip(b"=").decode("ascii")


def _b64decode(value: str) -> bytes:
    return base64.urlsafe_b64decode(value + "=" * (-len(value) % 4))


def sign_payload(payload: dict) -> str:
    body = _b64encode(json.dumps(payload, separators=(",", ":"), sort_keys=True).encode("utf-8"))
    signature = _b64encode(hmac.new(SECRET, body.encode("ascii"), hashlib.sha256).digest())
    return f"{body}.{signature}"


def read_payload(token: str | None) -> dict | None:
    if not token or "." not in token:
        return None
    body, signature = token.rsplit(".", 1)
    expected = _b64encode(hmac.new(SECRET, body.encode("ascii"), hashlib.sha256).digest())
    if not hmac.compare_digest(signature, expected):
        return None
    try:
        payload = json.loads(_b64decode(body))
    except (ValueError, json.JSONDecodeError):
        return None
    if not isinstance(payload, dict) or int(payload.get("exp", 0)) < int(time.time()):
        return None
    return payload


def safe_return_to(value: str) -> str:
    if not value.startswith("/") or value.startswith("//") or "\\" in value or len(value) > 240:
        return "/"
    return value


def clean_display_name(value: object, steamid: str) -> str:
    name = "".join(character for character in str(value or "").strip() if character.isprintable())[:64]
    return name or f"Steam {steamid[-6:]}"


def fetch_steam_name(steamid: str) -> str:
    cached = PROFILE_CACHE.get(steamid)
    if cached and cached[1] > time.time():
        return cached[0]
    name = ""
    try:
        if STEAM_WEB_API_KEY:
            params = urllib.parse.urlencode({"key": STEAM_WEB_API_KEY, "steamids": steamid})
            request = urllib.request.Request(
                f"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?{params}",
                headers={"User-Agent": "KICKOFF-SoccerMod/1.1"},
            )
            with urllib.request.urlopen(request, timeout=8) as response:
                payload = json.loads(response.read(262144).decode("utf-8"))
            players = payload.get("response", {}).get("players", [])
            if players:
                name = players[0].get("personaname", "")
        else:
            request = urllib.request.Request(
                f"https://steamcommunity.com/profiles/{steamid}/?xml=1",
                headers={"User-Agent": "KICKOFF-SoccerMod/1.1"},
            )
            with urllib.request.urlopen(request, timeout=8) as response:
                root = ET.fromstring(response.read(262144))
            name = root.findtext("steamID", default="")
    except (urllib.error.URLError, TimeoutError, ValueError, json.JSONDecodeError, ET.ParseError):
        name = ""
    result = clean_display_name(name, steamid)
    PROFILE_CACHE[steamid] = (result, time.time() + 900)
    return result


def upsert_user(steamid: str, display_name: str) -> str:
    now = int(time.time())
    with database() as connection:
        existing = connection.execute("SELECT role FROM users WHERE steamid = ?", (steamid,)).fetchone()
        role = "admin" if steamid == OWNER_STEAM_ID else (existing["role"] if existing else "user")
        connection.execute(
            """INSERT INTO users (steamid, display_name, role, created_at, last_login)
               VALUES (?, ?, ?, ?, ?)
               ON CONFLICT(steamid) DO UPDATE SET
                 display_name = excluded.display_name,
                 role = CASE WHEN excluded.steamid = ? THEN 'admin' ELSE users.role END,
                 last_login = excluded.last_login""",
            (steamid, display_name, role, now, now, OWNER_STEAM_ID),
        )
    return role


def user_record(steamid: str) -> dict | None:
    with database() as connection:
        row = connection.execute(
            "SELECT steamid, display_name, role, account_status FROM users WHERE steamid = ?", (steamid,)
        ).fetchone()
    if not row:
        return None
    role = "admin" if steamid == OWNER_STEAM_ID else row["role"]
    status = "active" if steamid == OWNER_STEAM_ID else row["account_status"]
    return {"steamid": row["steamid"], "name": row["display_name"], "role": role, "status": status}


def valid_role_selection(roles: object) -> list[str] | None:
    if not isinstance(roles, list):
        return None
    normalized = list(dict.fromkeys(str(role).upper() for role in roles))
    if len(normalized) < 2 or any(role not in ALLOWED_ROLES for role in normalized):
        return None
    return normalized


def valid_main_role(main_role: object, roles: list[str]) -> str | None:
    normalized = str(main_role or "").upper()
    return normalized if normalized in roles else None


def prepare_game_server(game: str, assignments: list[dict], half_seconds: int) -> bool:
    if not RCON_HELPER_URL or not RCON_HELPER_SECRET:
        return False
    body = json.dumps(
        {
            "game": game,
            "halfSeconds": half_seconds,
            "assignments": [
                {"id": player["id"], "role": player["role"], "team": player["team"]}
                for player in assignments
            ]
        },
        separators=(",", ":"),
    ).encode("utf-8")
    request = urllib.request.Request(
        RCON_HELPER_URL,
        data=body,
        headers={
            "Authorization": f"Bearer {RCON_HELPER_SECRET}",
            "Content-Type": "application/json",
            "User-Agent": "KICKOFF-SoccerMod/1.2",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=8) as response:
            return response.status == HTTPStatus.OK
    except (urllib.error.URLError, TimeoutError, ValueError):
        return False


def clear_game_server_cap(game: str) -> bool:
    if not RCON_HELPER_URL or not RCON_HELPER_SECRET or game not in SUPPORTED_CAP_GAMES:
        return False
    parsed = urllib.parse.urlsplit(RCON_HELPER_URL)
    helper_base_path = parsed.path.rsplit("/", 1)[0]
    clear_url = urllib.parse.urlunsplit(
        (parsed.scheme, parsed.netloc, f"{helper_base_path}/clear", "", "")
    )
    body = json.dumps({"game": game}, separators=(",", ":")).encode("utf-8")
    request = urllib.request.Request(
        clear_url,
        data=body,
        headers={
            "Authorization": f"Bearer {RCON_HELPER_SECRET}",
            "Content-Type": "application/json",
            "User-Agent": "KICKOFF-SoccerMod/1.2",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=8) as response:
            return response.status == HTTPStatus.OK
    except (urllib.error.URLError, TimeoutError, ValueError):
        return False


def stop_game_server_match(game: str) -> bool:
    """Ask the private helper to stop the CS2 SoccerMod match on cap cancel."""
    if not RCON_HELPER_URL or not RCON_HELPER_SECRET or game != "cs2":
        return False
    parsed = urllib.parse.urlsplit(RCON_HELPER_URL)
    helper_base_path = parsed.path.rsplit("/", 1)[0]
    stop_url = urllib.parse.urlunsplit(
        (parsed.scheme, parsed.netloc, f"{helper_base_path}/stop", "", "")
    )
    body = json.dumps({"game": game}, separators=(",", ":")).encode("utf-8")
    request = urllib.request.Request(
        stop_url,
        data=body,
        headers={
            "Authorization": f"Bearer {RCON_HELPER_SECRET}",
            "Content-Type": "application/json",
            "User-Agent": "KICKOFF-SoccerMod/1.2",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=8) as response:
            return response.status == HTTPStatus.OK
    except (urllib.error.URLError, TimeoutError, ValueError):
        return False


MATCH_STATUS_RE = re.compile(
    r"phase=(?P<phase>[A-Za-z]+)\s+period=(?P<period>\d+)/(?P<periods>\d+)"
    r"\s+score=.*?(?P<score_ct>-?\d+)\s+-\s+(?P<score_t>-?\d+)\s+\S+",
    re.IGNORECASE,
)


def parse_match_status_response(responses: object) -> dict | None:
    """Parse the allowlisted css_match status response into stable fields."""
    if not isinstance(responses, list):
        return None
    for response in responses:
        match = MATCH_STATUS_RE.search(str(response or ""))
        if not match:
            continue
        return {
            "phase": match.group("phase"),
            "period": int(match.group("period")),
            "periods": int(match.group("periods")),
            "scoreCt": int(match.group("score_ct")),
            "scoreT": int(match.group("score_t")),
        }
    return None


def read_game_match_status(game: str) -> dict | None:
    """Read match state through the private helper (never accepts arbitrary RCON)."""
    if game != "cs2" or not RCON_HELPER_URL or not RCON_HELPER_SECRET:
        return None
    parsed = urllib.parse.urlsplit(RCON_HELPER_URL)
    helper_base_path = parsed.path.rsplit("/", 1)[0]
    status_url = urllib.parse.urlunsplit(
        (parsed.scheme, parsed.netloc, f"{helper_base_path}/match-status/{game}", "", "")
    )
    request = urllib.request.Request(
        status_url,
        headers={
            "Authorization": f"Bearer {RCON_HELPER_SECRET}",
            "Accept": "application/json",
            "User-Agent": "KICKOFF-SoccerMod/1.2",
        },
        method="GET",
    )
    try:
        with urllib.request.urlopen(request, timeout=6) as response:
            if response.status != HTTPStatus.OK:
                return None
            payload = json.loads(response.read(32768).decode("utf-8"))
        return parse_match_status_response(payload.get("responses")) if isinstance(payload, dict) else None
    except (OSError, urllib.error.URLError, TimeoutError, ValueError, json.JSONDecodeError, UnicodeDecodeError):
        return None


def can_place_all(players: list[dict]) -> bool:
    ordered = sorted(players, key=lambda player: (len(player["roles"]), player["steamid"]))
    memo: set[tuple] = set()

    def search(index: int, capacity: dict[str, int]) -> bool:
        if index == len(ordered):
            return True
        key = (index, *(capacity[role] for role in ALLOWED_ROLES))
        if key in memo:
            return False
        for role in ordered[index]["roles"]:
            if capacity[role] <= 0:
                continue
            next_capacity = dict(capacity)
            next_capacity[role] -= 1
            if search(index + 1, next_capacity):
                return True
        memo.add(key)
        return False

    return search(0, dict(ROLE_CAPACITY))


def queue_members(connection: sqlite3.Connection | None = None) -> list[dict]:
    owns_connection = connection is None
    db = connection or database()
    try:
        rows = db.execute(
            """SELECT queue_members.steamid, queue_members.display_name, queue_members.roles_json,
                      queue_members.main_role, queue_members.joined_at, queue_members.activity_due_at,
                      COALESCE(user_profiles.country, '') AS country
                 FROM queue_members
                 LEFT JOIN user_profiles ON user_profiles.steamid = queue_members.steamid
                 ORDER BY queue_members.joined_at, queue_members.steamid"""
        ).fetchall()
        return [
            {
                "id": row["steamid"],
                "name": row["display_name"],
                "roles": json.loads(row["roles_json"]),
                "mainRole": row["main_role"] or json.loads(row["roles_json"])[0],
                "joinedAt": row["joined_at"],
                "activityDueAt": row["activity_due_at"],
                "country": row["country"],
                "time": "now",
            }
            for row in rows
        ]
    finally:
        if owns_connection:
            db.close()


def prune_inactive_queue(connection: sqlite3.Connection, now: int | None = None) -> list[str]:
    current_time = int(time.time()) if now is None else int(now)
    expired = connection.execute(
        "SELECT steamid FROM queue_members WHERE activity_due_at + ? <= ?",
        (QUEUE_ACTIVITY_GRACE, current_time),
    ).fetchall()
    expired_ids = [row["steamid"] for row in expired]
    if expired_ids:
        connection.executemany("DELETE FROM queue_members WHERE steamid = ?", ((steamid,) for steamid in expired_ids))
        reset_match_flow(connection)
        if connection.execute("SELECT COUNT(*) FROM queue_members").fetchone()[0] == 0:
            connection.execute("UPDATE settings SET value = '0' WHERE key = 'cap_active'")
            connection.execute("UPDATE settings SET value = '' WHERE key = 'cap_creator'")
            connection.execute("DELETE FROM cap_chat_messages")
    return expired_ids


def test_mode_enabled(connection: sqlite3.Connection | None = None) -> bool:
    owns_connection = connection is None
    db = connection or database()
    try:
        row = db.execute("SELECT value FROM settings WHERE key = 'test_mode'").fetchone()
        return bool(row and row["value"] == "1")
    finally:
        if owns_connection:
            db.close()


def cap_is_active(connection: sqlite3.Connection | None = None) -> bool:
    owns_connection = connection is None
    db = connection or database()
    try:
        row = db.execute("SELECT value FROM settings WHERE key = 'cap_active'").fetchone()
        return bool(row and row["value"] == "1")
    finally:
        if owns_connection:
            db.close()


def cap_creator(connection: sqlite3.Connection | None = None) -> dict | None:
    owns_connection = connection is None
    db = connection or database()
    try:
        row = db.execute(
            """SELECT users.steamid, users.display_name
               FROM settings JOIN users ON users.steamid = settings.value
               WHERE settings.key = 'cap_creator'"""
        ).fetchone()
        return {"id": row["steamid"], "name": row["display_name"]} if row else None
    finally:
        if owns_connection:
            db.close()


def cap_type(connection: sqlite3.Connection | None = None) -> str:
    owns_connection = connection is None
    db = connection or database()
    try:
        row = db.execute("SELECT value FROM settings WHERE key = 'cap_type'").fetchone()
        return row["value"] if row and row["value"] in ("standard", "custom") else "standard"
    finally:
        if owns_connection:
            db.close()


def clean_cap_name(value: object) -> str:
    name = "".join(character for character in str(value or "").strip() if character.isprintable())[:48]
    return name or "Soccer Mod"


def cap_details(connection: sqlite3.Connection | None = None) -> dict:
    owns_connection = connection is None
    db = connection or database()
    try:
        rows = db.execute(
            "SELECT key, value FROM settings WHERE key IN ('cap_name', 'cap_game', 'cap_map', 'cap_nonce')"
        ).fetchall()
        settings = {row["key"]: row["value"] for row in rows}
        game = settings.get("cap_game", "css")
        if game not in SUPPORTED_CAP_GAMES:
            game = "css"
        supported_maps = SUPPORTED_CAP_GAMES[game]["maps"]
        map_name = settings.get("cap_map", "")
        if map_name not in supported_maps:
            map_name = supported_maps[0]
        return {
            "name": clean_cap_name(settings.get("cap_name", "Soccer Mod")),
            "game": game,
            "map": map_name,
            "nonce": settings.get("cap_nonce", ""),
        }
    finally:
        if owns_connection:
            db.close()


def ensure_cap_nonce(connection: sqlite3.Connection) -> str:
    details = cap_details(connection)
    nonce = str(details.get("nonce", ""))
    if not re.fullmatch(r"[0-9a-f]{16}", nonce):
        nonce = secrets.token_hex(8)
        connection.execute("UPDATE settings SET value = ? WHERE key = 'cap_nonce'", (nonce,))
    return nonce


def reset_match_flow(connection: sqlite3.Connection) -> None:
    connection.execute("DELETE FROM match_starts")
    connection.execute("DELETE FROM duration_vote_sessions")


def cap_flow_signature(details: dict, members: list[dict], test_enabled: bool) -> str:
    lineup_key = "|".join(sorted(member["id"] for member in members))
    mode = "test" if test_enabled else "full"
    return hashlib.sha256(
        f'{details["game"]}|{details["map"]}|{details.get("nonce", "")}|{mode}|{lineup_key}'.encode("utf-8")
    ).hexdigest()


def resolve_duration_vote(connection: sqlite3.Connection, signature: str, now: int | None = None) -> int | None:
    current_time = int(time.time()) if now is None else int(now)
    session = connection.execute(
        "SELECT deadline, resolved_half_seconds FROM duration_vote_sessions WHERE signature = ?",
        (signature,),
    ).fetchone()
    if not session:
        return None
    if session["resolved_half_seconds"] in ALLOWED_HALF_SECONDS:
        return int(session["resolved_half_seconds"])
    if current_time < int(session["deadline"]):
        return None
    counts = {
        seconds: connection.execute(
            "SELECT COUNT(*) FROM duration_votes WHERE signature = ? AND half_seconds = ?",
            (signature, seconds),
        ).fetchone()[0]
        for seconds in ALLOWED_HALF_SECONDS
    }
    highest = max(counts.values(), default=0)
    leaders = [seconds for seconds, count in counts.items() if highest > 0 and count == highest]
    winner = leaders[0] if len(leaders) == 1 else DEFAULT_HALF_SECONDS
    connection.execute(
        "UPDATE duration_vote_sessions SET resolved_half_seconds = ? WHERE signature = ?",
        (winner, signature),
    )
    return winner


def duration_vote_payload(
    connection: sqlite3.Connection,
    signature: str,
    steamid: str,
    now: int | None = None,
) -> dict | None:
    current_time = int(time.time()) if now is None else int(now)
    resolved = resolve_duration_vote(connection, signature, current_time)
    session = connection.execute(
        "SELECT deadline FROM duration_vote_sessions WHERE signature = ?",
        (signature,),
    ).fetchone()
    if not session:
        return None
    counts = {
        str(seconds): connection.execute(
            "SELECT COUNT(*) FROM duration_votes WHERE signature = ? AND half_seconds = ?",
            (signature, seconds),
        ).fetchone()[0]
        for seconds in ALLOWED_HALF_SECONDS
    }
    own_vote = connection.execute(
        "SELECT half_seconds FROM duration_votes WHERE signature = ? AND steamid = ?",
        (signature, steamid),
    ).fetchone()
    deadline = int(session["deadline"])
    return {
        "signature": signature,
        "deadline": deadline,
        "secondsRemaining": max(0, deadline - current_time),
        "resolved": resolved is not None,
        "halfSeconds": resolved,
        "defaultHalfSeconds": DEFAULT_HALF_SECONDS,
        "options": list(ALLOWED_HALF_SECONDS),
        "counts": counts,
        "voterCount": sum(counts.values()),
        "ownVote": int(own_vote["half_seconds"]) if own_vote else None,
    }


def profile_needs_country(steamid: str) -> bool:
    with database() as connection:
        row = connection.execute("SELECT country FROM user_profiles WHERE steamid = ?", (steamid,)).fetchone()
    return not row or not str(row["country"]).strip()


def request_public_ip(handler: BaseHTTPRequestHandler) -> str:
    """Read the client IP only from the trusted reverse-proxy path."""
    try:
        peer = ipaddress.ip_address(handler.client_address[0])
    except ValueError:
        return ""
    if not (peer.is_private or peer.is_loopback):
        return ""
    forwarded = handler.headers.get("X-Forwarded-For", "")
    for candidate in forwarded.split(","):
        try:
            address = ipaddress.ip_address(candidate.strip())
        except ValueError:
            continue
        if address.is_global:
            return str(address)
    return ""


def country_from_request(handler: BaseHTTPRequestHandler) -> str:
    """Look up and retain only a country name; the IP itself is never stored."""
    client_ip = request_public_ip(handler)
    if not client_ip:
        return ""
    try:
        request = urllib.request.Request(
            f"https://ipwho.is/{urllib.parse.quote(client_ip, safe='')}",
            headers={"User-Agent": "KICKOFF-SoccerMod/1.0"},
        )
        with urllib.request.urlopen(request, timeout=3) as response:
            payload = json.loads(response.read(32768).decode("utf-8"))
        if payload.get("success") is False:
            return ""
        return clean_profile_text(payload.get("country"), 40) or ""
    except (urllib.error.URLError, TimeoutError, ValueError, json.JSONDecodeError, UnicodeDecodeError):
        return ""


def ensure_profile(connection: sqlite3.Connection, steamid: str, country_hint: str = "") -> None:
    connection.execute(
        """INSERT OR IGNORE INTO user_profiles (steamid, country, bio, favorite_game, availability, updated_at)
           VALUES (?, ?, '', 'css', 'flexible', ?)""",
        (steamid, country_hint, int(time.time())),
    )
    if country_hint:
        connection.execute(
            "UPDATE user_profiles SET country = ?, updated_at = ? WHERE steamid = ? AND trim(country) = ''",
            (country_hint, int(time.time()), steamid),
        )


def profile_for_user(steamid: str, country_hint: str = "") -> dict | None:
    with database() as connection:
        user = connection.execute(
            "SELECT steamid, display_name FROM users WHERE steamid = ?", (steamid,)
        ).fetchone()
        if not user:
            return None
        ensure_profile(connection, steamid, country_hint)
        profile = connection.execute(
            "SELECT country, bio, favorite_game, availability, updated_at FROM user_profiles WHERE steamid = ?",
            (steamid,),
        ).fetchone()
        preference = connection.execute(
            "SELECT roles_json, main_role FROM player_preferences WHERE steamid = ?", (steamid,)
        ).fetchone()
        appearances = connection.execute(
            "SELECT assigned_role, COUNT(*) AS appearances FROM cap_players WHERE steamid = ? GROUP BY assigned_role ORDER BY appearances DESC, assigned_role LIMIT 1",
            (steamid,),
        ).fetchone()
        caps_played = connection.execute(
            "SELECT COUNT(*) FROM cap_players WHERE steamid = ?", (steamid,)
        ).fetchone()[0]
    roles = json.loads(preference["roles_json"]) if preference else []
    return {
        "steamid": user["steamid"],
        "name": user["display_name"],
        "country": profile["country"] if profile else "",
        "bio": profile["bio"] if profile else "",
        "favoriteGame": profile["favorite_game"] if profile else "css",
        "availability": profile["availability"] if profile else "flexible",
        "updatedAt": profile["updated_at"] if profile else None,
        "preferences": {
            "roles": roles,
            "mainRole": preference["main_role"] if preference else None,
        },
        "stats": {
            "capsPlayed": caps_played,
            "mostPlayedRole": appearances["assigned_role"] if appearances else None,
        },
    }


def clean_profile_text(value: object, maximum: int) -> str | None:
    if not isinstance(value, str):
        return None
    cleaned = " ".join(value.split())
    if len(cleaned) > maximum:
        return None
    return cleaned


def soccermod_statistics() -> dict:
    result = {
        "available": False,
        "registeredPlayers": 0,
        "activePlayers30d": 0,
        "matchAppearances": 0,
        "gameplay": {key: 0 for key in (
            "goals", "assists", "ownGoals", "hits", "passes", "interceptions",
            "ballLosses", "saves", "roundsWon", "roundsLost", "mvp", "motm",
        )},
        "leaders": [],
    }
    if not SOCCERMOD_DATABASE_PATH or not os.path.isfile(SOCCERMOD_DATABASE_PATH):
        return result
    source = None
    try:
        source = sqlite3.connect(f"file:{SOCCERMOD_DATABASE_PATH}?mode=ro", uri=True, timeout=3)
        source.row_factory = sqlite3.Row
        player_row = source.execute(
            "SELECT COUNT(*) AS registered_players, "
            "COUNT(CASE WHEN last_connected >= ? THEN 1 END) AS active_players "
            "FROM soccer_mod_players",
            (int(time.time()) - 30 * 86400,),
        ).fetchone()
        stats_row = source.execute(
            "SELECT COALESCE(SUM(matches), 0) AS appearances, "
            "COALESCE(SUM(goals), 0) AS goals, COALESCE(SUM(assists), 0) AS assists, "
            "COALESCE(SUM(own_goals), 0) AS own_goals, COALESCE(SUM(hits), 0) AS hits, "
            "COALESCE(SUM(passes), 0) AS passes, COALESCE(SUM(interceptions), 0) AS interceptions, "
            "COALESCE(SUM(ball_losses), 0) AS ball_losses, COALESCE(SUM(saves), 0) AS saves, "
            "COALESCE(SUM(rounds_won), 0) AS rounds_won, COALESCE(SUM(rounds_lost), 0) AS rounds_lost, "
            "COALESCE(SUM(mvp), 0) AS mvp, COALESCE(SUM(motm), 0) AS motm "
            "FROM soccer_mod_match_stats"
        ).fetchone()
        leader_rows = source.execute(
            "SELECT p.name, s.matches, s.goals, s.assists, s.saves, s.interceptions, s.points "
            "FROM soccer_mod_players p JOIN soccer_mod_match_stats s ON s.steamid = p.steamid "
            "WHERE s.matches > 0 OR s.goals > 0 OR s.assists > 0 OR s.hits > 0 "
            "ORDER BY s.points DESC, s.goals DESC, s.assists DESC LIMIT 5"
        ).fetchall()
        result = {
            "available": True,
            "registeredPlayers": int(player_row["registered_players"]),
            "activePlayers30d": int(player_row["active_players"]),
            "matchAppearances": int(stats_row["appearances"]),
            "gameplay": {
                "goals": int(stats_row["goals"]),
                "assists": int(stats_row["assists"]),
                "ownGoals": int(stats_row["own_goals"]),
                "hits": int(stats_row["hits"]),
                "passes": int(stats_row["passes"]),
                "interceptions": int(stats_row["interceptions"]),
                "ballLosses": int(stats_row["ball_losses"]),
                "saves": int(stats_row["saves"]),
                "roundsWon": int(stats_row["rounds_won"]),
                "roundsLost": int(stats_row["rounds_lost"]),
                "mvp": int(stats_row["mvp"]),
                "motm": int(stats_row["motm"]),
            },
            "leaders": [
                {
                    "name": row["name"],
                    "matches": int(row["matches"]),
                    "goals": int(row["goals"]),
                    "assists": int(row["assists"]),
                    "saves": int(row["saves"]),
                    "interceptions": int(row["interceptions"]),
                    "points": int(row["points"]),
                }
                for row in leader_rows
            ],
        }
    except (OSError, sqlite3.Error, TypeError, ValueError):
        return result
    finally:
        if source is not None:
            source.close()
    return result


def community_statistics() -> dict:
    with database() as connection:
        cap_count = connection.execute("SELECT COUNT(*) FROM caps").fetchone()[0]
        preference_rows = connection.execute(
            "SELECT assigned_role AS main_role, COUNT(*) AS count FROM cap_players GROUP BY assigned_role"
        ).fetchall()
        recent_rows = connection.execute(
            "SELECT game, map_name, started_at, player_count, score_ct, score_t, ended_at, end_reason "
            "FROM caps ORDER BY started_at DESC LIMIT 5"
        ).fetchall()
    positions = {role: 0 for role in ALLOWED_ROLES}
    for row in preference_rows:
        if row["main_role"] in positions:
            positions[row["main_role"]] = int(row["count"])
    return {
        "capsPlayed": int(cap_count),
        "positionPreferences": positions,
        "recentCaps": [
            {
                "game": row["game"],
                "map": row["map_name"],
                "startedAt": int(row["started_at"]),
                "players": int(row["player_count"]),
                "scoreCt": int(row["score_ct"]) if row["score_ct"] is not None else None,
                "scoreT": int(row["score_t"]) if row["score_t"] is not None else None,
                "endedAt": int(row["ended_at"]) if row["ended_at"] is not None else None,
                "endReason": row["end_reason"] or None,
            }
            for row in recent_rows
        ],
        "soccermod": soccermod_statistics(),
    }


def valid_cap_lineup(value: object, members: list[dict]) -> list[dict] | None:
    if not isinstance(value, list) or len(value) != 12 or len(members) != 12:
        return None
    member_by_id = {member["id"]: member for member in members}
    lineup: list[dict] = []
    seen: set[str] = set()
    for item in value:
        if not isinstance(item, dict):
            return None
        steamid = str(item.get("id", ""))
        role = str(item.get("role", "")).upper()
        team = str(item.get("team", "")).lower()
        member = member_by_id.get(steamid)
        if not member or steamid in seen or role not in ALLOWED_ROLES or team not in ("home", "away"):
            return None
        seen.add(steamid)
        lineup.append({"id": steamid, "name": member["name"], "role": role, "team": team})
    if seen != set(member_by_id):
        return None
    for team in ("home", "away"):
        team_members = [player for player in lineup if player["team"] == team]
        if len(team_members) != 6:
            return None
        for role, capacity in ROLE_CAPACITY.items():
            if sum(player["role"] == role for player in team_members) != capacity // 2:
                return None
    return lineup


def preference_score(member: dict, role: str) -> int:
    if role == member["mainRole"]:
        return 100
    if role in member["roles"]:
        return 1
    return 0


def best_assignment_score(members: list[dict], require_full_roster: bool) -> int | None:
    ordered = sorted(members, key=lambda member: member["id"])
    memo: dict[tuple, int | None] = {}

    def search(index: int, capacity: dict[str, int]) -> int | None:
        if index == len(ordered):
            if require_full_roster and any(capacity.values()):
                return None
            return 0
        key = (index, *(capacity[role] for role in ALLOWED_ROLES))
        if key in memo:
            return memo[key]
        best = None
        for role in ALLOWED_ROLES:
            if capacity[role] <= 0:
                continue
            next_capacity = dict(capacity)
            next_capacity[role] -= 1
            suffix = search(index + 1, next_capacity)
            if suffix is None:
                continue
            score = suffix + preference_score(ordered[index], role)
            best = score if best is None else max(best, score)
        memo[key] = best
        return best

    return search(0, dict(ROLE_CAPACITY))


def valid_match_assignments(value: object, members: list[dict]) -> list[dict] | None:
    """Validate a full draw or the real underfilled roster used by admin test mode."""
    if not isinstance(value, list) or not members or len(value) != len(members) or len(value) > 12:
        return None
    member_by_id = {member["id"]: member for member in members}
    lineup: list[dict] = []
    seen: set[str] = set()
    team_counts = {"home": 0, "away": 0}
    role_counts = {role: 0 for role in ALLOWED_ROLES}
    team_role_counts = {
        "home": {role: 0 for role in ALLOWED_ROLES},
        "away": {role: 0 for role in ALLOWED_ROLES},
    }
    for item in value:
        if not isinstance(item, dict):
            return None
        steamid = str(item.get("id", ""))
        role = str(item.get("role", "")).upper()
        team = str(item.get("team", "")).lower()
        member = member_by_id.get(steamid)
        if not member or steamid in seen or role not in ALLOWED_ROLES or team not in team_counts:
            return None
        seen.add(steamid)
        team_counts[team] += 1
        role_counts[role] += 1
        team_role_counts[team][role] += 1
        lineup.append({"id": steamid, "name": member["name"], "role": role, "team": team})
    if seen != set(member_by_id) or any(count > 6 for count in team_counts.values()):
        return None
    if any(role_counts[role] > ROLE_CAPACITY[role] for role in ALLOWED_ROLES):
        return None
    if any(
        team_role_counts[team][role] > ROLE_CAPACITY[role] // 2
        for team in ("home", "away")
        for role in ALLOWED_ROLES
    ):
        return None
    if len(members) == 12 and not valid_cap_lineup(value, members):
        return None
    submitted_score = sum(preference_score(member_by_id[player["id"]], player["role"]) for player in lineup)
    optimal_score = best_assignment_score(members, len(members) == 12)
    if optimal_score is None or submitted_score != optimal_score:
        return None
    return lineup


def record_cap(signature: str, game: str, map_name: str, started_at: int, lineup: list[dict]) -> None:
    with database() as connection:
        inserted = connection.execute(
            "INSERT OR IGNORE INTO caps (signature, game, map_name, started_at, player_count) VALUES (?, ?, ?, ?, ?)",
            (signature, game, map_name, started_at, len(lineup)),
        ).rowcount
        if inserted:
            connection.executemany(
                """INSERT INTO cap_players
                   (cap_signature, steamid, display_name, assigned_role, team)
                   VALUES (?, ?, ?, ?, ?)""",
                [
                    (signature, player["id"], player["name"], player["role"], player["team"])
                    for player in lineup
                ],
            )


class KickoffHandler(BaseHTTPRequestHandler):
    server_version = "KICKOFF"
    sys_version = ""

    def log_message(self, fmt: str, *args) -> None:
        route = urllib.parse.urlsplit(self.path).path
        print(f'{self.address_string()} - "{self.command} {route}"', flush=True)

    def _cookies(self) -> SimpleCookie:
        cookies = SimpleCookie()
        try:
            cookies.load(self.headers.get("Cookie", ""))
        except Exception:
            return SimpleCookie()
        return cookies

    def _cookie_value(self, name: str) -> str | None:
        morsel = self._cookies().get(name)
        return morsel.value if morsel else None

    def _session_user(self) -> dict | None:
        session = read_payload(self._cookie_value(SESSION_COOKIE))
        steamid = str(session.get("steamid", "")) if session else ""
        if not re.fullmatch(r"\d{17}", steamid):
            return None
        user = user_record(steamid)
        if user:
            return user if user["status"] == "active" else None
        name = fetch_steam_name(steamid)
        role = upsert_user(steamid, name)
        return {"steamid": steamid, "name": name, "role": role, "status": "active"}

    def _request_json(self) -> dict | None:
        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            return None
        if length < 0 or length > 4096:
            return None
        try:
            value = json.loads(self.rfile.read(length) or b"{}")
        except (json.JSONDecodeError, UnicodeDecodeError):
            return None
        return value if isinstance(value, dict) else None

    def _send_headers(self, status: int, content_type: str, length: int = 0) -> None:
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(length))
        self.send_header("Cache-Control", "no-store")
        self.send_header("Pragma", "no-cache")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Referrer-Policy", "no-referrer")

    def _json(self, status: int, payload: dict) -> None:
        body = json.dumps(payload, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
        self._send_headers(status, "application/json; charset=utf-8", len(body))
        self.end_headers()
        self.wfile.write(body)

    def _redirect(self, location: str, cookies: list[str] | None = None) -> None:
        self.send_response(HTTPStatus.FOUND)
        self.send_header("Location", location)
        self.send_header("Cache-Control", "no-store")
        self.send_header("Referrer-Policy", "no-referrer")
        for cookie in cookies or []:
            self.send_header("Set-Cookie", cookie)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def _require_post_user(self, admin: bool = False) -> dict | None:
        if self.headers.get("X-Requested-With") != "KICKOFF":
            self._json(HTTPStatus.FORBIDDEN, {"error": "forbidden"})
            return None
        user = self._session_user()
        if not user:
            self._json(HTTPStatus.UNAUTHORIZED, {"error": "authentication_required"})
            return None
        if admin and user["role"] != "admin":
            self._json(HTTPStatus.FORBIDDEN, {"error": "admin_required"})
            return None
        return user

    def do_GET(self) -> None:
        url = urllib.parse.urlsplit(self.path)
        if url.path == "/auth/steam":
            self._start_steam_login(url.query)
            return
        if url.path == "/auth/steam/callback":
            self._finish_steam_login(url.query)
            return
        if url.path == "/api/me":
            user = self._session_user()
            if not user:
                self._json(HTTPStatus.UNAUTHORIZED, {"authenticated": False})
                return
            country_hint = country_from_request(self) if profile_needs_country(user["steamid"]) else ""
            profile_for_user(user["steamid"], country_hint)
            self._json(HTTPStatus.OK, {"authenticated": True, **user})
            return
        if url.path == "/api/queue":
            user = self._session_user()
            now = int(time.time())
            with database() as connection:
                expired = prune_inactive_queue(connection, now)
                nonce = ensure_cap_nonce(connection)
                members = queue_members(connection)
                details = cap_details(connection)
                active = cap_is_active(connection)
                creator = cap_creator(connection)
                mode = cap_type(connection)
                test_enabled = test_mode_enabled(connection)
                cap_closed_by_expiry = bool(expired) and not active
            if cap_closed_by_expiry:
                clear_game_server_cap(details["game"])
            self._json(
                HTTPStatus.OK,
                {
                    "members": members,
                    "capacity": ROLE_CAPACITY,
                    "capActive": active,
                    "creator": creator,
                    "capMode": mode,
                    "capName": details["name"],
                    "game": details["game"],
                    "map": details["map"],
                    "capNonce": nonce,
                    "testMode": test_enabled,
                    "serverTime": now,
                    "activityIntervalSeconds": QUEUE_ACTIVITY_INTERVAL,
                    "activityGraceSeconds": QUEUE_ACTIVITY_GRACE,
                    "activityExpired": bool(user and user["steamid"] in expired),
                },
            )
            return
        if url.path == "/api/match/duration-vote":
            user = self._session_user()
            if not user:
                self._json(HTTPStatus.UNAUTHORIZED, {"error": "authentication_required"})
                return
            query = urllib.parse.parse_qs(url.query)
            signature = str(query.get("signature", [""])[0])
            if not re.fullmatch(r"[0-9a-f]{64}", signature):
                self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_vote_signature"})
                return
            with database() as connection:
                connection.execute("BEGIN IMMEDIATE")
                prune_inactive_queue(connection)
                details = cap_details(connection)
                members = queue_members(connection)
                test_enabled = test_mode_enabled(connection)
                is_member = any(member["id"] == user["steamid"] for member in members)
                test_ready = test_enabled and user["role"] == "admin" and len(members) >= 1
                expected = cap_flow_signature(details, members, test_enabled)
                if (
                    not cap_is_active(connection)
                    or cap_type(connection) != "standard"
                    or signature != expected
                    or (not is_member and not test_ready)
                ):
                    connection.commit()
                    self._json(HTTPStatus.CONFLICT, {"error": "duration_vote_unavailable"})
                    return
                vote = duration_vote_payload(connection, signature, user["steamid"])
                connection.commit()
            if not vote:
                self._json(HTTPStatus.NOT_FOUND, {"error": "duration_vote_not_found"})
                return
            self._json(HTTPStatus.OK, {"vote": vote})
            return
        if url.path == "/api/match/status":
            user = self._session_user()
            if not user:
                self._json(HTTPStatus.UNAUTHORIZED, {"error": "authentication_required"})
                return
            query = urllib.parse.parse_qs(url.query)
            signature = str(query.get("signature", [""])[0])
            if not re.fullmatch(r"[0-9a-f]{64}", signature):
                self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_match_signature"})
                return
            with database() as connection:
                connection.execute("BEGIN IMMEDIATE")
                details = cap_details(connection)
                members = queue_members(connection)
                test_enabled = test_mode_enabled(connection)
                creator = cap_creator(connection)
                is_member = any(member["id"] == user["steamid"] for member in members)
                test_ready = test_enabled and user["role"] == "admin" and len(members) >= 1
                expected = cap_flow_signature(details, members, test_enabled)
                row = connection.execute(
                    "SELECT prepared_at, observed_started, last_phase, last_period, last_periods, "
                    "last_score_ct, last_score_t, ended_at, end_reason FROM match_starts WHERE signature = ?",
                    (signature,),
                ).fetchone()
                if (
                    details["game"] != "cs2"
                    or not cap_is_active(connection)
                    or cap_type(connection) != "standard"
                    or signature != expected
                    or not row
                    or row["prepared_at"] is None
                    or (not is_member and not (creator and creator["id"] == user["steamid"]) and not test_ready)
                ):
                    connection.commit()
                    self._json(HTTPStatus.CONFLICT, {"error": "match_status_unavailable"})
                    return
                previous = {
                    "observedStarted": bool(row["observed_started"]),
                    "phase": row["last_phase"],
                    "period": row["last_period"],
                    "periods": row["last_periods"],
                    "scoreCt": row["last_score_ct"],
                    "scoreT": row["last_score_t"],
                    "endedAt": row["ended_at"],
                    "endReason": row["end_reason"],
                }
                connection.commit()

            status = read_game_match_status("cs2")
            if status is None:
                self._json(HTTPStatus.SERVICE_UNAVAILABLE, {"error": "match_status_unavailable"})
                return

            phase = str(status["phase"])
            phase_lower = phase.lower()
            observed_started = previous["observedStarted"] or phase_lower != "warmup"
            ended = phase_lower == "finished" or (phase_lower == "warmup" and observed_started)
            reason = "full_time" if phase_lower == "finished" else "stopped" if ended else None
            ended_at = int(time.time()) if ended else None
            cap_closed = False
            with database() as connection:
                connection.execute("BEGIN IMMEDIATE")
                row = connection.execute(
                    "SELECT ended_at FROM match_starts WHERE signature = ?", (signature,)
                ).fetchone()
                if not row:
                    connection.commit()
                    self._json(HTTPStatus.OK, {"prepared": False, **status})
                    return
                if ended and row["ended_at"] is None:
                    connection.execute(
                        "UPDATE match_starts SET last_phase = ?, last_period = ?, last_periods = ?, "
                        "last_score_ct = ?, last_score_t = ?, observed_started = ?, ended_at = ?, end_reason = ? "
                        "WHERE signature = ?",
                        (
                            phase, status["period"], status["periods"], status["scoreCt"], status["scoreT"],
                            int(observed_started), ended_at, reason, signature,
                        ),
                    )
                    connection.execute(
                        "UPDATE caps SET score_ct = ?, score_t = ?, ended_at = ?, end_reason = ? WHERE signature = ?",
                        (status["scoreCt"], status["scoreT"], ended_at, reason, signature),
                    )
                    connection.execute("DELETE FROM queue_members")
                    connection.execute("DELETE FROM duration_vote_sessions")
                    connection.execute("DELETE FROM cap_chat_messages")
                    connection.execute("UPDATE settings SET value = '0' WHERE key = 'cap_active'")
                    connection.execute("UPDATE settings SET value = '' WHERE key = 'cap_creator'")
                    connection.execute("UPDATE settings SET value = 'standard' WHERE key = 'cap_type'")
                    connection.execute("UPDATE settings SET value = '0' WHERE key = 'test_mode'")
                    cap_closed = True
                else:
                    connection.execute(
                        "UPDATE match_starts SET last_phase = ?, last_period = ?, last_periods = ?, "
                        "last_score_ct = ?, last_score_t = ?, observed_started = ? WHERE signature = ?",
                        (
                            phase, status["period"], status["periods"], status["scoreCt"], status["scoreT"],
                            int(observed_started), signature,
                        ),
                    )
                connection.commit()
            if cap_closed:
                clear_game_server_cap("cs2")
            self._json(
                HTTPStatus.OK,
                {
                    "prepared": True,
                    **status,
                    "observedStarted": observed_started,
                    "ended": ended,
                    "reason": reason,
                    "capClosed": cap_closed,
                    "endedAt": ended_at if ended else previous["endedAt"],
                },
            )
            return
        if url.path == "/api/cap/chat":
            user = self._session_user()
            if not user:
                self._json(HTTPStatus.UNAUTHORIZED, {"error": "authentication_required"})
                return
            with database() as connection:
                prune_inactive_queue(connection)
                member = connection.execute(
                    "SELECT 1 FROM queue_members WHERE steamid = ?", (user["steamid"],)
                ).fetchone()
                creator = cap_creator(connection)
                if not cap_is_active(connection) or not (member or creator and creator["id"] == user["steamid"]):
                    self._json(HTTPStatus.FORBIDDEN, {"error": "chat_unavailable"})
                    return
                rows = connection.execute(
                    "SELECT id, steamid, display_name, message, created_at FROM cap_chat_messages "
                    "ORDER BY id DESC LIMIT 80"
                ).fetchall()
            messages = [
                {
                    "id": row["id"], "steamid": row["steamid"], "name": row["display_name"],
                    "message": row["message"], "createdAt": row["created_at"],
                }
                for row in reversed(rows)
            ]
            self._json(HTTPStatus.OK, {"messages": messages})
            return
        if url.path == "/api/profile":
            user = self._session_user()
            if not user:
                self._json(HTTPStatus.UNAUTHORIZED, {"error": "authentication_required"})
                return
            country_hint = country_from_request(self) if profile_needs_country(user["steamid"]) else ""
            profile = profile_for_user(user["steamid"], country_hint)
            if not profile:
                self._json(HTTPStatus.NOT_FOUND, {"error": "user_not_found"})
                return
            self._json(HTTPStatus.OK, {"profile": profile})
            return
        if url.path == "/api/community/stats":
            self._json(HTTPStatus.OK, community_statistics())
            return
        if url.path == "/api/admin/users":
            user = self._session_user()
            if not user:
                self._json(HTTPStatus.UNAUTHORIZED, {"error": "authentication_required"})
                return
            if user["role"] != "admin":
                self._json(HTTPStatus.FORBIDDEN, {"error": "admin_required"})
                return
            with database() as connection:
                rows = connection.execute(
                    "SELECT steamid, display_name, role, account_status, last_login FROM users ORDER BY last_login DESC"
                ).fetchall()
            users = [
                {
                    "steamid": row["steamid"],
                    "name": row["display_name"],
                    "role": "admin" if row["steamid"] == OWNER_STEAM_ID else row["role"],
                    "status": "active" if row["steamid"] == OWNER_STEAM_ID else row["account_status"],
                    "owner": row["steamid"] == OWNER_STEAM_ID,
                }
                for row in rows
            ]
            self._json(HTTPStatus.OK, {"users": users})
            return
        self._json(HTTPStatus.NOT_FOUND, {"error": "not_found"})

    def do_POST(self) -> None:
        url = urllib.parse.urlsplit(self.path)
        if url.path == "/auth/logout":
            if not self._require_post_user():
                return
            clear = f"{SESSION_COOKIE}=; Max-Age=0; Path=/; Secure; HttpOnly; SameSite=Lax"
            self._redirect("/", [clear])
            return
        if url.path == "/api/cap/chat":
            user = self._require_post_user()
            if not user:
                return
            payload = self._request_json()
            message = clean_profile_text(payload.get("message") if payload else None, 320)
            if not message:
                self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_message"})
                return
            now = int(time.time())
            with database() as connection:
                connection.execute("BEGIN IMMEDIATE")
                prune_inactive_queue(connection, now)
                member = connection.execute(
                    "SELECT 1 FROM queue_members WHERE steamid = ?", (user["steamid"],)
                ).fetchone()
                creator = cap_creator(connection)
                if not cap_is_active(connection) or not (member or creator and creator["id"] == user["steamid"]):
                    self._json(HTTPStatus.FORBIDDEN, {"error": "chat_unavailable"})
                    return
                recent = connection.execute(
                    "SELECT created_at FROM cap_chat_messages WHERE steamid = ? ORDER BY id DESC LIMIT 1",
                    (user["steamid"],),
                ).fetchone()
                if recent and now - recent["created_at"] < 2:
                    self._json(HTTPStatus.TOO_MANY_REQUESTS, {"error": "chat_rate_limited"})
                    return
                cursor = connection.execute(
                    "INSERT INTO cap_chat_messages (steamid, display_name, message, created_at) VALUES (?, ?, ?, ?)",
                    (user["steamid"], user["name"], message, now),
                )
                connection.commit()
            self._json(HTTPStatus.CREATED, {"message": {
                "id": cursor.lastrowid, "steamid": user["steamid"], "name": user["name"],
                "message": message, "createdAt": now,
            }})
            return
        if url.path == "/api/queue/join":
            user = self._require_post_user()
            if not user:
                return
            payload = self._request_json()
            roles = valid_role_selection(payload.get("roles") if payload else None)
            main_role = valid_main_role(payload.get("mainRole") if payload else None, roles or [])
            if not roles or not main_role:
                self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_roles"})
                return
            country_hint = country_from_request(self) if profile_needs_country(user["steamid"]) else ""
            with database() as connection:
                connection.execute("BEGIN IMMEDIATE")
                ensure_profile(connection, user["steamid"], country_hint)
                prune_inactive_queue(connection)
                opened_new_cap = not cap_is_active(connection)
                previous_game = cap_details(connection)["game"]
                if opened_new_cap:
                    connection.execute("DELETE FROM cap_chat_messages")
                    connection.execute("UPDATE settings SET value = '1' WHERE key = 'cap_active'")
                    connection.execute("UPDATE settings SET value = ? WHERE key = 'cap_creator'", (user["steamid"],))
                    connection.execute("UPDATE settings SET value = 'standard' WHERE key = 'cap_type'")
                    connection.execute("UPDATE settings SET value = ? WHERE key = 'cap_nonce'", (secrets.token_hex(8),))
                members = queue_members(connection)
                existing = next((member for member in members if member["id"] == user["steamid"]), None)
                if not existing and len(members) >= 12:
                    self._json(HTTPStatus.CONFLICT, {"error": "queue_full"})
                    return
                now = int(time.time())
                joined_at = existing["joinedAt"] if existing else now
                activity_due_at = now + QUEUE_ACTIVITY_INTERVAL
                connection.execute(
                    """INSERT INTO queue_members (steamid, display_name, roles_json, main_role, joined_at, activity_due_at)
                       VALUES (?, ?, ?, ?, ?, ?)
                       ON CONFLICT(steamid) DO UPDATE SET
                         display_name = excluded.display_name,
                         roles_json = excluded.roles_json,
                         main_role = excluded.main_role,
                         activity_due_at = excluded.activity_due_at""",
                    (user["steamid"], user["name"], json.dumps(roles), main_role, joined_at, activity_due_at),
                )
                connection.execute(
                    """INSERT INTO player_preferences (steamid, roles_json, main_role, updated_at)
                       VALUES (?, ?, ?, ?)
                       ON CONFLICT(steamid) DO UPDATE SET
                         roles_json = excluded.roles_json,
                         main_role = excluded.main_role,
                         updated_at = excluded.updated_at""",
                    (user["steamid"], json.dumps(roles), main_role, int(time.time())),
                )
                reset_match_flow(connection)
                connection.commit()
                members = queue_members(connection)
            if opened_new_cap:
                clear_game_server_cap(previous_game)
            self._json(HTTPStatus.OK, {"members": members})
            return
        if url.path == "/api/cap/create":
            user = self._require_post_user()
            if not user:
                return
            payload = self._request_json()
            mode = str(payload.get("mode", "")) if payload else ""
            if mode not in ("standard", "custom"):
                self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_cap_mode"})
                return
            game = str(payload.get("game", "")) if payload else ""
            map_name = str(payload.get("map", "")) if payload else ""
            if game not in SUPPORTED_CAP_GAMES or map_name not in SUPPORTED_CAP_GAMES[game]["maps"]:
                self._json(HTTPStatus.BAD_REQUEST, {"error": "unsupported_game_or_map"})
                return
            name = clean_cap_name(payload.get("name", "")) if payload else "Soccer Mod"
            with database() as connection:
                connection.execute("BEGIN IMMEDIATE")
                if cap_is_active(connection):
                    connection.commit()
                    self._json(HTTPStatus.CONFLICT, {"error": "cap_already_open"})
                    return
                previous_game = cap_details(connection)["game"]
                connection.execute("UPDATE settings SET value = '1' WHERE key = 'cap_active'")
                connection.execute("UPDATE settings SET value = '0' WHERE key = 'test_mode'")
                connection.execute("UPDATE settings SET value = ? WHERE key = 'cap_creator'", (user["steamid"],))
                connection.execute("UPDATE settings SET value = ? WHERE key = 'cap_type'", (mode,))
                connection.execute("UPDATE settings SET value = ? WHERE key = 'cap_name'", (name,))
                connection.execute("UPDATE settings SET value = ? WHERE key = 'cap_game'", (game,))
                connection.execute("UPDATE settings SET value = ? WHERE key = 'cap_map'", (map_name,))
                nonce = secrets.token_hex(8)
                connection.execute("UPDATE settings SET value = ? WHERE key = 'cap_nonce'", (nonce,))
                reset_match_flow(connection)
                connection.execute("DELETE FROM cap_chat_messages")
                connection.commit()
            clear_game_server_cap(previous_game)
            self._json(
                HTTPStatus.OK,
                {
                    "capActive": True, "capMode": mode, "capName": name,
                    "game": game, "map": map_name, "capNonce": nonce,
                },
            )
            return
        if url.path == "/api/cap/dismiss":
            user = self._require_post_user()
            if not user:
                return
            with database() as connection:
                connection.execute("BEGIN IMMEDIATE")
                creator = cap_creator(connection)
                if not cap_is_active(connection):
                    connection.commit()
                    self._json(HTTPStatus.NOT_FOUND, {"error": "cap_not_open"})
                    return
                if not creator or creator["id"] != user["steamid"]:
                    connection.commit()
                    self._json(HTTPStatus.FORBIDDEN, {"error": "cap_owner_required"})
                    return
                game = cap_details(connection)["game"]
                removed = connection.execute("SELECT COUNT(*) FROM queue_members").fetchone()[0]
                connection.execute("DELETE FROM queue_members")
                reset_match_flow(connection)
                connection.execute("DELETE FROM cap_chat_messages")
                connection.execute("UPDATE settings SET value = '0' WHERE key = 'cap_active'")
                connection.execute("UPDATE settings SET value = '0' WHERE key = 'test_mode'")
                connection.execute("UPDATE settings SET value = '' WHERE key = 'cap_creator'")
                connection.execute("UPDATE settings SET value = 'standard' WHERE key = 'cap_type'")
                connection.commit()
            server_stopped = stop_game_server_match(game)
            server_cleared = clear_game_server_cap(game)
            self._json(
                HTTPStatus.OK,
                {
                    "dismissed": True,
                    "removed": removed,
                    "serverStopped": server_stopped,
                    "serverCleared": server_cleared,
                },
            )
            return
        if url.path == "/api/profile":
            user = self._require_post_user()
            if not user:
                return
            payload = self._request_json()
            country = clean_profile_text(payload.get("country") if payload else None, 40)
            bio = clean_profile_text(payload.get("bio") if payload else None, 160)
            favorite_game = str(payload.get("favoriteGame", "")) if payload else ""
            availability = str(payload.get("availability", "")) if payload else ""
            if country is None or bio is None or favorite_game not in ("css", "cs2") or availability not in (
                "flexible",
                "weekday_evenings",
                "weekends",
                "late_night",
            ):
                self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_profile"})
                return
            preferences = payload.get("preferences") if payload else None
            roles = None
            main_role = None
            if preferences is not None:
                if not isinstance(preferences, dict):
                    self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_preferences"})
                    return
                raw_roles = preferences.get("roles", [])
                raw_main_role = preferences.get("mainRole")
                if raw_roles == [] and not raw_main_role:
                    roles = []
                else:
                    roles = valid_role_selection(raw_roles)
                    main_role = valid_main_role(raw_main_role, roles or [])
                    if not roles or not main_role:
                        self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_preferences"})
                        return
            with database() as connection:
                connection.execute(
                    """INSERT INTO user_profiles (steamid, country, bio, favorite_game, availability, updated_at)
                       VALUES (?, ?, ?, ?, ?, ?)
                       ON CONFLICT(steamid) DO UPDATE SET
                         country = excluded.country,
                         bio = excluded.bio,
                         favorite_game = excluded.favorite_game,
                         availability = excluded.availability,
                         updated_at = excluded.updated_at""",
                    (user["steamid"], country, bio, favorite_game, availability, int(time.time())),
                )
                if roles == []:
                    connection.execute("DELETE FROM player_preferences WHERE steamid = ?", (user["steamid"],))
                elif roles:
                    connection.execute(
                        """INSERT INTO player_preferences (steamid, roles_json, main_role, updated_at)
                           VALUES (?, ?, ?, ?)
                           ON CONFLICT(steamid) DO UPDATE SET roles_json = excluded.roles_json,
                             main_role = excluded.main_role, updated_at = excluded.updated_at""",
                        (user["steamid"], json.dumps(roles), main_role, int(time.time())),
                    )
            self._json(HTTPStatus.OK, {"profile": profile_for_user(user["steamid"])})
            return
        if url.path == "/api/queue/activity":
            user = self._require_post_user()
            if not user:
                return
            now = int(time.time())
            with database() as connection:
                connection.execute("BEGIN IMMEDIATE")
                expired = prune_inactive_queue(connection, now)
                if user["steamid"] in expired:
                    connection.commit()
                    self._json(HTTPStatus.GONE, {"error": "activity_expired", "members": queue_members(connection)})
                    return
                result = connection.execute(
                    "UPDATE queue_members SET activity_due_at = ? WHERE steamid = ?",
                    (now + QUEUE_ACTIVITY_INTERVAL, user["steamid"]),
                )
                if result.rowcount != 1:
                    connection.commit()
                    self._json(HTTPStatus.NOT_FOUND, {"error": "not_in_queue", "members": queue_members(connection)})
                    return
                connection.commit()
                members = queue_members(connection)
            self._json(
                HTTPStatus.OK,
                {
                    "members": members,
                    "serverTime": now,
                    "activityIntervalSeconds": QUEUE_ACTIVITY_INTERVAL,
                    "activityGraceSeconds": QUEUE_ACTIVITY_GRACE,
                },
            )
            return
        if url.path == "/api/match/duration-vote/start":
            user = self._require_post_user()
            if not user:
                return
            payload = self._request_json()
            game = str(payload.get("game", "")) if payload else ""
            if game not in SUPPORTED_CAP_GAMES:
                self._json(HTTPStatus.BAD_REQUEST, {"error": "unsupported_game"})
                return
            requested_map = str(payload.get("map", "")).strip()[:64] if payload else ""
            now = int(time.time())
            with database() as connection:
                connection.execute("BEGIN IMMEDIATE")
                if cap_type(connection) != "standard":
                    connection.commit()
                    self._json(HTTPStatus.CONFLICT, {"error": "custom_cap"})
                    return
                prune_inactive_queue(connection, now)
                ensure_cap_nonce(connection)
                details = cap_details(connection)
                if game != details["game"] or requested_map != details["map"]:
                    connection.commit()
                    self._json(HTTPStatus.CONFLICT, {"error": "cap_game_mismatch"})
                    return
                members = queue_members(connection)
                is_member = any(member["id"] == user["steamid"] for member in members)
                test_enabled = test_mode_enabled(connection)
                test_ready = test_enabled and user["role"] == "admin" and len(members) >= 1
                if (len(members) != 12 and not test_ready) or (not is_member and not test_ready):
                    self._json(HTTPStatus.CONFLICT, {"error": "queue_not_ready"})
                    return
                if not valid_match_assignments(payload.get("assignments"), members):
                    self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_lineup"})
                    return
                signature = cap_flow_signature(details, members, test_enabled)
                connection.execute(
                    """INSERT OR IGNORE INTO duration_vote_sessions
                       (signature, game, map_name, started_at, deadline, resolved_half_seconds)
                       VALUES (?, ?, ?, ?, ?, NULL)""",
                    (signature, game, details["map"], now, now + DURATION_VOTE_SECONDS),
                )
                vote = duration_vote_payload(connection, signature, user["steamid"], now)
                connection.commit()
            self._json(HTTPStatus.OK, {"vote": vote})
            return
        if url.path == "/api/match/duration-vote":
            user = self._require_post_user()
            if not user:
                return
            payload = self._request_json()
            signature = str(payload.get("signature", "")) if payload else ""
            half_seconds = payload.get("halfSeconds") if payload else None
            if not re.fullmatch(r"[0-9a-f]{64}", signature):
                self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_vote_signature"})
                return
            if isinstance(half_seconds, bool) or half_seconds not in ALLOWED_HALF_SECONDS:
                self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_half_length"})
                return
            now = int(time.time())
            with database() as connection:
                connection.execute("BEGIN IMMEDIATE")
                prune_inactive_queue(connection, now)
                details = cap_details(connection)
                members = queue_members(connection)
                test_enabled = test_mode_enabled(connection)
                is_member = any(member["id"] == user["steamid"] for member in members)
                test_ready = test_enabled and user["role"] == "admin" and len(members) >= 1
                expected = cap_flow_signature(details, members, test_enabled)
                session = connection.execute(
                    "SELECT deadline, resolved_half_seconds FROM duration_vote_sessions WHERE signature = ?",
                    (signature,),
                ).fetchone()
                if signature != expected or not session or (not is_member and not test_ready):
                    connection.commit()
                    self._json(HTTPStatus.CONFLICT, {"error": "duration_vote_unavailable"})
                    return
                if session["resolved_half_seconds"] is None and now < int(session["deadline"]):
                    connection.execute(
                        """INSERT INTO duration_votes (signature, steamid, half_seconds, voted_at)
                           VALUES (?, ?, ?, ?)
                           ON CONFLICT(signature, steamid) DO UPDATE SET
                             half_seconds = excluded.half_seconds,
                             voted_at = excluded.voted_at""",
                        (signature, user["steamid"], int(half_seconds), now),
                    )
                vote = duration_vote_payload(connection, signature, user["steamid"], now)
                connection.commit()
            self._json(HTTPStatus.OK, {"vote": vote})
            return
        if url.path == "/api/match/prepare":
            user = self._require_post_user()
            if not user:
                return
            payload = self._request_json()
            game = str(payload.get("game", "")) if payload else ""
            if game not in SUPPORTED_CAP_GAMES:
                self._json(HTTPStatus.BAD_REQUEST, {"error": "unsupported_game"})
                return
            requested_map = str(payload.get("map", "")).strip()[:64] if payload else ""
            vote_signature = str(payload.get("voteSignature", "")) if payload else ""
            requested_half_seconds = payload.get("halfSeconds") if payload else None
            if not re.fullmatch(r"[0-9a-f]{64}", vote_signature):
                self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_vote_signature"})
                return
            if isinstance(requested_half_seconds, bool) or requested_half_seconds not in ALLOWED_HALF_SECONDS:
                self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_half_length"})
                return
            now = int(time.time())
            with database() as connection:
                connection.execute("BEGIN IMMEDIATE")
                if cap_type(connection) != "standard":
                    connection.commit()
                    self._json(HTTPStatus.CONFLICT, {"error": "custom_cap"})
                    return
                prune_inactive_queue(connection, now)
                details = cap_details(connection)
                if game != details["game"] or requested_map != details["map"]:
                    connection.commit()
                    self._json(HTTPStatus.CONFLICT, {"error": "cap_game_mismatch"})
                    return
                map_name = details["map"]
                members = queue_members(connection)
                is_member = any(member["id"] == user["steamid"] for member in members)
                test_enabled = test_mode_enabled(connection)
                test_ready = test_enabled and user["role"] == "admin" and len(members) >= 1
                if (len(members) != 12 and not test_ready) or (not is_member and not test_ready):
                    self._json(HTTPStatus.CONFLICT, {"error": "queue_not_ready"})
                    return
                verified_lineup = valid_match_assignments(payload.get("assignments"), members)
                if not verified_lineup:
                    self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_lineup"})
                    return
                signature = cap_flow_signature(details, members, test_enabled)
                if vote_signature != signature:
                    connection.commit()
                    self._json(HTTPStatus.CONFLICT, {"error": "duration_vote_mismatch"})
                    return
                resolved_half_seconds = resolve_duration_vote(connection, signature, now)
                if resolved_half_seconds is None:
                    connection.commit()
                    self._json(HTTPStatus.CONFLICT, {"error": "duration_vote_pending"})
                    return
                if resolved_half_seconds != requested_half_seconds:
                    connection.commit()
                    self._json(HTTPStatus.CONFLICT, {"error": "duration_vote_result_changed"})
                    return
                mode = "test" if test_enabled else "full"
                previous = connection.execute(
                    "SELECT started_at, prepared_at FROM match_starts WHERE signature = ?", (signature,)
                ).fetchone()
                if previous and now - previous["started_at"] < 300:
                    connection.commit()
                    if previous["prepared_at"] is None:
                        self._json(
                            HTTPStatus.ACCEPTED,
                            {"prepared": False, "pending": True, "game": game, "halfSeconds": resolved_half_seconds},
                        )
                        return
                    if verified_lineup and mode == "full":
                        record_cap(signature, game, map_name, int(previous["started_at"]), verified_lineup)
                    self._json(
                        HTTPStatus.OK,
                        {"prepared": True, "reused": True, "game": game, "halfSeconds": resolved_half_seconds},
                    )
                    return
                connection.execute(
                    """INSERT INTO match_starts (signature, requested_by, started_at, prepared_at)
                       VALUES (?, ?, ?, NULL)
                       ON CONFLICT(signature) DO UPDATE SET
                          requested_by = excluded.requested_by,
                          started_at = excluded.started_at,
                          prepared_at = NULL""",
                    (signature, user["steamid"], now),
                )
                connection.commit()
            if not prepare_game_server(game, verified_lineup, resolved_half_seconds):
                with database() as connection:
                    connection.execute("DELETE FROM match_starts WHERE signature = ?", (signature,))
                self._json(HTTPStatus.BAD_GATEWAY, {"error": "server_prepare_failed"})
                return
            with database() as connection:
                connection.execute(
                    "UPDATE match_starts SET prepared_at = ? WHERE signature = ?",
                    (int(time.time()), signature),
                )
            if verified_lineup and mode == "full":
                record_cap(signature, game, map_name, now, verified_lineup)
            self._json(
                HTTPStatus.OK,
                {"prepared": True, "reused": False, "game": game, "halfSeconds": resolved_half_seconds},
            )
            return
        if url.path == "/api/queue/leave":
            user = self._require_post_user()
            if not user:
                return
            with database() as connection:
                game = cap_details(connection)["game"]
                connection.execute("DELETE FROM queue_members WHERE steamid = ?", (user["steamid"],))
                reset_match_flow(connection)
                members = queue_members(connection)
                cap_closed = not members
                if cap_closed:
                    connection.execute("UPDATE settings SET value = '0' WHERE key = 'cap_active'")
                    connection.execute("UPDATE settings SET value = '0' WHERE key = 'test_mode'")
                    connection.execute("UPDATE settings SET value = '' WHERE key = 'cap_creator'")
                    connection.execute("DELETE FROM cap_chat_messages")
            server_cleared = clear_game_server_cap(game) if cap_closed else False
            self._json(
                HTTPStatus.OK,
                {"members": members, "capClosed": cap_closed, "serverCleared": server_cleared},
            )
            return
        if url.path == "/api/admin/queue/empty":
            if not self._require_post_user(admin=True):
                return
            with database() as connection:
                game = cap_details(connection)["game"]
                removed = connection.execute("SELECT COUNT(*) FROM queue_members").fetchone()[0]
                connection.execute("DELETE FROM queue_members")
                reset_match_flow(connection)
                connection.execute("DELETE FROM cap_chat_messages")
                connection.execute("UPDATE settings SET value = '0' WHERE key = 'cap_active'")
                connection.execute("UPDATE settings SET value = '' WHERE key = 'cap_creator'")
            server_cleared = clear_game_server_cap(game)
            self._json(HTTPStatus.OK, {"members": [], "removed": removed, "serverCleared": server_cleared})
            return
        if url.path == "/api/admin/queue/remove":
            admin_user = self._require_post_user(admin=True)
            if not admin_user:
                return
            payload = self._request_json()
            steamid = str(payload.get("steamid", "")) if payload else ""
            if not re.fullmatch(r"\d{17}", steamid):
                self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_steamid"})
                return
            with database() as connection:
                game = cap_details(connection)["game"]
                row = connection.execute(
                    "SELECT display_name FROM queue_members WHERE steamid = ?", (steamid,)
                ).fetchone()
                if not row:
                    self._json(HTTPStatus.NOT_FOUND, {"error": "queue_member_not_found"})
                    return
                connection.execute("DELETE FROM queue_members WHERE steamid = ?", (steamid,))
                reset_match_flow(connection)
                members = queue_members(connection)
                if not members:
                    connection.execute("UPDATE settings SET value = '0' WHERE key = 'cap_active'")
                    connection.execute("UPDATE settings SET value = '' WHERE key = 'cap_creator'")
                    connection.execute("DELETE FROM cap_chat_messages")
            server_cleared = clear_game_server_cap(game) if not members else False
            self._json(
                HTTPStatus.OK,
                {
                    "members": members,
                    "removed": {"id": steamid, "name": row["display_name"]},
                    "serverCleared": server_cleared,
                },
            )
            return
        if url.path == "/api/admin/test-mode":
            if not self._require_post_user(admin=True):
                return
            payload = self._request_json()
            enabled = payload.get("enabled") if payload else None
            if not isinstance(enabled, bool):
                self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_test_mode"})
                return
            with database() as connection:
                connection.execute("BEGIN IMMEDIATE")
                removed = 0
                was_enabled = test_mode_enabled(connection)
                game = cap_details(connection)["game"]
                if was_enabled and not enabled:
                    removed = connection.execute("SELECT COUNT(*) FROM queue_members").fetchone()[0]
                    connection.execute("DELETE FROM queue_members")
                    connection.execute("UPDATE settings SET value = '0' WHERE key = 'cap_active'")
                    connection.execute("UPDATE settings SET value = '' WHERE key = 'cap_creator'")
                    connection.execute("DELETE FROM cap_chat_messages")
                connection.execute(
                    "UPDATE settings SET value = ? WHERE key = 'test_mode'",
                    ("1" if enabled else "0",),
                )
                reset_match_flow(connection)
                members = queue_members(connection)
                connection.commit()
            server_cleared = clear_game_server_cap(game) if was_enabled and not enabled else False
            self._json(
                HTTPStatus.OK,
                {"testMode": enabled, "members": members, "removed": removed, "serverCleared": server_cleared},
            )
            return
        if url.path == "/api/admin/users/role":
            if not self._require_post_user(admin=True):
                return
            payload = self._request_json()
            steamid = str(payload.get("steamid", "")) if payload else ""
            role = str(payload.get("role", "")) if payload else ""
            if not re.fullmatch(r"\d{17}", steamid) or role not in ("user", "admin"):
                self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_role_change"})
                return
            if steamid == OWNER_STEAM_ID and role != "admin":
                self._json(HTTPStatus.CONFLICT, {"error": "owner_must_remain_admin"})
                return
            with database() as connection:
                result = connection.execute("UPDATE users SET role = ? WHERE steamid = ?", (role, steamid))
            if result.rowcount != 1:
                self._json(HTTPStatus.NOT_FOUND, {"error": "user_not_found"})
                return
            self._json(HTTPStatus.OK, {"steamid": steamid, "role": role})
            return
        if url.path == "/api/admin/users/status":
            admin_user = self._require_post_user(admin=True)
            if not admin_user:
                return
            payload = self._request_json()
            steamid = str(payload.get("steamid", "")) if payload else ""
            status = str(payload.get("status", "")) if payload else ""
            if not re.fullmatch(r"\d{17}", steamid) or status not in ("active", "suspended", "banned"):
                self._json(HTTPStatus.BAD_REQUEST, {"error": "invalid_account_status"})
                return
            if steamid == OWNER_STEAM_ID or steamid == admin_user["steamid"]:
                self._json(HTTPStatus.CONFLICT, {"error": "protected_account"})
                return
            with database() as connection:
                connection.execute("BEGIN IMMEDIATE")
                result = connection.execute("UPDATE users SET account_status = ? WHERE steamid = ?", (status, steamid))
                if result.rowcount != 1:
                    self._json(HTTPStatus.NOT_FOUND, {"error": "user_not_found"})
                    return
                if status != "active":
                    connection.execute("DELETE FROM queue_members WHERE steamid = ?", (steamid,))
                    connection.execute("DELETE FROM cap_chat_messages WHERE steamid = ?", (steamid,))
                    reset_match_flow(connection)
                    if connection.execute("SELECT COUNT(*) FROM queue_members").fetchone()[0] == 0:
                        connection.execute("UPDATE settings SET value = '0' WHERE key = 'cap_active'")
                        connection.execute("UPDATE settings SET value = '' WHERE key = 'cap_creator'")
                        connection.execute("DELETE FROM cap_chat_messages")
                connection.commit()
            self._json(HTTPStatus.OK, {"steamid": steamid, "status": status})
            return
        self._json(HTTPStatus.NOT_FOUND, {"error": "not_found"})

    def _start_steam_login(self, raw_query: str) -> None:
        query = urllib.parse.parse_qs(raw_query)
        return_to = safe_return_to(query.get("return_to", ["/"])[0])
        nonce = secrets.token_urlsafe(24)
        state = sign_payload({"nonce": nonce, "return_to": return_to, "exp": int(time.time()) + 600})
        callback = f"{ORIGIN}/auth/steam/callback?state={urllib.parse.quote(nonce)}"
        params = {
            "openid.ns": OPENID_NS,
            "openid.mode": "checkid_setup",
            "openid.return_to": callback,
            "openid.realm": f"{ORIGIN}/",
            "openid.identity": IDENTIFIER_SELECT,
            "openid.claimed_id": IDENTIFIER_SELECT,
        }
        destination = f"{STEAM_OPENID}?{urllib.parse.urlencode(params)}"
        cookie = f"{STATE_COOKIE}={state}; Max-Age=600; Path=/auth/steam/callback; Secure; HttpOnly; SameSite=Lax"
        self._redirect(destination, [cookie])

    def _finish_steam_login(self, raw_query: str) -> None:
        query = urllib.parse.parse_qs(raw_query, keep_blank_values=True)
        state = read_payload(self._cookie_value(STATE_COOKIE))
        nonce = query.get("state", [""])[0]
        clear_state = f"{STATE_COOKIE}=; Max-Age=0; Path=/auth/steam/callback; Secure; HttpOnly; SameSite=Lax"
        if not state or not hmac.compare_digest(str(state.get("nonce", "")), nonce):
            self._redirect("/?auth=failed", [clear_state])
            return
        if query.get("openid.mode", [""])[0] == "cancel":
            self._redirect("/?auth=cancelled", [clear_state])
            return
        callback = f"{ORIGIN}/auth/steam/callback?state={urllib.parse.quote(nonce)}"
        claimed_id = query.get("openid.claimed_id", [""])[0]
        identity = query.get("openid.identity", [""])[0]
        endpoint = query.get("openid.op_endpoint", [""])[0].rstrip("/")
        match = STEAM_ID_PATTERN.fullmatch(claimed_id)
        valid_shape = all(
            (
                query.get("openid.ns", [""])[0] == OPENID_NS,
                query.get("openid.mode", [""])[0] == "id_res",
                query.get("openid.return_to", [""])[0] == callback,
                endpoint == STEAM_OPENID.rstrip("/"),
                identity == claimed_id,
                match is not None,
            )
        )
        if not valid_shape or not self._verify_with_steam(query):
            self._redirect("/?auth=failed", [clear_state])
            return
        steamid = match.group(1)
        name = fetch_steam_name(steamid)
        upsert_user(steamid, name)
        account = user_record(steamid)
        if account and account["status"] != "active":
            self._redirect("/?auth=restricted", [clear_state])
            return
        session = sign_payload({"steamid": steamid, "iat": int(time.time()), "exp": int(time.time()) + 604800})
        session_cookie = f"{SESSION_COOKIE}={session}; Max-Age=604800; Path=/; Secure; HttpOnly; SameSite=Lax"
        self._redirect(safe_return_to(str(state.get("return_to", "/"))), [clear_state, session_cookie])

    def _verify_with_steam(self, query: dict[str, list[str]]) -> bool:
        values: list[tuple[str, str]] = []
        for key, items in query.items():
            if key.startswith("openid."):
                for item in items:
                    values.append((key, item))
        values = [(key, "check_authentication" if key == "openid.mode" else value) for key, value in values]
        try:
            request = urllib.request.Request(
                STEAM_OPENID,
                data=urllib.parse.urlencode(values).encode("utf-8"),
                headers={"Content-Type": "application/x-www-form-urlencoded", "User-Agent": "KICKOFF-SoccerMod/1.1"},
                method="POST",
            )
            with urllib.request.urlopen(request, timeout=10) as response:
                result = response.read(4096).decode("utf-8", "replace")
        except (urllib.error.URLError, TimeoutError, ValueError):
            return False
        return any(line.strip() == "is_valid:true" for line in result.splitlines())


if __name__ == "__main__":
    initialize_database()
    server = ThreadingHTTPServer(("0.0.0.0", PORT), KickoffHandler)
    server.daemon_threads = True
    print(f"KICKOFF app service listening on {PORT}", flush=True)
    server.serve_forever()

# KICKOFF — self-hosted cap website (optional)

This is the source for the community's "KICKOFF" website: a Steam-login
web page where players join a queue, get auto-drafted into two teams by
position, and are auto-connected to the server once a cap is ready. It
drives the plugin's `css_sm2webcap_*` bridge commands over RCON.

**You do not need this to play SoccerMod.** The plugin has its own
in-game cap menu (`!cap`, or `!menu → Cap`) that needs nothing extra to
run. Only set this up if you want the website experience for your own
community and have a VPS to host it on.

## What it is

- `frontend/` — static HTML/CSS/JS, served by any web server.
- `auth/` — Python (stdlib only) HTTP service: Steam OpenID login, session
  cookies, the cap queue/draft state, a SQLite database.
- `rcon/` — Python (stdlib only) HTTP service that turns a small,
  allowlisted set of HTTP calls from `auth` into RCON commands against
  your game server (`css_sm2webcap_begin/reference/assign/commit/clear`).
  It is deliberately the only thing that ever touches your RCON password.

```
Browser → Caddy (HTTPS, your domain)
            ├─ /auth/*, /api/* → kickoff-auth  (Steam login + cap logic)
            └─ everything else → static frontend files
kickoff-auth → kickoff-rcon (internal only) → your CS2 server's RCON
```

## Requirements

- A VPS (or any host) with Docker and Docker Compose.
- A domain name pointing at that host (or a free wildcard service like
  `sslip.io`), for HTTPS via Caddy's automatic certificates.
- RCON access to your own CS2 SoccerMod server from that host.
- A Steam Web API is **not** required — login uses Steam OpenID directly.

## Setup

1. Copy `.env.example` to `.env` and fill in every value. Generate the two
   secrets with:
   ```bash
   python3 -c "import secrets; print(secrets.token_urlsafe(32))"
   ```
   `SESSION_SECRET` and `RCON_HELPER_SECRET` must each be at least 32
   characters. Set `RCON_HELPER_SECRET` once; Compose passes the same
   value to both services to authenticate `auth` → `rcon` calls. Only the
   RCON helper receives the game servers' RCON passwords.
2. Copy `Caddyfile.example` to `Caddyfile` and replace the domain with
   your own.
3. Edit `frontend/app.js` (`GAME_OPTIONS`) and `frontend/index.html` (the
   `connectCommand`/`connectServer` fallback near the server room) with
   your real server address and password. These are shown to signed-in
   players so they can join — don't put your admin RCON password here,
   only the game join password.
4. Set `KICKOFF_OWNER_STEAM_ID` in `.env` to your own SteamID64 — this is
   the account that gets owner controls on the site.
5. Start it:
   ```bash
   docker compose up -d --build
   ```
   Caddy issues its own HTTPS certificate on first request to your domain.
6. On your CS2 server, confirm the RCON password in its own config
   matches `CS2_RCON_PASSWORD` in `.env`, and that `kickoff-rcon` can
   reach the server's RCON port (same host: `127.0.0.1`; separate host:
   open that port to this VPS's IP only, never publicly).

## Notes

- `kickoff-rcon` runs with `network_mode: host` and binds only to the
  Docker bridge gateway address (`RCON_HELPER_BIND`, default
  `172.30.20.1`) on the explicitly configured `KICKOFF_SUBNET`
  (`172.30.20.0/24`). Choose another unused subnet and gateway together
  if these overlap an existing network. This is a Linux host-network
  deployment; do not
  change this to bind `0.0.0.0` or publish its port.
- Set `CSS_RCON_PASSWORD`, `CSS_RCON_HOST` and `CSS_RCON_PORT` to enable
  CS:S. When running the helper directly on a host, an unset
  `CSS_RCON_PASSWORD` falls back to reading the RCON password from
  `CSS_RCON_CONFIG`. This is the admin RCON password, not the player join
  password. If you only run CS2, ignore the CS:S environment
  variables and remove the `css` block from `frontend/app.js` /
  `index.html`.
- The plugin-side commands this talks to
  (`css_sm2webcap_begin/reference/assign/commit/clear/evict/status`) are
  server-console/RCON-only by design — they never accept a call from an
  in-game player, only from this bridge.
- Player-facing text (`frontend/app.js` → `COPY`) is in English and
  German; edit or add a locale as needed.
- SQLite is stored in `/data/kickoff.sqlite3` on the named `kickoff-data`
  volume. Fresh volumes inherit the image's writable `nobody` ownership.
- Optional CS:S ranking import still accepts `SOCCERMOD_DATABASE_PATH`.
  Mount that database read-only into the auth container using a Compose
  override, then set this variable to its path inside the container.

## Upgrading an existing website

Before rebuilding or recreating the old auth container, check where its
database is stored. Earlier default deployments wrote to
`/tmp/kickoff-data` inside the container despite mounting `/data`.
For that layout, migrate the stopped database, including any SQLite WAL
files, into the existing named volume:

```bash
(
set -e
docker compose stop kickoff-auth
backup_dir="$(mktemp -d "$HOME/kickoff-backup.XXXXXX")"
docker cp kickoff-auth:/tmp/kickoff-data/. "$backup_dir/"
# Refuse to overwrite a database that is already in the volume.
docker compose run --rm --no-deps --user root kickoff-auth sh -c 'test ! -e /data/kickoff.sqlite3'
docker cp "$backup_dir/." kickoff-auth:/data/
docker compose run --rm --no-deps --user root kickoff-auth chown -R nobody:nobody /data
docker compose up -d --build
)
```

Keep the backup until you have verified existing users and cap history.
For custom storage paths, copy from the actual old path instead. If the
database already lives in `/data`, skip the copy steps; an existing
root-owned volume may still need the ownership command while auth is
stopped. Never use `docker compose down -v` during this migration.

## Tests

From the repository root, run:

```bash
python3 -m unittest discover -s test -p 'test_*.py' -v
```

These test database commit/rollback/cleanup, malformed sessions and redirects,
request validation, password parsing, and fragmented and multi-packet RCON
responses. They do not contact Steam or a live game server.

Source and issue tracker for the plugin itself:
https://github.com/Deidakom/CS2_Soccer_Mod

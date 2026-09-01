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
   characters. `RCON_HELPER_SECRET` must be **identical** in both places
   in `.env` (it authenticates `auth` → `rcon` calls).
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
  `172.18.0.1`) — it is not reachable from outside the host. Do not
  change this to bind `0.0.0.0` or publish its port.
- CS:S support exists in the code (`ALLOWED_GAMES = {"css", "cs2"}`) but
  needs an on-host `server.cfg` path (`CSS_RCON_CONFIG`) to read the
  join password from; if you only run CS2, ignore the CS:S environment
  variables and remove the `css` block from `frontend/app.js` /
  `index.html`.
- The plugin-side commands this talks to
  (`css_sm2webcap_begin/reference/assign/commit/clear/evict/status`) are
  server-console/RCON-only by design — they never accept a call from an
  in-game player, only from this bridge.
- Player-facing text (`frontend/app.js` → `COPY`) is in English and
  German; edit or add a locale as needed.

Source and issue tracker for the plugin itself:
https://github.com/Deidakom/CS2_Soccer_Mod

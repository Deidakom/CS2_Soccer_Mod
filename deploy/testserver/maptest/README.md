# Map test server (second CS2 instance)

A second SoccerMod server on the same VPS, used only to test our own map
`soccer_soccermod_stadium`. Set up 2026-09-25.

| | Main test server | Map test server |
| --- | --- | --- |
| Service | `cs2-soccermod-test` | `cs2-soccermod-maptest` |
| Game port | 27017 | 27018 |
| Client / TV port | 27007 / 27022 | 27008 / 27023 |
| Install | `/home/gameserver/cs2` | `/home/gameserver/cs2-maptest` (overlay) |
| Env file (GSLT, RCON) | `/etc/cs2-soccermod-test.env` | `/etc/cs2-soccermod-maptest.env` |
| Config | `soccermod_test.cfg` | `soccermod_maptest.cfg` |

## How it shares the install

The CS2 install is 70 GB and the disk has about 19 GB free, so the second
server does not get a copy. `/home/gameserver/cs2-maptest` is an overlayfs:

- lower: `/home/gameserver/cs2` (the main install, read-only here)
- upper: `/home/gameserver/cs2-maptest-data/upper` (everything this server
  writes: plugin state such as stats/ELO/bans, logs, Workshop downloads, its
  config, and later its own plugin DLL if one is installed into its path)

Mount unit: `/etc/systemd/system/home-gameserver-cs2\x2dmaptest.mount`
(saved here as `cs2-maptest-overlay.mount`, since Windows file names cannot
contain a backslash). Service: `cs2-soccermod-maptest.service` (this folder).

Consequences:

- A plugin DLL installed into the main install reaches the map test server on
  its next start (it reads the lower layer). Installing a DLL into
  `/home/gameserver/cs2-maptest/...` copies it into the upper layer; from then
  on this server keeps its own DLL until that copy is removed.
- CS2 updates (`update-server.sh`) update the lower install for both. Stop
  both services for an update. Restarts always need the owner's OK.

## Commands

```bash
python3 deploy/testserver/serverctl.py rcon 127.0.0.1 27018 /etc/cs2-soccermod-maptest.env "status"
systemctl status cs2-soccermod-maptest
```

Players connect with `connect 212.87.212.58:27018` (same server password as
the main server). Firewall: ufw allows 27018/tcp+udp and 27023/udp.

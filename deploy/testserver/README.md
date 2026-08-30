# CS2 SoccerMod MVP test server

This deployment is intentionally separate from the existing CS:S server:

- CS2 root: `/home/gameserver/cs2`
- systemd service: `cs2-soccermod-test.service`
- game port: `27017` (keeps the existing CSS main/test ports separate)
- client port: `27007`
- TV port: `27022`
- baseline Workshop map: CSF Football Stadium, item `3361075564`
- internal map name: `soccer_cssl_stadium_v8`
- lab addon retained on disk: `soccermod_phase1`

The first online baseline uses the unmodified CSF Workshop map. It starts on
`de_dust2` so Steamworks can initialize, then `host_workshop_map` downloads and
mounts item `3361075564` and changes to `soccer_cssl_stadium_v8`. The Phase 1
lab addon remains available for isolated ball-script work, but it is not
injected into the third-party Workshop map.

The stock August 2026 CS2 dedicated server remains fixed at 64 Hz with
sub-tick input. Supplying `-tickrate 128` does not change the measured server
loop interval. Do not add that flag unless Valve restores support.

## Server install

Install CS2 app `730` with SteamCMD into `/home/gameserver/cs2`. Do not reuse or
overwrite `/home/gameserver/css`.

Run `preflight.sh` first. After extracting the generated package, run
`install-or-update.sh` as root. Put a fresh app-730 GSLT and a new random RCON
password in `/etc/cs2-soccermod-test.env`, keep that file mode `0600`, then:

```text
systemctl enable --now cs2-soccermod-test.service
journalctl -u cs2-soccermod-test.service -n 200 --no-pager
```

The first proof is direct-IP connection to port `27017`, an A2S response with
`map=soccer_cssl_stadium_v8`, and a two-player check of the CSF map's built-in
left-click ball interaction and goal behavior. This establishes the stable
server/map baseline. Our own match commands, CAP flow, and map-independent ball
controller are the next implementation layer.

Clients should subscribe to CSF Football Stadium Workshop item `3361075564`.
The server also requests that item through Steam Workshop during startup.

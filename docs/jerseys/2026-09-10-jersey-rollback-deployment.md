# Jersey rollback deployment — September 10, 2026

The approved rollback of Workshop item `3797479770` is live on the SoccerMod
test server.

## Release

- Map: `soccer_cssl_stadium_v8` (Workshop item `3361075564`)
- Package: `3797479770_dir.vpk`
- Size: `33,898,029` bytes
- SHA-256: `6170c2eb6ed5cfddd7fd570956aeed7053c8800e7e75f7d3dfce2bd2744a26e6`
- Static state: Home `8`, Away `6`, goalkeeper `1`
- Away GK: white shirt and white trousers, blue `1`/`Away` lettering with white borders

This is the prior white-jeans static state, restored after the blue Away-GK
trousers revision caused some skins to break. The package was downloaded from
Workshop after approval, independently matched against the preserved rollback
VPK, and refreshed through MultiAddonManager.

## Runtime state

The approved Workshop package is mounted for the static kit configuration.

Dynamic jersey names/numbers remain deliberately disabled:

```text
css_sm2jerseydynamic off
```

The dynamic repair/prototype work is paused and was not included in this
deployment.

## Client refresh

Players should fully exit CS2, let Steam update Workshop item `3797479770`,
restart CS2, and reconnect. If the old/new appearance persists, verify the
client VPK at `steamapps/workshop/content/730/3797479770/3797479770_dir.vpk`
against the SHA-256 above.

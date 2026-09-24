# Jersey belt geometry revert — September 9, 2026

The latest visual check showed a jagged strip at the waist of the field-player
models after the geometry-cleanup Workshop revision. The regression was in the
four compiled player model resources, not in the jersey textures.

## Package

The belt-revert candidate keeps the current approved textures and materials,
including:

- Home field `8` and Away field `6` with the Home/Away labels;
- front numbers and Adidas marks;
- purple/black Home GK with white gloves;
- white Away GK with black `1` and black `Away` with a white border; and
- the jersey-name, jersey-number and weapon attachment sockets.

Only these four compiled model resources were restored from the preceding
known-good package:

```text
models/soccermod/kits/kit_home.vmdl_c
models/soccermod/kits/kit_away.vmdl_c
models/soccermod/kits/kit_gkhome.vmdl_c
models/soccermod/kits/kit_gkaway.vmdl_c
```

The candidate VPK is:

```text
artifacts/kits-jersey-belt-revert-upload/3797479770_dir.vpk
```

- Size: `33,898,209` bytes
- SHA-256: `81625939862B5225DA3770E25F4176556FF816583C9205A82EB842A615D8FF47`
- Source2Viewer VPK verification: passed

The corrected current GK Away body texture remains in this package; the model
rollback is intentionally limited to the belt/waist geometry regression.

## Publication and rollout

The VPK was staged on the server as
`/home/gameserver/kits-jersey-belt-revert-upload/3797479770_dir.vpk` and
published as an update to Workshop item `3797479770` with
`workshop-jersey-belt-revert-update.vdf`. After Steam approval, a fresh
authenticated Workshop download matched the target hash above. The live
server cache was then refreshed with:

```text
mm_download_addon 3797479770
host_workshop_map 3361075564
```

The CS2 service remained active and the stadium reloaded with addon `3797479770`
mounted alongside the existing map/menu addons.

Clients must fully exit CS2, let Steam update Workshop item `3797479770`,
restart CS2 and reconnect. The VPK hash should match the value above before
checking the in-game belt.

Static kits remain active and the dynamic jersey renderer remains disabled.

# Jersey installation

This guide installs the approved custom Home, Away, Home GK and Away GK
jerseys for the SoccerMod server.

## What this package contains

Workshop item [`3797479770`](https://steamcommunity.com/sharedfiles/filedetails/?id=3797479770)
contains the four compiled player models, materials and textures:

| Squad | Field player | Goalkeeper |
|---|---|---|
| Home | red kit with fixed `8` | purple/black kit with fixed `1` and white gloves |
| Away | blue kit with fixed `6` | white kit with black `1` and black `Away` label with white border |

The latest belt-revert revision restores the previously approved belt/waist
geometry. The other previously removed gear remains removed from the models;
the belt correction is a model change, not a texture cover-up.

The current verified package is 33,898,209 bytes with SHA-256:

```text
81625939862b5225da3770e25f4176556ff816583c9205a82eb842a615d8ff47
```

## Server prerequisites

The server needs:

1. CounterStrikeSharp and Metamod:Source.
2. Workshop map `soccer_cssl_stadium_v8` (item `3361075564`).
3. Source2ZE MultiAddonManager, so the server mounts the jersey Workshop
   addon and clients download it when they connect.

Add the jersey item to the existing MultiAddonManager configuration. Do not
remove other addon IDs already in use:

```text
// game/csgo/cfg/multiaddonmanager/multiaddonmanager.cfg
mm_extra_addons "<existing-addon-ids>,3797479770"
```

If the value is currently empty, use:

```text
mm_extra_addons "3797479770"
```

Restart the server or reload the map after changing this file. Confirm that
the server log mounts addon `3797479770` and that `mm_print_searchpaths`
contains it.

## Enable the static jerseys

Run these commands from server console/RCON as an administrator. The `css_`
names are the console commands; they can also be sent through the matching
chat command when the server exposes chat commands.

```text
css_sm2kit home models/soccermod/kits/kit_home.vmdl
css_sm2kit away models/soccermod/kits/kit_away.vmdl
css_sm2kit gkhome models/soccermod/kits/kit_gkhome.vmdl
css_sm2kit gkaway models/soccermod/kits/kit_gkaway.vmdl
css_sm2teammodel kits
css_sm2jerseydynamic off
```

The four `css_sm2kit` paths are saved for the next map precache. Change back
to `soccer_cssl_stadium_v8` (or restart the server) after setting them:

```text
changelevel soccer_cssl_stadium_v8
```

After the map loads, check:

```text
css_sm2teammodel
css_sm2jerseydynamic
```

The expected state is `kits` for team models and `requested=off` for dynamic
jersey text. With dynamic jerseys off, the numbers and labels come from the
approved textures: Home `8`, Away `6`, and goalkeeper `1`. No number command
is needed for the static package.

## Client installation and verification

Players should subscribe to the Workshop item and connect after the server
has mounted it. If a player sees missing models or old textures:

1. Exit CS2 completely.
2. Let Steam finish updating Workshop item `3797479770`.
3. Start CS2 again and reconnect.
4. Confirm the connection console contains `Mounting addon '3797479770'`.

On Windows, the downloaded VPK normally appears at:

```text
<SteamLibrary>\steamapps\workshop\content\730\3797479770\3797479770_dir.vpk
```

Hash that file and compare it with the verified SHA-256 above before
debugging model or texture errors. Do not replace a client Workshop cache by
copying the server's staged VPK; that would hide a delivery problem.

## Dynamic jerseys (currently disabled)

The dynamic renderer is installed but deliberately disabled in the current
server settings. Leave it off for the fixed-texture Home `8` / Away `6`
configuration.

If a later package has been verified on both the server and clients and
dynamic text is intentionally wanted, enable it with:

```text
css_sm2jerseydynamic on
css_sm2jerseynumber 7
```

The number command affects outfield dynamic overlays only. Goalkeepers retain
their painted `1`. Dynamic text must stay off when the compatible socket model
package is not present on every client.

## Rollback

To return to stock models without removing the Workshop addon:

```text
css_sm2jerseydynamic off
css_sm2teammodel stock
changelevel soccer_cssl_stadium_v8
```

Keep the previous server DLL, match-settings JSON and Workshop VPK backed up
before replacing them. Removing `3797479770` from MultiAddonManager is a
separate step; reload the map only after all players have left.

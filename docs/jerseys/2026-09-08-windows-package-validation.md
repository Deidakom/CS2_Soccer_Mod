# Windows Route 2 kit package — September 8, 2026

Continues the reviewed `54bb63d` deployment. **The custom-model package is
built, approved, independently downloaded and mounted on the server.
The Windows client now has the identical package and kits are enabled live
for the user's visual test. Rendering/animation acceptance remains pending.**

## Latest: client delivery confirmed, kits enabled

After Steam/CS2 reopened, the local Workshop VPK hash matched
`2e9b024f00a293a1992ba2fe6ecec5ac8075c78354026c2b23d4f5d733455f23`.
The client log at **10:32:25 local time** confirms mounting addon
`3797479770`; the new connection no longer reports missing kit models.
The remaining missing map radar/icon resources are unrelated to the kits.

`css_sm2teammodel kits` was then applied and queried successfully without
another map reload. The crouch lift remains zero. Match settings immediately
before enabling kits were backed up as
`/home/gameserver/cs2/backups/jersey-route2-20260908-082825/match-settings-before-enable.json`.
The delivery gap is resolved for this client; no visual success is claimed
until the user confirms the four kits and animation/first-person checks.

## Earlier checkpoint: client update required (resolved)

After the owner resumed work, anonymous SteamCMD download and the live
server's own `mm_download_addon 3797479770` both received the correct
39,688,300-byte VPK with SHA-256 `2e9b024f00a293a1992ba2fe6ecec5ac8075c78354026c2b23d4f5d733455f23`.
At **08:28:25 UTC** the four custom paths were saved and the stadium was
reloaded, preserving the existing `3796041025` menu addon. Persistent and
runtime extra addons are now `3796041025,3797479770`. Server search paths
confirm the jersey addon is mounted; the live DLL was not changed.

Windows CS2 automatically reconnected but mounted its **old** VPK, still
SHA-256 `814d79c571ece997e0cb099604e1e5db6efdb9487b253387019a23f57d39e4bf`.
The client console at 10:28:31 local time reported all four kit model files
missing. The client Workshop ACF still records manifest `9119487456242131740`.
Do not enable kits until this cache updates. The owner was asked to close
CS2, allow Steam's Workshop update, then reopen and reconnect. No local
Workshop cache was overwritten or deleted to hide the delivery problem.

After reconnect, hash the client's
`E:/SteamLibrary/steamapps/workshop/content/730/3797479770/3797479770_dir.vpk`,
check the connection log for model errors, and only then enable kits for
the user's visual test. `css_sm2ball_crouch_lift` remains saved at zero and
`mp_maxrounds` remains 999999.

Pre-rollout MAM config and ball settings backup:
`/home/gameserver/cs2/backups/jersey-route2-20260908-082825`.
Kit mappings are stored separately in match settings; their pre-rollout
values were verified as the four stock defaults, not backed up in that
directory. Restore those four mappings explicitly for rollback as described
below rather than assuming the ball-settings backup contains them.

## Earlier checkpoint: approval pending (resolved)

The owner re-authenticated SteamCMD. At **2026-09-08 08:04:00 UTC**, Steam
reported a successful content upload for item `3797479770`, manifest
`3003804870588383764`. The owner subsequently confirmed another approval
is required. Do not upload again merely because approval is pending.

Independent anonymous and owner-authenticated download checks still returned
the preceding approved package: **37,401,789 bytes**, SHA-256
`814d79c571ece997e0cb099604e1e5db6efdb9487b253387019a23f57d39e4bf`.
That is not the new four-model package. Downloads were checked in the
separate SteamCMD cache at
`/home/gameserver/Steam/steamapps/workshop/content/730/3797479770`;
no staged file was substituted into that cache to fake verification.

After approval, repeat the independent download and require the new
39,688,300-byte package hash below before proceeding to the rollout steps.
Stock skins and the existing live addon configuration remain unchanged.

## Verified on Windows

- Recovered the existing sources from
  `E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/content/csgo_addons/soccermod_jerseys`.
  No repainting, mesh editing or rig changes were necessary.
- Ran `docs/jerseys/compile-kits.ps1 -Force` with the real Valve resource
  compiler. All eight custom materials and four custom models passed.
- Tightened `pack-kits-vpk.ps1` to include only `models/soccermod/kits/`,
  `materials/soccermod/kits/` and `addoninfo.txt` for Route 2. This excludes
  the compiler-generated stock `materials/default/default_mask` resource
  as well as stale Route 1 overrides and unrelated assets.
- Final `3797479770_dir.vpk`: **35 entries, 39,688,300 bytes** — four models,
  eight materials, 22 textures, and addon metadata.
- SHA-256:
  `2e9b024f00a293a1992ba2fe6ecec5ac8075c78354026c2b23d4f5d733455f23`.
- Source2Viewer 20.0 `--vpk_verify` passed archive and entry checksums.
- Compiled RERL inspection found 44 unique references: 30 supplied by this
  package and 14 present in the installed stock `game/csgo/pak01_dir.vpk`.
  No unresolved runtime references were found. Mesh data is embedded;
  there are no custom external DMX or mesh dependencies to deliver.
- All eight source color PNGs match the committed approved kit PNGs by
  SHA-256. Material remaps and first-person sleeves match Home=a, Away=b,
  GK Home=c, GK Away=d.
- Six PowerShell integration tests passed, including the additional
  stock/unrelated-resource exclusion fixtures.
- The wider Python run passed 17 tests; four existing Linux deployment
  tests could not start because `bash` is not on this Windows runner's PATH
  (`FileNotFoundError`). No deployment script was changed in this work.

These checks prove package integrity and dependency availability, **not**
successful Workshop delivery, animation compatibility or in-game rendering.
Detailed evidence is in `2026-09-08-route2-package-audit.json`.

## Local files and preserved work

Worktree:
`C:/Users/sergi/Documents/ChatGPT/Privat/cs2-soccermod-jersey-delivery`
on branch `codex/jersey-delivery-windows`, based on `54bb63d`.
The original `cs2-soccermod` checkout and its uncommitted edits are intact.

Generated files under that worktree:

- `artifacts/kits-upload/3797479770_dir.vpk`
- `artifacts/kits-upload/3797479770_dir.vpk.manifest.txt`
- `artifacts/jersey-source-backup/soccermod-jerseys-route2-20260908.tar.gz`

The source archive preserves `models/soccermod/kits` and
`materials/soccermod/kits`, including editable model/DMX/material sources
and texture inputs. Size: 65,701,281 bytes. SHA-256:
`c723277f4f835f762b9b7cecb8172d92839b7acbe1b3ebff35dc73e6aefdff97`.
It is local, ignored by Git, and contains third-party-derived assets; do
not mistake it for a committed or publicly licensed source release.

## Publication setup and resolved login blocker

The verified VPK was staged at
`/home/gameserver/kits-route2-upload/3797479770_dir.vpk` on the German
server and its SHA-256 was checked after upload. This is **not** the live
Workshop cache. The previous `/home/gameserver/kits-upload` is untouched.
The updated VDF is staged at `/home/gameserver/kits-route2-update.vdf`.
It updates existing item `3797479770` without changing its title or visibility.

The initial SteamCMD attempt, run as `gameserver`, reported `Cached credentials
not found` and requested a password. That attempt was bounded by a timeout
and did not reach publication. The owner then signed in and the retry
succeeded as recorded above. Never request credentials or Steam Guard codes
in chat. The commands below are reference only, not a request to resubmit.

The owner can re-authenticate interactively from Windows PowerShell:

```powershell
ssh -t root@212.87.212.58 "runuser -u gameserver -- /home/gameserver/steamcmd/steamcmd.sh +login s5dgtgcb +quit"
```

After successful login, publish through the same Unix user:

```sh
runuser -u gameserver -- /home/gameserver/steamcmd/steamcmd.sh \
  +login s5dgtgcb \
  +workshop_build_item /home/gameserver/kits-route2-update.vdf +quit
```

Do not treat exit code alone as success: check SteamCMD's submission result
and any moderation status. Then independently download that published
revision and compare the downloaded VPK's hash to the value above. Do not
replace the server's cached VPK with the staged upload and call that a
download test. Check the same package on a clean client without Workshop
Tools assets mounted.

## Live rollout remains pending

Read-only server checks showed stock skins, `mm_extra_addons=3796041025`,
stadium `soccer_cssl_stadium_v8`, and zero players. No DLL, live configuration,
menu, sprint HUD, gameplay settings, player data, map or match was changed.

Once publication and independent download verification pass:

1. Recheck the current player count and back up the existing model settings
   and MultiAddonManager configuration.
2. Preserve all current extra-addon IDs while adding `3797479770`. Be
   aware MultiAddonManager can reload the map automatically after download.
3. Keep stock mode active while setting all four custom paths to
   `models/soccermod/kits/kit_{home,away,gkhome,gkaway}.vmdl`.
4. Reload the stadium for mount/precache if it has not already reloaded;
   confirm all paths were registered before switching to kits for a test.
5. Have the user verify all four kits from two clients: third-person
   appearance, movement/animation, first-person arms/legs, respawn and
   halftime. Capture client resource errors if any remain.

Rollback starts with `css_sm2teammodel stock`. Before removing the addon,
restore the four kit paths to
`agents/models/tm_leet/tm_leet_variant{a,b,c,d}.vmdl` respectively and reload
the stadium; otherwise the next precache still requests custom content.

## Separate outstanding request

Ball-hit debris suppression is not implemented. The requested hit indicator
was explicitly cancelled and must remain unchanged. Do not silently bundle
physics, surface properties, hit sounds or damage-feedback changes into this
jersey rollout. The accepted gameplay fixes in `54bb63d` stay in place.

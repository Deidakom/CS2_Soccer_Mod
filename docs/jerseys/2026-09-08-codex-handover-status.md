# Handover to Codex — football kits: where we are, what's left

Written by Claude for the user to hand to a Codex session. Covers the
skins/jerseys project end to end. The crouch-lift fix mentioned in the
title is separate and already shipped - see the one paragraph at the
bottom; nothing there needs continuing unless the user wants a different
number after more testing.

## 1. The short version

Four kits are painted, textures verified pixel-exact, and the plugin side
(`TeamModelMode.Kits`) is done and live. **Two content routes were tried
to actually SHOW the kits to players; the first is proven dead, the second
compiles cleanly but has an unresolved visual bug the user hasn't
described yet.** Nothing is live on the server right now - it's back to
stock models, on purpose, at the user's request ("revert the skins for
now, it still bugged").

## 2. Route 1 - material override via Workshop addon: DEAD, don't retry

The original plan was to publish an addon whose files sit at the exact
same path as Valve's stock materials (`characters/models/tm_leet/
materials/tm_leet_v2_body_variant<x>.vmat`), so a mounted addon would
shadow the stock file. This was built, published, approved by Steam
moderation (twice - the Workshop Tools publisher **silently drops any
`characters/` content**, first discovered when the published VPK only
contained 3 of the ~32 files; fixed by packing the VPK by hand, see
`pack-kits-vpk.ps1` and `workshop-update.vdf`), and **proven not to work**:

- Server-side: addon downloaded, mounted, confirmed in
  `mm_print_searchpaths`.
- Client-side (`-condebug` console.log): `Received S2C_CONNECTION
  [addons:'...,3797479770']` -> `Mounting addon '3797479770'` -> game loop
  with it active.
- Player still rendered Valve's stock plaid `tm_leet` shirt. No errors.

Conclusion, confirmed empirically not just by community convention: **a
CS2 addon can ADD assets under permitted roots (`models/`, `materials/`,
`panorama/`, `maps/`, `scripts/`, `sounds/`, ...) but cannot REPLACE a
stock file under a base-game path like `characters/`.** Do not spend more
time on this route. Workshop item `3797479770` ("Soccermod Skins") is
still published and approved, but is now only useful as a place to
publish Route 2's content later if that's the direction - see  §5.

## 3. Route 2 - our own model files: WORKS TECHNICALLY, visual bug unresolved

Since a stock-path override can't ship, the alternative is a `.vmdl` under
our own `models/` root that **references tm_leet's existing mesh files
directly** (no new geometry, no re-rigging) with a `MaterialGroupList`
remap pointing the two materials at our painted kit `.vmat`s instead.
Everything else (skeleton, animation graph, arms, gloves, head) stays
exactly as Valve shipped it.

**Confirmed working:**
- `tm_leet_variant{a,b,c,d}.vmdl` (decompiled to KV3 text) reference their
  skeleton/animgraph by **path** (`animation/skeletons/characters/
  worldmodel.vnmskel`, `animation/graphs/worldmodel/worldmodel.vnmgraph`),
  not by an embedded binding - so a copy of that vmdl text keeps working
  with zero animation-side changes.
- All 4 kit `.vmdl`s compiled cleanly with `resourcecompiler.exe`
  (`OK: N compiled, 0 failed` each), producing `kit_home.vmdl_c` (557,506
  bytes), `kit_away.vmdl_c` (581,972), `kit_gkhome.vmdl_c` (578,442),
  `kit_gkaway.vmdl_c` (571,258) - all in the same size range as the stock
  `tm_leet_varianta.vmdl_c` (553,340), which is a good sign nothing is
  obviously broken structurally.
- Deployed once as loose files directly into the live server's
  `game/csgo/models/soccermod/` + `materials/soccermod/` (a stock-VPK
  content path search location, no addon/mounting needed for a same-
  session server-side test), pointed `css_sm2kit <slot> models/soccermod/
  kits/kit_<slot>.vmdl` at them, reloaded the stadium map so precache
  picked them up, and turned on `css_sm2teammodel kits`.
- **The user tried it and said "it still bugged"** - no further detail was
  captured before the ask to revert. This is the actual open item:
  **find out what's wrong, from the user, with eyes on it.** Candidates
  nobody has ruled in or out yet, roughly in order of how likely a stock-
  mesh-reuse approach is to hit them:
  - T-pose / broken animation despite the skeleton/animgraph paths being
    identical (would mean something else about anim binding didn't
    survive the copy - check first, it's the highest-risk assumption).
  - Wrong material actually applied (e.g. still showing plaid, or showing
    a different kit than the one assigned) - check whether the
    `MaterialGroupList`/`DefaultMaterialGroup` remap block is even being
    honoured, vs. just falling back to whichever material the mesh's own
    embedded default points to.
  - Visible seams/wrong UV alignment on the actual 3D model (the flat
    2D texture QA earlier only checked the atlas, never the mesh).
  - Something about the arms/gloves (left un-remapped, still pointing at
    stock `characters/models/shared/arms/...` paths) clashing visually
    with the new torso material.
  - Scale/collision/hitbox mismatch (unlikely - same mesh files, but not
    actually checked).

**Next step for whoever picks this up:** get the user in front of it again
with a specific ask ("what exactly looks wrong - is it the pose, the
color, a seam, something else") and a screenshot, rather than guessing.
Re-deployment is cheap (see §4) since everything is still compiled and
sitting on disk.

## 4. Exact files and how to redeploy Route 2 for another look

Nothing was deleted except the copies that were on the live server.

- **Content (editable source)**:
  `E:\SteamLibrary\...\content\csgo_addons\soccermod_jerseys\models\soccermod\kits\`
  - `kit_home.vmdl`, `kit_away.vmdl`, `kit_gkhome.vmdl`, `kit_gkaway.vmdl`
    (generated from the stock vmdls, path-rewritten + MaterialGroupList
    remap added)
  - `src\` - the 24 copied `.dmx` mesh/anim source files these reference
    (4 variants x 6 files each: thirdperson_body, thirdperson_default_
    gloves1, firstperson_default_gloves_arms2, firstperson_sleeves3,
    tools_preview, eye_test)
  - `..\materials\soccermod\kits\` - `kit_{a,b,c,d}_{body,lower_body}.vmat`
    (stock vmat text, texture paths rewritten to this folder) + the
    painted color PNGs + copied stock ao/metal/normal/cloth/rimmask/rough
    support maps (see §5 for where the painted textures themselves live)
- **Compiled (game-side)**:
  `E:\SteamLibrary\...\game\csgo_addons\soccermod_jerseys\models\soccermod\kits\*.vmdl_c`
  + `..\materials\soccermod\kits\*.vmat_c` and their `.vtex_c` textures.
  Recompile after any content change:
  `docs/jerseys/compile-kits.ps1` (compiles the addon's `.vmat`s; extend it
  or run resourcecompiler directly on the `.vmdl` files too - the exact
  command used originally:
  `resourcecompiler.exe -nop4 -game <root>\game\csgo -i <content>\models\soccermod\kits\kit_home.vmdl`,
  repeat per kit).
- **To get it live on the server again** (VPS, no restart needed):
  1. Copy the compiled `models/soccermod` and `materials/soccermod`
     folders from the game-side addon path (above) into
     `/home/gameserver/cs2/game/csgo/` on the VPS (same relative
     structure - `scp -r` or `tar | ssh`).
  2. `css_sm2kit home models/soccermod/kits/kit_home.vmdl` (repeat for
     away/gkhome/gkaway).
  3. `host_workshop_map 3361075564` (reloads the stadium map so the new
     paths get precached - do NOT `changelevel de_dust2` alone, that
     drops the stadium; this cost a live mistake once already this week).
  4. `css_sm2teammodel kits`.
  5. Have the user connect/reconnect and look with `!tp`.
  Revert: `css_sm2teammodel stock` (kit paths can stay pointed at the
  custom models harmlessly since Stock mode ignores them).

## 5. Painted textures - done, verified, don't repaint from scratch

All 8 kit color textures (Home/Away/GK Home/GK Away x body+legs) are
painted, committed, and independently verified pixel-exact against their
own stock bases (SHA-256 + changed-pixel counts cross-checked, not just
trusted) - commits `2aa9be0`, `8300a34`. Located at
`docs/jerseys/refs/{home,away,gkhome,gkaway}_{body,legs}_color.png` in the
repo. The reproducible painter (mask-driven, not hand-touched-up) is at
`docs/jerseys/paint-home-kits.py` + `paint-remaining-kits.py` +
`kit-painter-geometry.json` - if Route 2's bug turns out to be a seam/
alignment problem in the paint itself, fix the mask there and re-run
rather than repainting by hand. Full history of the texture work (what
was tried, corrected, and why) is in `docs/jerseys/2026-09-07-codex-
return.md` and `2026-09-07-codex-remaining-return.md`.

**GK gloves were investigated and found impossible via texture/model
work**: gloves are a separate mesh bound to a **shared, cross-agent**
material path (`characters/models/shared/arms/glove_*`) with no
tm_leet-specific variant - overriding it would affect every player on the
server, not just keepers. If gloves are still wanted, the lever is
**WeaponPaints** (already installed, v3.3a, does per-player skin/glove/
agent selection) integrated from the plugin when someone claims `!gk`,
not a texture change.

## 6. Plugin side - done, this part is not in question

`TeamModelMode { Off, Stock, Kits }` (commit `5af06a8`) is live and
correct regardless of which content route eventually works:
- `css_sm2teammodel <off|stock|kits>`
- `css_sm2kit <home|away|gkhome|gkaway> <model.vmdl path>` - accepts ANY
  path, so pointing it at Route 2's `models/soccermod/kits/*.vmdl` needed
  zero code changes.
- Kits follow the **squad** (tracks `_teamsSwapped`), not the raw T/CT
  side, so halftime doesn't flip them.
- Precached automatically next to the stock model paths - no manual
  precache work needed when repointing kits.

## 7. Everything else worth knowing

- `docs/jerseys/2026-09-07-football-kits-plan.md`,
  `2026-09-07-texture-brief.md`, `2026-09-07-codex-handover.md`,
  `2026-09-07-codex-followup-remaining-kits.md` are the earlier planning/
  brief documents, in order - background only, nothing actionable left in
  them.
- `pack-kits-vpk.ps1` (hand-rolled VPK v2 writer, bypasses the Workshop
  Tools publisher's root whitelist) is only needed again if Route 1 is
  ever revisited, or if Route 2's content gets published to the Workshop
  item instead (it would need re-pointing at `models/`+`materials/`
  instead of `characters/`, and would actually survive the publisher this
  time since those ARE permitted roots - worth trying the NORMAL publisher
  for Route 2 content first, since the whole reason the hand-rolled packer
  was needed was the `characters/` root specifically).
- The stray `docs/jerseys/refs/home_body_color.ai-draft.png` is a
  superseded AI-generated draft, explicitly marked not-UV-correct by
  Codex when it was produced. Never committed, ignore it.
- Steam account for any future Workshop action: `s5dgtgcb` (account name,
  not the display name "Natsu"). A cached steamcmd login session may or
  may not still be valid - if a future upload fails with a credential
  error, the user needs to re-run the one interactive
  `steamcmd +login s5dgtgcb +quit` (Steam Guard on phone) again.

## 8. Unrelated: the crouch-kick lift fix (already shipped, not part of this handover's open work)

A same-session, unrelated fix: crouched left/right-click kicks on a
grounded ball were launching correctly (power was already fine, even
stronger than standing) but at a near-zero launch angle purely because
crouching puts the eyes close to the ball's own height, geometrically
squeezing out the pitch range needed to loft it - the ball stayed in
constant ground contact and bled speed to rolling friction fast. Fixed by
adding a flat lift-angle floor for crouched grounded kicks only, default
12 degrees, live-tunable and persisted: `css_sm2ball_crouch_lift <0-45>`
(also in the Ball Workbench menu, "Lift and soft passes" group). Shipped
and deployed live, commit `f53741e`. Nothing pending here except iterating
the 12-degree default if the user wants it stronger/weaker after more
play - no further investigation needed.

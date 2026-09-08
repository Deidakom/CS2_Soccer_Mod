# Handover to Codex — football kits: where we are, what's left

> **Review correction, 2026-09-08:** the user has now confirmed **ERROR
> models**, not T-poses or bad paint. The server's remaining jersey VPK has
> 32 entries and **zero `.vmdl_c` files**. Route 2 was installed only as
> loose server files; client delivery was never demonstrated. See
> [the review and fixes](../reviews/2026-09-08-soccermod-review.md) before
> continuing. Successful compilation is not proof of a working player model.

Written by Claude for the user to hand to a Codex session. Covers the
skins/jerseys project end to end. The crouch-lift fix mentioned in the
title is separate and already shipped - see the one paragraph at the
bottom; nothing there needs continuing unless the user wants a different
number after more testing.

## 1. The short version

Four kits are painted, textures verified pixel-exact, and the plugin side
(`TeamModelMode.Kits`) exists. **Two content routes were tried
to actually SHOW the kits to players; the first is proven dead, the second
compiled locally but rendered ERROR models for the user.** Custom jerseys
are not live on the server right now - it's back to
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

The observed result establishes that **this stock-material override did
not render the kits**; it does not establish a universal rule about all
CS2 content overrides. Do not spend more time on this route. Workshop
item `3797479770` ("Soccermod Skins") is
still published and approved, but is now only useful as a place to
publish Route 2's content later if that's the direction - see  §5.

## 3. Route 2 - compiled locally; client delivery and rendering unverified

Since a stock-path override can't ship, the alternative is a `.vmdl` under
our own `models/` root that **references tm_leet's existing mesh files
directly** (no new geometry, no re-rigging) with a `MaterialGroupList`
remap pointing the two materials at our painted kit `.vmat`s instead.
Everything else (skeleton, animation graph, arms, gloves, head) stays
exactly as Valve shipped it.

**Reported compilation evidence, not runtime validation:**
- `tm_leet_variant{a,b,c,d}.vmdl` (decompiled to KV3 text) reference their
  skeleton/animgraph by **path** (`animation/skeletons/characters/
  worldmodel.vnmskel`, `animation/graphs/worldmodel/worldmodel.vnmgraph`),
  in the model source. The compiler accepted those references; that does
  not prove runtime animation compatibility.
- All 4 kit `.vmdl`s compiled cleanly with `resourcecompiler.exe`
  (`OK: N compiled, 0 failed` each), producing `kit_home.vmdl_c` (557,506
  bytes), `kit_away.vmdl_c` (581,972), `kit_gkhome.vmdl_c` (578,442),
  `kit_gkaway.vmdl_c` (571,258) - all in the same size range as the stock
  `tm_leet_varianta.vmdl_c` (553,340). Similar file sizes do not validate
  skeleton binding, dependencies or client rendering.
- Deployed once as loose files directly into the live server's
  `game/csgo/models/soccermod/` + `materials/soccermod/` (a stock-VPK
  content path search location, no addon/mounting needed for a same-
  session server-side test), pointed `css_sm2kit <slot> models/soccermod/
  kits/kit_<slot>.vmdl` at them, reloaded the stadium map so precache
  picked them up, and turned on `css_sm2teammodel kits`.
- **The user confirmed ERROR models on September 8.** Check the actual
  downloaded client package and model paths first. No evidence shows the
  custom `.vmdl_c` files reached the client; the retained jersey VPK has
  only stock-path materials/textures. A loose server install does not
  deliver files to clients. If a complete matching package still produces
  ERROR models, inspect the client's resource/dependency errors before
  changing the meshes, rig or paint.

**Next step:** recover the Route 2 source from Sergio-PC, build and publish
a complete custom model package, and test with a client that has no local
Workshop Tools assets. Preserve the existing stock-skin rollback.

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
  Recompile after any content change with `docs/jerseys/compile-kits.ps1`.
  Its default `CustomModels` route now compiles all eight custom materials
  and all four custom models, and fails on missing inputs/outputs or
  compiler errors. The `LegacyMaterials` option is historical only.
- **Deployment requires publication, client delivery and a map reload:**
  1. Build Route 2 on Windows. Inspect the four models and their material/
     texture dependencies in the resulting package. The default
     `pack-kits-vpk.ps1 -ItemId 3797479770` now requires the custom models
     and materials and excludes Route 1's `characters/` overrides.
  2. Publish the complete package to Workshop. Download that published
     revision on the server and a clean client; check that their actual
     packages contain all four models and dependencies. Replacing only
     the server's cached VPK does **not** update Workshop clients.
  3. Add the verified kit Workshop ID to MultiAddonManager's extra addons
     while retaining existing IDs. Keep stock skins active while setting
     `css_sm2kit home models/soccermod/kits/kit_home.vmdl` and the matching
     away/gkhome/gkaway paths.
  4. Reload the stadium with `host_workshop_map 3361075564` so the package
     mounts and the configured paths are precached. Clients must finish
     downloading the same content before the visual test.
  5. Enable `css_sm2teammodel kits` for the test. Verify Home, Away, both
     GKs, respawn, halftime, animation and first-person arms/legs from two
     clients. Compilation alone does not satisfy this check.
  Rollback begins with `css_sm2teammodel stock`. Before removing custom
  content, restore all four stock default paths from section 6 and reload
  the stadium; otherwise the precache manifest still requests those files.

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

**The attempted stock-glove override was unsuitable**: gloves use a
**shared, cross-agent**
material path (`characters/models/shared/arms/glove_*`) with no
tm_leet-specific variant - overriding it would affect every player on the
server, not just keepers. This does not prove all custom-model material
remapping impossible. Per-player **WeaponPaints** integration is another
candidate (installed, v3.3a), but neither approach is implemented or
validated for these kits. Keep it separate from resolving ERROR models.

## 6. Plugin side - mapping retained; precache regression fixed in review

`TeamModelMode { Off, Stock, Kits }` (introduced in `5af06a8`) retains:
- `css_sm2teammodel <off|stock|kits>`
- `css_sm2kit <home|away|gkhome|gkaway> <model.vmdl path>` now validates
  relative resource paths. Saved path changes apply at the next map's
  precache; existing map models remain active until then.
- Kits follow the **squad** (tracks `_teamsSwapped`), not the raw T/CT
  side, so halftime doesn't flip them.
- Previously a live path edit was assigned immediately despite only
  being precached on the next map. The review adds a map-specific snapshot
  and a stock fallback when the plugin has not seen a precache pass.
- Stock compatibility defaults are
  `agents/models/tm_leet/tm_leet_variant{a,b,c,d}.vmdl`, respectively
  Home, Away, GK Home and GK Away. These are ordinary stock skins.
- Precache registration is not a file download or proof of a valid model.

## 7. Everything else worth knowing

- `docs/jerseys/2026-09-07-football-kits-plan.md`,
  `2026-09-07-texture-brief.md`, `2026-09-07-codex-handover.md`,
  `2026-09-07-codex-followup-remaining-kits.md` are the earlier planning/
  brief documents, in order - background only, nothing actionable left in
  them.
- `pack-kits-vpk.ps1` now defaults to Route 2, requires all four custom
  models and eight materials, and excludes the obsolete `characters/`
  overrides. It creates a local VPK and manifest only. A normal Workshop
  Tools publication is also usable if inspection confirms the required
  resources survived packaging. Neither tool is evidence of client delivery.
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

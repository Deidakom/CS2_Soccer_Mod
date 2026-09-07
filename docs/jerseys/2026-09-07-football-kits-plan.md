# Football kits for CS2 SoccerMod — implementation plan (2026-09-07)

## 1. Goal and decision

Real football kits instead of tinted military operators: **Home**, **Away**,
**GK Home**, **GK Away**, one shared player model, kits differ by textures,
kits stay with their *squad* across the halftime side swap.

Design brief (settled 2026-09-01, do not re-ask):

| Kit | Jersey | Shoes / extras |
|---|---|---|
| Home | red / black | pink boots |
| Away | blue / white | yellow boots |
| GK Home | orange / black | black gloves |
| GK Away | white with a little blue | black gloves |

All four: generic three-stripe motif (trademark-safe, no real logo), one
uniform hairstyle. Name + number on the back is a later phase.

**Chosen route: material override on Valve's stock `tm_leet` agent.**
The alternative in the Codex plan — modelling and rigging a new footballer
mesh — is the honest "proper" version, but it needs a 3D artist plus CS2's
current animation system (AG2) to be satisfied; a mis-rigged model shows as a
T-pose. It is the *upgrade path*, not the first version. The override route
touches no mesh, no skeleton and no animation: the server assigns a stock
rigged model, an addon replaces its body/leg materials. The plugin side below
is model-path-agnostic, so it works unchanged if a real mesh is produced later.

Known ceiling: `tm_leet`'s one thin shoulder strap is fused into the mesh, and
the stock normal/roughness maps keep fabric/strap bump detail. Expect a
"cleanly recolored operator in a football kit", not a geometrically clean
jersey. The Home-only look-check in Phase 2 exists to judge exactly that
before the other three kits are painted.

## 2. Which model — proposal, verified against the live VPK today

Parsed `game/csgo/pak01_dir.vpk` directly (no tools needed). Facts:

- **Real rigged models: `agents/models/tm_leet/tm_leet_variant{a..k}.vmdl`**
  (11 variants, 537–792 KB each). Never use `characters/models/tm_leet/*.vmdl`
  — those are 4.8 KB stubs (same trap already hit with `tm_phoenix`).
- Each variant's look is three materials under
  `characters/models/tm_leet/materials/`:
  - `tm_leet_v2_body_variant<x>.vmat` — torso + arms → **jersey**
  - `tm_leet_v2_lower_body_variant<x>.vmat` — legs → **shorts, socks, boots**
    (new finding: the earlier plan only covered the body; boot colours need
    this second override)
  - `tm_leet_v2_head_variant<x>.vmat` — head/hair → leave stock
- Variants with their own body **and** lower-body material (usable as kits):
  **a, b, c, d, f, g, h, i, j**. Own head material: a, b, c, f, h, i, j, k
  (d, e, g share a head — a hint that some variants share the same head mesh,
  which is what "uniform hairstyle" wants; confirm visually in ModelDoc).
- Why `tm_leet` at all (2026-09-01 roster scan, still valid): `tm_phoenix`,
  `ctm_fbi`, `ctm_swat` have vests/plate carriers baked into the mesh; `tm_leet`
  is a plaid shirt with one strap. There is no civilian CT archetype, so both
  squads use `tm_leet` — the server already assigns models per team freely.

**Proposed mapping (change letters only if the ModelDoc check shows different
heads):** Home = `varianta`, Away = `variantb`, GK Home = `variantc`,
GK Away = `variantd`.

## 3. Phase 0 — prerequisites and coordination

1. **Concurrent editing.** As of 15:09 today the working tree has 18 modified,
   uncommitted files (Codex: ball handling, GK sprint box, sprint HUD, Match,
   Menu, `SoccerModMvpPlugin.cs`, tests). `TeamColor.cs`, `Config.cs`,
   `MenuParity.cs` are untouched by that batch, but the precache block in
   `SoccerModMvpPlugin.cs` and `GkSkin.cs` are in it. **Do the plugin work
   after that batch is committed**, or on a branch/worktree from `main`, never
   in the same tree at the same time (2026-09-05 lesson).
2. **Valve policy decision (yours).** Valve's server guidelines restrict
   custom-model services and the CS2 model-changer projects warn of GSLT
   bans. Nothing in Phases 1–2 touches the public server; the default mode
   stays `stock`. Decide before Phase 3.
3. Still parked: git stash `pending: GkSkin/TeamColor tint tweaks` (Codex
   tint changes from 2026-09-05, undecided).
4. `Source2Viewer-CLI` is no longer on this machine (only its old extraction
   in the scratchpad). Phase 2 needs it again to extract the b/c/d and
   lower-body maps — a download, so it will be asked for explicitly.

## 4. Phase 1 — plugin: kit mode (works before any addon exists)

Files: `SoccerModMvpPlugin.TeamColor.cs`, `.Config.cs`, `.MenuParity.cs`,
`SoccerModMvpPlugin.cs` (precache), `test/team-appearance.test.js`,
`test/managed/Program.cs`.

- `enum TeamModelMode { Off, Stock, Kits }` replaces the bool
  `_teamModelEnabled`. Persist as `string? TeamModelMode`; keep reading the old
  `bool? TeamModelEnabled` for migration (false→Off, true→Stock) — the
  structural JS test (`team-appearance.test.js:45`) also expects that property
  to remain.
- Four kit model paths, defaults from section 2, live-tunable and persisted:
  `css_sm2kit <home|away|gkhome|gkaway> [agents/models/...vmdl]` (no path →
  echo current). Permission `match`, like the existing toggles.
- `css_sm2teammodel <off|stock|kits>`; keep accepting `on|off` (on = stock).
- **Squad mapping:** `HomeTeam => _teamsSwapped ? CT : T`. `Match.cs`
  already resets `_teamsSwapped` at `StartMatch` and flips it in
  `StartNextPeriodOrFinish` (before the `mp_restartgame` respawn), so the
  existing spawn/round-start/team-change reapplication makes kits follow the
  squad with no new hooks. Kit = (home squad ? Home : Away) × (`IsGkSlot` ?
  GK : outfield). Outside a match `_teamsSwapped` keeps its last value, which
  matches where the players physically are.
- In `Kits` mode `pawn.Render` = white (legs alpha preserved) — textures
  carry the colours; `css_sm2teamcolor` keeps affecting only Off/Stock.
  Order stays SetModel → Render → `SetStateChanged` (tested).
- Precache: add the four kit paths next to `ModelPathT/Ct` in
  `OnServerPrecacheResources`. They are stock assets, so this needs no addon
  and no client download — which is the point: **`css_sm2teammodel kits` can
  be verified live immediately** (players turn into plaid `tm_leet` variants),
  reversible with `css_sm2teammodel stock`.
- Menu (`MenuParity.cs:183`): "Team models: Off / Stock / Kits" cycles.
- Tests: extract the kit choice into a static
  `ResolveKitModel(mode, teamsSwapped, team, isGk, paths)` and cover the
  8 squad×GK×swap cases in `test/managed/Program.cs`; update the JS
  structural assertions (`bool? TeamModelEnabled` stays, add `TeamModelMode`).
- Build on the VPS (`/tmp/dotnet10`), run both suites, hot-reload, commit.

## 5. Phase 2 — addon `soccermod_jerseys` (content)

Precedent: `content/csgo_addons/soccermod_phase1` (the ball addon; the
author → compile → publish pipeline is proven on this machine).

1. Extract with Source2Viewer-CLI (after the download is approved): for
   variants a/b/c/d the `body` and `lower_body` vmat text plus every map they
   reference (color, ao, metal, normal, cloth, rimmask, rough). The scratchpad
   already holds variant a's body set:
   `…/7f83f0a8-…/scratchpad/jerseys/vmatref/`.
2. Scaffold `content/csgo_addons/soccermod_jerseys/characters/models/tm_leet/
   materials/` with eight override vmats — same path and file name as the
   stock ones, stock text unchanged except `TextureColor` → the new PNG;
   every other map = copy of the stock map (keeps wrinkles/strap shading
   realistic). Plus `addoninfo.txt` (empty KV3 `{}` like phase1).
   Mounted addons shadow base-game paths, so no model file is authored.
3. Texture art via Codex (your call from 2026-09-01: Codex paints, Claude
   writes the brief). Brief = `docs/jerseys/texture-brief.md`: exact UV
   reference PNGs (body 2048×2048; lower-body size known after step 1), the
   colour table above, stripe motif, "only recolour cloth/boot regions, copy
   skin/face/hands pixels from the stock texture", sRGB PNG, same size.
4. **Gate: Home only first.** Compile in Workshop Tools, look in the ModelDoc
   viewport, then a local run (movement, lighting, distance). Decide: continue
   with the other three, or stop and go the new-mesh route. Also confirm the
   four chosen variants share a head.
5. Publish → Workshop ID (your Steam account).

## 6. Phase 3 — live server

1. `cfg/multiaddonmanager/multiaddonmanager.cfg`:
   `mm_extra_addons "3796041025,<jerseys id>"`; restart (you run it —
   standing rule). MultiAddonManager is already armed and serving 3796041025.
2. `css_sm2teammodel kits`. Rollback: `css_sm2teammodel stock`, remove the ID.
3. Verify: both squads + both GKs correct; halftime swap keeps kits with the
   squads; `!gk` claim/release re-skins; `!legs` still works; joining clients
   download the addon automatically; no `RESOURCE_TYPE_MODEL … not resident`
   or `status=139` in the journal; ball contact unchanged (same mesh, same
   collision dims).

## 7. Later (not in this plan)

Names/numbers on the back (CS:S `soccermod_jersey.sp` reference is archived),
first-person hands/gloves, a real footballer mesh if the override look is not
good enough.

## 8. Risks

| Risk | Handling |
|---|---|
| Valve policy / GSLT | your decision before Phase 3; default stays `stock` |
| Chosen variants have different heads/hair | swap letters after the ModelDoc check; 9 candidates |
| Strap/gear detail visible in the kit | Home-only gate before painting the rest |
| Concurrent Codex edits in the same files | Phase 0.1 sequencing |
| Override affects every `tm_leet` a–d on this server | intended: the server forces models anyway |

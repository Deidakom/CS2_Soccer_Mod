# Community repos evaluation + implementation plan (2026-08-30)

User supplied four repos and asked what's usable to improve SoccerMod
**without changing the core logic we built**. Verdict first, plan after.
Nothing here is implemented yet - this doc is the plan only.

## Verdicts

| Repo | Verdict | Why |
|---|---|---|
| `newix1/cssharp-soccerball` | **2 usable nuggets** | Single-file spawner, no kick/sound/menu code at all - far simpler than ours. But it proves two cheap mechanisms we don't use yet: (1) Valve ships a stock soccer ball model in the base game, (2) `entity.Render` RGB tint works on a ball prop. |
| `TitaniumLithium/CS2FunMatchPlugin` | Nothing to take | Its football mode spawns a prop and lets native physics do everything: no kick code, no spin, no sounds; goal = 500u proximity box, scoring = forcing the losing team to suicide. Strictly behind our implementation on every axis. GPL-3.0, so no code copying anyway. |
| `zakriamansoor47/SLAYER_Football` | Nothing to take | Repo contains NO source and not even the DLL - just README, server.cfg, plugin JSON, lang file. Locked to its own workshop map (3238565662). Nothing inspectable, nothing reusable. |
| `kus/cs2-modded-server` | **1 usable pointer** (ops, not gameplay) | It's an installer bundle, not a plugin. Gameplay-wise its soccer offering IS SLAYER_Football (see above). But its plugin list points at **MultiAddonManager**, which is exactly the missing enabler for our deferred Tier 3 items. |

## What to implement (when approved - NOT now, server is in use)

### A. Ball color tint / "ball skins" (from newix1) - S effort, zero core-logic risk
The one genuinely player-facing win. Our ball is two entities: the invisible
physics hull (`_ball`) and the visible Jabulani `prop_dynamic`
(`_ballVisual`). Tint the VISUAL only:

1. `_ballVisual.Render = Color.FromArgb(r, g, b)` +
   `Utilities.SetStateChanged(_ballVisual, "CBaseModelEntity", "m_clrRender")`.
   (Render setter confirmed present on CBaseModelEntity in 1.0.373 via
   reflection this session; the SetStateChanged may be redundant but is
   harmless - verify in-game once.)
2. Re-apply wherever the visual is (re)created - `EnsureOwnedBallVisual` /
   `SyncOwnedBallVisual` path - since round restarts rebuild it.
3. Command `css_sm2ball_color <r g b|white|off>` ("ball" permission flag),
   persisted in `BallSettingsStore` like every other tunable (remember the
   menu-mode lesson: never add a live tunable without persisting it).
4. Optional follow-up: `!menu` entry under a "Fun" submenu, or per-team
   tints at match start. Decide with the user later.

Caution: tint multiplies the texture, so it darkens the white panels of the
Jabulani - full-white (255,255,255) must be the neutral default and "off"
must restore exactly that.

### B. Stock Valve ball model as an always-available visual variant - XS
`models/props/de_dust/hr_dust/dust_soccerball/dust_soccer_ball001.vmdl`
ships with CS2 itself - every client has it, no workshop download, survives
any map vpk change. Add it as a second `css_sm2ball_visual` option next to
the Jabulani (visual swap only - the physics hull, radius and all kick
logic stay untouched). Also the natural ball model for a future multi-ball
training mode, since it has zero addon dependencies.

### C. MultiAddonManager spike (via kus's plugin list) - the Tier 3 enabler
> Superseded: full implementation plan now lives in
> `2026-08-30-goal-roundwin-multiaddon-plan.md` (Task 2).
Not gameplay code - infrastructure. Source2ZE's MultiAddonManager mounts
extra workshop addons server-side and makes clients download them alongside
the map. That is the missing prerequisite for every deferred content item:
custom kick/bounce sounds, shouts, jerseys/GK skins, and - importantly -
**shipping a patched stadium map** (the roof-scoreboard PVS fix and the CSF
logo removal both need map edits; distributing our own edited map as a
workshop item is the clean end-state, and MultiAddonManager is how a second
content addon could ride along if we keep the original map).
Plan: install on the TEST server only, verify (1) it coexists with
CounterStrikeSharp 1.0.373 and our plugin, (2) a trivial test addon
downloads to a connecting client, (3) no interference with
`host_workshop_map`. Only then design the actual content addon.
Note: it's a Metamod plugin - the user must run any `meta` load commands
themselves (standing rule; the agent is blocked from that).

### D. Explicitly rejected
- FunMatchPlugin football mechanics (all of it) - inferior to ours.
- SLAYER_Football - nothing to inspect; its map-locked design contradicts
  our stadium.
- kus as a server base - our server is already purpose-built; adopting a
  50-plugin bundle would ADD moving parts for zero gameplay gain.

## Order (when the user says go)
1. A (ball tint) - small, visible, fun; in-game verify with the user.
2. B (stock model variant) - rides the same visual-path edit.
3. C (MultiAddonManager spike) - separate session, test server only,
   user runs the meta commands.

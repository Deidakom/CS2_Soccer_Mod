# SoccerMod review — September 8, 2026

Reviewed the changes from `c8ec27a` through `229e134`, including the jersey
handover, goal flow, ball contact/rollout, held knife swings, crouch lift,
goalkeeper sprint and team appearance. This is a correction of confirmed
regressions, not another menu redesign or a retuning of the accepted ball feel.

## Confirmed findings and repairs

| Finding | Evidence | Repair |
| --- | --- | --- |
| Latest source cannot build | Clean Release build fails with CS0117: `MatchRuleMath.BallFitsBelowCrossbar` is missing. GitHub Build failed for `ea7d40f`, `f53741e` and `229e134`. | Implemented the whole-ball crossbar check with pitch-relative height, measured underside and finite geometry validation. Added boundary and invalid-input tests. |
| Immediate goal reset loses the scorer's name | `ResetBallForGoalSafety` clears `_lastKickerSlot` through `ResetDerivedMotion` before the announcement reads it. | Capture the name before reset/death callbacks; preserve existing statistics attribution. |
| Reset ball can be played during the goal pause | The reset left motion/contact enabled and assumed nobody could reach centre before the restart. A player may already be there. | Use the existing full-stop reset, then the existing pause lock. Round start releases that lock. This prevents premature touches consuming the next kickoff wall. |
| Live kit edits assign paths before their precache pass | `css_sm2kit` saved a new path and immediately called `SetModel`; the manifest was only updated on the next map. | Keep a snapshot of the models registered for the map. Edits remain pending until the next precache; hot reload uses stock models and tint until that pass. Validate relative resource paths on commands and configuration load. |
| Kit build can give false confidence | The old compiler only targeted Route 1 materials, counted unrelated old outputs, and merely printed a warning on failure. The packer accepted a model-free addon. | Default both scripts to Route 2. Require four exact custom models and eight exact materials; throw on missing files, compiler errors or empty outputs. Exclude old stock-path overrides from the custom package and emit a manifest/checksum. |

The existing regression suite passed after these repairs, including held knife
input, volley direction, finite rollout, body contacts, the restored wall
response, GK box bounds and stamina transitions, cannon goal suppression,
all 47 ball workbench controls, spectator input and kickoff lifetime. Those
checks do not measure subjective CS:S parity or prove a model renders in CS2.

## Jersey diagnosis

The user clarified that the failed test showed **ERROR models**.

Read-only inspection of the German server found:

- Stock team models enabled, with the four kit settings back on stock
  `agents/models/tm_leet/tm_leet_variant{a,b,c,d}.vmdl` paths.
- The retained `3797479770_dir.vpk` contains 32 files and **zero player
  models**: eight stock-path material overrides, texture resources and
  `addoninfo.txt`.
- The custom loose model files were removed by the skin rollback, as the
  handover states. Logs confirm the custom Home/Away paths were assigned
  during the September 7 test, but do not prove clients loaded them.
- The current MultiAddonManager configuration requests addon `3796041025`;
  the failed jersey addon is disabled.

The handover records a server-only installation for Route 2, with no published
custom-model package or successful clean-client download. That is the first
delivery gap to resolve. A server `SetModel` log and a successful resource
compiler run do not establish that a client has the required model and its
dependencies. [MultiAddonManager's documentation](https://github.com/Source2ZE/MultiAddonManager#convars)
describes addon delivery and the map reload for precaching; CounterStrikeSharp
documents [registration during the precache callback](https://github.com/roflmuffin/CounterStrikeSharp/blob/main/managed/TestPlugin/TestPlugin.cs).

The painted textures and custom-source work remain reusable. Do not repaint
or rebuild the rig to diagnose an absent client model. Restore delivery first,
then inspect resource errors, animation, material mapping and first-person
appearance on two clients. The report of ERROR models does not rule out
additional problems once the content actually loads.

The corrected [jersey handover](../jerseys/2026-09-08-codex-handover-status.md)
contains the deployment sequence. Route 1 remains a failed experiment. The
earlier universal claims about all stock overrides and all possible GK glove
model work were stronger than the evidence and have been corrected.

## Validation and remaining work

- Release build: zero warnings/errors; managed gameplay tests passed.
- 127 Node tests passed, including goal-reset ordering and existing menu/HUD checks.
- 21 Python tests passed, including six PowerShell integration tests for the actual
  kit scripts. The latter use fixture assets and a fake compiler, verify the
  VPK directory/CRCs/MD5s and deterministic output, and exercise failure paths.
  They do **not** claim real Valve assets compiled on this Mac.
- Local release packaging and the Linux installer smoke test passed. The
  installer also no longer tells operators to expect the obsolete `1.1.0` label.

## German deployment

Deployed the reviewed DLL on September 8 with the existing settings-preserving
installer. Live SHA-256:
`7df7b720c660b856c6dc292d3cebccadb82f224d52c0fa0d54048377a638c602`.
The service loaded SoccerMod and its other eight plugins successfully.

An empty-server match test launched one ball through each goal. The score
progressed exactly `0–0 → 1–0 → 1–1`; each goal logged an immediate full-stop
reset in the same tick, followed by kickoff waiting. After each restart the
ball was at `(0, 0, -13.19)` with zero sampled and reported velocity. The test
does not replace an in-game check of human scorer names or visual wall rendering.

All pre-existing JSON files matched the backup after the test, including
gameplay settings and player statistics. The previous match log was restored
and the server returned to a fresh warmup. Stock skins remain enabled.

Rollback on the German server:

```sh
bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260908T072525Z-Hf7L2k/rollback.sh
```

This restores the preceding DLL and ball/match/menu tuning. Player ranks,
admins, bans and competitive history retain their current values.

## Remaining jersey work

The current review does not publish or re-enable jerseys. The actual Route 2
model/DMX/material sources are on Sergio-PC, outside the committed repository.
A complete published package and a clean-client visual test remain necessary.
Stock models are the working live configuration meanwhile. The 12-degree
crouch lift, wall response, GK sprint, compact sprint HUD and reverted menu
presentation remain in place.

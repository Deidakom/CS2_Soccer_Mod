# German server: CS:S menu catch-up 1.4.0

Deployed on 2026-09-05 to the existing German CS2 instance, game port 27017,
service `cs2-soccermod-test.service`, map `soccer_cssl_stadium_v8`.
The connected owner explicitly authorized restarts and live testing.

See the [release notes](../releases/v1.4.0.md) and
[feature-by-feature parity audit](../css-menu-parity-2026-09-05-v1.4.md).

## Artifact and rollback

Final `SoccerModNativeHull.dll` SHA-256:

```text
2567eeb8b6e19add76be62f73378e07a7513c08d0388e2d92fd573c301acb03d
```

The complete pre-1.4.0 plugin snapshot is:

```text
/home/gameserver/cs2-soccermod-backups/ball-handling-20260905T083055Z-xgHXHo
```

To return to the previously deployed 1.3.0 plugin and its gameplay settings,
run on the German server as root:

```sh
bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260905T083055Z-xgHXHo/rollback.sh
```

This restarts CS2. The targeted rollback restores the binary and ball/match/menu
settings. It retains current public ranks, competitive-history sidecar, saved
training layouts, admin and ban data. Old versions do not understand every new
public-stat metadata field; the separate competitive-history file is not rewritten
by 1.3.0. A full directory snapshot is available for manual recovery.

Intermediate development snapshots ending `083722Z-QVsbdx` and `084204Z-UhEbxG`
also exist. Use the **083055Z-xgHXHo** script above to undo this feature release;
the later snapshots contain intermediate 1.4.0 builds.

All three installs used the installer's `preserve` profile mode. The actual
pre-install selection was `legacy` and remains `legacy`; this release did not
switch the owner's ball profile. The Jabulani model and existing physics tuning
remain selected. Improved and creative profiles are still available separately.

## Verification

- Final cold start loaded CS2 SoccerMod **1.4.0**; all nine installed plugins
  reported loaded. The initial candidate exposed a cold-start engine-globals
  error; player enumeration now runs only on hot reload, with authorization
  callbacks handling new players on cold start.
- Native cone, can and plate each spawned with `SOLID_VPHYSICS`, physics enabled,
  and nonzero collision bounds. Diagnostics remove temporary props after eight
  seconds; starting a match also cleared them. Client appearance and detailed
  collision feel still need an in-game play test.
- Kickoff outline preview produced 36 beam segments and automatically returned
  to inactive with zero segments. This verifies entity creation/cleanup, not
  every connected player's movement or visual presentation.
- Controlled ball launch: 600 units/s horizontally, 200 units/s lift. Pause held
  position `(46.59, -0.01, -0.51)` unchanged across samples two seconds apart.
  After the resume countdown the ball reached `(514.06, -1.20, -12.49)` with
  measured speed about 319 units/s. The engine's `AbsVelocity` reports zero for
  this moving VPhysics ball, so pause restoration uses the plugin's measured
  velocity. Angular-momentum restoration has not been established.
- Pause and resume preserve touch attribution. Explicit server resume returned
  the phase to Live and cleared `pausedBall`. Test matches were stopped; the
  instance was left in Warmup, score 0–0, no training devices, no kickoff preview,
  and a single ball at center following the final round restart.
- No plugin exception or failed callback was observed after the final cold start
  at 08:42:04 UTC. Existing engine/map resource warnings are not represented as
  resolved by this plugin update.
- Local validation: 100 Node tests, 15 Python tests and 56 managed gameplay
  scenarios. CS:S companion fixes compiled all eight SourcePawn plugins with no
  errors; its 12 Node and 10 Python tests passed. Existing SourcePawn warnings
  remain. GitHub CI provides an independent Linux build/package check.

## Outstanding differences

This is not a claim of complete client-observed CS:S parity. Original sounds,
grass, jerseys and prop artwork still require Source 2 compilation and client
delivery. A physical hoop rim is not implemented; the current hoop is a scored
outline. Source 1-specific ragdoll/dissolve/class-selection, exact dead-chat
routing and arbitrary HUD placement remain differences documented in the audit.
Ready-check input, full CAP drafts and celebration weapons still need a group
play test; celebration weapons are disabled by default. There is no active CS:S
installation on this host, so the companion CS:S ranking fixes were built and
committed, not deployed as a running CS:S service.

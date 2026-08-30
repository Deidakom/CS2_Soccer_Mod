# Automated physics diagnostics — 2026-08-27

## Result

The fixed-profile VConsole runner replaces repeated operator clicks for the
current development gates. It controls only audited transforms, velocities,
entity inputs, and traces; retains correlated per-think JSONL; stops on the
first hard failure or exact target; and analyzes counts and repeatability.

The most important result is that the documented physics-prop
`DisableMotion` input is a viable reset candidate. The baseline
`teleport_only` reset repeatedly left microscopic nonzero angular state after
moving-ball tests. With `disable_motion`, the same strict transform, linear,
and exact-angular-zero gates pass without widening a tolerance. Physics is
re-enabled immediately before `Wake` and the bounded velocity Teleport.

This does **not** close Phase 1. After the strict terminal-tail and lifecycle
checks passed, `disable_motion` was promoted to the staged loose-runtime
default. The retained map VPK still contains an older adapter. A rebuilt VPK,
flat symmetric roll lanes, a vertical wall fixture, multiplayer replication,
clean delivery, and soak remain open.

## Fingerprint

- CS2 build/target: `24957633`.
- Installed `point_script.d.ts` SHA-256:
  `2da5d7d10ffcea1aac52e668cf153974a3d973aeb8e7dc9a15fb8a2227b50bf9`.
- Frozen Stadium package SHA-256:
  `052bb4a46e7b80bf509f70ce53425185d4e35a6f59e600c8df21651b46eaa6cc`.
- Unchanged generated VMAP SHA-256:
  `34f07c7a6eb40ee6f9367929a2cd3d3f61ba8f3250427575338e2c808ff04653`.
- Current staged loose runtime bundle SHA-256:
  `0ce4ceff785302961aae2a33d6eb97e0d3e55c400b7798796f5bcd3571db8698`.
- Current modular physics-profile source SHA-256:
  `e847044360ad59ab3361dbbfcf18ea048147af3b466c251118a4b1e08b3171b7`.
- Repository verifier: 58/58 tests, no pinned precondition blocker.

The loose runtime auto-reloaded in Workshop Tools and passed the tests below.
The packaged map VPK still contains the older adapter and must not be
attributed the loose bundle hash; one retained Hammer build is required before
packaged or clean-delivery qualification.

## Fixed profiles and runner

`sm2lab_physics_trial PROFILE COUNT` accepts 19 fixed profiles and at most 100
trials. It rejects arbitrary methods, transforms, velocities, and extra input.
The runner is `tools/run-phase1-physics-gate.mjs`; the analyzer is
`tools/analyze-phase1-physics-run.mjs`.

The drop detector was corrected during live execution. Its first version used
the original release height as the rebound maximum. The retained corrected
fields separate impact center Z, post-impact apex center Z, rebound rise, and
maximum depth relative to the swept-sphere floor-contact plane. The invalid
pre-correction artifact is preserved and is not used as bounce-height evidence.

## Live matrix

| Profile | Count | Result | Key observation | Artifact |
|---|---:|---|---|---|
| `wake_y_200` | 10 | Pass | 200 max speed, 5.75246 two-think displacement, 0% CV | `artifacts/phase1-physics/2026-08-27-wake-y-200-10-rerun.jsonl` |
| `speed_cap_y_1250`, baseline | 10 | Motion passed; cleanup failed | 1250 max speed and 0% CV; strict reset failed `angular_motion` | `artifacts/phase1-physics/2026-08-27-speed-cap-y-1250-10.jsonl` |
| `goal_east_1250` | 10 | Pass | 10 exact forward goal commits | `artifacts/phase1-physics/2026-08-27-goal-east-1250-10.jsonl` |
| `goal_west_1250` | 10 | Pass | 10 exact forward goal commits | `artifacts/phase1-physics/2026-08-27-goal-west-1250-10.jsonl` |
| `reverse_east_1250` | 10 | Pass | zero reverse goals | `artifacts/phase1-physics/2026-08-27-reverse-east-1250-10.jsonl` |
| `reverse_west_1250` | 10 | Pass | zero reverse goals | `artifacts/phase1-physics/2026-08-27-reverse-west-1250-10.jsonl` |
| `near_miss_east_1250` | 10 | Pass | zero outside-aperture goals | `artifacts/phase1-physics/2026-08-27-near-miss-east-1250-10.jsonl` |
| `drop_64`, corrected baseline | 3 | Measurements passed; cleanup failed | rebound rise 2.32290, takeoff 19.3992, depth 2.93131, all 0% CV | `artifacts/phase1-physics/2026-08-27-drop-64-3-corrected.jsonl` |
| `drop_128`, baseline | 3 | Measurements passed; cleanup failed | rebound rise 2.62410, 0% CV | `artifacts/phase1-physics/2026-08-27-drop-128-3.jsonl` |
| `drop_256`, baseline | 3 | Pass | rebound rise 0.610525, 0% CV | `artifacts/phase1-physics/2026-08-27-drop-256-3.jsonl` |
| `roll_x_200`, baseline | 3 | Measurements passed; cleanup failed | 325.386 units; still 4.16433 units/s after 10 s | `artifacts/phase1-physics/2026-08-27-roll-x-200-3-rerun.jsonl` |
| `roll_y_200`, baseline | 3 | Pass | settled at 5.35938 s and 271.034 units; displacement CV 0.000196% | `artifacts/phase1-physics/2026-08-27-roll-y-200-3.jsonl` |
| `wall_y_300_0` fixture qualification | 1 | Blocked before motion | trace hit a ramp (`normal.z=0.970142`, approach dot `-0.242536`), not a wall | `artifacts/phase1-physics/2026-08-27-wall-fixture-qualification.jsonl` |

X/Y roll differs by more than 5%, but the paths cross different template
geometry. It is fixture-confounded and cannot yet be attributed to ball
anisotropy. Likewise, the ramp cannot be used to infer wall restitution.

## Reset A/B

The installed Hammer FGD explicitly documents `Wake`, `Sleep`, `EnableMotion`,
and `DisableMotion` for physics props. It does not document a general vector
`SetAngularVelocity` input for those props.

Observed sequence:

1. `Sleep` alone preserved the residual angular value.
2. `Sleep` before the ordinary Teleport reset still allowed contact simulation
   to reintroduce nonzero angular motion.
3. `DisableMotion` made linear and angular motion exactly zero.
4. The unchanged strict Teleport reset then passed write and settled gates on
   attempt 1 and remained exact at Z15.
5. `EnableMotion`, `Wake`, and the velocity Teleport restored real movement.

Automated candidate results:

| Profile under `disable_motion` | Count | Result | Artifact |
|---|---:|---|---|
| `speed_cap_y_1250` | 10 | Pass, including strict cleanup | `artifacts/phase1-physics/2026-08-27-speed-cap-y-1250-10-disable-motion.jsonl` |
| `drop_64` | 3 | Pass, including strict cleanup | `artifacts/phase1-physics/2026-08-27-drop-64-3-disable-motion.jsonl` |
| `goal_east_1250` | 10 | Pass; ten freeze/reset/re-enable cycles | `artifacts/phase1-physics/2026-08-27-goal-east-1250-10-disable-motion.jsonl` |
| Final east goal plus strict tail | 1 | Pass; all 8 samples stayed at exact `(512,0,15)` with zero linear and angular motion | `artifacts/phase1-physics/2026-08-27-goal-east-disable-motion-tail-content-validated-1.jsonl` |

The high-speed A/B is decisive for the known failure: the baseline and
candidate used the same 1250-unit motion profile and strict verification. The
candidate passed 10/10 with no tolerance change.

## Default and lifecycle qualification

The final terminal-tail runner validated the contents, correlation, and exact
indexes of all eight post-reset samples, not merely the completion event. The
promoted default then passed the same fixed one-trial wake gate after each
lifecycle transition:

| Transition | Readiness after transition | Motion proof | Artifact |
|---|---|---|---|
| Loose script auto-reload | `disable_motion`, valid ball, play enabled | 200 units/s exact; cleanup passed | `artifacts/phase1-physics/2026-08-27-lifecycle-script-reload-wake-1.jsonl` |
| `mp_restartgame 1` | Fresh ball generation 2, `disable_motion`, play enabled | 200 units/s exact; cleanup passed | `artifacts/phase1-physics/2026-08-27-lifecycle-round-restart-wake-1.jsonl` |
| Workshop Tools `restart` | Server time reset and fresh ball generation 1, `disable_motion`, play enabled | 200 units/s exact; cleanup passed | `artifacts/phase1-physics/2026-08-27-lifecycle-map-reload-wake-1.jsonl` |

`map soccermod_phase1_lab` and `changelevel soccermod_phase1_lab` were rejected
by this Workshop Tools session as invalid direct map requests. They are not
counted as tests. The engine-documented `restart` command performed the actual
current-map unload/reload from disk.

## Next gates

1. Retain `teleport_only` as the regression control; do not widen the strict
   reset tolerances.
2. Add a flat symmetric two-axis roll fixture and a qualified vertical wall to
   the writable lab, rebuild once, and rerun 200/400/800 roll plus wall angles.
3. Compile the promoted default into a retained VPK and rerun its packaged
   smoke checks.
4. Complete the remaining automated local physics/goal matrix, then proceed to
   remote-client replication and private-Workshop clean delivery.

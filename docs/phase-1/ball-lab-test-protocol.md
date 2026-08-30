# Phase 1 ball-lab test protocol

> Control-scope correction (2026-08-28): the CS:S-compatible product control
> is the single primary/left-click knife kick. Secondary-shot and lob trials
> below are retained only as historical engine experiments; they are not MVP or
> parity requirements.

This protocol separates mathematical correctness from engine behavior. A pure
test passing does not prove VPhysics, networking, or human feel; an in-game test
is not accepted unless its build, assets, script declaration, and telemetry are
fingerprinted.

## Execution surfaces

| Surface | What can be proven there |
|---|---|
| Compiled CSF Stadium now | Package/load smoke checks, live entity queries, and manual video/telemetry baselines for native ball behavior |
| Our writable Hammer lab | Official script API, automated physics, kick, goal, reset, lifecycle, telemetry, and packaging tests |
| CSF Stadium with permitted `.vmap` source | Directly attach and qualify our `point_script` inside the Stadium |
| Private Workshop plus dedicated server | Clean delivery, remote replication, latency/loss, player load, and soak |

An ordinary second addon cannot merge entities into the Stadium's compiled
entity lump, and a `.vjs_c` file does not auto-start. Cross-addon resource use
is not assumed until mounting, precaching, and rights are separately proven.

## Configuration policy

The current kick constants in `src/ball-lab/core/kick.js` are provisional lab
inputs, not claims that they match CSS:

| Parameter | Initial value |
|---|---:|
| Maximum eye-to-ball-center reach | 96 units |
| Aim cone | 55 degrees |
| Accepted-kick cooldown | 0.16 seconds |
| Pass speed | 520 units/second |
| Shot speed | 900 units/second |
| Lob speed | 650 units/second |
| Pass/shot/lob lift | 55 / 85 / 390 units/second |
| Existing velocity inherited | 15% |
| Maximum commanded speed | 1,250 units/second |
| Commanded angular velocity | Off pending API/unit calibration |

They may change only after telemetry-backed comparison. The CSS reference
provides size and geometry but not a portable Source 2 kick-force formula; CSS
used native Source 1 knife/VPhysics response.

Reference geometry for the current writable template lab:

- scaled Dust ball diameter approximately 30 units and reset center
  `(512, 0, 15)`;
- virtual goal planes at X `384` and `640`;
- goal lateral aperture Y `-104..104`, Z `0..80`.

The separate Stadium reference uses goal-side witnesses near Y `+1421.44` and
`-1420`, with net triggers near `+/-1424`. These axes, positions, and its roughly
37.61-unit Jabulani ball are not transferable to the template lab.

Every physics candidate records its compiled collision bounds,
effective radius, pivot-to-floor offset, resource hash, entity class, surface,
effective mass/damping, and candidate-specific spawn/rest transform. The live
Stadium model is about 37.61 units across, so it cannot be compared fairly at a
30-unit reset height without controlling scale and floor clearance.

## Required runtime fingerprint

Every run starts with one structured record containing:

- CS2 build ID, client/server version, Source revision, and map name;
- listen or dedicated mode;
- map/addon package SHA-256 and script revision;
- installed `point_script.d.ts` SHA-256;
- ball entity class, model, collision-hull variant, surface property, mass,
  damping, and reset transform;
- test ID, run ID, tick/think cadence, connected clients, latency, and loss.

Runtime events are JSON Lines prefixed with `[SM2LAB]` and use this common
envelope:

```text
[SM2LAB] {"schema":"cs2-soccermod.balllab/1","runId":"...","seq":42,"event":"kick_result","testId":"K-LOS-002","candidateId":"simple-hull-v1","serverTime":12.3456,"thinkSeq":811,"ballGeneration":1,"resetSequence":3,"goalSequence":0,"data":{"accepted":true,"reason":"accepted","kind":"pass","distance":60,"aimDot":0.99,"velocity":{"x":520,"y":0,"z":55},"unclampedSpeed":522.9006,"finalSpeed":522.9006,"maximumBallSpeed":1250,"wasClamped":false,"writeAngularVelocity":false}}
```

`seq` is a positive safe integer and strictly monotonic within a run. Think and
generation counters are safe nonnegative integers (`ballGeneration` starts at
one). All numeric values must be finite JSON numbers; `NaN`, infinities, and
numeric strings are invalid. Distances are Source units, time is seconds, and
linear velocity is Source units/second. Values are serialized with enough
digits to round-trip the engine value; displays may round copies only.
When diagnostic telemetry is explicitly enabled, `state_sample` and
`client_state_sample` are stored at least 20 Hz while goal/safety evaluation
runs every server think. MVP gameplay leaves high-frequency telemetry disabled
to keep the developer console usable. Configured latency/loss and
observed measurements are separate fields. Numeric angular telemetry stays
absent until units are verified; reset telemetry reports the unit-independent
boolean `angularMotionZero`.

Required event names are exactly `run_start`, `run_end`, `ball_bind`, `ball_invalid`,
`duplicate_ball`, `test_start`, `test_end`, `state_sample`,
`client_state_sample`, `clock_alignment`, `kick_attempt`,
`kick_result`, `trigger_enter`, `goal_candidate`, `goal_commit`, `goal_ignored`,
`reset_begin`, `reset_write_verify`, `reset_settle_verify`, `reset_end`,
`speed_clamp`, `assertion`, `script_exception`, and `run_summary`.

The exact field, reason, nullability, and type rules are in
[`telemetry-contract.md`](telemetry-contract.md). The adapter must validate
emitted fixtures against that contract before API smoke can pass; adapters may
not put arbitrary prose in a `reason` field.

## Adapter rules

The future Valve-facing adapter must stay thin:

1. Resolve one named ball; zero or multiple matches are a fatal test failure.
2. Translate current documented `OnKnifeAttack`, entity, trace, think, input,
   transform, and velocity APIs into plain inputs for the tested core.
3. Reject dead/spectator/ineligible players, disallowed match states, cooldown,
   excess reach, outside-cone aim, and obstructed line of sight on the server.
4. Apply only finite, speed-capped transforms and velocities.
5. Evaluate center-plane goal crossings every server think. Triggers are
   witnesses, not the sole scoring authority. Whole-ball and legacy overlap
   timing are separately named diagnostics and must never expand the aperture.
6. Lock the goal before emitting a score. The pure core releases that latch only
   for a settled reset with the matching reset sequence and ball generation.
7. Reset position, angles, linear velocity, angular velocity, touch history, and
   previous-position history together so teleportation cannot resemble a goal.
8. Verify reset in two stages: confirm the transform/zero write on the next
   think, then require the candidate to be within its rest tolerance for at
   least two consecutive thinks. Only the second stage can resume play.
   One captured moving post-goal reset produced an angular-only next-think
   failure on both the original zero-motion command and its single immediate
   retry. The adapter may reissue the same logical zero-motion command once,
   with a fresh issued-think identity, only when `angular_motion` is the sole
   failed write condition. The failed first attempt remains in smoke telemetry,
   and a recovered cycle is diagnostic-only rather than a formal reset pass.
   The retry does not relax exact angular zero; any other first failure or any
   second failure remains fatal. The failed live retry disproves only the
   immediate identical rewrite, not every possible delayed/contact strategy.
   A later controlled spike found that the Hammer-FGD documented
   `DisableMotion` input clears the residual and permits the unchanged strict
   reset to verify, while `EnableMotion` before `Wake` and the velocity
   Teleport restores next-think motion. Its strict terminal tail and
   loose-script, round-restart, and current-map-reload checks passed, so it is
   the loose-runtime default; `teleport_only` remains the A/B control. See
   [`automated-physics-diagnostics-2026-08-27.md`](automated-physics-diagnostics-2026-08-27.md).
9. Do not write angular velocity during a kick in the first adapter
   (`writeAngularVelocity:false`). Writing the zero vector during an atomic
   reset is permitted and required because zero is unit-independent. Calibrate
   the current `RotationVector` units, radius relationship, wake behavior, and a
   safe angular cap before enabling nonzero spin.

No Valve method name will be guessed before the current installed declaration
is hashed and inspected.

The current diagnostic revision compares two fixed goal-reset profiles without
changing formal verification. `contact` writes and rests at the marker center,
currently Z15. `radius_clearance` writes at Z30—one nominal radius above that
rest center—while keeping the required rest transform at Z15. Non-goal resets
always remain at Z15. Raw linear/angular state and the guarded ground-entity
descriptor are captured before and immediately after each Teleport, on the
next script think, and for a bounded eight-think terminal tail. The stream is
explicitly uncalibrated and cannot qualify the reset suite. A clearance write
is expected to fail closed if gravity creates position or velocity error before
the next-think gate; the experiment measures that ordering rather than
bypassing it.

The 2026-08-27 packaged comparison confirmed that expectation. The contact
cycle passed on write 1 and remained angular-zero for its complete eight-think
tail. The radius-clearance cycle was exact and motionless immediately after the
Z30 Teleport, then read Z `29.8047256` with downward speed `12.4975214` on the
next think and failed only `velocity`. Ten additional contact cycles passed
10/10 with no retry and no nonzero angular terminal sample. These are smoke and
repeatability data, not a substitute for the formal counts below; see
[goal-reset-profile-comparison-2026-08-27.md](goal-reset-profile-comparison-2026-08-27.md).
One subsequent manually issued secondary attack also passed the complete
callback, accepted-shot, goal, first-write contact-reset, and eight-sample tail
chain. It is a secondary smoke pass only; the 100-secondary count gate remains
unchanged.

The automated fixed-profile harness has now completed the first local physics
matrix without further operator clicking. It retained 50/50 forward/reverse/
near-miss goal cases, repeated drop and roll measurements, and the decisive
moving-reset A/B. The current template's apparent wall is a ramp and fails the
new surface-normal qualification before motion; X/Y roll paths are not
geometrically symmetric. Wall restitution and axis symmetry therefore require
dedicated fixtures rather than more repetitions on the wrong geometry.

## Test suites and pass criteria

| Suite | Procedure | Required result |
|---|---|---|
| API smoke | Bind each candidate ball, sample for 60 seconds, reload map 10 times | One valid server ball, no exception or duplication |
| Knife callback | 100 primary and 100 secondary attacks | Exactly one callback per discrete attack |
| Wake/write | Set velocity on a fully settled ball 100 times | Server motion begins by the next physics think every time |
| Drop | Three heights, 10 trials per model | Diagnostic until approved reference bands are frozen; no tunnelling and CV at most 5% |
| Roll | Speeds 200/400/800 along both pitch axes, 10 trials | Diagnostic until approved reference bands are frozen; CV and axis difference at most 5% |
| Walls | Speeds 300/600/1000 at 0/30/45 degrees | Diagnostic until approved reference bands are frozen; no penetration and angle spread at most 5 degrees |
| Corners/posts | 100 mixed impacts | No escape, invalid entity, or unrecoverable stuck state |
| Sleep/wake | 100 settle-then-kick cycles | 100 successful wake-ups |
| Kick validity | 100 accepted plus 100 per rejection category | Exact accept/reject outcome; no through-wall kick |
| Speed cap | Repeated pass/shot/lob inputs | Observed server speed never exceeds cap by more than 1% |
| Goals | 100 crossings per side at varied positions/speeds | Exact count, no duplicate or missed goal |
| Near misses | 100 post/crossbar/outside-aperture paths | Zero goals |
| Wrong activator | 100 player and stray-prop trigger contacts | Zero goals |
| Reverse crossing | 100 goal-to-field crossings | Zero goals |
| Reset | 100 goal resets and 100 moving/admin resets | One ball at transform within 0.5 unit, zero stale motion, next kick works |
| Lifecycle | Reconnect, team change, round/map/server restart, script reload | No stale state, missing ball, or duplicate handler |
| Network | Remote clients at 30/60/100 ms; 1% loss and short 3% stress | No wrong goal or persistent freeze; the positional-correction gate below must pass |
| Load/soak | 12 players and 60 continuous minutes | No exception, entity growth, duplicate score, or unrecoverable state |
| Clean delivery | Private Workshop package on a client without project files | No missing map, model, material, sound, or script |

Server telemetry cannot by itself prove client-rendered smoothness. Let `S(t)`
be the server ball center and `C(t)` the client-rendered center after measured
clock/interpolation alignment `d`; positional error is
`e(t) = ||C(t) - S(t-d)||`. Capture both positions at at least 20 Hz and record
the candidate's compiled collision diameter `D`. Fail if `e(t) > D` persists
for more than 250 ms, or if an adjacent-sample client correction exceeds `D`
without a corresponding server displacement. The suite cannot pass from video
judgment alone: synchronized positional capture or a separately calibrated
tracking method must first be proven. Until then, network video is qualitative
evidence and this gate remains blocked.

## Physics response bands

Repeatability alone can approve a consistently useless ball. Before drop, roll,
wall, and kick-response suites become pass/fail gates, first run prerequisite
`CSF-CAP-001`: on the unmodified compiled Stadium, demonstrate repeatable
control of exact drop transforms and initial velocities plus per-think capture
of server position and velocity for ten repeated trials. The control/capture
mechanism, raw output, build, and package hash must be retained. Only if that
experiment passes may quantitative CSF bands be frozen for:

- first-bounce height and rebound-speed ratio;
- minimum/maximum roll displacement and stopping time;
- wall rebound-speed retention and normal-angle error;
- minimum next-think and short-window displacement after a kick;
- sleep time, successful wake time, and maximum correction/freeze duration.

If `CSF-CAP-001` fails, the compiled Stadium remains a qualitative/manual native
baseline. Quantitative bands must instead come from an instrumentable CSS
reference, permitted Stadium source, or another explicitly approved reference;
until one exists, candidate suites report repeatability and hard failures such
as tunnelling but cannot declare CSS/CSF-like feel. Controlled suites compare
project-owned candidates at both native dimensions and equal diameter; they do
not assume the Stadium resource can be copied or mounted.

## Candidate order

1. Record the compiled Stadium's native 20-hull ball manually as the reference;
   do not copy or cross-mount its resource into our addon yet.
2. Test `prop_physics_multiplayer` in our lab with one project-owned simple
   spherical convex hull.
3. Test another project-owned spherical hull at controlled equal diameter.
4. Test `prop_physics` with the same owned models.
5. Use `prop_physics_override` only as a diagnostic fallback.
6. Try a server-controlled kinematic sphere using official traces/movement APIs if
   documented VPhysics variants fail.

Native hooks are not part of this matrix. If all official approaches fail, the
phase stops and presents the collected evidence before any native helper is
proposed.

## Human parity check

After objective tests pass, record comparable drop, roll, wall, pass, shot, and
lob scenarios in CSS and CS2 using matched starting geometry. Before calling a
kick scenario comparable, document which CSS knife input/action maps to each
CS2 pass, shot, or lob; Source 1 provides no portable three-force formula. A
blind A/B play review evaluates ball scale, response, rolling, bounce, close
control, high-ping play, and predictability. Phase 1 needs both measured
stability and human acceptance; neither substitutes for the other.

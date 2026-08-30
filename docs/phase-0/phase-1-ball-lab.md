# Phase 1 ball-lab specification

Phase 1 answers one question: **can CS2 provide a server-authoritative soccer
ball that is fun, repeatable, and maintainable enough to justify the full mod?**

The subscribed CSF Football Stadium is the geometric and behavioral baseline.
The executable harness remains a writable grey-box lab because the received
Stadium package is compiled-only and contains no `point_script`; see the
[Stadium audit](../phase-1/csf-football-stadium-audit.md).

## Build contents

- Flat regulation test surface with measured grid markers.
- Four wall/corner materials, a shallow ramp, goal mouth, thick goal trigger,
  reset pit, and out-of-bounds recovery volume.
- One ball `point_template` with candidate physics entities:
  `prop_physics`, multiplayer variant where available, and override variant.
- Several ball model/physics configurations with spherical convex collision,
  explicit mass, surface friction, and elasticity.
- A `cs_script` controller with test-state reset, telemetry, kick validation,
  touch history, speed clamp, goal detection, and failure logging.
- Minimal world-space or map HUD labels showing ball position/velocity, last
  touch, goal count, test ID, and server build/API fingerprint.

## CSS reference targets

These are comparison anchors, not assumptions that Source 1 units map perfectly
to Source 2:

- CSS ball diameter: approximately 30 units.
- CSS ball center at reset: `0 0 17`.
- Reference goal plane: approximately 208 units wide and 80 units high.
- Reference team capacity: at least 16 spawns per side.
- Preserve player-relative ball size, pitch proportions, goal proportions, and
  broad rolling/bounce character before tuning exact numerical values.

## Test sequence

### A. Passive physics

For every candidate entity/model configuration:

1. Drop from fixed heights onto the pitch and ramp.
2. Roll from fixed initial velocities along two axes.
3. Strike flat walls, inside/outside corners, posts, and crossbar.
4. Allow the ball to sleep, then wake it through player contact and scripted
   kick.
5. Record stopping distance, bounce height, rebound angle, maximum velocity,
   and any client-visible divergence.

### B. Explicit kicking

Implement the single CSS-style primary/left-click knife kick using the current
documented `cs_script` callback. Secondary attack and lob are not product
controls. Every accepted kick must:

- originate from a living eligible player during an allowed state;
- pass distance, aim-cone, line-of-sight, cooldown, and team-state checks;
- reject through-wall and remote attempts;
- apply a bounded deterministic linear/angular velocity change;
- record player, team, server time, pre/post velocity, distance, and kick type.

Tune numbers only after the validation and replication path works.

### C. Goals and resets

- Use a thick physics trigger filtered to the authoritative ball.
- Independently check whether the ball's movement segment crosses the goal plane
  inside the mouth. This protects against high-speed trigger tunnelling.
- Debounce on a monotonic goal sequence ID.
- On goal/reset, zero linear and angular velocity, move the ball to the exact
  reset transform, clear transient touch state, and start a controlled kickoff.

### D. Multiplayer and lifecycle

Run at minimum:

- one local diagnostic client;
- two remote clients at approximately 30, 60, and 100 ms latency;
- modest packet loss (target 1%, with a short 3% stress run);
- twelve connected players or representative bots/load where bots exercise the
  same relevant collision path;
- reconnect, team change, round restart, map restart, and server restart;
- CSTV recording if present, while keeping CSTV outside the pass criterion;
- a continuous 60-minute soak after functional tests pass.

## Objective pass criteria

- 100 consecutive scripted/manual goal-plane crossings: exactly 100 goals, no
  duplicates and no false player/prop goals.
- 100 consecutive resets: correct transform, zero stale velocity, no duplicate
  ball, no retained goal debounce, and no stuck/sleep failure on the next kick.
- Remote/through-wall kick attempts are rejected in every recorded validation
  case.
- The ball never permanently escapes or becomes unrecoverable during the wall,
  corner, ramp, twelve-player, and soak suites.
- Kick velocity changes wake and replicate to remote clients without persistent
  disagreement about ball position or goal outcome.
- No unbounded entity growth, repeating error spam, script exception, or server
  crash across restart/reconnect/soak tests.
- A clean client with no project files can join the private Workshop map and has
  every required asset.
- Human A/B review against recorded CSS play accepts the ball's size, response,
  rolling, bounce, and latency feel. Objective telemetry accompanies the review;
  subjective approval alone is not enough.

## Fallback order

1. Tune official VPhysics entity and ball physics asset.
2. Try another documented physics-prop variant.
3. Prototype one server-controlled kinematic sphere using official traces and
   movement APIs.
4. If all official approaches fail, stop and present evidence before proposing
   a narrow native helper.

## Gate result

Passing Phase 1 authorizes the playable MVP architecture, not the entire parity
backlog. Failing both official approaches means no-go or a consciously accepted
high-maintenance native research project.

# Phase 1 telemetry contract

Contract version: `cs2-soccermod.balllab/1`

This is the normative adapter boundary. It remains a design contract until the
current Valve declaration is installed and the engine adapter exists; Phase 1
API smoke cannot pass until emitted fixtures are schema-tested against it.

## Common envelope

Every `[SM2LAB]` JSON object has exactly these top-level fields:

| Field | Rule |
|---|---|
| `schema` | constant string `cs2-soccermod.balllab/1` |
| `runId` | nonempty string, unchanged for the run |
| `seq` | positive safe integer, increasing by exactly one |
| `event` | one required event name from the protocol |
| `testId` | nonempty string, or `null` only outside a test |
| `candidateId` | nonempty string, or `null` before `ball_bind` |
| `serverTime` | finite number at least zero, in seconds |
| `thinkSeq` | safe nonnegative integer |
| `ballGeneration` | positive safe integer, or `null` before binding |
| `resetSequence` | safe nonnegative integer, or `null` before binding |
| `goalSequence` | safe nonnegative integer, or `null` before binding |
| `data` | object following the event rule; never a string or `null` |

No numeric field accepts `NaN`, infinity, or a numeric string. Vectors are
objects with exactly finite numeric `x`, `y`, and `z`. Optional measurements use
explicit `null`; absence is permitted only where a row below says optional.

## Core-result events

| Event | Required `data` fields |
|---|---|
| `state_sample` | `source:"server"`, `position` vector, `velocity` vector, `speed` number >= 0, `asleep` boolean or `null`, `angularMotionZero` boolean or `null` |
| `client_state_sample` | `source:"client_render"`, nonempty `clientId` and `alignmentId`, safe nonnegative `renderFrameSeq`, finite nonnegative `clientTime`, and rendered-center `position` vector |
| `clock_alignment` | nonempty `clientId` and `alignmentId`; `method` (`engine_clock`, `shared_marker`, or `cross_correlation`); finite `clientToServerOffsetSeconds`; finite nonnegative `interpolationDelaySeconds` and `residualErrorMs`; positive integer `sampleCount` |
| `kick_attempt` | `playerId` string, `attackType` string, `isDucking` boolean, `eyePosition` vector, `eyeAngles` finite `{pitch,yaw,roll}` |
| `kick_result` | `accepted` boolean, `reason` kick enum, active `kind` is `pass` for the single primary/left-click kick or `null` when rejected; historical diagnostic logs may contain retired `shot`/`lob` values; `distance`, `aimDot`, `velocity`, `unclampedSpeed`, `finalSpeed`, `maximumBallSpeed`, and `wasClamped` are finite/boolean values when accepted and otherwise `null`; `writeAngularVelocity` must be `false` in Phase 1 |
| `goal_candidate` | `crossed` boolean, `reason` crossing enum, `goalId` string or `null`, `scoringTeam` 2, 3, or `null`, `fraction`, `lateral`, and `height` finite numbers or `null`, plus `previousThinkSeq` and `currentThinkSeq` safe integers |
| `goal_commit` | `accepted:true`, `reason:"accepted"`, `goalId` string, `scoringTeam` 2 or 3 |
| `goal_ignored` | `accepted:false`, `reason` goal-accept enum, `goalId` string or `null` |
| `reset_begin` | `commandSequence` positive integer, `position` vector, `restPosition` vector, `angles` finite `{pitch,yaw,roll}`, `zeroLinearVelocity:true`, `zeroAngularVelocity:true` |
| `reset_write_verify` | `stage:"write"`, `passed` boolean, `reasons` reset enum array, `positionError`, `angleError`, and `speed` finite nonnegative numbers or `null`, `angularMotionZero` boolean or `null`, `sampleThinkSeq` safe integer or `null` |
| `reset_settle_verify` | `stage:"settled"`, `passed` boolean, `reasons` reset enum array, `positionError` and `speed` finite nonnegative numbers or `null`, `angularMotionZero` boolean or `null`, `sampleThinkSeq` safe integer or `null` |
| `speed_clamp` | `beforeSpeed` and `afterSpeed` finite nonnegative numbers, `cap` finite positive number |

## Lifecycle events

| Event | Required `data` fields |
|---|---|
| `run_start` | `mode` (`listen` or `dedicated`), nonempty `mapName`, decimal-string `cs2BuildId`, nonempty `clientVersion`, `serverVersion`, and `sourceRevision`, 64-character lowercase SHA-256 strings `mapPackageSha256` and `pointScriptApiSha256`, nonempty `scriptRevision`, positive finite `thinkCadenceHz`, nonnegative integer `connectedClients`, finite nonnegative `configuredLatencyMs` and `configuredLossPercent` |
| `run_end` | `status` (`passed`, `failed`, `blocked`, or `aborted`), `reason` run-end enum, finite nonnegative `durationSeconds`, positive integer `summarySeq` |
| `ball_bind` | `reason:"accepted"`, nonempty `entityId`, `targetName`, `model`, `modelSha256`, and `collisionVariant`; `entityClass` (`prop_physics_multiplayer`, `prop_physics`, or `prop_physics_override`); `boundsMin`, `boundsMax`, `spawnPosition`, and `restPosition` vectors; `surfaceProperty` string or `null`; `effectiveMass`, `linearDamping`, and `angularDamping` finite nonnegative numbers or `null` |
| `ball_invalid` | `reason` ball-invalid enum, `entityId` string or `null` |
| `duplicate_ball` | `reason:"multiple_balls"`, integer `matchCount` >= 2, and `entityIds` array of exactly `matchCount` distinct nonempty strings |
| `test_start` | `suite` test-suite enum, positive integer `attempt`, nonempty `parameterSetId`, 64-character lowercase `parameterSetSha256` |
| `test_end` | `suite` test-suite enum, positive integer `attempt`, `status` (`passed`, `failed`, `diagnostic`, `blocked`, or `aborted`), `reason` test-end enum, nonnegative integer `assertionsPassed` and `assertionsFailed` |
| `trigger_enter` | nonempty `triggerId`, `activatorId`, and `activatorClass`; `activatorTargetName` nonempty string or `null` |
| `reset_end` | `passed` boolean and `reason` reset-end enum |
| `assertion` | nonempty `assertionId`, `passed` boolean, `reason` assertion enum, and discriminated `actual` and `expected` values as defined below |
| `script_exception` | stable lowercase-snake-case `code`, UTF-8 `message` of 1 to 512 characters, and nonempty `sourceEvent` event name |
| `run_summary` | nonnegative integers `eventsBeforeSummary`, `testsPassed`, `testsFailed`, `testsDiagnostic`, `testsBlocked`, `assertionsPassed`, `assertionsFailed`, `exceptionCount`, `duplicateGoalCount`, and `duplicateBallCount`; `status` (`passed`, `failed`, `blocked`, or `aborted`) |

`assertion.actual` and `assertion.expected` each have exactly `kind` and `value`.
`kind` is `number`, `boolean`, `string`, `vector`, or `null`; `value` must match
that kind and must itself be `null` for kind `null`. Arbitrary nested objects are
not allowed. Parameter sets live in versioned test data; telemetry identifies
their bytes by ID and SHA-256 instead of embedding an untyped config object.

Test-suite enum:

```text
csf_capture
api_smoke
knife_callback
wake_write
drop
roll
walls
corners_posts
sleep_wake
kick_validity
speed_cap
goals
near_misses
wrong_activator
reverse_crossing
reset
lifecycle
network
load_soak
clean_delivery
```

## Exact reason enums

`kick_result.reason`:

```text
accepted
invalid_config
invalid_input
player_not_alive
player_ineligible
play_disabled
invalid_vector
invalid_time
unsupported_attack
cooldown
out_of_reach
outside_aim_cone
obstructed
invalid_aim_direction
```

`goal_candidate.reason`:

```text
crossed
invalid_vector
invalid_goal
invalid_context
invalid_number
parallel
no_forward_crossing
outside_segment
outside_width
outside_height
```

`goal_ignored.reason`:

```text
invalid_state
invalid_candidate
stale_candidate
goal_locked
```

Reset verification reason entries:

```text
invalid_command
write_not_verified
invalid_tolerance
invalid_observation
ball_count
ball_generation
reset_sequence
stale_sample
not_settled
invalid_vector
position
angles
velocity
angular_motion
```

Reset unlock results use exactly `unlocked`, `invalid_state`,
`reset_not_verified`, `stale_reset`, or `ball_generation`. Ball replacement
uses exactly `replaced`, `invalid_state`, or `invalid_ball_generation`.

Lifecycle enums:

```text
ball_invalid: missing_ball | multiple_balls | wrong_class | missing_model |
              invalid_transform | invalid_velocity | stale_generation |
              entity_destroyed
test_end: completed | assertion_failed | precondition_blocked |
          script_exception | aborted
run_end: completed | test_failed | precondition_blocked | script_exception |
         aborted
reset_end: settled | write_not_verified | not_settled | ball_invalid | aborted
assertion: matched | mismatch | missing_measurement | invalid_measurement |
           precondition_blocked
```

## Validation gate

Before an adapter run is accepted, tests must parse every emitted line, reject
unknown top-level fields/events/reasons, enforce counter monotonicity and
generation/reset relationships, and verify that `run_summary` counts equal the
events observed in that run. A schema version change requires a new contract
identifier rather than silently widening this one.

## Diagnostic probe channel

The temporary physical-input and velocity-write spike uses a separate prefix
and schema:

```text
[SM2PROBE] {"schema":"cs2-soccermod.diagnostic-probe/1",...}
```

This is an intentionally non-frozen diagnostic stream. Its input/write events
are `probe_configuration`, `player_status`, `switch_requested`,
`switch_rejected`, `input_edge`, `knife_callback`, `kick_write_dispatched`, and
`kick_write_observation`. Reset experiments can additionally emit
`reset_write_retry`, `goal_reset_profile_configuration`,
`goal_reset_profile_applied`, `reset_physics_snapshot`,
`reset_post_terminal_sample`, `reset_post_terminal_complete`, and
`reset_post_terminal_cancelled`. Automated physics diagnostics additionally
use `physics_trial_configuration`, `physics_trial_begin`,
`physics_trial_sample`, `physics_trial_end`, `physics_trial_run_end`, and
`physics_trial_run_cancelled`. The fixed reset-motion comparison uses
`reset_motion_profile_configuration`.

These records are factual diagnostics, not normative test assertions: they do
not carry a `passed` result, are never included in `[SM2LAB]`
assertion/run-summary counts, and cannot by themselves qualify a Phase 1 suite.
The fixed `sm2lab_goal_reset_profile` values are `contact` and
`radius_clearance`; arbitrary offsets are rejected. The latter applies only to
goal resets and writes the ball center one nominal radius above the contact
rest transform. Activation, reload, round, rebind, and console/manual resets
remain at the contact transform. Selecting either value explicitly enables the
temporary reset diagnostic for future goal resets; a correlated cycle is
diagnostic-only even when `contact` is selected.

The fixed reset-motion values are `teleport_only` and `disable_motion`.
Arbitrary inputs are rejected. `disable_motion` invokes the Hammer-FGD
documented `DisableMotion` input before the unchanged zero-motion Teleport and
leaves motion disabled at verified rest; a kick or physics trial invokes the
documented `EnableMotion` input before `Wake` and its bounded velocity
Teleport. This candidate does not relax transform, speed, or exact-angular-zero
verification. After its strict eight-sample terminal tail plus loose-script,
round-restart, and current-map-reload checks passed, `disable_motion` became the
loose-runtime default. `teleport_only` remains selectable as the regression
control.

Every physics-trial run records one accepted fixed configuration, one begin
and end per trial, at least one correlated sample per begun trial, and exactly
one run end. Samples carry authoritative-ball, ball-generation, and reset-
sequence correlation booleans. End records carry fixed-profile qualification,
sample count, elapsed thinks/time, displacement/speed extrema, the terminal
snapshot, and only the mode-specific finite metrics that were observed. The
runner fails closed on missing/duplicate indexes, lost correlation, a hard
failure, non-passing cleanup, malformed prefixed JSON, timeout, or repeated-
metric CV above 5%.

`reset_physics_snapshot` records `before_write`, `immediate_after_write`, and
`next_think` observations for each of at most two writes. Position, linear
velocity, and the raw `RotationVector` are copied as plain data. Angular units
are labelled `not_declared_by_point_script_api`; no radians/second or
degrees/second interpretation is claimed. The guarded ground descriptor is
only a raw `none`, `invalid`, `valid`, or `read_error` observation. It does not
prove contact causality or stable entity identity, and a script think is not
claimed to be a physics tick. Measurements are buffered during the reset so
console output does not occur between the pre-write observation and Teleport.
After terminal state, at most eight further script-think samples are captured;
a new reset, kick write, invalid target, or changed ball correlation cancels
the bounded tail.

A reset that needs `reset_write_retry` is a diagnostic recovery, not a
qualified reset pass: the smoke stream retains the failed first
`reset_write_verify` as well as the final attempt, and any failed write attempt
excludes that cycle from formal pass counts. In particular,
`kick_write_dispatched` means the documented Wake and Teleport calls returned;
only the correlated next-script-think observation reports the raw entity state
that followed.

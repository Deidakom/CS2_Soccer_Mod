# Phase 1 status

Date: 2026-08-27

Status: **the official-script ball/kick/goal loop, strict disable-motion reset,
automated local goal matrix, and core reload lifecycle are live-proven; full
Phase 1 qualification remains open**

Control scope was corrected on 2026-08-28: the product has one CSS-style
primary/left-click kick. Any secondary-shot evidence below is retained only as
historical API diagnostics and is not an MVP/parity requirement.

## Current checkpoint

This checkpoint supersedes the older execution narrative and evidence matrix
below.

- CS2 build and target are `24957633`. The installed `point_script.d.ts` raw
  SHA-256 is
  `2da5d7d10ffcea1aac52e668cf153974a3d973aeb8e7dc9a15fb8a2227b50bf9`.
  Its change from the prior audited build is comment-only; normalized
  declarations are byte-identical.
- The repository suite passes **70/70** tests. It covers the strict reset core,
  the sole-angular two-write ceiling, mixed-failure no-retry behavior, exact
  tiny nonzero angular values, fixed contact/one-radius goal profiles, guarded
  ground reads, buffered write snapshots, and an exactly eight-think terminal
  trace with no third write. It also includes a fail-closed live-log parser and
  correlation validator for exact callback, kick, goal, reset, snapshot, and
  terminal-tail counts, plus a bounded live-gate tracker that enforces attack,
  profile, correlation, first-write, tail, and count ceilings while the run is
  active.
- The current reproducible packaged map is the 2026-08-27 10:51 Fast Hammer
  build. Its installed VPK is 704,728 bytes, MD5
  `aa0937299d6b39024017ab32d5415ba7`, and SHA-256
  `f29fa6b00921530b90b2516382dd17faca3a2c8bf6819d3d78908e017d8b7752`.
  It contains the 67,425-byte bundled adapter SHA-256
  `1068b8c6641ec23cce092b1cdf6ea997b6d91e88b098e39a846064a1fba7328b`;
  Workshop Tools compiled that source to a 68,273-byte `adapter.vjs_c`
  SHA-256
  `51aeedfa590477a724a663f83fb4e91e063c6512dfa2355ac760333e653ec3a`.
- A controlled primary click produced one official knife callback, one
  accepted pass, one persisted server velocity of approximately
  `(525.0944, 0, 50.8451)`, eventual real motion, and exactly one east-goal
  commit about 0.316 seconds later. The first script observation retained the
  written velocity but had no displacement, so next-*physics*-tick motion is
  not claimed.
- The ensuing goal reset failed solely on exact angular zero on attempt 1 and
  again after the one permitted immediate retry. Attempt 1's angle error was
  about `5.08e-9`; attempt 2 still exposed nonzero angular state around
  `7.65e-40`. Both attempts retained valid transform/linear state, no third
  write occurred, and play remained locked. This disproves the identical
  immediate rewrite for that moving-goal case; it does not prove the cause or
  rule out delayed/contact-aware strategies.
- The current diagnostic revision adds raw uncalibrated angular and
  guarded ground snapshots plus fixed `contact` and `radius_clearance` goal
  profiles. Activation, reload, round, rebind, and console/manual resets stay
  at Z15; only the selected radius-clearance goal reset writes at Z30 and still
  requires rest at Z15. It preserves the original deadline, strict tolerances,
  exact-zero rule, maximum two writes, and fail-closed unlock. This revision is
  a deterministic 67,425-byte bundle with SHA-256
  `1068b8c6641ec23cce092b1cdf6ea997b6d91e88b098e39a846064a1fba7328b`
  and source-manifest SHA-256
  `35fc5cc2b735b36d156e574fd5142b378131c6bc1c8fe41d3e7abf1cea2a4843`.
  It has now been staged, compiled, loaded, and live-tested against the exact
  package fingerprints above.
- In the controlled A/B trial, `contact` passed write and settle verification
  on its first write and retained exact zero angular motion through all eight
  terminal-tail samples. `radius_clearance` wrote Z30 exactly and zeroed both
  velocity vectors immediately, but gravity produced Z `29.8047256` and
  downward speed `12.4975214` on the next think. It therefore failed solely on
  `velocity`, performed no ineligible retry, and locked play as designed.
- After recovery, ten identical contact-profile primary cycles produced
  exactly 10 knife callbacks, 10 accepted kicks, 10 goal commits, and 10
  first-write settled resets. Across 80 terminal-tail samples there was no
  nonzero angular motion. This rejects radius clearance under the current
  contract and supports contact as the baseline, but it does not erase the
  earlier isolated angular failure or satisfy the formal 100-reset gate. Full
  evidence is in
  [goal-reset-profile-comparison-2026-08-27.md](goal-reset-profile-comparison-2026-08-27.md).
- A later manually issued secondary click was captured end to end and passed
  the validator: one secondary callback/edge, one accepted `shot` with command
  velocity `(900, 0, 85.0000000241)`, one east-goal commit, and one contact
  reset settled on write 1. Reset sequence 24 retained exact zero angular
  motion across its next-think snapshot and all eight terminal-tail samples.
- The bounded formal runner was then exercised with real primary input. Its
  final retained partial soak passed the correlation analyzer at 10/10: 10
  knife callbacks and matching input edges, 10 accepted kicks and goal commits,
  10 first-write reset passes, 10 settle passes, 30 correlated reset snapshots,
  80 terminal samples, and 10 terminal completions. It recorded zero retries,
  zero failed writes/settles, and maximum terminal angular magnitude `0`; the
  minimum input interval was `1.640625` seconds. The operator deliberately
  ended the manual soak after 10 cycles, so this is development evidence, not
  a completed 100-cycle qualification. Raw evidence and the explicit partial
  analysis are in `artifacts/phase1-live/2026-08-27-primary-partial-soak-10.*`.
- A fixed-profile automated physics runner now removes the need for repeated
  operator clicks during development. The baseline passed 10/10 wake trials
  and 50/50 forward-goal, reverse-crossing, and near-miss trials. Drop and roll
  metrics were repeatable, but moving-ball cleanup reproduced the exact
  `angular_motion` reset failure. The wall trace proved that the template
  fixture is a ramp rather than a qualified vertical wall, and the X/Y roll
  paths are not symmetric enough for an axis conclusion.
- The installed FGD documents physics-prop `DisableMotion`/`EnableMotion`.
  `Sleep` did not clear the residual. In contrast, the
  `disable_motion` candidate passed the unchanged strict reset and restored
  motion after `EnableMotion`/`Wake`. It then passed the formerly failing
  1250-unit speed-cap case 10/10, the corrected 64-unit drop 3/3, and ten
  repeated east-goal freeze/reset/re-enable cycles. No tolerance was relaxed.
  A final goal reset retained exact transform plus zero linear/angular motion
  through all eight content-validated terminal samples. The candidate was then
  promoted to the loose-runtime default.
  Full artifacts and limitations are in
  [automated-physics-diagnostics-2026-08-27.md](automated-physics-diagnostics-2026-08-27.md).
- The current loose diagnostic bundle SHA-256 is
  `0ce4ceff785302961aae2a33d6eb97e0d3e55c400b7798796f5bcd3571db8698`;
  it auto-reloaded under Workshop Tools. Readiness and exact 200-unit wake
  motion passed after that script reload, `mp_restartgame 1`, and the Workshop
  Tools `restart` current-map unload/reload. The map reload reset server time
  and ball generation, ruling out a mere round reset. The generated VMAP
  stayed unchanged. This hash is not yet inside the retained map VPK, so one
  Hammer build remains necessary before packaged qualification.

## Historical execution narrative

Workshop Tools are installed and pinned to CS2 build `24934554`. Hammer's first
full build completed with `18 compiled, 0 failed, 0 skipped` and produced a
712,161-byte local map VPK. The missing detail-prop data, sound-stack attribute,
local signing-key, and KeyValues leak messages did not stop the build. The
overlapping-triangle visibility warning is nonfatal but remains a later geometry
cleanup item.

The first runtime load then produced the decisive error:

```text
Exception during InstantiateModule
Invalid module specifier "./core/goal.js". Relative values are not supported.
```

That failure was not treated as a missing-file guess. The installed server
resolver explicitly rejects relative module specifiers, ResourceCompiler leaves
them intact, and Valve's installed scripts import only
`cs_script/point_script`. Compiling each child file separately cannot fix that
resolver rule.

The staged runtime is now a deterministic, readable single-file bundle. The
modular layout/core/adapter sources remain separate in the repository for unit
tests, while the generated runtime keeps exactly one supported static import:

```text
cs_script/point_script
```

The current pure and bundle suite passes 44/44 tests. The latest corrected
diagnostic source is staged with these hashes:

- generated VMAP: `34f07c7a6eb40ee6f9367929a2cd3d3f61ba8f3250427575338e2c808ff04653`;
- bundled `adapter.js`: `6ee023a824d445673059beb879d9b1b986f0c0f64bd2603bad6c5fe1439f80a4`;
- bundle source manifest: `3bd9e24722fcf2dc6b7cfab918b9448de363ecd7a4db7c5fce6e02b8b298dab9`.

Hammer's second, incremental build packaged an earlier bundled gameplay revision with
`15 compiled, 0 failed, 1 skipped`. It wrote and verified a 704,711-byte VPK
with MD5 `59ada32166b8c3ead72bf80ba69a924b`, unmounted the old live VPK, copied
the new artifact into the game addon, and loaded it. The installed VPK SHA-256
is `947cbea9e2fa904b904f272323847947f00e14a903a86c8a384ad3ed51eb49cf`.
Its compiled map references exactly one script resource, the bundled adapter;
there are no layout/core script references or entries.

The latest user-supplied Hammer run at 13:01 completed with
`15 compiled, 0 failed, 1 skipped` and packaged the physical-input diagnostic
revision. It wrote, verified, copied, and loaded a 704,727-byte VPK with MD5
`fbae4075cabb49c67fba5a5adf7633fc`; the installed SHA-256 is
`7daca5b664b1ad1d0f208c53f980487952dcc1d04331bbccd765bea0fe5c71028`.

After the runtime spike, the probe was separated from smoke assertions and a
correlated next-script-think write observation was added. Workshop Tools has
already compiled that staged 54,778-byte source to a 55,626-byte
`adapter.vjs_c` with SHA-256
`90e36b00776e5eb84e437fd207ee3e4eed5dfb2f0e48d63dd05757751f9269a4`.
Dependency checking reports it current, and its binary contains the separate
input-probe schema plus write-dispatch/observation records. This final
diagnostic revision auto-reloaded as a loose Tools resource and emitted its new
`player_status` record. It still needs one Hammer F9 build before the exact
revision is embedded in the map VPK.

On a fresh map restart, VConsole then proved:

- `run_start` in listen-server mode;
- unique lab geometry and goal markers bound;
- one `prop_physics_multiplayer` ball bound with the expected model and scale;
- activation reset write verification passed;
- activation reset settle verification passed;
- `sm2lab_status` emitted `api_smoke_ready` with `passed:true`;
- `RunScriptInput phase1_smoke` printed `SM2_PHASE1_SMOKE_OK`;
- a later `sm2lab_reset` again passed write and settle verification.

After the 13:01 fresh build/reload, a filtered physical-input spike also proved:

- slot 0 was connected, alive, on team 2, and had a valid knife selected;
- raw primary/secondary input edges reached the player pawn;
- the official `OnKnifeAttack` handler emitted genuine callbacks;
- remote swings were rejected as `out_of_reach` at about 247.03 units;
- a close primary swing was accepted at 33.58 units with line of sight,
  `aimDot` 0.9952, and a finite 529.08-unit/second pass command;
- later close swings were accepted, while one poorly aimed swing was rejected
  as `outside_aim_cone` at `aimDot` 0.3876.

This proves the callback/context and kick-validator paths, but it was not a
controlled one-click trial: a round start changed the scripted pose, multiple
attacks occurred, and the filtered capture omitted the post-write state samples.
Because `kick_result` precedes Wake/Teleport in the adapter, it is not yet proof
that VPhysics moved by the next think. It also does not prove callback counts,
goal accuracy, multiplayer replication, lifecycle stability, clean delivery,
or CSS-like ball feel.

## Historical evidence matrix (superseded by current checkpoint)

| Check | Result | Evidence |
|---|---|---|
| Current CS2 runtime | Pass | App build and target build `24934554` |
| Workshop Tools | Pass | Hammer, ResourceCompiler, DMXConvert, content tree, and Valve samples installed |
| Current `cs_script` declaration | Pass | SHA-256 `dbb8ae95f12c6f513909a527609a8df498ae5bb54a2024445a27537b33d61752` |
| Stadium subscription/reference | Pass | Item `3361075564` installed; frozen package hash still matches |
| Pure core and bundle checks | Pass | 44/44 Node tests, including runtime-bundle instantiation |
| Hammer map compile | Pass | Latest supplied run: `15 compiled, 0 failed, 1 skipped`; VPK written, verified, copied, and loaded |
| Relative-import spike | Failed, fixed | Exact resolver exception captured; runtime replaced by one-file bundle |
| Script activation | Pass | `[SM2LAB] run_start`, geometry bind, and ball bind emitted after restart |
| API smoke input/command | Pass | `api_smoke_ready passed:true` and `SM2_PHASE1_SMOKE_OK` |
| Atomic reset smoke | Pass | Both automatic and console resets passed write and settle stages |
| Revised packaged VPK | Pass | 704,727 bytes; SHA-256 `7daca5b664b1ad1d0f208c53f980487952dcc1d04331bbccd765bea0fe5c71028`; copied and live-loaded |
| Physical knife callback spike | Exploratory pass | Raw attack edges, official callback, accepted pass, reach rejection, and aim-cone rejection observed |
| Wake/write observation | Not run | Corrected probe is staged, compiled, and loose-loaded; no physical attack arrived during the two controlled capture windows |
| Formal kick, wake, goal, physics suites | Not run | Requires controlled execution, exact counts, and retained telemetry |
| Remote/dedicated qualification | Not run | Follows local physics and lifecycle gates |

## Stadium limitation and decision

The subscribed **CSF Football Stadium** package is the approved behavior and
geometry reference, but it is not the executable map currently under test. The
download contains compiled VPK resources and no editable `.vmap`; its map has
no `point_script` bootstrap. A second ordinary addon cannot merge our entity
lump into it, and cross-addon script auto-start is not assumed.

The live Phase 1 harness is therefore a generated derivative of Valve's
writable addon template. It contains no CSF/Jabulani/Stadium dependency. Its
X-axis goal layout and scaled Dust ball are deliberately lab-specific and may
not be transferred to the Stadium.

Literal Stadium integration needs one of these separately approved inputs:

1. editable source plus the author's permission, followed by Stadium-specific
   measurement and rebuilding; or
2. a server-plugin architecture that attaches to or spawns into the immutable
   compiled map, with its own compatibility and packaging qualification.

Until one route exists, describing the current lab as "based on CSF Football
Stadium" would be inaccurate.

## Prepared implementation

- `src/ball-lab/engine/adapter.js` binds one authoritative ball, validates
  knife kicks through the tested core, performs server traces, writes bounded
  velocity, evaluates goal-plane crossings, and verifies atomic resets.
- `src/ball-lab/layout.js` is the shared source of truth for the template-lab
  reset marker and virtual goal planes.
- `tools/bundle-phase1-adapter.mjs` builds a self-contained runtime while
  rejecting unmanaged imports or export forms and recording a source-manifest
  hash.
- `tools/generate-phase1-vmap.mjs` now declares only the bundled adapter as a
  script asset.
- `tools/stage-phase1-addon.ps1` syntax-checks and bundles before staging,
  round-trips the VMAP through Valve's parser, verifies hashes, and refuses to
  overwrite changed or unmanaged files.
- `tools/run-phase1-live-gate.mjs` runs a bounded physical-input qualification
  capture, stops at the exact target or first disqualifying record, writes
  filtered evidence plus a JSON summary, and invokes the post-run correlation
  analyzer. The exact operator procedure is in
  [formal-live-gate-procedure.md](formal-live-gate-procedure.md).

## Exact next execution

1. Keep `contact` as the formal baseline. Retain `radius_clearance` only as a
   diagnostic counterexample; do not relax velocity tolerances or add a retry
   for its gravity-driven fall.
2. Use the bounded live-gate runner and post-run validator for every repeated
   trial so exact callback, kick, goal, reset, and tail counts do not depend on
   manual console review. Its unit and menu-state fail-closed smoke gates pass.
3. The machine-validated 10/10 primary partial soak is complete and is
   sufficient to continue implementation; do not require the operator to
   perform hundreds of manual clicks. Retain one primary endurance gate as
   pre-release qualification, preferably with an approved deterministic input
   driver. Preserve the first
   correlated failure in full if angular motion reappears; do not auto-recover
   over evidence.
4. The `disable_motion` terminal-tail and core lifecycle checks are complete.
   Add a flat symmetric roll lane and a qualified vertical wall to the writable
   lab before finishing roll/rebound bands. The repeated wake/write,
   high-speed goal, reverse, near-miss, speed-cap, and first drop diagnostics
   are automated and retained.
5. Compile the promoted loose default into a retained VPK, then rerun packaged
   smoke and clean-start checks.
6. Only after the single-client physics gates pass, proceed to multiplayer
   replication, lifecycle/reload, private-Workshop clean delivery, and soak.

Phase 1 remains open until the full physics, goal/reset count, multiplayer,
lifecycle, clean-delivery, and soak gates in the test protocol pass.

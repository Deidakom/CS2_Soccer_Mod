# Implementation plan: closing the gap to the CS:S XSL B1 ball

Date: 2026-08-29. Written as a self-contained handoff — everything needed to
execute is in this file. Prior evidence:
`docs/ball-foundation/2026-08-29-hull-compiler-root-cause.md`.

---

## 1. Target definition (measured, not guessed)

From `artifacts/css-reference/*.log` (server-side CS:S telemetry of the real
XSL B1 ball) plus the user's decisions:

| # | Property | CS:S measurement | Status in CS2 today |
|---|---|---|---|
| T1 | Roll decay | two regimes: λ≈0.6/s above ~65 u/s, then coasts at ~30 u/s "forever" (924u travelled, still moving at 19.9s) | ✅ reproduced by compound hull (see §2) |
| T2 | Bounce | restitution ≈0.16–0.18 (6.3u apex from 244u drop) | ⚠️ ~12u, but **user accepts it** — leave it |
| T3 | Kick | impulse ≈1359 u/s at ≈10.6° above aim; ball's own momentum survives | ✅ delta-velocity kick, power tunable live |
| T4 | Wall | strong energy loss (−334 → +60 u/s planar) **and vertical hop +43 u/s** ("hochbuggen") | ❌ hop missing — THE open gap |
| T5 | Spin/curve | rolls drift sideways 216u over 19s; spin causes hop and curve | ❌ kick imparts zero spin |
| T6 | Size | user wants CSF size: 37.61u diameter (larger than true XSL 28.96u — user's explicit call) | ✅ live (`large1850`) |
| T7 | Mass/gravity | 60.694092 kg, gravity 1.0 | ✅ compiled into model |

**Non-negotiables (user):** left-click knife kick only, body push, wall
hochbuggen, no lob/menu mechanics, no return to the v2 analytic position
controller.

**Everything still missing traces to one thing: the kick imparts no spin.**
Spin is what produces the wall hop (a ball with topspin rolling into a wall has
its contact surface moving downward against the wall; friction pushes it up)
and the sideways curve. Fix spin and T4+T5 follow.

## 2. Architecture decision (settled — do not relitigate)

**Keep:** native `prop_physics_multiplayer` on a compound collision model:
80-face geodesic hull + slightly smaller inner sphere, `surface_prop weapon`,
`mass_override 60.694092`, damping 0. This is the only architecture that
reproduces T1's two-regime decay (facets shed energy at speed; the inner
sphere lowers the rolling barrier so the ball coasts instead of dying at
~40 u/s). Verified constraints that must not be violated:

- Source 2's hull cooker silently corrupts hulls >80 faces. Never exceed 80.
  Always verify a compiled model with
  `Source2Viewer-CLI -i <vmdl_c> --block PHYS` (check `m_flVolume`, `m_Bounds`,
  `m_vCentroid`, `m_flMass`).
- The entity `elasticity` field is **inert** for MOVETYPE_VPHYSICS props
  (measured: 0.05/0.20/0.95 → identical bounce). Restitution comes only from
  the compiled surface property, and stock CS2 has nothing between 0.30
  (bounce=0) and 0.95 (`weapon`, bounce≈12u). Since the user accepts the
  current bounce, do not chase a custom surfaceproperties file now.
- `mass_override` in the vmdl is required; without it the compiler emits
  `m_flMass = 0`.

**Retire once §3 verifies:** the `phys_thruster` kick path
(`ApplyThrusterKick`, `KickMode.Thruster`, `css_sm2ball_thrust`,
`css_sm2ball_kickmode`). Measured behaviour was unusable: the `force` keyvalue
had no effect (scale 1/5/20 identical), `forcetime` dominated nonlinearly
(0.05s → ~15 u/s, 0.20s → 583 u/s, 0.50s → nothing). Do not build on it.

## 3. Phase A — verify the two engine inputs (do this first, ~30 min)

`server.dll` contains the strings `ApplyAbsVelocityImpulse` and
`ApplyLocalAngularVelocityImpulse` (the classic Source CBaseEntity inputs; not
listed in the FGD, which omits base-entity inputs). If they work via
`AcceptInput`, they are the *exact* mechanism CS:S-era plugins used and replace
both the Teleport-velocity hack and the thruster.

Add two throwaway RCON commands to the plugin
(`src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.cs`; commands are
registered with `AddCommand`, guarded by `RequireServerConsole`):

```csharp
// css_sm2ball_impulse_input <x> <y> <z>
_ball.AcceptInput("ApplyAbsVelocityImpulse", value: $"{x} {y} {z}");
// css_sm2ball_spin_input <x> <y> <z>   (deg/s, local axes)
_ball.AcceptInput("ApplyLocalAngularVelocityImpulse", value: $"{x} {y} {z}");
```

Wake the ball first (`_ball.AcceptInput("Wake")`). Test over RCON:

```
css_sm2ball_reset_center
css_sm2ball_impulse_input 500 0 0     # expect: ball moves +X ~500 u/s
css_sm2ball_reset_center
css_sm2ball_spin_input 0 1800 0       # expect: ball spins, starts rolling +X
```

Read the result from `css_sm2ball_status` (`origin`, `derivedSpeed`) a second
apart, or run `css_sm2ball_trial roll 1` right after to sample positions.
If the value string `"x y z"` is rejected, try `"x,y,z"`.

**Outcome A-pass:** both inputs move/spin the ball → proceed to §4.
**Outcome A-fail:** inputs are dead in CS2 → keep Teleport-velocity for linear
(it works and is exact) and get spin from a torque-only thruster
(`phys_thruster` spawnflags `1|4` = Start On + Apply Torque, no Apply Force,
placed off-centre) or `phys_torque`. Calibrate empirically with the §5 trials;
expect the same forcetime weirdness, so prefer short bursts (0.10–0.20s) and
tune by measurement, not by formula.

## 4. Phase B — kick with spin

On an accepted kick (`TryApplyPrimaryKnifeKick`), after computing
`finalVelocity` and `launchDirection`:

1. Linear: `ApplyAbsVelocityImpulse` with the *delta* (i.e. what the current
   code adds on top of inherited velocity). Drop the Teleport call.
2. Angular: topspin about the horizontal axis perpendicular to the launch
   direction. For launch yaw ψ the axis is `(-sin ψ, cos ψ, 0)` scaled to
   rolling rate: `ω = k · v / r`, with `v` = planar kick speed, `r = 18.805`,
   `k` starting at `1.0` (pure rolling). In deg/s: `ω_deg = k · v/r · 180/π`
   (v=1800 → ≈5480 deg/s at k=1). Make `k` tunable
   (`css_sm2ball_spinfactor <k>`, range 0..2, default 1.0) — CS:S kicks were
   probably sub-rolling; the user tunes by feel.
3. Sign convention (verify live, don't trust this blindly): ball travelling +X
   rolling forward has ω about **+Y**. Check by spinning a resting ball
   (`spin_input 0 1800 0`) and observing which way it rolls; flip if needed.

Body-push, dribbling and the aim/reach/cooldown gates stay untouched.

## 5. Phase C — the wall experiment (decides hochbuggen)

The wall trial today launches from field centre and dies before reaching a
wall — that is why CS2 wall behaviour has never been measured. Arena geometry
(measured via `css_sm2ball_trace_arena`): side walls (`func_wall`) at
**x = ±1280**, back walls at y≈±1663, goals behind `func_brush` at y≈±1460,
floor z = −32, ball rest z = −13.19.

1. Extend the trial: `css_sm2ball_trial wall <speed> [startX]` — teleport the
   ball to `(startX, 2.60, restZ)` before launching at −X… **better: launch
   toward +X** at the x=1280 wall with `startX` default `600`, so impact
   happens at ~500+ u/s. (Direction is arbitrary; pick one and keep it.)
2. Add `css_sm2ball_trial wallspin <speed> [startX]`: same, plus rolling-rate
   topspin via the Phase-A spin input immediately after launch.
3. Run both at 600 u/s. Extract the Z profile around impact from the
   `[SM2CSSREF] trial_sample` log lines.

**Decision gate:**
- `wallspin` shows a clear post-impact rise (CS:S: +43 u/s vertical, ~8u rise)
  while `wall` doesn't → Rubikon converts spin at the wall natively.
  Hochbuggen comes free once kicks impart spin (Phase B). Done.
- Neither rises → Rubikon's wall friction doesn't do the conversion. Fallback:
  a *spin-conditioned* wall assist in the plugin — on wall contact (detect via
  position reversal against a known wall plane), add
  `Δv_z = μ_eff · ω · r · alignment`, calibrated to CS:S's +43 u/s at rolling
  rate. This is physically derived from actual ball state — it is NOT the old
  v2 wall-pop (which fired on aim heuristics with no physics state) and is the
  legitimate fallback if the engine won't do it. Keep it off unless needed.

## 6. Phase D — calibration loop with the user

All knobs are live RCON commands; no redeploy needed:

| Knob | Command | Current | CS:S ref |
|---|---|---|---|
| Kick power / clamp | `css_sm2ball_power <delta> [max]` | 1800 / 3500 | 1359 (smaller ball!) |
| Spin factor | `css_sm2ball_spinfactor <k>` (new) | — | ~1.0 |
| Collision model | `css_sm2ball_model <key>` | `large1850` | — |
| Physics profile | `css_sm2ball_physics` | mass 1.0 / fric 0.5 | (elasticity arg is inert) |

User test script (short, in one session): ① air ball wegschießen — flies far
and flat now? ② ground kick speed — matches CS:S feel? (tune power live)
③ rolling ball into wall — hochbuggen? ④ dribbling unchanged?

Then re-run the reference trials and diff against CS:S
(`artifacts/css-reference`): `roll 400` (expect ≈924u, moving at 19.9s),
`wallspin 600` (expect planar −334→+60-ish, vertical rise), `drop` (bounce
"good" per user, ~12u).

## 7. Infrastructure (for whoever executes this)

- Repo: `C:\Users\sergi\Documents\ChatGPT\Privat\cs2-soccermod`.
  Plugin: `src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.cs`
  (assembly `SoccerModNativeHull.dll`, namespace still `SoccerModMvp` — do not
  refactor yet).
- Build (no system dotnet — use the portable SDK):
  ```
  DOTNET_CLI_HOME=C:\Users\sergi\Documents\ChatGPT\Privat\.codex-tmp\dotnet-home
  NUGET_PACKAGES=C:\Users\sergi\Documents\ChatGPT\Privat\.codex-tmp\nuget-packages
  C:\Users\sergi\Documents\ChatGPT\Privat\.codex-tmp\dotnet-sdk\dotnet.exe build
    src\server-plugin\SoccerModMvp\SoccerModMvp.csproj -c Release --no-restore
  ```
- Deploy (one command, key-auth via `~/.ssh/config` host `cs2-soccermod`):
  `SOCCERMOD_HOST=cs2-soccermod bash deploy/testserver/push-ball-build.sh`
  — backs up, installs model+DLL, restarts `cs2-soccermod-test.service`,
  prints the plugin load lines. (Script embeds payloads base64 in one SSH
  session; never pipe a tar stream alongside a `bash -s` heredoc — stdin
  collides.)
- RCON from the server itself: `ssh cs2-soccermod '/root/rcon "<command>"'`
  (`/root/rcon` sources the password from `/etc/cs2-soccermod-test.env`;
  the secret never appears on a command line).
- Telemetry: `ssh cs2-soccermod "journalctl -u cs2-soccermod-test.service
  --since '10 min ago' --no-pager | grep SM2CSSREF"`.
- Model compile (local, then push the `.vmdl_c`): content lives in
  `E:\SteamLibrary\...\content\csgo_addons\soccermod_phase1\models\soccermod\`,
  compile with `resourcecompiler.exe -f -nop4 -game <game/csgo> <vmdl>`,
  verify with `.local/tools/valve-resource-format-20.0/cli/Source2Viewer-CLI.exe`.
  Hull generator: `tools/make-ball-hull.cs` (run via `dotnet run`).
- Existing RCON commands in the plugin: `css_sm2ball_status`, `_model`,
  `_power`, `_trial roll|wall|drop [speed]`, `_reset_center`, `_impulse`,
  `_physics`, `_kickmode`, `_thrust`, `_trace_arena`, `_replace_test`,
  `_restore_map`, `css_sm2knife_give`, `css_sm2inventory_status`.

## 8. Explicit not-now list

- Custom surface property for exact 6.3u bounce (user accepts current bounce).
- Refactoring namespace/folder names.
- Goal detection, score, teams, match/CAP — paused until ball acceptance.
- Native Metamod C++ shim — only if BOTH §3 outcomes fail, which is unlikely.
- The true-XSL 28.96u size — user chose CSF 37.61u; revisit only if asked.

---

## 9. Execution log (2026-08-29, Sonnet 5) — Phase A/B/C outcome

**Phase A: FAILED for both inputs.** `ApplyAbsVelocityImpulse` and
`ApplyLocalAngularVelocityImpulse` via `AcceptInput` produced zero directed
motion at any tested magnitude (500–6000 for velocity, up to 1800 deg/s for
angular). No exception, no "unknown input" warning — a silent no-op. Test
commands `css_sm2ball_impulse_input` / `css_sm2ball_spin_input` are left in
place (harmless probes) in case a future CS2 patch changes this.

**Phase A fallback, sub-finding: torque-only `phys_thruster` is ALSO dead.**
Contrary to the FGD ("if off, torque only" / "if off, linear only" implies
independence), spawnflags `Start On | Apply Torque` (no Apply Force) produced
**zero motion** at force values 500 through 100000 (200× range, statistically
indistinguishable from wake/settle jitter). The SAME force value with Apply
Force added (`1|2|4`) launched the ball instantly and violently (force=8000 →
ball crossed 1000+ units within 0.5s). **Conclusion: Apply Torque requires
Apply Force to be set to do anything at all in this CS2 build — they are not
independent, whatever the FGD says.** Verified with
`css_sm2ball_torque_test <zSign> <force> <seconds> [includeForce 0|1]`.

**Force+torque combined thruster: confirmed unreliable, not usable for a
precise gameplay kick.** A further isolation test
(`css_sm2ball_spin_isolate`) fired a force+torque burst then overwrote the
ball's linear velocity to zero one frame after `forcetime` should have
elapsed (per the FGD, forcetime is "automatic shut-off"). The ball kept
accelerating substantially AFTER the zero-out, indicating the thruster does
NOT reliably respect its own `forcetime` — consistent with earlier session
notes that `force` scale had no effect while `forcetime` behaved
non-linearly (0.05s ≈ nothing, 0.20s ≈ 583 u/s, 0.50s ≈ nothing).
**Decision: do not build the live kick's spin on `phys_thruster`.** The
mechanism is real (it moves things) but not controllable enough to trust in
gameplay — an unpredictable multiplier on every kick is worse than no spin.

**Net effect: T5 (spin/curve) is NOT implemented.** This is a genuine,
reported limitation, not a silent gap — see §10. Reaching it would need
Option C from the root-cause doc (a native Metamod C++ shim with direct
`IPhysicsBody` access), which is out of scope for one session.

**Phase C pivoted to the plan's own sanctioned fallback, and it works.**
Root cause of "wall trial never reaches a wall": the trial launches along
pure −X from field centre, which passes straight through the west goal
mouth (`func_door` at x=−1592) instead of hitting the `func_wall` segments
at x=±1280. Fixed by adding a Y-offset parameter:
`css_sm2ball_trial wall <speed> <startYOffset>`.

Implemented `TryApplyWallAssist` in `UpdateDerivedMotion`: detects a real
planar-velocity reversal from the ball's own tracked motion (not aim, not
prediction) and adds a vertical component sized from the speed the bounce
actually removed (`addedVertical = speedLost * ratio`, ratio defaults to
0.129 — CS:S's measured +43 u/s from a −334 u/s hit). First attempt
(comparing only adjacent ticks) missed the real bounce and false-triggered
on unrelated settle jitter later in the trial, because the actual wall-
contact tick has near-zero velocity for one frame, failing a simple
"was the previous tick fast" gate. Fixed by keeping a 4-tick rolling window
and using the fastest sample in it as the approach reference.

**Verified live**, ball launched at 1500 u/s toward the arena wall
(600-unit Y offset to clear the goal mouth): approach 658.7 u/s → rebound
210.5 u/s (speed lost 448.2) → added 57.8 u/s vertical → ball visibly rose
from resting Z≈−13 to Z≈+19 (a ~32-unit hop) before settling. Regression-
checked against `roll` and `drop` trials: zero false positives (the 150 u/s
approach-speed gate is well above normal rolling/settling jitter, which
stays under ~50 u/s).

Tune live: `css_sm2ball_wallassist <on|off|ratio> [maxAdded]`.

**This is explicitly NOT the old v2 wall-pop.** v2 fired during kick
processing based on aim/trace heuristics, before any real collision existed.
This fires only after Rubikon has already computed a genuine bounce, and
only augments it with a vertical component proportional to real, measured
energy loss.

## 10. Honest status for the user

Done and live on the test server:
- Ball is round, correctly massed, correct CSF size (unchanged from before
  this session).
- Kick direction/power fixes from the previous session (unchanged).
- **Wall hochbuggen now happens**, driven by real physics state, tunable
  live. Needs your in-game feel-check — the 0.129 ratio is CS:S's measured
  value but CS2's bounce dynamics aren't identical, so it may want tuning
  (`css_sm2ball_wallassist <ratio>`).

Not done, and not achievable without more invasive engine access:
- **The kick still imparts no spin**, so there is no deliberate sideways
  curve on kicked or rolling balls (T5). Wall hochbuggen was recovered
  through a different, physics-state-driven mechanism that doesn't need
  spin — but rolling-ball curve genuinely does need spin, and every
  in-engine route tried (direct impulse inputs, torque-only thruster,
  force+torque thruster) failed or proved too unreliable for gameplay.
  This is the honest tradeoff point from the user's "wenn wir Abstriche
  machen müssen dann ist das okay" — flagged as agreed, not swept under.

Left in place as diagnostic tooling (matches this file's existing pattern of
permanent RCON probes, e.g. `css_sm2ball_trace_arena`): `_impulse_input`,
`_spin_input`, `_torque_test`, `_spin_isolate`, `_thrust`/`_kickmode
thruster`. None of these are on the live kick path (`_kickMode` defaults to
`Velocity`, never `Thruster`).

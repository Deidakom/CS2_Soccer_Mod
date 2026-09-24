# Ball realism analysis (2026-09-24)

Read-only analysis of the ball on the test server: code, today's server logs
(18:00–21:25 UTC) and literature values for real footballs. Nothing was
changed. Tags: **[M]** measured in today's logs, **[D]** measured earlier and
recorded in the repo docs, **[C]** read from the code, **[E]** estimate or
literature value.

## Main findings

1. **Flight already has realistic drag.** The engine slows the ball in the
   air, and the slowdown grows with the square of speed, like real air drag
   [M]. Its strength is within about 1.5× of a real ball when game units are
   scaled so gravity matches real gravity [E]. What is missing in the air is
   **curve from spin (the Magnus effect)** and the lift that backspin gives.
2. **A bug cuts the new ground bounce.** The older landing limiter runs one
   tick after the ground-bounce assist and cuts its rebound to about 42%
   [M, C]. This hits 22–29% of landings from 250 u/s or faster.
3. **Slow balls roll out far too long.** Below 220 u/s the rolling assist
   lets the ball lose at most 6 u/s². That is 8–20× less rolling resistance
   than real grass [M, C, E], so the ball glides as if on ice.
4. **Walls return 0.18 of the incoming speed**, against roughly 0.5–0.8 for a
   real football. This is deliberate, to match CS:S.

## 1. Current behaviour by stage

The live profile is `improved`; the logs show `legacy` until about 19:00.
Values come from `soccermod_settings.json` [C/M].

| Stage | What it does | Code | Live values |
|---|---|---|---|
| Tick order | Motion + ground bounce → landing limiter → spin sampling / wall assist → body push → body impact → rolling assist | `SoccerModMvpPlugin.cs:892-926` | — |
| Velocity source | The engine reports zero velocity for this ball, so all velocities come from position differences [C] | `.Training.cs:39-41` | — |
| Kick power | Base impulse × stance/button scale × reach (the knife's tip cushions) × soft pass × soft pitch × volley scale (airborne ball) | `.cs:1413-1419`, `BallContactMath.cs:71-84` | 1602 u/s; left click 0.9, right 0.4, crouched left 1.0 / right 0.7, volley 0.92, overhead bonus 0.14 |
| Lift | Where the aim ray crosses the ball (2° / 10.6° / 35°) + 0.55 × view pitch on the ground; grounded kicks never below 0° | `.cs:1305-1375`, `:3731` | sensitivity 0.55, crouch lift 0 |
| Soft pass / soft pitch | Grounded ball only | `.cs:1399-1412` | 0.35→1.6 radii → 0.25×; 30°→85° → 0.35× |
| Incoming ball | Cancels the part moving against the shot; volleys keep ¼ of sideways drift, max 3°; tip cushion; absorbs the incoming speed | `.cs:1437-1451`, `BallContactMath.cs:39-49, 98-111, 206-217` | absorb 1.0 (minimum 60 u/s), cap 3500 |
| Kick spin | Contact offset × kick change, kept spin minus roll against the new direction | `.ContactSpin.cs:37-60` | spin factor 0.1, cap 6000°/s |
| Flight | Engine only: gravity scale 1, native drag [M]; no Magnus [D]; the plugin writes nothing in the air | `.cs:86-89` | — |
| Ground bounce | Vertical rebound raised to about 0.55 × impact (×1.1 soft to ×0.9 hard); grass takes `grip × (impact + rebound)` of forward speed, capped at 25% (first bounce) or 5% (later) | `.GroundBounce.cs:40-72`, `BallContactMath.cs:23-69` | 0.55, grip 0.25, only from 80 u/s impact; **match ball only** |
| Landing limiter | Upward speed capped at 0.55 × the previous tick's downward speed | `.Landing.cs:13-56`, `BallContactMath.cs:119-120` | hard-coded 0.55 |
| Roll-out | At ≤220 u/s, grounded, with no player or wall near, the loss is capped at 6 u/s², for up to 20 s | `.Rolling.cs:13-68`, `BallContactMath.cs:145-164` | hard-coded |
| Settle | Improved profile only watches low speed; it never zeroes the ball | `.BallHandling.cs:163-171` | 8 u/s, 16 ticks |
| Walls | Detected by a speed drop or reversal plus a trace; normal speed restored to 0.18 of incoming, plus lift = 0.159 × speed lost (max 200); held for 4 frames | `.BallHandling.cs:203-354` | 0.18 / 0.159 / 200 |
| Body push | Ball driven toward 1.26 × the player's approach speed (max 396, first nudge 135); pushes from several players averaged | `.cs:3243-3393` | 1.26 / 396 |
| Ball hits player | Swept contact test; 0.6 restitution on the contact normal, 0.7 horizontal kept | `.ContactImpact.cs:23-116`, `.BodyImpact.cs:39-81` | push 0.4, max 1750 |
| Duels | Within 0.10 s the first kick wins; if two land on the same tick, the cleaner one wins | `.KickDuel.cs:34-65` | 0.10 s |

## 2. What today's logs show

Source: `journalctl` since 18:00 UTC (205 min), parsed on the server with
microsecond timestamps. **Caveats:** the plugin was loaded 44 times (many
builds), the profile was legacy until about 19:00, the 20:00–21:00 data is
largely training-cannon testing, and ground-bounce logging only starts at
19:38.

- **Kick attempts [M]:** 1152 accepted, 253 rejected near the ball (145 out of reach, median 109 u against 88.8 u reach; 81 outside the aim cone; 26 cooldown).
- **Kick speed [M]:** final speed p10 736, p50 1446, p90 1629 u/s. 78% of kicks fall between 1250 and 1750 u/s.
  - Clean kicks on a dead ball leave at exactly 1442 or 1602 u/s, so power is effectively on/off.
  - Soft pass was active in 1.6% of kicks, soft pitch in 10.5%.
  - Grounded kicks launch at a median 0.7° (p90 17°).
- **Flight [M]:** 435 kicks with a clean 0.25 s airborne window (kick velocity compared with the +0.25 s snapshot):
  - Horizontal speed kept: 0.95–0.97 below 600 u/s, 0.92 at 1200–1500, 0.91 at 1500–1800.
  - The implied drag length (distance over which speed falls by a factor of e) stays at **3.7–4.1k u across 600–1800 u/s**, which is the signature of drag that grows with v².
  - Vertical deceleration is 875 u/s² while rising and 680 while falling (n=4). Gravity is 800, so drag adds to it going up and opposes it coming down.
  - The first tick keeps 0.99 of the kick speed, so there is no immediate loss at the kick.
- **Ground kicks [M]:** 287 kicks. From ≥800 u/s the ball keeps 0.80–0.83 in 0.25 s, roughly 1000 u/s² of deceleration (skidding plus drag).
- **Mid-speed roll [M]:** 42 pairs of a flat kick and the next kick. From 300–1000 u/s the ball loses 100–200 u/s².
- **Slow roll [M]:** 60 pairs of periodic snapshots with no contact between them. From 60–130 u/s the ball loses 0–6.4 u/s² (for example 67 → 59 u/s over 612 u in 10 s).
- **Ground bounce [M]:** 1137 bounces.
  - Impact speed p50 139, p90 422 u/s. The engine's own rebound is about 0.03× at the detection tick; the assist lifts it to 0.55–0.60.
  - Forward speed kept: first bounce median 0.88 (the 25% cap is reached at p25); later bounces 0.95, which is always the 5% cap.
- **Landing limiter after a ground bounce (within 100 ms) [M]:** 2% of bounces at 80–150 u/s impact, 9% at 150–250, 22% at 250–400, 29% at ≥400. It cuts the assisted rebound to a median 0.42.
  - Cause [C]: it reads the contact tick's already reduced downward speed as the incoming speed.
- **Walls [M]:** 607 assists (516 after 19:00, against 998 kicks). Incoming normal speed p50 1179 u/s. The engine returns about 0.02× on its own; everything else is the assist (0.18, plus a median 141 u/s of lift).
- **Ball hits player [M]:** 150 hits. Rebound/incoming speed median 0.31.
- **Kick duels [M]:** 0 events today, so this rule has not been tested in play.

## 3. Gaps compared with a real football

Scale: gravity-matched (800 u/s² = 9.81 m/s², so 1 u ≈ 1.23 cm) [E]. On
that scale a 1450 u/s kick is about 18 m/s, the pitch is about 31×36 m and
the ball is about 2× real size. Ratios, and deceleration divided by g, do
not depend on the scale.

| Gap | Real football | Now | Doable here? | Effort / risk | How to verify |
|---|---|---|---|---|---|
| Air drag | Drag coefficient ≈0.2–0.25 above the drag crisis (the speed where drag suddenly falls), ≈0.45 below it (~12–15 m/s). Drag length ≈74 m / 41 m, i.e. ≈6000 / 3340 u [E, Asai 2007; Goff & Carré 2009] | ≈4000 u, no crisis [M] | Optional extra drag at low speed | S / low | Same +0.25 s kept ratio split by speed |
| Magnus / curve | Lift coefficient ≈ spin parameter S up to about 0.3; free kicks bend several metres [E, Goff & Carré; Asai] | None; spin only matters at contacts [D] | **Yes:** per-tick velocity nudge from the measured spin | M / medium (stutter) | Sideways offset at +0.25 s; gravity-plus-drag in the air must stay ≈ today's value |
| Backspin lift ("float") | Comes from the same Magnus force | None | Falls out of the Magnus item | same | Apex height on lofted kicks |
| Spin decay in flight | Slow, time constant of seconds [E] | Model has 0 angular damping [D] | Not needed yet | — | — |
| Rolling resistance | ≈0.06–0.15 g, from the FIFA ball-roll test (4–10 m off a 1 m ramp) [E] | ≤0.0075 g below 220 u/s [M]; about right from 300 u/s up | **Yes** | S–M / high feel impact | Periodic snapshot pairs |
| Bounce restitution | 0.55–0.65 (FIFA turf rebound test: 2 m drop → 0.6–0.85 m) | 0.55–0.60 set by the assist, but clipped (bug) [M] | Fix the clipping | S / low | No landing_limit within 100 ms of a bounce |
| Spin effect on bounce | Topspin skids on, backspin checks; a spinless steep landing keeps about 0.6 of forward speed (hollow sphere) [E] | Flat caps (0.75 / 0.95) regardless of spin [C, M] | **Yes:** grip/slip model plus a spin write-back | M / medium | Forward speed kept, split by spin sign |
| Wall restitution | 0.5–0.8; a ball on steel rebounds 1.35–1.55 m from 2 m [E, FIFA ball test] | 0.18 [M] | Existing dial | S / high feel impact | wall_assist log |
| Run-up in the kick | Ball speed ≈1.2–1.3 × foot speed, and foot speed includes the run-up [E, Lees & Nolan 1998] | Player speed ignored [C] | Yes | S / medium feel impact | Kick speed by player speed |
| Striking an incoming ball | A full strike returns the ball *faster* ("use the pace"); a trap cushions it [E] | Always absorbs (owner's CS:S choice) [C] | Yes, split by kick power | S / owner decision | Kick speed vs opposing speed |
| Off-centre contact | Curled kicks come off slower than clean drives [E] | Full speed at any offset [C] | Yes | S | Kick log |
| Knuckleball (erratic flight with little spin) | Sideways wobble at low spin [E, Hong & Asai 2014] | None | Possible, but feels random | — | Not recommended |

## 4. Recommendations, in priority order

Each keeps the default equal to today's behaviour. ⚑ marks a feel change for
the owner to decide.

1. **Fix the landing limiter clipping the ground bounce** (bug, S).
   - Where: `.Landing.cs:25-31`. For the match ball, skip when a ground bounce ran within the last 3 ticks (`_lastGroundBounceTime`, `.GroundBounce.cs:37`), or skip entirely when `groundBounceRestitution > 0`. Training balls keep the limiter.
   - Also make `LandingVertical`'s hard-coded 0.55 (`BallContactMath.cs:119-120`) follow `GroundBounceRestitutionAt`.
   - Verify: landing_limit events within 100 ms of a bounce drop to 0.
2. **Magnus effect in flight** (M) ⚑.
   - Where: new partial `SoccerModMvpPlugin.Aero.cs` with `UpdateBallAerodynamics()`, called right after `UpdateSharedBallHandling()` (`.cs:899`) so it can use this tick's `State(ball).MeasuredSpin` (`.ContactSpin.cs:19-36`, local frame → world via `Rotation`).
   - Only for an airborne ball: no contact within the last 4 ticks, not a kick tick, no wall within R+8.
   - Formula: `a = magnusStrength · S · v²/1000 u` along ω×v, with S = R·|ω⊥|/v. The 1000 u comes from the measured drag length ≈4000 u × drag coefficient 0.25 [E].
   - Put the math in a pure `BallContactMath.MagnusStep`. Add `magnusStrength` to `BallDials()` (0–2, default 0).
   - Caveat: at spin factor 0.1, S is at most about 0.1, which gives only ~40 u of bend on a 0.6 s flight. Realistic curlers need a spin factor of about 0.3–0.5 [E].
   - Risk: a per-tick `Teleport(velocity)` in the air. There is a precedent (the creative profile's curve step, `.BallHandling.cs:173-178`), and it is a velocity nudge, not a scripted ball. Still, **check for client-side stutter**, and check that vertical deceleration in the air stays at today's ≈860 u/s².
3. **Real rolling resistance** (S–M) ⚑⚑, the biggest feel change.
   - Where: `.Rolling.cs` and `BallContactMath.cs:145-164`.
   - Turn the hard-coded 6 u/s² and 220 u/s into dials `rollDecel` and `rollAssistMaxSpeed`. Add `rollResistance` (u/s², 0 = today): an active brake on grounded, contact-free rolling balls.
   - Realistic value: 50–100 u/s² [E]. A 200 u/s ball would then stop in about 3 s / 300 u, instead of rolling for up to 20 s.
4. **Spin-aware ground bounce** (M) ⚑.
   - Where: replace the flat caps in `GroundBouncePlanarScale` (`BallContactMath.cs:60-69`, called at `.GroundBounce.cs:65`) with a grip/slip impulse model. The contact-point velocity is u = v + ω×(−R ẑ); forward speed changes by −min(0.4·|u|, μ(1+e)|v_z|) along û.
   - Write the resulting spin change through `SendAngularImpulse`.
   - Dials: reuse `groundBounceGrip` as μ; add `groundBounceSpinCoupling` (0 = today). Log spin before and after.
5. **Run-up and contact quality in the kick** (S) ⚑.
   - Where: `.cs:1413-1419`.
   - `kickRunUpTransfer` (0–1, default 0): adds that share of the player's velocity along the shot.
   - `kickOffCentreLoss` (0–0.5, default 0): scales power by 1 − loss × (sideways offset ratio)². The offset is computed the same way as `.ContactSpin.cs:45`.
   - Log both in kick_accepted.
6. **Striking vs trapping an incoming ball** (S) ⚑. This reverses today's CS:S choice for full strikes.
   - Where: `AbsorbIncoming` at `.cs:1450`. Scale absorb by (1 − kick power share), so a full left-click absorbs close to nothing and soft/right-click taps absorb fully.
   - Dial: `kickIncomingAbsorbFullPower` (default 1 = today).
7. **More realistic walls** (S) ⚑⚑, the CS:S arena feel.
   - Raise `wallAssistMinimumNormalRetention` (dial already exists) from 0.18 toward 0.45–0.6.
   - Tie wall lift to topspin rather than to speed lost (`AdditiveWallLift`, `.BallHandling.cs:322`); a rolling ball climbing a wall through friction is real physics.
8. **Optional:** extra drag at low speed (drag crisis), dial `lowSpeedExtraDrag`, S. **Not recommended:** knuckleball.

For every new per-tick write, skip ticks where another system already wrote
(`State(ball).LastContactTick == Server.TickCount`), to avoid the same-tick
overwrites that the ball-and-menu optimization doc flags.

# Ball root cause: the Source 2 hull cooker was destroying the XSL hull

Date: 2026-08-29. Supersedes the "native XSL hull" assumption in the 2026-08-28
handoff.

## Summary

The CS2 ball did not feel wrong because of tuning. Three independent defects
stacked up, and the first one made the ball measurably not round.

1. **The compiled collision hull was not the XSL hull.** Source 2's hull cooker
   silently simplifies convex hulls above roughly 80 faces. The XSL B1 hull has
   316 faces, so the compiled shape lost 17.6% of its volume and its centroid
   moved off-centre. The ball was lopsided.
2. **The compiled model had no mass.** `m_flMass = 0.0`, so the engine fell back
   to a default and the plugin's `massscale 0.35` multiplied an unknown number.
3. **The kick overwrote velocity instead of adding an impulse**, with a fixed
   1336 u/s planar and 250 u/s lift. Every kick was identical regardless of the
   ball's own motion or the player's aim.

## Evidence

### The hull

`m_flMass`, `m_flVolume` and `m_Bounds` read out of the compiled `.vmdl_c` PHYS
block with `Source2Viewer-CLI --block PHYS`.

| Source | Verts | Faces | Compiled volume | Compiled bounds | Centroid |
|---|---:|---:|---:|---|---|
| Source 1 XSL B1 (truth) | 160 | 316 | 11951.80 | symmetric ±14.48 | ~0 |
| XSL hull through CS2 compiler | 160 | 316 | **9843.95** | −13.77..14.48 / −14.10..14.48 / **−11.45..12.86** | (−0.24, −0.37, 0.76) |

The 3.0-unit vertical asymmetry the previous handoff recorded as a property of
the XSL hull was compiler damage, not the shape.

Controlled experiments, all compiled with the same vmdl and read back the same
way:

| Test shape | Verts | Faces | Compiled volume (target 11951.80) | Centroid |
|---|---:|---:|---:|---|
| Cube ±10 (control, target 8000) | 8 | 6 | **8000.00 exact** | 0,0,0 |
| Icosahedral geodesic, frequency 2 | 42 | 80 | **11951.81 exact** | 0,0,0 |
| Octahedral geodesic, frequency 4 | 66 | 128 | 11085.30 | (0.57, 0.60, −0.27) |
| Tetrahedral geodesic, frequency 6 | 74 | 144 | 11534.29 | (−0.11, 0.59, −0.01) |
| Icosahedral geodesic, frequency 3 | 92 | 180 | 9512.19 | (1.23, 0.98, −0.64) |
| Icosahedral geodesic, frequency 4 | 162 | 320 | 9836.04 | (0.31, 0.19, 0.23) |

The cube proves the compiler and the reader are exact, so the distortion is real
and shape-dependent. No `PhysicsHullFile` option changes it: `faceMergeAngle`
0.0 / 0.001, `maxHullVertices`, and `optimization_algorithm` `Exact` / `None`
were each tried against the 180-face sphere and all landed between 9306 and 9512.

**Conclusion: 42 verts / 80 faces is the finest ball hull Source 2 reproduces
faithfully.** It is coarser than the XSL original (facet edge ~6.1u instead of
~3.1u on a 29.7u ball) but it is symmetric, which matters more — a lopsided ball
veers and wobbles no matter how it is tuned.

### The physics values

`glass` in `surfaceproperties.vsurf` (both `csgo` and `core`) is
`elasticity 0.2, friction 0.5, density 2700`. The XSL ball used `surfaceprop
glass` in Source 1, and the mod's live model already declared it.

The CS:S captures in `artifacts/css-reference` confirm it independently:

- **Gravity**: the drop from Z=256 fits `g ≈ 800` across samples 4–8, so gravity
  scale 1.0.
- **Restitution**: impact at ~624 u/s, rebound apex 6.3 units above the resting
  height ⇒ rebound ~100 u/s ⇒ **e ≈ 0.16**. The wall trial gives the same
  answer: approach −334 u/s, rebound +60 u/s ⇒ **e ≈ 0.18**. Both match the
  `glass` value of 0.2; the previous build used **0.65**.
- **Mass**: Source 1 reported 60.694092 for a hull of volume 11951.8 and surface
  area 2555.4. Solid glass would be 529 kg; a 0.5-unit glass *shell* is 56.5 kg.
  The XSL ball was a hollow shell, which is why it behaves like a football.

### The decay curve

Smoothed planar speed from `roll-seq1.log` (5-sample windows):

```
t=0.0  281    t=2.0   84    t=6.0  40    t=13.0  30
t=0.5  208    t=2.5   66    t=8.0  30    t=19.0  28
t=1.0  152    t=3.0   58    t=10.0 32
t=1.5  113    t=4.0   49    t=11.0 28
```

Two regimes: a clean exponential at λ ≈ 0.6/s above ~65 u/s, then **no
measurable decay at all** — the ball still rolls at ~30 u/s after 19 seconds.
The wall trial decays faster (λ ≈ 0.98/s) from a higher launch speed, so the
rate scales with speed.

This cannot be reproduced by a damping constant, which decays to zero
uniformly. It is an emergent property of a faceted low-restitution hull: at
speed the ball hops between facets and each impact costs energy, and below a
threshold it rolls smoothly and coasts almost forever. **That is the argument
for the native-hull architecture and against any analytic controller.** It is
also why linear and angular damping are both left at 0.0 in the model.

Two more behaviours worth keeping in mind when comparing captures: a pure
vertical drop ended up travelling laterally at 26 u/s, and the roll launched
straight along +X drifted 216 units sideways over 19 seconds. The XSL ball
curves, and the facets are the reason.

## What changed

- `src/assets/models/soccermod/soccer_ball_hull.dmx` and
  `soccer_ball_physics.vmdl` — new symmetric hull, volume matched to the XSL
  hull, `mass_override = 60.694092`, `surface_prop = glass`, damping 0.
  Generated by `tools/make-ball-hull.cs`.
- Plugin physics profile: massScale `0.35 → 1.0`, friction `0.15 → 0.5`,
  elasticity `0.65 → 0.2`.
- Plugin kick: delta-velocity added to the ball's existing velocity, with the
  launch angle driven by view pitch (flat 2° looking down, the measured CS:S
  10.6° at level aim, up to 35° looking up). Mass response is now linear in
  mass, as an impulse should be.
- `css_sm2ball_trial roll|wall|drop` replays the CS:S probe trials and logs
  `[SM2CSSREF]` lines in the same shape as `artifacts/css-reference`.
- Removed: the v2 `ApplyGameplayMotionController`, the wall-assist probe, and
  the unreachable number-key tuning menu. All three were dead code.

## Known gap (superseded — see the addendum below)

CS:S applied the knife hit as an off-centre impulse, which produced spin,
and spin is what made the ball curve.  The delta-velocity kick imparts no
spin, so facet scatter on bounces remains but a deliberate curve does not.
The addendum below documents how to get spin back; the first route is
implemented and needs one live test.

## Addendum: routes to spin (2026-08-29)

The "no impulse or torque API" statement above was about CounterStrikeSharp's
typed wrapper, not about what is reachable. Three routes exist, in increasing
cost.

### A. Native CS2 force entities — no reverse engineering (implemented)

`phys_thruster`, `phys_torque`, `phys_impact` and `point_push` are all present
as registered classnames in `game/csgo/bin/win64/server.dll`, appear in the CS2
FGD, and `CPhysThruster` / `CPhysTorque` / `CPhysForce` are in the live schema
with `m_force`, `m_attachedObject` and `m_forceTime`.

The `phys_thruster` FGD text describes exactly the CS:S mechanic:

> The force and torque is calculated using the position and direction of the
> thruster as an impulse. So moving those off the object's center will cause
> torque as well.

`ForceController` (its base) supplies `attach1` (target by targetname), `force`,
`forcetime` (auto shut-off), the `Activate` / `Deactivate` / `Scale` inputs, and
spawnflags for Start On (1), Apply Force (2), Apply Torque (4), Orient Locally
(8), Ignore Mass (16).

So the kick can be a real off-centre impulse, spawned and fired from C#, with no
signature scanning. `ApplyThrusterKick` does this: it places the thruster one
ball radius behind the launch direction and `backspinBias` radii below centre,
orients it along the launch vector, runs it for `forcetime`, then removes it.
Force is derived from the wanted delta-velocity through the known mass:
`force = deltaV * 60.694092 * scale / forcetime`.

**Open question, deliberately not assumed:** whether `attach1` binds for an
entity spawned at runtime rather than placed in the map. `ApplyThrusterKick`
returns false on any failure and the caller falls back to the delta-velocity
kick, so a failed binding degrades to the current behaviour instead of eating
the input.

Switch and calibrate over RCON:

```
css_sm2ball_kickmode thruster [scale] [seconds] [backspin]
css_sm2ball_kickmode velocity
```

### B. Call engine functions from CounterStrikeSharp

CounterStrikeSharp ships `MemoryFunctionVoid`, `MemoryFunctionWithReturn`,
`CreateVirtualFunctionBySignature` / `BySymbol` / `FromVTable`, and exposes raw
entity pointers. Arbitrary server functions, including Rubikon body methods in
`vphysics2`, are therefore callable from C#. The cost is finding and maintaining
byte signatures, which break on CS2 updates. Not worth it if A works.

### C. Own Metamod:Source 2 plugin in C++

Full access to `IPhysicsBody`, collision callbacks and per-tick physics
intervention. Worth noting that this is not an either/or with CounterStrikeSharp
— CounterStrikeSharp *is* an MM:S plugin, so a small native plugin can expose
just the physics primitives while the game logic stays in C#. The cost is a
Linux C++ toolchain, CS2/MM:S SDK headers, offset maintenance, and crashes that
take the server down instead of throwing a managed exception.

### D. VScript in the map addon

Ruled out: the phase-1 toolchain audit found the declaration has no force,
impulse or torque API.

## Addendum 2: live measurements after deployment (2026-08-29)

Deployed to the test server and measured with `css_sm2ball_trial` against the
CS:S captures. Live snapshot confirms the hull fix: `collisionMins/Maxs` are now
`±14.84` symmetric, versus `(-13.77,-14.10,-11.45)..(14.48,14.48,12.86)` before.

### Two findings that change the tuning model

**1. The entity `elasticity` field is inert for MOVETYPE_VPHYSICS props.** Three
drops on the same model at entity elasticity 0.05, 0.20 and 0.95 gave 11.88,
11.68 and 11.88 units of bounce — identical. Restitution comes only from the
model's compiled surface property. `css_sm2ball_physics <...> <elasticity> <...>`
therefore does nothing and should not be trusted as a knob.

**2. Surface-property restitution is a step, not a curve.** Measured bounce on
the same sphere: `glass` (0.20) → 0.00u, `soccerball` (0.25) → 0.00u, `tile`
(0.30) → 0.00u, `weapon` (0.95) → 11.88u. No stock CS2 surface property exists
between 0.30 and 0.95, so the CS:S target of 6.30u is not reachable by picking a
stock prop.

### Shape sweep (roll: CS:S = 924u, still moving at 19.91s; drop bounce = 6.30u)

| model | dist | moving until | bounce | note |
|---|---:|---:|---:|---|
| 80-face hull, glass | 402.6 | 5.30s | 0.78 | early decay matches, then dies |
| 80-face hull, weapon | 357.6 | 4.39s | 12.68 | |
| sphere, glass/tile/soccerball | ~1218 | wall hit | 0.00 | almost frictionless |
| sphere, weapon | 953.1 | 19.91s | 11.68 | too slippery early |
| **hull + sphere r=14.45** | 549.3 | 11.70s | 12.27 | best early-decay match |
| **hull + sphere r=14.50** | 602.8 | 8.70s | 12.21 | |
| **hull + sphere r=14.60** | 742.7 | 19.00s | 12.14 | best overall, now default |

The compound shape is the useful discovery. A `PhysicsShapeList` holding both
the 80-face hull and a slightly smaller sphere keeps the facets that produce
CS:S's speed-dependent energy loss, while the sphere fills the facet valleys and
lowers the rolling barrier from `circumradius - inradius` = 0.977 units to
`circumradius - r`. That barrier sets the minimum rolling speed at
`sqrt(2*g*h)`: 39.5 u/s for the bare hull (which is why it died at ~5s), 24 u/s
at r=14.60. CS:S coasted at ~30 u/s.

`hull + sphere r=14.45` reproduces the CS:S decay curve almost exactly through
the first 2.5 seconds — 206 vs 208 at t=0.5, 83.5 vs 83.8 at t=2.0 — then falls
away. r=14.60 keeps rolling for the full 19s but decays too slowly early.
Something between the two is the next thing to sweep.

### Open

Bounce is stuck at ~12u, roughly twice CS:S, for every shape using `weapon`, and
at 0u for every prop at or below 0.30. Giving the hull and the sphere different
surface properties does not help (12.30u): the hull vertices protrude past the
sphere, so they make first contact and dominate. Reaching 6.3u needs a custom
surface property shipped with the addon, which is untested on this server.

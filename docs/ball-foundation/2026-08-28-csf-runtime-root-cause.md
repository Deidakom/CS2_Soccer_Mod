# CSF stadium ball foundation: runtime root cause

Date: 2026-08-28

## Scope gate

Match, score, goal, and CAP work is paused. The only active milestone is a
server-authoritative ball with XSL/CS:S-like body contact, knife-primary kick,
rolling, bouncing, and multiplayer replication. Secondary attack and lob are
out of scope.

## Verified runtime stack

- CS2 dedicated server build: `1.41.7.8/14178 10896`.
- Metamod:Source: `2.0.0-dev+1410`, build commit `2667e8e`.
- CounterStrikeSharp: `1.0.373`, build commit `3a59f2d`.
- SoccerMod foundation plugin: `0.6.2-xsl-profile-b`.
- Workshop item: `3361075564`.
- Runtime map: `soccer_cssl_stadium_v8`.

The initially staged Metamod `1.12.0-git1225` archive was rejected during
startup verification because it is the Source 1 branch. It was replaced by the
official Source 2/CS2 `2.0.0-git1410` build before any ball controller was
activated.

## Proven CSF map defects

The decompiled entity lump and live schema state agree:

- The Workshop ball is `prop_physics_multiplayer`, target `filter_ball`, model
  `models/ball/jabulani_edit.vmdl`.
- Its map `spawnflags` value is `5`: start asleep (`1`) plus debris (`4`).
- CS2 reports both entity and physics collision group `5`, which is
  `COLLISION_GROUP_DEBRIS`: it collides with the world/static geometry but not
  players or other debris.
- Live baseline state was `SOLID_VPHYSICS`, `MOVETYPE_VPHYSICS`, physics
  enabled, asleep, never awakened, and never touched by a player.
- The map tries to execute `phys_pushscale 900`. CS2 rejects this Workshop
  convar because the Source 1-era convar no longer exists.
- The ball declares parent `ballon`, but no matching target exists; the server
  logs an unrecognized parent warning.
- The map contains an untriggered `game_player_equip` with `weapon_knife`; it
  does not automatically equip players at spawn.

These findings explain both observed symptoms without relying on speculative
GPU, tickrate, or client-console fixes.

## Controlled physics result

A one-shot server impulse of `(300, 0, 25)` proved that the Workshop ball's
rigid-body/world simulation itself runs. It moved from approximately
`(7.73, 2.60, -13.06)` to `(181.39, -1.65, -13.09)` after one second.
`AbsVelocity` remains zero for this entity type, so the diagnostic plugin now
also calculates velocity from authoritative position deltas. The measured
derived speeds were approximately 300 u/s on the first frame, 180 u/s after
0.25 seconds, and 136 u/s after one second.

This isolated the original failure to the map entity's collision setup rather
than an immobile or missing VPhysics object.

## Clean replacement-ball probe

The plugin can temporarily park the defective map ball and spawn a clean ball
using the same Workshop model with:

- `prop_physics_multiplayer`;
- `physicsmode = 1` (solid, server-side);
- `spawnflags = 1` (start asleep; no debris flag);
- tuned model mass (`massscale = 0.35`).

CS2 initializes this clean ball in collision group `20`
(`COLLISION_GROUP_PUSHAWAY`), the intended multiplayer-physics path, instead of
debris group `5`. Its controlled impulse produced the same world-motion profile
as the original model. The original map ball remains parked for immediate
rollback, and a map restart fully restores the Workshop state.

The clean replacement is now automatic after map and round lifecycle events.
Real-client testing proved native body contact and the user accepted the tuned
push resistance. The plugin also restores a knife to eligible players and the
user proved that primary/left-click attacks move the authoritative ball. No
synthetic proximity push, secondary kick, or lob is present.

## CS:S/XSL reference calibration

A passive SourceMod recorder was added to the original CS:S SoccerMod test
server. It observes the existing `func_physbox` `OnDamaged` output and samples
position without replacing or modifying the original SoccerMod behavior. Three
user hits were captured; two contacted nearby geometry and were rejected as
calibration samples. The clean third hit established these reference values:

- horizontal travel direction differed from player yaw by only about `0.32`
  degrees;
- early planar speed was about `1336 u/s`;
- the ball rose about `37.6` units by the quarter-second sample;
- from the next-frame sample to the one-second sample it travelled about
  `1043` units;
- the original mod adds the hit to existing ball motion, so CS2 must not discard
  most of the current velocity on each kick.

CS2 profile A used the Stadium's approximately 29-unit, one-hull alternate
Jabulani because its dimensions match the roughly 30-unit CS:S reference.
Automated qualification rejected it: a commanded `(1750, 0, 210)` velocity
decayed to about `96 u/s` at 0.25 seconds and about `23 u/s` at one second.
Its size is closer, but its motion is unusable.

Profile B therefore retains the Stadium's stable 20-hull Jabulani and applies a
reference-derived normal primary kick of `1750 u/s` planar speed plus `270 u/s`
vertical speed, inheriting 100 percent of the measured current ball velocity
with a `2500 u/s` safety cap. In the controlled CS2 trial:

- next-frame derived speed was about `1742 u/s`;
- quarter-second horizontal speed was about `1436 u/s`;
- quarter-second rise was about `35.6` units versus `37.6` in CS:S;
- next-frame-to-one-second travel was about `1098` units versus `1043` in
  CS:S, a difference of roughly five percent.

This is the first data-calibrated profile, not a claim of final 1:1 feel. It is
now live for real-player review before roll, rebound, repeated-hit, and
multiplayer tuning.

## Acceptance sequence

1. **Passed:** automatic clean ball, native body contact, acceptable body push,
   knife provisioning, and one primary/left-click kick.
2. **Passed:** passive original-CS:S reference capture and automated CS2 profile
   A/B rejection/selection.
3. Manually review profile B for normal kicks, running kicks, moving-ball
   inheritance, and close control.
4. Tune passive roll, wall bounce, corner behavior, stopping distance, and
   multiplayer replication against the CS:S/XSL reference.
5. Declare the ball gate passed. Match/CAP work may resume only then.

## Operational note

For this Workshop map, a plain `changelevel soccer_cssl_stadium_v8` drops the
Workshop addon context and can stall map initialization. Reset through the
service's `host_workshop_map 3361075564` startup path instead.

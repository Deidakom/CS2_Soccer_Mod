# CSF Football Stadium audit

Audit date: 2026-08-26

## Identity and provenance

| Field | Observation |
|---|---|
| Workshop item | `3361075564` |
| Public title | `CSF Football Stadium` |
| Local publish title | `CSF Soccer Stadium` |
| Runtime map | `soccer_cssl_stadium_v8` |
| Publish-metadata source-folder label | `csf_soccerstadium_v8` (not editable source) |
| Published | 2024-11-06 |
| Last Workshop update observed | 2026-01-16 14:26:59 UTC |
| Outer package SHA-256 | `052BB4A46E7B80BF509F70CE53425185D4E35A6F59E600C8DF21651B46EAA6CC` |
| Nested map VPK SHA-256 | `6E36743F4269A88F83259204F9761C5BE15A3EEC74C484DD3386F1EE71BCFCFD` |

The Workshop description says the map is intended for football practice and
can operate with or without plugins. It does not provide an editable source
archive or an explicit license granting modification or redistribution. Local
testing of the subscribed item is in scope; publishing a derivative is not
assumed to be permitted.

Workshop page:
[CSF Football Stadium](https://steamcommunity.com/sharedfiles/filedetails/?id=3361075564)

## Package contents

The installed outer VPK contains a nested compiled map and its supporting
models, sounds, and materials. Relevant paths include:

```text
maps/soccer_cssl_stadium_v8.vpk
models/ball/jabulani_edit.vmdl_c
models/jabulani/jabulani.vmdl_c
```

No editable `.vmap` source was present. The nested map contains one entity lump,
`default_ents`, and its decompiled contents have no `point_script` bootstrap.

## Ball and goal observations

The compiled entity lump defines the active ball as:

```text
classname  prop_physics_multiplayer
targetname filter_ball
model      models/ball/jabulani_edit.vmdl
massscale  0.0
spawnflags 5
```

VRF renders compiled target names with its `[PR#]` prefix marker; the live game
resolved the ball name to `filter_ball`. The compiled ball also contains a
`parentname` value `ballon`, but no corresponding targetnamed entity appears in
the decompiled entity lump. We do not infer runtime parenting from that alone.

Relevant goal-side entities include:

| Entity | Class | Origin |
|---|---|---|
| `terro_But` | `trigger_once` | `0 1421.439941 10.15` |
| `ct_But` | `trigger_once` | `0 -1420 10.15` |
| north net-sound witness | `trigger_multiple` | `0 1424 19.5` |
| south net-sound witness | `trigger_multiple` | `0 -1424 19` |

The map also contains counters, team/reset logic, goal buttons, sound witnesses,
and a name filter related to the ball. These are valuable behavior references,
but they do not replace the SoccerMod's server-validated goal-plane check.

## Compiled ball-model physics

The audited VPK contains two football models with materially different physics
payloads:

| Model | Used by live ball | Convex hulls | Compiled bounds | Mass field | Collision group |
|---|---:|---:|---|---:|---|
| `models/ball/jabulani_edit.vmdl` | Yes | 20 | about `37.61 x 37.61 x 37.61` | `0.0` | `ConditionallySolid` |
| `models/jabulani/jabulani.vmdl` | No | 1 | about `28.87 x 29.35 x 29.32` | `1.0` | `default` |

Both report zero linear and angular damping in their embedded PHYS blocks. The
fields are compiled-resource observations, not measured effective runtime
physics. In particular, `mass = 0.0` must not be interpreted without an engine
experiment because the entity and compiler may apply defaults or overrides.

The 20-hull live model is visually useful but is not automatically the best
networked soccer ball. With the compiled package alone, it can provide a manual
native-behavior baseline but not a controlled, instrumented A/B suite. Phase 1
will run repeatable drop, roll, wall, wake, and latency tests on project-owned
low-complexity spherical hulls. Direct controlled comparison with the Stadium
ball remains blocked until we have permitted source or another separately
qualified control-and-capture route.

## Live smoke test

The current CS2 client was launched in insecure developer/VConsole mode and the
map was loaded with:

```text
map_workshop 3361075564 soccer_cssl_stadium_v8
```

Observed console evidence included:

```text
Loading custom game "3361075564" with map "soccer_cssl_stadium_v8"
@ Current : game
prop_physics_multiplayer : filter_ball
filter_activator_name : soccerball_filter
```

The live query returned one matching `prop_physics_multiplayer` line during the
check. The complete raw VConsole stream was not retained, so this is an observed
smoke result rather than the future one-ball acceptance proof. Entity indices
are intentionally not treated as stable evidence. Steam's process log records
that test client exiting with code `0` after the VConsole quit request.

## Consequence for implementation

The Stadium is approved as the Phase 1 reference map. It is not yet an editable
implementation base. The possible decision routes are:

1. obtain the author's `.vmap`/source assets and explicit permission, then add
   the SoccerMod `point_script` and rebuild;
2. develop and qualify the official-script core in our own writable lab, then
   transfer it when source becomes available;
3. separately approve a server-plugin entity-injection approach if direct use
   of the immutable Workshop package becomes a hard requirement.

Route 2 is the current plan because it keeps physics research moving without
claiming ownership or relying on a fragile native hook.

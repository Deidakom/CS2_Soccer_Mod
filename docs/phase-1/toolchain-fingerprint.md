# Installed Workshop Tools fingerprint

Audit date: 2026-08-27

The Phase 1 verifier reports no remaining precondition blocker for CS2 build
`24957633`. Build and target build match, the Stadium reference hash remains
current, and the pure suite passes.

| Artifact | Installed path / value |
|---|---|
| Workshop Tools DLC | `2279721`, enabled |
| Content root | `E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\content` |
| Tools launcher | `game\bin\win64\csgocfg.exe` |
| Resource compiler | `game\bin\win64\resourcecompiler.exe` |
| Hammer module | `game\bin\win64\tools\hammer.dll` |
| Installed script demo | `content\csgo_addons\cs_script_demo` |
| API declaration | `content\csgo_addons\cs_script_demo\maps\scripts\point_script.d.ts` |
| API declaration SHA-256 | `2DA5D7D10FFCEA1AAC52E668CF153974A3D973AEB8E7DC9A15FB8A2227B50BF9` |
| API declaration size | 42,338 bytes, 876 lines |

## Audited declaration drift

The update from build `24934554` to `24957633` changed the declaration's raw
SHA-256 from
`DBB8AE95F12C6F513909A527609A8DF498AE5BB54A2024445A27537B33D61752`
to
`2DA5D7D10FFCEA1AAC52E668CF153974A3D973AEB8E7DC9A15FB8A2227B50BF9`.
An exact comparison found documentation/comment changes only; no declaration
or API signature changed. After removing comments and normalizing the
declaration text, both the old and current files are exactly 14,301 bytes and
have SHA-256
`7EA7AA89027FB3BDF9144E1B3CD37CC76716B047BDEE3F7CE6E96EEDA8544BC1`.
The stager therefore pins the current raw file while allowing only the audited
old raw hash as a known prior staged API artifact.

## Confirmed minimal API surface

The installed declaration directly exposes the Phase 1 fundamentals:

- `FindEntityByName`, `FindEntitiesByName`, and class equivalents;
- `OnKnifeAttack`, with owner recovery through `weapon.GetOwner()`;
- `TraceLine`, `TraceSphere`, and `TraceBox`;
- `SetThink`, `SetNextThink`, `GetGameTime`, and experimental
  `QueueAfterThinks`;
- absolute transform, velocity, and angular-velocity reads;
- `Teleport` and `Move` writes, including atomic angular-zero reset writes;
- round, player, activation, and Tools reload callbacks;
- `Msg` for console telemetry.

The bundled Valve sample demonstrates a linear velocity write with
`Teleport({ velocity })`. The local runtime spike proved that relative module
specifiers are rejected, so the staged adapter is now one deterministic bundle
with only `cs_script/point_script` external. The current packaged build is a
704,728-byte VPK with MD5 `AA0937299D6B39024017AB32D5415BA7` and SHA-256
`F29FA6B00921530B90B2516382DD17FACA3A2C8BF6819D3D78908E017D8B7752`.
Its live-captured diagnostic run includes twelve controlled primary kicks: the
single-profile comparison plus ten contact repeats. All twelve produced one
callback, accepted kick, real motion, and one goal. Eleven contact resets
settled on their first write; the one radius-clearance reset failed closed on
gravity-driven linear velocity. One later manual secondary click also produced
exactly one callback, accepted shot, east goal, and first-write settled contact
reset. This is not yet the repeated
next-physics-tick wake/write or formal 100-reset suite. Phase 1 continues to
prohibit nonzero angular-velocity writes until their units and safety cap are
measured.

## Fingerprinted lab inputs

| Input | SHA-256 |
|---|---|
| Valve `addon_template` vmap | `3514a445f23c54427a37cdb8776d7bac44738a653a3fba0ebfa99e05762e485c` |
| Valve script `tsconfig.json` | `c923105c41bc5020828d32e60b9212a3d6c012e65f6aa8786d1ed72b11df718c` |
| Installed DMXConvert | `4fffab89c45624f251b376c6256f55ff1bc77d4ff48258dc19143fda295ee3ea` |

The audited template floor is at `Z=0` and covers X `-256..1280`, Y
`-976..960`. The lab uses the clear center corridor at `(512,0)`, a nominal
radius-15 ball resting at `Z=15`, and virtual goal markers at X `384` and
`640`. These coordinates belong only to this writable template lab and are not
assumed to describe the compiled Stadium.

The generated vmap was converted from keyvalues2 to Valve binary and back with
the installed DMXConvert. The round trip retained exactly one
`point_script`, one named `prop_physics_multiplayer`, the three named layout
markers, the server-solid physics mode, model/scale, and every `.vjs` asset
reference. Hammer/ResourceCompiler and runtime behavior remain separate gates.

## Confirmed gaps

The declaration has no general entity spawn, force/impulse/torque API, mass or
damping getters, collision bounds/material getters, sleep-state getter, generic
wake method, trace mask, tick number/interval, map-end callback, or entity
deletion callback. Hammer's installed FGD does document the physics-prop `Wake`
input, which the smoke adapter invokes through `EntFireAtTarget`; one live kick
persisted and moved, while the required repeated sleep/wake qualification
remains open.

The initial owned lab candidate references Valve's base-game model
`models/props/de_dust/hr_dust/dust_soccerball/dust_soccer_ball001.vmdl` without
copying it. Its compiled physics payload is one sphere of radius `7.9`, surface
property `soccerball`, mass `1.0`, linear damping `0.0`, angular damping `1.0`,
and collision group `default`. The lab scales it uniformly by
`1.8987341772` to a nominal 30-unit collision diameter; engine verification of
scaled collision and floor clearance is required.

## Supported addon creation boundary

The installed `addon_template` is explicitly marked as a template. The safest
creation path is Workshop Tools Addon Manager: select `addon_template`, choose
**Duplicate**, name it `soccermod_phase1`, and let Valve create both content and
game roots. Only after those roots exist will project files be staged. This
avoids guessing or overwriting Valve-generated addon metadata.

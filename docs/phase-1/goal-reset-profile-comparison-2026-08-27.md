# Goal-reset profile comparison

Date: 2026-08-27

## Decision

Keep `contact` as the Phase 1 goal-reset baseline. Do not promote
`radius_clearance` into the formal reset path.

This is a live result from the packaged CS2 addon, not a unit-test inference.
The contact profile passed the controlled comparison and then passed ten
additional identical primary-kick cycles. The clearance profile failed closed
on its first and only controlled cycle because normal gravity produced
downward velocity before the unchanged next-think write-verification gate.

The earlier captured contact reset that exposed tiny nonzero angular motion is
not discarded. The new result shows that failure is not deterministic for the
controlled pose; it does not yet establish its frequency or cause. Formal
qualification therefore remains open.

## Tested package

| Artifact | Fingerprint |
|---|---|
| CS2 build and target | `24957633` |
| `point_script.d.ts` SHA-256 | `2da5d7d10ffcea1aac52e668cf153974a3d973aeb8e7dc9a15fb8a2227b50bf9` |
| Bundled adapter source | 67,425 bytes; SHA-256 `1068b8c6641ec23cce092b1cdf6ea997b6d91e88b098e39a846064a1fba7328b` |
| Compiled adapter | 68,273 bytes; SHA-256 `51aeedfa590477a724a663f83fb4e91e063c6512dfa2355ac760333e653ec3a` |
| Installed map VPK | 704,728 bytes; MD5 `aa0937299d6b39024017ab32d5415ba7`; SHA-256 `f29fa6b00921530b90b2516382dd17faca3a2c8bf6819d3d78908e017d8b7752` |
| Source VMAP | 231,018 bytes; SHA-256 `34f07c7a6eb40ee6f9367929a2cd3d3f61ba8f3250427575338e2c808ff04653` |

Hammer's Fast build completed and loaded this package in CS2. Runtime status
reported `api_smoke_ready passed:true` before the controlled comparison.

## Controlled input

Both profile trials used the same live state:

- player slot 0, team 3, alive, knife active;
- player position `(452, 0, 0.0312509984)`;
- eye angles `(39.1999054, 0, 0)`;
- ball at approximately `(512, 0, 15.0312138)` and at rest;
- one physical primary click through the game window;
- accepted pass command approximately
  `(525.0943781, 0, 50.8451409)`, final speed `527.5503144`;
- the same east goal plane and unchanged strict reset verifier.

Input sensitivity was temporarily clamped to CS2's minimum `0.0001` to hold
the view steady. It was restored to its exact prior value, `3.21895`, after
testing. Input diagnostics were also disabled after capture.

## Profile results

| Observation | `contact` | `radius_clearance` |
|---|---|---|
| Goal reset command | write/rest Z15 | write Z30, require rest Z15 |
| Goal crossing | accepted, east goal | accepted, east goal |
| Before-write angular vector | `(0, 1156.9819336, 0)` | `(0, 1156.9819336, 0)` |
| Immediately after Teleport | exact requested transform; zero linear and angular vectors | exact requested transform; zero linear and angular vectors |
| Next-think position | Z `15.0059376` | Z `29.8047256` |
| Next-think speed | `3.2782555e-7` | `12.4975214` downward |
| Next-think angular state | exact zero | exact zero |
| Ground descriptor | `none` | `none` |
| Formal write gate | pass | fail: `velocity` |
| Retry | none | none; retry is intentionally angular-only |
| Terminal result | `settled`, pass on write 1 | `write_not_verified`, fail closed |
| Eight-think tail | 8/8 captured; angular zero throughout | 8/8 captured; fell to Z `21.2427597`, speed `111.7976227`; angular zero throughout |

The clearance failure is expected under the present contract: the ball is
airborne, so gravity creates a nonzero velocity before the next script think.
Making that profile pass would require a different semantic operation, such as
a fall-and-settle reset state with different verification timing. That would
be a new design and test contract, not a safe fix to the current atomic reset.

## Ten-cycle contact repeatability batch

After recovering with the always-contact manual reset, ten more physical
primary clicks were issued 1.25 seconds apart from the same pose.

| Metric | Result |
|---|---:|
| Physical clicks | 10 |
| Knife callbacks | 10 |
| Accepted kicks | 10 |
| Goal commits | 10 |
| Reset passes | 10 |
| Reset failures / retries | 0 / 0 |
| Maximum next-think position error | `0.0059375763` |
| Maximum next-think speed | `3.2782555e-7` |
| Maximum settle position error | `0.0146427155` |
| Maximum settle speed | `1.6087040e-7` |
| Terminal-tail samples | 80/80 |
| Nonzero terminal angular samples | 0 |
| Maximum terminal angular magnitude | `0` |
| Ground descriptor set | `none` |

The batch advanced goal sequences 5 through 14 and reset command sequences 11
through 20. Every reset settled on its first write. Final recovery status was
`api_smoke_ready passed:true`, profile `contact`, play enabled, no pending
reset, input probes off, and sensitivity `3.21895`.

## Controlled secondary smoke cycle

A later manual right-click from the same controlled player pose was captured
and checked with the live-run validator using `--attack secondary`.

| Metric | Result |
|---|---|
| Secondary knife callbacks / input edges | 1 / 1 |
| Accepted kick | `kind:"shot"`; velocity `(900, 0, 85.0000000241)`; speed `904.0049779` |
| Goal | east goal; height `17.2532134`; goal sequence 16 |
| Reset | contact; command sequence 24; passed on write 1 |
| Next-think reset state | position error `0.0059375763`; speed `0`; angular zero |
| Settled reset state | position error `0.0146427155`; speed `1.6087040e-7`; angular zero |
| Terminal tail | 8/8 samples; maximum angular magnitude `0` |

The validator reported `passed:true`, no failures, and no parse errors. Final
runtime recovery again reported play enabled, no pending reset, contact active,
input diagnostics off, and sensitivity `3.21895`.

## Consequence for Phase 1

This evidence rejects the proposed radius-clearance strategy under the current
strict atomic-reset contract and supports retaining direct contact placement.
It is strong smoke evidence, but it is not the protocol's 100-goal reset suite,
and the single secondary success is not the 100-secondary callback gate. It
also does not explain the earlier rare nonzero-angular contact result. The next
gate is therefore repeatability and failure characterization, followed by the
remaining physics, multiplayer, lifecycle, clean-delivery, and soak suites.

# Phase 0 report

Date: 2026-08-25

Status: **conditionally ready for setup; not yet ready for Phase 1 execution**

## Outcome

A new CS2 SoccerMod is credible enough to prototype. It is a rewrite across a
new engine and content pipeline, not a SourcePawn port. The workspace contains
a strong behavioral oracle for the CSS version, but the decisive CS2 physics
behavior still has to be proven in a real server-authoritative ball lab.

Phase 0 establishes the following architecture:

1. A Source 2 Workshop addon and map own the authoritative ball, explicit kick
   validation, goal detection, resets, the match state machine, and a fallback
   UI using Valve's official `cs_script` API.
2. A later optional C# server adapter owns only features that need a global
   server context: public/admin commands, permissions, SteamID persistence,
   database work, CSTV, and integrations.
3. The map-level soccer core must remain playable if that adapter is absent or
   temporarily broken after a Valve update.
4. Native C++ hooks are a last-resort fallback, not an MVP dependency.

## Evidence-based decisions

| Decision | Basis |
|---|---|
| Do not use SourceMod for CS2 | Current SourceMod does not target Source 2; the `.sp`/`.smx` implementation cannot be reused as a CS2 binary. |
| Use official map scripting for the core | Valve ships `cs_script` with entity transforms/velocity, traces, input and knife callbacks, entity I/O, timers, team/loadout helpers, and map UI facilities. The installed `point_script.d.ts` will be canonical. |
| Keep Metamod optional | Metamod 2.x supports CS2, but native/plugin stacks can break after game updates. Core gameplay should not inherit that availability risk. |
| Prefer CounterStrikeSharp if a server adapter is needed | It provides the broadest established managed CS2 server API. Its calls must sit behind a narrow project-owned adapter. |
| Do not freeze the full project yet | Documentation proves API availability, not acceptable replicated ball physics under latency. Phase 1 is a hard gate. |

Current references:

- [Valve CS2 update feed](https://store.steampowered.com/news/posts/?appids=730)
- [Metamod:Source official site](https://www.metamodsource.net/)
- [Metamod Source 2 sample](https://github.com/alliedmodders/metamod-source/tree/master/samples/s2_sample_mm)
- [Source2Mod roadmap](https://github.com/alliedmodders/source2mod/issues/2)
- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)
- [SwiftlyS2](https://github.com/swiftly-solution/swiftlys2)

## Baseline policy

There is no single locally reproducible artifact pairing for the latest CSS
runtime and source. Phase 0 therefore uses a layered baseline:

- **Operational baseline:** documented deployed SoccerMod 1.5.10 and
  thirdperson 1.3.0 on `ka_soccer_titans_club_2026`.
- **User-confirmed field baseline:** the stable 1.5.7/1.5.8 pitch,
  grass-replacer, ball, kickoff-wall, and collision behavior.
- **Feature-discovery baseline:** current 1.5.18 experimental SourcePawn source,
  frozen by hash when inspected. Late features found here enter the backlog;
  they are not silently promoted to MVP requirements.
- **Historical specification:** upstream SoMoE-19 1.3.7.1 documentation and
  source.

This avoids losing recent work without claiming that unpaired experimental
source was production-tested.

## Phase 0 completed work

- Located and classified the SourcePawn generations, live configuration
  snapshot, maps, VMFs/BSPs, models, materials, sounds, jersey system, server
  configs, deployment notes, and verification tools.
- Measured the active SourcePawn dependency closure and identified stale or
  duplicate source files outside it.
- Verified the preserved CSS ball geometry, physics reference, team spawns,
  map bounds, package integrity, and overview in the reproducible XSL-derived
  reference map.
- Reconstructed the feature tiers and selected the initial MVP boundary.
- Audited the local CS2 and development-tool installations.
- Defined the Phase 1 ball-lab matrix, objective acceptance checks, and fallback
  order.
- Recorded architectural, maintenance, licensing, security, and provenance
  risks.

## Conditions still required to close Phase 0

1. Install the CS2 Workshop Tools so Hammer, the `content` tree, and Valve's
   current `point_script.d.ts` are locally available.
2. Hash and archive that installed API declaration together with the exact CS2
   build ID used for the ball lab.
3. Prepare a clean dedicated-server target and at least two remote test clients.
   Local listen-server success is not sufficient.
4. Confirm that the new project may use or redistribute each legacy map, ball,
   model, material, sound, and incorporated code element. No project-level
   license was found in the local upstream checkout.
5. Rotate the plaintext credentials present in legacy server configuration
   before committing or sharing the workspace. They are not copied into this
   project.
6. Accept the default scope policy: 1.5.10 behavior is authoritative while
   1.5.18-only behavior is a reviewed backlog. A different choice requires an
   explicit baseline decision.

## Go/no-go status

**Go:** finish the tool installation and prepare the Phase 1 lab.

**Not yet approved:** production server adapter, database migration, final map,
jerseys, third person, or full feature-parity development.


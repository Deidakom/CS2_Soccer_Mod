# Phase 0 risk register

| ID | Risk | Severity | Current likelihood | Mitigation / decision gate |
|---|---|---:|---:|---|
| R1 | Source 2 ball physics does not feel consistent or replicate cleanly under latency. | Critical | High | Phase 1 lab is a hard gate. Compare physics prop variants, then one script-controlled kinematic fallback. Stop before full build if both fail. |
| R2 | Velocity writes do not reliably wake the physics object or are visually late on clients. | Critical | Medium | Instrument kick time, velocity, sleep state where exposed, and client observation. Test 30/60/100 ms plus loss. |
| R3 | High-speed balls tunnel through walls or goals. | High | Medium | Thick triggers plus segment/goal-plane crossing check; speed clamp; 100-shot goal matrix. |
| R4 | Valve changes experimental `cs_script` or custom HUD APIs. | High | High | Compile against the installed declaration, isolate API calls, retain fallback UI, record build/API hashes. |
| R5 | Metamod/managed framework breaks after a CS2 update. | High | High | Keep core map-owned, pin versions, stage updates, rehearse rollback, and make adapter absence non-fatal. |
| R6 | The current CSS source baseline drifts during ongoing experiments. | High | High | Use deployed 1.5.10 as authority, freeze hashes, rerun the Phase 0 verifier, and review later-only features. |
| R7 | Legacy content or incorporated code cannot legally be redistributed. | Critical | Unknown | Complete ownership/license audit before publishing any copied asset or derived source. Prefer new original CS2 content where unclear. |
| R8 | Rebuilding/converting the stadium produces incorrect collision or scale. | High | High | Build a minimal ball lab first; later validate pitch, goal, boundary, and spawn measurements independently of visuals. |
| R9 | Player respawn, team score, round termination, or movement parity is unavailable in map script. | High | Medium | Use an independent long-round soccer state machine; test CS2 cvars; add the smallest managed adapter operation only for proven gaps. |
| R10 | Sprint prediction or movement modification causes jitter/exploits. | High | Medium | Defer sprint to a dedicated Tier 2 spike with server validation; exclude unsafe per-update memory hooks. |
| R11 | Custom HUD is unstable across resolutions or updates. | Medium | High | Keep a simple fallback and test common aspect ratios; never couple match correctness to HUD rendering. |
| R12 | Historical statistics cannot be migrated. | Medium | High | No local SQLite/database snapshot is present. Treat migration as unavailable until a production backup is supplied and verified. |
| R13 | Plaintext legacy server credentials are committed or shared. | Critical | High | Rotate credentials, use secret injection, and never copy legacy server configs into the new project. |
| R14 | Native hooks create crashes and ongoing signature maintenance. | Critical | Medium if adopted | Do not adopt native physics hooks until official physics and kinematic prototypes both fail and a narrow helper is separately approved. |
| R15 | Success is judged only on a listen server. | High | Medium | All gates require the intended dedicated-server OS, remote clients, clean content delivery, latency/loss, reconnect, and soak tests. |

## Immediate stop or re-scope conditions

- Neither a standard physics ball nor the official-script kinematic fallback can
  produce acceptable authoritative behavior under realistic latency.
- Goal crossings cannot be made deterministic without native engine hooks.
- Required movement behavior needs unsafe, update-sensitive memory manipulation.
- Workshop delivery fails on clean clients or requires distributing content for
  which rights are not confirmed.
- The project cannot keep core gameplay working without an update-sensitive
  native plugin.


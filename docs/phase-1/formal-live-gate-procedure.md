# Formal live-gate procedure

Date: 2026-08-27

> Scope correction (2026-08-28): only the primary/left-click run remains a
> product qualification gate. Secondary-run instructions are archived
> diagnostic procedure and must not block MVP work.

## Purpose

This procedure runs the Phase 1 callback, kick, goal, and moving-reset count
gate with physical player input and machine-checked evidence. The active
product gate covers primary attack only. Earlier smoke cycles are not combined
with the formal run.

The runner is external tooling. It does not alter the addon, relax the reset
contract, synthesize a knife callback, or require a Hammer rebuild.

## Runner behavior

`tools/run-phase1-live-gate.mjs` connects through the tested VConsole helper and
keeps that connection for preparation and capture. Before its explicit arming
marker it disables input probes, applies the lab settings, performs a controlled
round restart and uncounted reset, restores the scripted pose, and selects the
knife. It then enables input probes, checks knife readiness,
`api_smoke_ready`, and the active contact profile. Preparation records are not
counted as formal evidence. This single-connection sequence avoids the stale
VConsole `CLOSE_WAIT` behavior observed with short-lived setup clients.

After arming, the runner filters high-volume state/cubemap noise and writes only
structured evidence to a JSONL artifact. It reports progress after each complete
eight-think reset tail and fails closed if measured input edges are less than
1.25 seconds apart.

The gate stops in either condition:

- exactly the requested number of terminal reset cycles has completed; or
- the first disqualifying record appears.

Disqualifying records include a wrong attack type, rejected kick, invalid goal,
non-contact goal reset, retry, failed write/settle stage, incomplete or
cancelled terminal tail, lost ball/reset correlation, nonzero post-write
angular state, malformed telemetry, or count overshoot. A timeout or missing
in-map preflight also fails. The runner never continues over the first known
failure.

At shutdown it asks the live script to disable input probes before ending the
VConsole capture. If the map script is absent, such as while CS2 is at its main
menu, it uses a bounded process-termination fallback and records that result.

## Preconditions for each run

1. Load `soccermod_phase1_lab` from the current packaged addon.
2. Require current build/target `24957633`, current API SHA-256
   `2da5d7d10ffcea1aac52e668cf153974a3d973aeb8e7dc9a15fb8a2227b50bf9`,
   and `api_smoke_ready passed:true`.
3. Use `mp_ignore_round_win_conditions 1`, no bots, team 3, and an alive slot 0.
4. Select `sm2lab_goal_reset_profile contact` and complete a manual contact
   reset before capture.
5. Put slot 0 at `(452,0,0.031251)`, eye angles
   `(39.199905,0,0)`, and select the knife through
   `sm2lab_prepare_player 0`.
6. Use sensitivity `0.0001` only during controlled input, retaining the exact
   prior value for restoration. The current operator value is `3.21895`.
7. Do not move, jump, crouch, switch weapons, open the console, or issue the
   other attack type during a gate.

## Commands

From the repository root in PowerShell:

```powershell
$sm2Node = 'C:\Users\sergi\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
& $sm2Node '.\tools\run-phase1-live-gate.mjs' --trials 100 --attack primary --profile contact --timeout-ms 900000
```

For the separate secondary run:

```powershell
$sm2Node = 'C:\Users\sergi\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
& $sm2Node '.\tools\run-phase1-live-gate.mjs' --trials 100 --attack secondary --profile contact --timeout-ms 900000
```

Without `--output`, artifacts are created below `artifacts/phase1-live`. Each
run produces a filtered `.jsonl` evidence file and a
`.jsonl.summary.json` result. A successful result requires both the live
fail-fast tracker and the post-run correlation analyzer to pass.

## Manual input

- Primary run: press left mouse once per cycle.
- Secondary run: press right mouse once per cycle.
- Wait until the ball visibly returns to the center and the knife is ready.
  The proven controlled cadence is at least 1.25 seconds between inputs.
- Watch terminal progress in the form `[gate] completed N/100`.
- Stop clicking immediately if the runner exits or reports `fail-closed`.

Do not estimate or repair the count manually. The gate completes only when the
runner observes the 100th correlated `reset_post_terminal_complete` record.

## Acceptance

For each attack type, the summary must report:

- `passed:true`;
- exactly 100 callbacks, matching input edges, accepted kicks, kick writes,
  goal candidates, goal commits, reset begins, write verifications, settle
  verifications, reset ends, and terminal completions;
- exactly 300 reset snapshots and 800 terminal samples;
- 100 unique matching goal/reset sequence relationships;
- zero retries, failures, cancellations, parse errors, or count overshoots;
- every reset settled on write 1 with exact post-write angular zero.

After each run, restore sensitivity to the retained exact value and explicitly
send `sm2lab_probe_inputs off`. Query `sm2lab_status` and require play enabled,
no pending reset, and `api_smoke_ready passed:true`.

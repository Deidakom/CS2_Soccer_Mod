# German server queue update — 2026-09-05

The owner explicitly authorized deployment and committing/pushing all queued
updates, superseding the earlier deployment/publication hold. Original custom
sounds, jerseys, grass and prop artwork remain deferred.

## Shipped source and binary

CS2 build identity: **1.4.2-dev**. The development suffix is retained because
client-observed UI and multiplayer parity checks remain open.

- Ball workbench: 46 tuning controls, validated exact/step editing, presets,
  startup checkpoint, undo, valid zero-value persistence and warmup ball controls.
- Kickoff outline: no automatic expiry, rebuild after round restart and repair
  of removed beam segments; actual kickoff activity/contact releases the wall.
- Compact private sprint bar; separate from menu/score center text. Existing
  personal HUD preferences are retained; use `!sprintbar on` to enable it.
- Spectator menu setup: the owner confirmed the client convar remedy. Updated
  help, spectator hints and optional client config; no claim of bind-free keys.
- Referee/CAP, settings, dead-chat recipient routing and training-layout queue
  fixes described in the [queue record](../menu-queue-2026-09-05.md).
- CS:S source: referee KeyValues handle cleanup and corrected changelog heading.

## Validation

Local: CS2 105 Node tests, 15 Python tests, 99 managed scenarios; CS:S 12 Node
and 10 Python tests, all eight SourcePawn plugins compiled, release package hash
verification passed. Existing SourcePawn warnings remain. Both repositories
passed whitespace checks; the pending files were scanned for token/private-key
patterns with no matches.

At 09:59:08 UTC the installer backed up the full live plugin directory, installed
the checked DLL and restarted `cs2-soccermod-test.service`. All nine plugins loaded,
including CS2 SoccerMod 1.4.2-dev. All 46 runtime tuning values were read back;
the owner's **legacy** profile and existing numeric tuning were preserved.

The live `ballPushMaxSpeed` zero-value edit saved successfully and undo restored
it. Kickoff preview created 36 segments before a requested round restart.
The restriction and all 36 segments remained active after that round restart
and beyond the former ten-second expiry. Explicit preview off returned the
count to zero. Readback confirmed push speed restored to 396. No startup/runtime
failure markers were found in the service journal; the Before workbench preset
exists. Final service state was active, with no paused ball or training devices.

DLL SHA-256:

```text
1c4de1875b33ce18a026c0f4703b981e3e2565a0dd8ca3c0d86c3107ce4b90c5
```

## Rollback

Run on the German server as root:

```sh
bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260905T095908Z-3Pdhz8/rollback.sh
```

This restores the prior 1.4.1-dev DLL and ball/match/menu settings. It preserves
current ranks, admins, bans, competitive history, training layouts and saved
ball presets. Older rollback snapshots remain available.

The host runs CS2, not a CS:S game service. CS:S changes are compiled and published
in its GitHub repository; no SourcePawn binary is installed into the CS2 service.

Client-visible sprint orientation/size/smoothness, kickoff line visibility and
menu input still need connected-player observation. Group CAP/ready/dead-chat
checks remain separate from server startup and headless test evidence.

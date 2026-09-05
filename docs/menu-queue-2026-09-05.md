# Menu queue after the 1.4.0 deployment

> Publication update: the owner authorized deployment and both GitHub pushes.
> This batch is deployed as 1.4.2-dev; see the [deployment report](deployments/2026-09-05-queue-update.md).
> Earlier local/hold statements below record the development history.

The owner requested that the remaining queue be worked before any further
commits or pushes. The existing CS2 `ec05a7c` and CS:S `03e7a5c` commits had
already been pushed before that instruction arrived. Changes below are in the
working trees and are not a published release.

The owner explicitly deferred original sounds, jerseys, grass and prop artwork.
They are outside this batch; no Windows Workshop Tools access is currently
needed for the work below.

## Latest queue instruction and Ball workbench

The owner subsequently requested that the queue be finished **before any new
deployment**, as well as keeping commits and pushes until the end. The new
[Ball workbench](ball-workbench-2026-09-05.md) is a local 1.4.2-dev change with
46 controls, presets, undo and warmup ball controls. It has not been deployed.
The 1.4.1-dev runtime evidence below describes the earlier deployment only.

## Kickoff outline correction (local, deployment held)

The owner reported the line disappearing during the match-start round restart
and disappearing without a ball touch. The kickoff code had a ten-second
expiry and created its 36 beam segments only when initially armed. Round
cleanup could remove those entities while the restriction remained active.

The local fix removes automatic expiry, redraws after round start using the
current restriction state, and repairs missing/invalid beam segments during
tick maintenance. Healthy outlines are retained without timed redraws. An
accepted playing-team contact or actual kickoff-clock activation removes both
the outline and restriction. Match stop/end, map cleanup and explicit disabling
still clean them up. The diagnostic preview now requires touch or explicit off.

Regression coverage includes event-driven lifetime, contact release, no
resurrection after completion, round callback wiring and invalid-beam repair
wiring. Actual client visibility across a round restart remains to be checked
in the later deployment/testing stage. No server change was made for this fix.

## Compact sprint bar (local, deployment held)

The [sprint bar](sprint-bar-2026-09-05.md) adds a small player-only display below
the crosshair with ten segments and a percentage, actual stamina/recharge,
menu/death/CAP hiding and entity cleanup. It replaces the competing center-text
sprint status. New preferences default to activity-only; saved choices remain
intact and `!sprintbar on` enables it. Build, 103 Node tests and 99 managed
scenarios pass. Final client appearance and movement stability remain unverified.

## Spectator menu input

The owner confirmed that `spec_usenumberkeys_nobinds 0` restored number-key
input in spectator mode. The [spectator input fix](spectator-menu-2026-09-05.md)
adds corrected setup instructions, `!menukeys`, a spectator hint and optional
client config. Existing `!1` through `!9` chat selection also works without
number-key binds. This client-only setting is not a server.cfg default; automatic
client application has not been verified. Local build and 105 Node tests pass.

## Implemented in the development batch

| Item | Result |
| --- | --- |
| Remove yellow / red cards | Separate menus list persisted cards, including offline players; selection revalidates permission and current card state by SteamID. |
| Referee score reset | Score submenu has add/remove CT/T and confirmed reset; command supports `css_refscore reset`. Hostname and scoreboard update together. |
| Referee permissions | Every mutation rechecks match permission, including callbacks retained while permissions change. Console attribution is `Console`, not the target player. |
| Referee persistence | Card changes are saved before applying the sending-off; failed writes restore the previous in-memory store. A third yellow cannot undo a red. |
| Sending-off versus CAP | Red cards take priority over website/in-game draft assignments, respawns and team-join shortcuts. Red-carded players are excluded from draft eligibility. |
| Killfeed | Persisted Misc toggle; disabling it suppresses death-event broadcasts while retaining CAP fight feedback and internal death handling. Default remains enabled to preserve existing CS2 behavior. |
| GK saves only | Persisted Misc toggle; matches CS:S's fallback to ordinary saves when no keeper is assigned on that side. Default off; a player who switches sides before credit is finalized cannot receive the old side’s save. |
| In-game CAP switch | Persisted Misc toggle guards entry points and callbacks; does not disable the website CAP system. Default on. |
| Dead-chat visibility | Default / Teammates / Everyone choices add missing recipients to actual SayText2 messages, preserving their original formatting. Everyone explicitly includes team chat. Mode off remains the default. |
| Card log category | Disabling card logging now filters card events from the match log as well as hiding its card view. |
| Saved training layouts | Confirmed deletion frees saved-layout capacity; failed saves/deletes report failure and restore previous data. |
| CS:S referee leak | `PlayerHasCard` now closes its KeyValues handle on every result. |

Dead-chat API verification used the installed CounterStrikeSharp 1.0.373
assembly and its [UserMessage implementation](https://github.com/roflmuffin/CounterStrikeSharp/blob/main/managed/CounterStrikeSharp.API/Modules/UserMessages/UserMessage.cs).
The [game protobuf schema](https://github.com/SteamDatabase/Protobufs/blob/master/csgo/usermessages.proto)
identifies base message 118 and its `chat`, `entityindex` and `messagename` fields.
The old statement that this API cannot modify recipients was incorrect.
The code only extends messages correlated with a recent player `say`/`say_team`
command and validates the author SteamID, preventing slot reuse from confusing
the audience selection. It does not synthesize or rebroadcast chat text.

## Validation and publication status

Local checks: 100 Node tests, 15 Python tests, 73 managed gameplay scenarios.
The added managed cases cover referee card transitions and the dead-chat
recipient matrix. CS:S compiled all eight plugins with no errors, and its
12 Node / 10 Python tests and release-package hash verification passed;
existing SourcePawn warnings remain. The earlier pushed CS:S CI run exposed a
missing changelog version heading. The working-tree changelog now matches the
plugin version and that test passes locally. GitHub remains red until these
changes are eventually committed/pushed. The earlier CS2 CI run passed.

Runtime build identity: **1.4.1-dev**. This deliberately distinguishes the
working-tree deployment from the already committed 1.4.0. The final correction
was installed at 09:08:41 UTC. The 09:03:02 snapshot below is the rollback to
1.4.0; the later 09:08:41 snapshot contains an intermediate development build.

Live verification: final cold start loaded successfully at 09:08:46 UTC, with
no plugin exception observed in the following checks. The SayText2 diagnostic
reported `CUserMessageSayText2`, accepted all three expected fields and retained
zero recipients without sending a message. Referee commands produced scores
1–0, 0–0, 0–1, then 0–0 after reset. Final phase was Warmup with no training
devices or paused ball. The owner had not reconnected for the menu audit;
connected-player branch construction and visual/input confirmation remain open.

SHA-256:

```text
c2c9b1e939152a05c594264370a300751a8ac79186045031a6cf8a9b44917e8b
```

Rollback to the tested 1.4.0 binary and its settings, on the German server:

```sh
bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260905T090302Z-D1eGs1/rollback.sh
```

The earlier [1.4.0 deployment report](deployments/2026-09-05-parity-completion.md)
contains the separate rollback to 1.3.0. Both snapshots keep current ranks,
competitive-history sidecar, administrators, bans and layouts when rolling back
the binary/gameplay settings.

## Remaining validation and platform differences

- A menu construction audit checks live branches for a selected connected player
  and returns them to the main menu. Client-visible rendering and number-key
  input still require an in-game observation.
- The SayText2 schema diagnostic does not send any message. Dead-chat recipient
  behavior needs multiple connected players to verify across teams/alive/dead
  states; pure recipient rules alone are not evidence of client delivery.
- Full CAP drafts, ready checks and optional celebration weapons need a group
  play test. They cannot be fully exercised by the single connected owner.
- Source 1-specific class selection, dissolve effects and its tickrate extension
  are not claimed as identical CS2 features. Ragdoll behavior and arbitrary HUD
  placement remain separate engine/client work.
- Arbitrary map support still requires measured goals, reset positions and
  boundaries; the current calibrated stadium remains the supported live map.
- Original client assets are deferred at the owner's request, including the
  referee whistle and physical hoop artwork.

No further commit or push is part of this development-batch validation.

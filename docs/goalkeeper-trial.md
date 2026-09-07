# Goalkeeper trial (retired)

Removed on 2026-09-07 at the user's request. The commands, menu, dives,
catches, parries and throws below are historical and no longer available.
Use the [GK box sprint replacement](goalkeeper-sprint.md) instead: `!gk`
plus normal sprint controls, with no extra opt-in or trial menu.

## Historical prototype

This is an opt-in server-plugin prototype. It is disabled by default and resets
to disabled on map change, plugin reload or server restart. It needs no Workshop
assets. It retains normal player collision and has no custom dive/hand animation.

## Start and controls

1. A match administrator runs `!gktest on` (RCON: `css_gktest on`).
2. An alive player claims their team's goalkeeper role with `!gk`.
3. That keeper opts in with `!gktrial on`.

`!gktrial` opens the controls/stats menu. It is also available under
Settings → Goalkeeper trial. Commands can be bound to keys of the player's choice:

| Console command | Action |
| --- | --- |
| `css_gkdive left` / `css_gkdive right` | Short lateral movement burst relative to view yaw |
| `css_gkcatch` | Arm catching/parrying for 0.30 seconds |
| `css_gkthrow short` / `css_gkthrow long` | Distribute a caught ball |
| Right-click while holding | Short release |
| Left-click while holding | Long release |
| `css_gktrial off` | Opt out |
| `css_gktest off` | Administrator disables the trial and releases possession |

No client key bindings are changed automatically. Normal knife clicks work as
before when not holding a ball or recovering from a dive.

## Initial balance

- Only designated, opted-in keepers, in warmup or live play, inside their own
  calibrated GK save box. This is the existing 250-unit half-width, 220-unit depth
  box, not a claim that it matches every map's painted penalty area. The area
  follows the defending end and the current GK-area settings.
- Dive: 440 units/s for 0.22 seconds, then 0.55 seconds of recovery capped to
  100 units/s horizontally. Four-second cooldown; grounded start only. No
  kicks or new catch activation during recovery. Arm catch before diving to
  combine them. Normal world collision remains responsible for blocking movement.
- Catch: 65-unit eye-to-ball-centre reach, forward-facing 60-degree cone,
  brush visibility check and swept contact detection. Two-second cooldown.
- Up to 700 units/s incoming speed: catch. Faster balls: aimed parry with 55%
  retained speed, capped to 1000. No speed boost or automatic save.
- Caught ball stays stationary **at its catch location** for this prototype;
  it is not parented to the hand or carried. Release within three seconds.
  Moving more than 85 units away also drops it. Physical simulation resumes
  on release; short/long release speeds are 650/1250 units/s along view aim.
- A teammate's last deliberate knife kick/keeper distribution blocks handling;
  an intervening opponent touch clears that backpass chain.
- Trial counters show catches, parries and distributions received by a teammate
  within eight seconds and at least 200 units away, without an intervening touch.
  Full-match clean sheets require participation from match start, uninterrupted
  role/opt-in, no conceded goals and normal full time (not stop/forfeit).
  Existing save detection and rewards remain; no new ranked point bonuses.
- Trial role follows the automatic halftime team swap. Ordinary team changes,
  role release and disconnect remove participation. Death releases possession;
  an opted-in keeper can continue after respawn. Pauses, rounds and ball resets
  clear actions/possession. Match stop/full time also clear actions/possession.

## Verification and playtest

Managed checks cover both ends' area bounds, goal-line exclusion, facing/reach,
swept/stationary contacts, backpasses and production reset/disable state. Existing
managed regression checks also pass. These do not establish how the movement
burst feels or how the client renders the caught ball.

In-game testing remains with the user. Start with a stationary/slow opponent ball,
then fast shots; try each release; test a backpass; miss a dive and check recovery;
leave the box, die, pause and stop while holding; and test both keepers through
halftime. Turn the trial off immediately if a held ball or movement behaves oddly.

The existing Node suite has one unrelated Windows CRLF-sensitive spectator-bind
assertion failure; its bindings are present. No spectator configuration was changed.

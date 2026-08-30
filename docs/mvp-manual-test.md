# SoccerMod playable-MVP manual test

Date: 2026-08-27

This is a short gameplay gate, not another calibration campaign. One clean
pass is enough to decide what should be tuned next.

## Build and load

1. Open `soccermod_phase1_lab.vmap` in Hammer.
2. Press F9, select **Fast**, enable **Load in Engine**, and build.
3. Confirm the map loads without a JavaScript exception and shows the match
   and cap status near the top of the screen.

The staged runtime adapter must have SHA-256
`FE3C0E98F15884B44244DF3AFBBA3F97E52F99A793EC996D30EAA0251ABA75D0`.

## One-player smoke

1. Join T or CT and equip the knife.
2. Primary attack (left click) near the ball: it must make the single
   CSS-style kick. Secondary attack and crouching must not add a separate shot
   or lob action.
3. Enter `!start` in chat. After the three-second countdown, the clock must
   run and the ball must become playable.
4. Enter `!pause`; kicks must be blocked and the clock must stop. Enter
   `!resume`; both must resume.
5. Put the ball through either goal. The correct team score must increase once,
   followed by a short pause and a center reset.
6. Enter `!stop`; the map must return to warmup with free ball interaction.

## Two-player cap and match

1. Player A enters `!cap`.
2. Player B enters `!join`.
3. Player A enters `!draft`.
4. Player A must be assigned to T, Player B to CT, and the match countdown
   must start automatically.
5. Play for several minutes and test the left-click kick, both goals, pause,
   resume, and restart.
6. Disconnect one participant. The cap must cancel safely and the match must
   return to warmup.

For more than two players, `!teams` shows the available player slots. The two
captains alternate `!pick <slot>` until the draft is complete.

## Report only gameplay blockers

Record whether each item passed, plus concise observations about:

- left-click kick strength;
- kick range and responsiveness;
- rolling/bouncing feel;
- goal/reset behavior;
- score/timer readability;
- team assignment and cap flow.

Exact CS:S parity, UI polish, sounds, menus, long soak runs, and exhaustive
physics repetition remain later milestones unless one of them blocks play.

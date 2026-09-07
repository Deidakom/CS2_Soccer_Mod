# Low rebound ghost hits — local candidate, not deployed

User reports knife feedback without a shot when meeting a low wall rebound.
Screenshots do not identify a particular server tick, so these are code-confirmed
failure paths, not a claim of an in-game reproduction.

## Changes

- Kick aim qualification intersects the configured cone with the ball sphere
  instead of testing only its centre. A ball 10 units ahead and 45 below the
  eyes previously failed the 70-degree cone despite its edge being inside.
  Radius is the existing collision radius; reach, cooldown, obstruction,
  knife eligibility and kickoff restrictions are unchanged. Centres behind
  the aim remain rejected. No delayed/automatic swing retry was added.
- Normal and wall-pop kicks clear pre-kick impact history for match and training
  balls and reset the legacy settle counter. Both body response paths and
  legacy/shared settle/wall processing respect kick ownership for that tick.
  This prevents old incoming samples overwriting a newly applied kick. Ordinary
  physics and subsequent-tick contacts remain enabled.
- Native knife particles alone are not proof that the plugin accepted a kick.
  Existing logs also include cooldown rejections; the 0.48-second gameplay
  cooldown is deliberately not removed.

## Validation

- Release build: success, zero errors/warnings.
- Managed regressions: all pass, including 15 new low-ball geometry cases.
  NuGet vulnerability-feed lookup warned due to unavailable network access.
- Three new integration/source wiring checks pass.
- Full Node suite: 107 pass, one existing unrelated Windows CRLF-sensitive
  spectator-bind assertion fails (`test/spectator-menu.test.js:12`).
- `git diff --check`: passes (line-ending conversion notices only).
- Candidate DLL SHA256:
  `4a9b82af675c9a5a24892130a27bf5406e1452664956e002a431260dea78bccf`.

## Deployment status and eventual user check

NOT deployed or restarted: user asked to finish the queue before deployment.
After the combined deployment, check repeated low wall returns while standing
and crouching, both buttons, match/training balls; compare knee-height and high
volleys. Check legitimate misses, walls, and cooldown remain enforced. If it
recurs, correlate `kick_rejected` versus `kick_accepted` and subsequent impact
logs at precise timestamps before changing timing or native collision behavior.

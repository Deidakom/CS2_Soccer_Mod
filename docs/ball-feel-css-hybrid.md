# CS:S-inspired ball feel — 2026-09-07

Reference reviewed: the user's local CS:S recording `Counter-Strike Source
2026.09.07 - 12.43.19.07.mp4`, including early cushioning, close volleys and
wall-to-ground sequences. These are deliberately chosen hybrid settings, not
claims of exact physics coefficients measured from that video.

## Contact

- The inner 90% of knife surface reach keeps normal strength. The outer 10%
  smoothly blends into a tip touch only when the ball is airborne and moving toward the
  player's body and closing relative to the player's movement. At maximum
  reach it brakes incoming motion first; a strong incoming ball can stop.
  Grounded, stationary, sideways and outgoing balls bypass both the reach-power penalty
  and cushioning. Running after a ball does not qualify. Tiny resting jitter
  is ignored. A ball falling toward the player from above qualifies; a ground
  pass coming toward the player does not. Existing aim-based soft-pass and
  pitch controls are unchanged; only the early-contact penalty is restricted.
- Existing valid hits execute immediately. A missed initial sample gets up to
  80 ms to enter actual knife reach, with the original aim locked and at most
  eight degrees of allowed view movement. This is not an 80 ms delay on hits.
- One accepted kick consumes the swing. Pawn/weapon changes, cooldown, blocked
  line of sight, pause and kickoff restrictions cancel it. Range/cone checks
  remain mandatory on every sample; this does not extend knife range.
- Previous low-ball contact ownership/history fixes remain. Volleys retain
  their existing three-degree drift bound.

## Pushback

- Desired transfer is 65% of incoming ball speed, starting at 150 u/s and capped
  at 1750 u/s. Player velocity opposing the ball no longer cancels this target.
- A twelve-tick pulse decays to 75% of its initial target; it restores missing
  velocity rather than repeatedly adding an impulse. Pawn identity, team,
  death, pause and sequence checks stop stale callbacks.
- Closing direction is checked at swept capsule entry rather than after a fast
  ball has passed the player. A simultaneous valid knife hit retains a genuine
  body push, but no body deflection can overwrite the resulting deliberate shot.
- No camera shake or health loss. `impact_motion` logs observed displacement
  and velocity separately from the requested impulse; requested force alone is
  not proof of a client-visible result.

## Walls, landing and rolling

- User-requested rollback to pre-September-7 wall behaviour: retention 0.18,
  additive vertical conversion 0.159, maximum added vertical speed 200 u/s,
  cooldown 0.35 s. Values come from the first September 7 pre-deploy backup,
  which contains the September 5 DLL that was still live on September 6.
- Both profiles use the original additive wall lift, without today's flat-hop
  or near-floor wall-lift limiters. Legacy (the live profile) restores the old
  four-frame XYZ separation and wall-only spin impulse. New-contact, pawn/ball
  identity and pause safeguards remain. The wall-pop kick's extra 35%-reach
  restriction is removed; its previous angle/chance/full-power gates remain.
- A detected landing on a nearby flat static floor cannot rebound upward above
  55% of its sampled incoming downward speed. Horizontal motion is preserved;
  nearby player contacts, recent kicks and teleports are excluded. The restored
  wall response is also excluded during its four separation frames so this
  newer landing limiter cannot immediately undo the rollback.
- Clear-ground slow rollout uses 6 u/s² decay with an absolute twenty-second
  assistance budget from the episode's original speed. A recorded low-speed
  roll can bridge a native hull-induced stop below 70 u/s. Initial resting balls
  are never started; stale history, walls, nearby players, reversals and new
  contacts stop/invalidate assistance. It ends below 4 u/s, with no speed floor.
- Existing model/visibility foundation, global friction, gravity and spin
  settings are unchanged. No Workshop asset or map rebuild is required.

## Goalkeeper replacement

The whole trial is removed. See [goalkeeper sprint](goalkeeper-sprint.md): `!gk`
holders use normal sprint controls for unlimited 1.175× speed in their own
small box, with normal stamina/speed outside. No dives, catches or throws.

## Verification

Shared or personal cannon activation automatically suppresses goal detection
before the first shot. Suppression lasts until the last cannon stops (including
disconnect, round cleanup and failed cannon shutdown). The administrator's
manual Disable Goals setting remains independent and is not reset by this.
The menu identifies cannon suppression and cannot override it while one runs.

Managed tests cover the numeric rules and lifecycle regressions. Node tests
cover production wiring and removal of all trial hooks. Use RCON
`css_sm2ball_feel` to confirm the loaded revision and live key settings.
Live gameplay feel still needs player confirmation; neither screenshots nor
server command responses establish that subjective result.

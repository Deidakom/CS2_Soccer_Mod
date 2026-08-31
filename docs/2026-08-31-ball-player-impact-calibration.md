# Ball-to-player impact calibration — 2026-08-31

## What CS:S actually did

The original SoMoE-19 plugin did not calculate player knockback. The XSL
`func_physbox` ball and the Source physics solver produced it. SoMoE's
`OnTakeDamageArmor` hook changed physics-prop damage to `0`, and the default
configuration also enabled health godmode. Thus the remembered “hurt” feel
was motion/client feedback, not health loss.

The in-repo CS:S probe now provides `sm_xslref_impact <speed>`. It creates an
isolated exact-XSL ball and a temporary fake player, captures the immediate
post-contact velocity, health, `m_takedamage`, and any `player_hurt` event,
then removes both entities.

| Ball launch speed | Immediate player planar speed | Transfer |
|---:|---:|---:|
| 300 | 141.004 | 0.470 |
| 600 | 294.993 | 0.492 |
| 900 | 443.368 | 0.493 |
| 1200 | 590.032 | 0.492 |
| 1500 | 740.999 | 0.494 |

Every trial kept health at `100`, reported `m_takedamage=0`, and fired no
`player_hurt` event. The result is a near-linear **0.50 player-velocity
transfer**, with no observed cap through 1500 u/s.

## CS2 implementation

- Keep `BallImpactPlayerPushRatio = 0.50`.
- Raise the old invented 250 u/s cap to 1750 u/s, preserving the measured
  linear response through the configured 3500 u/s maximum ball speed.
- Continue blocking real ball damage in `OnPlayerTakeDamagePre`.
- Add a client-only `Damage` user message scaled from the same push strength.
  It provides a hurt cue without changing health or synthesizing a
  `player_hurt` gameplay event. The initially shipped `Shake` message was
  removed after live user feedback; physical knockback is the only motion
  effect.
- Tune with:
  - `css_sm2ball_impact_push <minSpeed> <ratio> <max>`
  - `css_sm2ball_impact_feedback <on|off> [maxVisualDamage]`

# Balanced ball handling — implemented and deployed

User explicitly authorized implementing and deploying the earlier physics
proposal and distance-based power, retaining all preceding fixes.

## Changes

- Knife impulse scales smoothly from full power inside 35% of surface reach
  to 35% at maximum reach, on ground and in air. This scales the added impulse,
  not inherited momentum. Close crouched/standing differences remain. Outer
  reach cannot activate the fixed-power wall-pop bypass.
- Horizontal acceptance is capped at a 45-degree half-cone plus ball-surface
  angular radius. Existing vertical/low-ball forgiveness and reach checks stay.
- Legacy kick/rebound spin now transforms the desired world spin into the
  ball's local frame and subtracts observed current spin before applying the
  additive native impulse. No blind correction without recent readback.
- Automatic wall lift set to zero in saved live settings, with normal rebound
  minimum retention 0.25. This is a hybrid tuning choice, not a measured CSS
  constant. The deliberate close wall-pop remains separate.
- Slow rolling assistance only for grounded 8–220 u/s motion, low vertical
  speed, away from nearby players and obstacles, after contact protection.
  It does not revive a stopped ball, restore large collision losses, or affect
  held/paused balls. Velocity support cannot exceed a fixed allowance decaying
  at 12 u/s² from initial rolling speed, expiring within 12 seconds. Transient
  native speed fluctuations cannot renew it. New contacts/reset clear history.
- `css_sm2ball_rollassist off` disables only rollout assistance for the session.

## Verification and deployment

Managed regressions including new reach/aim/spin/rollout checks pass. Eight
focused source-wiring tests pass. Full Node suite retains the unrelated
Windows CRLF-sensitive spectator bind assertion failure. No client gameplay
test was performed.

Initial live rollout trial revealed a sustained ~45 u/s plateau: native
speed fluctuations renewed per-frame assistance. This was corrected with an
absolute per-contact time/speed allowance and redeployed; do not restore the
first candidate as a final solution.

Final DLL SHA256:
`04a47dd13910e152a05723385f13c3141e9e8daa485355dc6e55c351738d4728`

Final deployment at 10:28:45 UTC, service `cs2-soccermod-test.service`,
212.87.212.58:27017. Stadium loaded, legacy profile preserved, GK trial ON.
Confirmed wall settings survive restart: ratio 0, added vertical 0, retention .25.

Pre-feature stable rollback (restores binary AND prior tuning):
`bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260907T102508Z-bD2VZm/rollback.sh`

Intermediate backup (contains first rollout candidate, NOT preferred rollback):
`/home/gameserver/cs2-soccermod-backups/ball-handling-20260907T102845Z-ER1r58`

Final live empty-server roll trial (150 u/s launch): measured speed 64.9 at
2s, 50.3 at 4s, 47.5 at 6s, 37.3 at 8s, 26.3 at 10s, 11.4 at 12s, and
below 1 u/s from 14–20s (residual facet jitter, not continuing travel).
Unlike the first candidate it does not maintain the ~45 u/s plateau.
This is a controlled rollout check, not validation of every player/knife case.
No plugin exceptions were found in the checked final-startup/trial interval.

Final wall trial (1400 u/s launch) reached the side wall around 1.6s, returned
at about 153 u/s horizontally, with a native hop peaking around Z=11 versus
resting Z=-13.3 (roughly 24 units). After landing at ~2.0s, sampled speed was
50.4 at 2.11s and 40.3 at 2.91s; the trial completed stopped. Extra wall lift
remains zero; native hull bounce is intentionally not flattened entirely.
The ball was reset to midfield after the completed checks.

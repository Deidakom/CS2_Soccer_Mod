# Goalkeeper box sprint

Replaces the removed GK trial. There are no dives, catches, parries, held balls,
throws, recovery locks, trial counters, trial commands or trial menu.

- Claim your team's keeper skin with `!gk` (one keeper per team).
- Use your usual `!sprint` / `+use` controls, including Hold/Toggle preferences.
- Inside your own small goalkeeper box, sprint is unlimited at **1.175×**
  ordinary movement speed: 70% of the normal 1.25× sprint's extra speed.
  Multiplying total sprint speed by 0.7 would be slower than ordinary running.
- The region uses the calibrated `GkBoxFor` save box, not the large penalty area.
  It includes a two-unit floor tolerance, and is evaluated every tick from the
  player's feet, current team and current `!gk` assignment.
- Warmup and live play qualify; pauses, cap fights, dead players, spectators,
  non-keepers and the opposing box do not.
- Normal stamina is not consumed while the free sprint is active, nor refilled
  on entry. Leaving/releasing the skin restores normal stamina rules immediately:
  continue at normal sprint speed if stamina allows, otherwise stop. Entering
  with exhausted stamina still permits box sprint, but does not erase exhaustion.
- The stamina countdown is hidden inside the box because the special sprint
  has no countdown; HUD preferences outside the box are unchanged.
- The legacy burst profile also supports the perk. Remaining burst/cooldown
  time is frozen while in the box, then resumed outside; it is not reset by
  crossing the boundary.
- Halftime team swaps preserve both keeper assignments and apply the perk to
  the new team's defending box. Normal team changes release the assignment;
  deaths/respawns reset sprint, and disconnects/map changes clear transient state.

This changes keeper movement only. No ball-physics or hit-response tuning is
included beyond removing the old trial's possession/input interception.

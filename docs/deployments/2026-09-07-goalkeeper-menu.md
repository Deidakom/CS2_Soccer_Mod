# Goalkeeper trial menu repair — queued, not deployed

The six ability/participation menu callbacks used
`ExecuteClientCommandFromServer("css_...")` to route server plugin commands
through the client. Replaced this with direct server-side actions shared by
the registered chat/console commands and menu callbacks.

Join/leave refreshes the menu label and reports the result. Ability actions
report rejection reasons (trial disabled, not opted in, role/alive requirement,
kickoff/pause, own keeper area, cooldown/recovery, or no held ball). Existing
gameplay eligibility, possession, cooldown preservation and release safety
checks remain in effect; no perk balance or input binding changes.

Release build succeeds with zero errors/warnings. All managed regressions and
the five focused menu/low-ball source-wiring checks pass. Managed dependency
audit emitted the existing unavailable NuGet vulnerability-feed warning.
An actual CS2 menu selection has not been exercised in this session.

No server changes or deployment: keep queued with the low-ball ghost-hit fix.
The broader ball-behaviour proposal and distance-based kick power remain
design proposals, not implemented as part of this repair.

# GK dive grounding and feedback — 2026-09-07

User received the ambiguous combined dive rejection message. Previous code
gated on historical `OnGroundLastTick`; changed to current `GroundEntity.IsValid`,
matching the existing jump-blocker implementation. No claim that the exact
reported event was reproduced: its original message did not record the blocker.

Each gate now gives its own message, with seconds remaining for recovery and
cooldown. Logs capture current ground entity validity, historical ground flag,
remaining timers, holding state and result on each attempted dive.
No changes to 4-second cooldown, recovery duration or held-ball restriction.

Build passes (zero warnings/errors). Managed suite and menu wiring tests pass,
including six new dive eligibility/rejection scenarios. Server was empty.

Deployed at 10:36:57 UTC via preserve-mode installer. SHA256:
`1d482d4c246041c56723125829dd8b8f58442b0d7f58ec1c0d1817034b71c45c`

Rollback:
`bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260907T103657Z-elRlmA/rollback.sh`

GK trial restored ON. A real player dive still needs in-game confirmation;
the next attempt now provides a precise reason and diagnostic evidence.

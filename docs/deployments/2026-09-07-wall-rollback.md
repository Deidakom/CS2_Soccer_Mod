# Targeted wall rollback — 2026-09-07

User requested yesterday's wall and wall-jump behaviour, not a whole-plugin
rollback. Baseline is the snapshot immediately before today's first deployment:
`/home/gameserver/cs2-soccermod-backups/ball-handling-20260907T081807Z-tPeyRP/plugin`.
Its DLL has modification time September 5 16:26:05 UTC and SHA256
`676dd057c8c042d581da43d0d26a806cc7630fc35a0d80c88f3ff38bf1336857`.

Snapshot settings: legacy profile, wall assist enabled, conversion 0.159,
maximum added vertical 200, normal retention 0.18; wall-pop chance 0.3,
vertical 850, lateral 220, spin factor 0.5. The latter four already match live.
Pre-rollback live wall settings had been changed to 0.2 / 500 / 0.225.

Restores the original additive vertical bonus in both wall profiles, 0.35 s
cooldown, and legacy four-frame XYZ separation plus legacy wall spin impulse.
Removes today's extra short-reach wall-pop condition. Keeps the newer
new-contact/identity/pause guards so wall callbacks cannot undo a fresh kick.
The newer landing cap skips the wall-separation window. Ordinary kicks,
incoming airborne tip touches, rollout, player push, cannon goal suppression,
GK sprint, menus and client assets are retained. This deliberately restores
the old wall response, including lift stacking; it is not a further retune.

Validation: managed regressions including snapshot constants and 22 restored
lift cases; focused Node checks cover both wall call sites, callback guards
and preservation of other current mechanics. Client feel remains user-tested.

## Deployment verified

Installed at 13:12:31 UTC with the existing checksum-verifying installer in
`preserve` mode. SHA256:
`46baf2f2e5347fb40231f94276350a09302c11a88b793bf0af798c927ea547d1`.

Pre-change backup:
`/home/gameserver/cs2-soccermod-backups/ball-handling-20260907T131231Z-mSgvgX`.

After startup, applied and re-read `css_sm2ball_wallassist 0.159 200 0.18`.
Runtime reports `feel=2026-09-07-wall-rollback`, `walls=pre-September-7`,
legacy profile and 0.35 s cooldown. `mp_maxrounds=999999`, service active,
and no startup exceptions found. All managed checks and 20/20 focused Node
tests passed. Only the three wall-assist values were intentionally changed;
no map, Workshop, password or other feature settings were deployed.

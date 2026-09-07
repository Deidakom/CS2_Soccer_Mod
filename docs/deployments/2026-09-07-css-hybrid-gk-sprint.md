# CSS hybrid ball feel + goalkeeper sprint — deployed 2026-09-07 11:30 UTC

Implemented the approved queue since the CS:S video review. Details:
[ball feel](../ball-feel-css-hybrid.md), [keeper sprint](../goalkeeper-sprint.md).
The trial is removed, not merely switched off.

## Installed artifact and rollback

- Server: `212.87.212.58:27017`, `cs2-soccermod-test.service`.
- DLL SHA256: `d6c661584ef9ba0f39789c0744ed63d61fce1d941d8b0662e251a26017225ba9`.
- Backup: `/home/gameserver/cs2-soccermod-backups/ball-handling-20260907T113026Z-eDMaHF`.
- Rollback: `bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260907T113026Z-eDMaHF/rollback.sh`.
- Uploaded staging: `/tmp/soccermod-css-hybrid-4GoEmVOs/SoccerModNativeHull.dll`.
- Used the inspected existing installer in `preserve` mode, with checksum
  validation, a stopped-service backup, and recovery on installation errors.
- Brief restart; no client/UI control or map/Workshop update.

Persisted through RCON after startup:

```text
css_sm2ball_impact_push 150 0.65 1750
css_sm2ball_wallassist 0 0 0.35
```

Live status confirmed the existing `legacy` handling profile and `stamina`
sprint profile, rollout enabled, `soccer_cssl_stadium_v8`, and current hostname
`KA Soccer Mod - Public Server`. The hostname was preserved, not changed as
part of this ball/GK request. Existing friction 0.5, elasticity 0.2, gravity 1
and mass 1 remained unchanged.

`css_sm2ball_feel` returned the new revision with 80 ms contact window, 82%
full-strength reach, 200 u/s maximum tip pass, 6 u/s² finite coast, 0.65 impact
ratio/12-tick pulse, 0.35 wall retention and unlimited own-box 1.175× GK sprint.
Installed DLL checksum matches the locally tested build. Service is active.

## Verification

- Release build and all managed regression groups pass, including 39 GK sprint
  scenarios and the new contact/push/hop/landing/finite-coast checks.
- All 14 focused Node integration/source-wiring checks pass.
- Full Node suite: 118/119 pass. The remaining existing spectator-menu test
  assumes LF while the checked-out client config uses CRLF; no menu behavior
  was changed to silence that unrelated test.
- `git diff --check` passes (Git emits its normal LF/CRLF warnings).
- NuGet vulnerability metadata was unavailable (NU1900); compilation/tests
  used already restored dependencies. No claim of a completed vulnerability audit.
- No plugin exceptions found in the startup/rollout log slice inspected.

While the server had zero humans/bots, ran `css_sm2ball_trial roll 80` with
observed resting spin before launch. All 200 samples completed. Observed
speeds: about 57 u/s at 1 s, 46 at 3 s, 35 at 5 s, 27 at 7 s, 19 at 8 s,
16 at 9 s and 7 at 10 s. Motion reduced to below 1 u/s around 12 s and stayed
stopped through 20 s. The native hull produces some lateral wobble at the
tail; this was not presented as exact straight-line or 1:1 CS:S realism.
The server remained empty; sent the center-reset command after the trial.

No client was controlled. Actual player-impact feel, knife responsiveness,
wall interactions and keeper movement still require user gameplay confirmation.
Changes are local + deployed, not committed/pushed to GitHub by this task.

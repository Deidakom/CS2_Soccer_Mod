# German CS2 deployment: ball handling 1.2.0

Installed on 212.87.212.58:27017 at 2026-09-05 06:55 UTC. Server was empty.
`css_plugins list` confirmed CS2 SoccerMod 1.2.0 loaded. Live runtime reports
`models/ball/jabulani_edit.vmdl`, improved profile, mass scale 1, friction 0.5,
elasticity 0.2 and gravity scale 1. No new managed exceptions were observed.

Artifact SHA256:
`9973743a5e90f4151f672c8de04f733c2152d358621c556c95dea9d01228bcf8`

Exact pre-install snapshot:
`/home/gameserver/cs2-soccermod-backups/ball-handling-20260905T065508Z-UPUoyP`

Immediate behavior rollback (verified live):

```text
css_sm2ball_profile legacy
```

Full rollback over SSH as root:

```sh
bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260905T065508Z-UPUoyP/rollback.sh
```

Rollback restores the old binary and ball tuning while preserving newer ranks,
admins, bans and match records. Installer tests verified successful rollback,
automatic recovery after a failed service start, and checksum rejection.
The full binary rollback was tested in the isolated harness, not by restarting
the public server twice. Switching legacy → improved was verified on the host.

Live `flightside 600 20 1000` and `wall 600` captures each completed 200 samples.
The wall run from centre did not reach a wall; it verifies the trial lifecycle,
not rebound accuracy. The sidespin request was logged by the native bridge,
but the airborne orientation samples showed almost zero spin, rather than the
requested 1000 degrees/second. Ground contact then produced observable rotation
(~457–1021 degrees/second), confirming the sampler can detect movement. Thus
the bridge's angular input remains **unverified** and must not be represented
as calibrated sidespin. Optional creative curve is a separate scripted force.

No live human contact, client overlay appearance or subjective CS:S ball-feel
comparison was possible on the empty server. The previous CS:S installation
`/home/gameserver/css` is absent; the CSS probe update is repository/build-only.

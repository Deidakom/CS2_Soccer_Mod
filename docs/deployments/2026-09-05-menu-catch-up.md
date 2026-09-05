# German server: menu catch-up 1.3.0

Final artifact SHA256:
`27d57174765cfafd3d3875b71222e153de06ddd915980b0d8179c22a7a2613af`

Installed on 212.87.212.58:27017. Final update began at 07:24:48 UTC; no players
were connected. Ball profile remains `improved`, kickoff wall is `on` with
`outline` style, sprint uses `stamina`. Existing ball size and power remain.

Independent rollback of all menu/Sprint changes to the previously deployed
1.2.0 ball build:

```sh
bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260905T072028Z-IU6H1Y/rollback.sh
```

An additional intermediate-build snapshot exists at
`/home/gameserver/cs2-soccermod-backups/ball-handling-20260905T072448Z-WLOiZ6`.
Use the **07:20:28** snapshot above to remove the whole menu catch-up, rather
than only reverting the final preview-timeout fix.

Behavior switches without replacing the DLL:

```text
css_sm2kickoffwall off
css_sm2kickoffwall_style legacy
css_sm2sprint_profile legacy
css_sm2ball_profile legacy
```

Local validation: 100 Node tests, 14 Python tests (including rollback restoring
old match/menu settings and preserving newer ranks), and 39 managed scenarios.
Release compiler reports no warnings/errors. GitHub CI for the preceding ball
and CS:S probe commits passed, including the packaged-release installer check.

Live checks: CounterStrikeSharp loaded SoccerMod 1.3.0; kickoff preview created
36 beam entities; explicit cleanup removed all 36; stamina → legacy → stamina
switch worked; runtime still reports the Workshop Jabulani and unchanged core
physics values. No managed plugin exception was observed. A pre-existing map
team-intro visibility resource warning is distinct from plugin loading.

The first preview exposed a warmup-only expiry bug, fixed before this final
artifact: warmup skips MatchOnTick, so the outline now has an independent timer
that cannot remove a newer kickoff's boundary. See final live check notes below.

Client appearance and input, moving players against the boundary, Sprint feel,
and Source 1-versus-Source 2 visual comparison remain unverified in-game. The
[parity audit](../css-menu-parity-2026-09-05.md) lists all known missing areas;
this deployment is not a claim of complete CS:S feature parity.

Final preview check passed: `active=True lines=36` at launch became
`active=False lines=0` automatically after its timeout. The feature itself
remained enabled (`css_sm2kickoffwall` reported `on`). The service remained active
and the ball was reset to center after testing.

To roll back **both** today's ball and menu updates in one step:

```sh
bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260905T065508Z-UPUoyP/rollback.sh
```

That original snapshot's rollback script was extended to restore its original
match settings and remove the newly introduced menu-parity file. Its old script
is retained as `rollback.original.sh`. New player ranks remain untouched.

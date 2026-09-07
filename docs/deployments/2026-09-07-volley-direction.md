# Airborne kick direction — 2026-09-07

Airborne kicks previously retained all velocity perpendicular to the intended
launch direction. Real Natsu volleys logged at 08:10:42 and 08:10:56 UTC on
September 7 left 8.66 and 7.45 degrees off horizontally, with substantially
less upward elevation than aimed. The deviation existed in the requested
velocity before any subsequent body collision.

Volleys now retain 25% of perpendicular momentum, capped to a 3-degree 3D
deflection from the launch direction. Forward momentum is retained and opposing
momentum is cancelled as before. Ground-kick calculations, power settings,
contact lift, native collisions and optional curve behaviour are unchanged.
The limit applies to the initial momentum contribution, not the later flight.

Validation: 96 executable volley scenarios (including both recorded volleys,
weak kicks, downward and vertical shots) and the full managed suite passed.
The Node suite had one unrelated existing spectator-config assertion failure:
it expects LF while the checkout has CRLF. All ten bindings pass that assertion
after in-memory newline normalization; no menu files were changed.

Deployed DLL SHA256:
`8c2fe170b2f7a4d901da7b7de97cb57953b04d20ee3375aacad9a512aeb1bbb8`

Backup: `/home/gameserver/cs2-soccermod-backups/ball-handling-20260907T081807Z-tPeyRP`

Installer used `preserve` mode. Visual gameplay testing is left to the user.

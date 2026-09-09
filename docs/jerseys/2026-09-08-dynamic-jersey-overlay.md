# Dynamic jersey attachment candidate

The old renderer placed one billboarding `point_worldtext` in world space and
teleported it from a feet-relative offset. That implementation was removed.
The new renderer creates independent name and number children, parents each to
the current `CCSPlayerPawn`, binds them to authored torso sockets, and uses
`POINT_WORLD_TEXT_REORIENT_NONE`. Stable play does not move or recreate them.

The renderer is intentionally opt-in and remains off when the nullable
`DynamicJerseysEnabled` match setting is absent or false. `on|off` changes are
persisted. Status reports the requested setting separately from model/socket
availability. Unknown models, missing sockets, failed binds and stale pawn
handles produce no origin fallback and no unbounded retry loop.

Layout behavior:

- outfield players get one sanitized name (maximum 10 ASCII characters) and
  one dynamic number from 2–99;
- each goalkeeper gets a name only, retaining the kit's painted number 1;
- the four approved model paths use the `spine_2` calibration and the
  `soccermod_jersey_name` / `soccermod_jersey_number` sockets recorded in
  `jersey-attachment-calibration.json`;
- `css_sm2jerseyprototype on|off` is an admin-only, disabled-by-default Home
  proof path that renders one number `88`.

Commands:

```text
css_sm2jerseydynamic           # status; match permission for changes
css_sm2jerseydynamic on|off    # persist the experimental renderer toggle
css_sm2jerseyprototype on|off  # admin-only one-number attachment proof
css_sm2jerseynumber             # show/assign a session number
css_sm2jerseynumber 2-99       # choose an unused number for your squad
css_sm2jerseynumber random      # choose a new number
```

## Local asset evidence

The candidate was generated from the accepted gearless source after verifying
the recorded package `artifacts/kits-gk-gearless-upload/3797479770_dir.vpk`
(35,000,606 bytes,
`BBEEED16F860BEFE1EAC88C9429E2204046C491E325B870F01423592447F8E91`). The
generator changed only the four modeldoc `AttachmentList` blocks; the existing
`weapon -> wpn` transform was preserved.

The isolated candidate is `soccermod_jerseys_dynamic` and was compiled locally
with the four goalkeeper material groups included. Its model source hashes are
recorded in the calibration manifest. The local package is:

- `artifacts/kits-dynamic-candidate/3797479770_dir.vpk`
- 35,002,052 bytes,
  `B219E1725A6D13F7DB5F3A7875F603AFDE3C1B43CF9086D7FDC4C5E159938D0E`
- 44 files: 4 models, 14 materials and 25 textures plus `addoninfo.txt`;
- Source2Viewer VPK verification passed;
- dependency audit: 43 compiled resources, zero unresolved references.

The resource compiler completed all 18 required route resources. These facts
prove local build/package integrity, not client delivery or visual alignment.

## Test status

Managed checks, targeted JavaScript checks and generator checks pass. The full
JavaScript suite is 132/133: its single failure is the existing
`test/spectator-menu.test.js` LF/CRLF-sensitive bind assertion, unrelated to
jersey files. The candidate has not been mounted on a client and no
screenshot/video was captured in this run.
Gate 1 and Gate 2 therefore remain pending: behind/rear-oblique Home `88`
motion checks, crouch/run/turn/knife-swing checks, then all four kits and
lifecycle/observer checks. Do not call the visual issue fixed until those
client tests pass. No Workshop publication, server deployment, restart or
desktop control was performed.

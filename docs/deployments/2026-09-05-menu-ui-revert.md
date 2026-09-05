# Restore pre-redesign menu — 1.4.11-dev

User requested reverting the SoccerMod menu UI to before today's redesigns.
`SoccerModMvpPlugin.Menu.cs` restores its presentation from commit `fdf2aaf`, before
`41fb318` introduced larger headings and separate navigation. This restores the
old fonts, padding, page capacities, navigation behaviour and 30-second timeout.
Redesign-specific managed assertions are removed. Other gameplay and the compact
1.4.9 sprint HTML display are retained.

Deployment discovered a newer, unpushed Windows experiment already live as
1.4.10-dev (DLL 0b2e87a8f687d3a497e1fe4789c8a3206c75f68600a51624ec1073b6c74c7fab).
The Windows task confirmed its Workshop addon mounted but script readiness failed,
leaving Classic mode on plain fallback. That build was backed up before replacing
it. Version 1.4.11 avoids reusing its version number. The live renderer was explicitly
set to HTML, as it was before today's redesign. Experimental Workshop files and
manager installation were not deleted; the menu no longer selects Classic.

Deployed 2026-09-05 13:21 UTC. All nine plugins loaded, and HTML mode was confirmed.
All 105 Node tests, the managed regression suite and git diff --check passed.
The only menu-file difference against fdf2aaf is the retained slot10 zero-key
compatibility listener; it does not change the UI. Actual visual confirmation is separate.

DLL SHA-256: `8b440d42c5a852ed7d52bb84238754fd6376375cf908359ec40cf0cad3a6cce9`.

Rollback to the backed-up Windows experimental build and settings:

```sh
bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260905T132153Z-s5u4sM/rollback.sh
```

This is a menu UI rollback, not a rollback of ball tuning, kickoff fixes, cards,
CAP, ranking or other functional updates. Settings were preserved by the installer;
only the menu renderer was explicitly changed to HTML. Windows work should fetch
this commit before any subsequent deployment and must respect the user's UI revert.

Final compatibility-preserving build deployed at 13:24 UTC; intermediate backup:
`/home/gameserver/cs2-soccermod-backups/ball-handling-20260905T132448Z-Z3xRep`.
The 13:21 rollback above retains the original Windows experiment.

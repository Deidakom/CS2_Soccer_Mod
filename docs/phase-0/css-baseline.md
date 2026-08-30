# CSS behavioral baseline

## Source-of-truth hierarchy

| Rank | Artifact | Role |
|---:|---|---|
| 1 | `ball-reference-analysis/DEPLOYMENT_1.5.10_2026-08-25.md` | Latest documented deployed runtime and plugin binary hash. |
| 2 | `ball-reference-analysis/STABLE_STATE_2026-08-25.md` | User-confirmed pitch, grass, ball, kickoff, and collision behavior. |
| 3 | `ball-reference-analysis/ka_soccer_titans_club_2026.bsp` | Active CSS production-map behavior. |
| 4 | `ball-reference-analysis/natsu_xsl_arena/ka_soccer_xsl_natsu_arena_v1.vmf` and `.bsp` | Reproducible map/geometry reference with exact preserved XSL ball. |
| 5 | `ball-reference-analysis/jersey-system/source` | Latest experimental feature-discovery source. |
| 6 | `ball-reference-analysis/somoe19-original` | Upstream 1.3.7.1 source and historical documentation. |

When artifacts disagree, the higher-ranked artifact governs current Phase 1
behavior. Experimental source may add a requirement only after review.

## Frozen key artifacts

Hashes observed on 2026-08-25:

| Artifact | SHA-256 |
|---|---|
| Upstream 1.3.7.1 main source | `efb0759f7e5f658d00302759c8601288b78af4ca58ce7b63a023051684362503` |
| Natsu 1.4.0 main source | `104e3b5d9344c79780720cab4e629e42c314cf535b2911c79bb57d2736e1c264` |
| Jersey 1.5.0 reference main source | `f2eeb32e55463dd3bf8e0b98d1782b73fb176b2029e451262edc871f2fc57602` |
| Current experimental 1.5.18 main source | `12490a43f2a95d4602579d3f37b6e5e745f1f210d8533a2c88e3b82c4fc6a243` |
| Active `ka_soccer_titans_club_2026.bsp` | `922514e39f5ce4ac9050825e7e807359f1678a58c1b3f82aaac962d725f86d7c` |
| Reproducible XSL-derived VMF | `657ed707b7310a508970809cdfE5d27395193b76a0ee7e8266fd3fd164a2fba7` |
| Live-snapshot main config | `b2597218b0661d7b64ada82fe6b56d718e024d456840ce7f13949c3301293b78` |

Hash comparison is case-insensitive. Run `tools/phase0-verify.ps1` to regenerate
current observations before relying on this table.

## Source inventory

- Current source declaration: `1.5.18-NATSU-TEST-CAP-CLANTAGS`.
- SourcePawn files physically present under the current source tree: 73.
- Files reachable from the main plugin's quoted include graph: 60.
- Lines in that reachable graph: approximately 26,000; the verifier reports
  the exact current count because this experimental tree is still changing.
- Thirteen files are duplicates, old top-level copies, or unreferenced
  experiments. In particular, `modules/ballstabilizer.sp` is present but not
  compiled into the current main file.
- The workspace does not contain a matching compiled 1.5.10 or 1.5.18 `.smx`,
  local `spcomp`, or a locked SourceMod compiler/include distribution.

Consequently, the latest SourcePawn directory is useful for feature discovery
but cannot be called a reproducible release.

## Core ball and map facts

The CSS mod relies on Source 1 physics and map entity logic for much of the
actual football feel:

- Authoritative ball class: `func_physbox`.
- Target name: `ballon`.
- Active-map inline model: `*515`.
- Spawn origin: `0 0 17`.
- Bounds: approximately `-15..+15` on each axis, so the ball is approximately
  30 CSS units in diameter.
- Physics material value: `5`; propdata value: `17`.
- The reproducible XSL-derived map preserves the exact reference ball physics
  payload and uses inline model `*135`.
- The reference map has 16 T and 16 CT spawns.
- Reference-map world bounds are approximately
  `(-3456,-3840,-448)..(3456,3840,2080)`.
- Reference goal-plane triggers are approximately 208 units wide, 80 units
  high, and 10 units thick at `y=+1414..1424` and `y=-1424..-1414`.

The SourcePawn plugin observes ball damage/touch outputs for attribution. The
native knife-to-physics response supplies the CSS kick. This is why copying
SourcePawn formulas cannot reproduce the old ball feel on its own.

## Live default match rules

The captured live configuration specifies:

- Two periods of 900 seconds each.
- Five-second period break.
- Golden goal enabled.
- Ready check enabled.
- God mode enabled.
- Ten-second respawn delay.
- Kickoff wall enabled.
- Sprint enabled at 1.25x for three seconds with a 7.5-second cooldown.
- Ranking categories for goals, assists, own goals, hits, passes,
  interceptions, ball losses, saves, team results, MVP, and MOTM.
- Training ball and advanced training settings.

These values are requirements inputs, not automatically correct CS2 tuning
values. Physics, movement, respawn, and timing must be revalidated in CS2.

## Reusable behavior versus rewrite work

Reusable as specifications:

- Match state machine, periods, break, golden goal, forfeit, and score rules.
- Ready/captain/picking workflows and position vocabulary.
- Touch history, scorer/assist/own-goal rules, statistic categories, and ranking
  weights.
- Admin roles, configuration concepts, goalkeeper zones, and training workflow.
- Pitch proportions, spawn layout, goal dimensions, and acceptance comparisons.

Must be rewritten:

- Commands, menus, events, entity hooks, damage handling, team/round control,
  respawn, movement, sprint, HUD, persistence, demo control, and update handling.

Must be recreated or converted through Source 2 tools:

- Map, ball physics model, goal triggers, materials, sounds, overview, player
  models, and jerseys. There are currently zero `.vmap`, `.vmdl`, or `.vmat`
  source files in the legacy workspace.

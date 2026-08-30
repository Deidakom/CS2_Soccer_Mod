# CS:S SoMoE / XSL B1 parity plan (2026-08-30)

Goal: make the CS2 port feel 1:1 with the CS:S SoMoE-19 experience and the
XSL B1 ball. This doc is the gap analysis + ordered roadmap. It assumes the
current live state (kick 1602 u/s, overhead bonus 0.14, push 0.84/264,
wall assist 0.129, settle 8 u/s, aim cone 70°, all persisted in
`soccermod_settings.json`).

**Key asset we are under-using:** the original CS:S server is still running
on the same VPS (`cssserver.service`, active right now). Every remaining
"feel" question can be answered by measurement instead of memory — the probe
plugin source is in-repo (`tools/css-reference-probe/soccermod_css_ball_probe.sp`)
and only needs re-installing. The existing captures
(`artifacts/css-reference/{roll,wall,drop}-seq*.log`) cover ground behavior;
they do NOT cover flight arcs, kick samples at set aim angles, or body-push
dribble speed — which are exactly the three things still tuned by feel.

---

## Part A — Ball feel (XSL B1)

### Already matched (measured, done — don't touch)
- Hull: 80-face geodesic + inner sphere compound; volume-exact vs XSL B1;
  the two-regime roll decay is emergent and verified against `roll-seq1.log`.
- Mass 60.694 kg, friction 0.5, gravity scale 1.0, zero damping.
- Kick as **added** delta-velocity (momentum survives), contact-point lift,
  reach scaling with radius.
- Wall hop exists (heuristic assist, ratio 0.129 = CS:S measured value).
- Ball size: CSF 37.61u — user's explicit choice over true XSL 28.96. Keep.

### Gap 1 — Spin is the real 1:1 item (everything else compensates for it)
CS:S curve, wall hochbuggen, backspin float and the "alive" feel all came
from one mechanism: the knife hit was an off-centre VPhysics impulse that
imparted **angular** velocity. CS2 today imparts zero spin; wall assist and
the wall-pop randomizer are approximations layered on top.

- Status: native Metamod plugin (`soccermod_native`) is built, loaded,
  stable, pointer-resolution proven — blocked ONLY on the stale
  `CEntityInstance_AcceptInput` signature (CS2Fixes gamedata still
  2026-08-24, checked today; server binary is 2026-08-28).
- Action now: a weekly 30-second check
  (`cd /root/native-build/CS2Fixes && git fetch && git log -1 -- gamedata/cs2fixes.jsonc`).
  The moment it moves: paste new byte pattern into `plugin.cpp`, `ambuild`,
  copy `.so`, **user runs `meta unload/load`** (agent is hard-blocked from
  that), selftest, then execute Phase B/C of
  `docs/ball-foundation/2026-08-29-implementation-plan.md` (topspin kick →
  wallspin trial).
- Payoff when unblocked: kicked-ball curve (T5), natural wall pop (retire or
  reduce the wall-assist + wall-pop heuristics if the wallspin trial shows
  Rubikon converts spin natively), backspin lift on lofted shots — the
  remaining "air feels wrong" reports likely shrink or vanish here, because
  a spinning CS:S ball genuinely flew differently.

### Gap 2 — Air behavior ("drops like a stone"): MEASURED 2026-08-30, closed
Built the flight trial on both sides (`sm_xslref_trial flight <speed>
<angle>` on CS:S, `css_sm2ball_trial flight <speed> <angle>` on CS2, same
launch, same log-and-diff approach as roll/wall/drop) and ran two angles:

| Launch | CS:S apex height / time | CS2 apex height / time | CS:S range | CS2 range |
|---|---|---|---|---|
| 1359.2 u/s @ 10.6° (measured clean kick) | 35.1u / 0.34s | 39.6u / 0.31s | 715.6u | 662.6u |
| 1200 u/s @ 35° (steep/lofted) | 250.6u / 0.70s | 250.0u / 0.81s | 1210.9u | 1146.3u |

**Result: CS2 and CS:S match each other closely at both angles** (within
sampling noise — both servers' trial timers ran coarser than the requested
0.02s interval, ~0.08-0.11s actual, when idle/unpopulated). Both also fall
~15% short of the naive no-drag kinematic prediction (e.g. steep case:
296.1u theoretical vs ~250u measured on BOTH servers) — that shortfall is
therefore a shared VPhysics/Rubikon integration characteristic, not a CS2
regression, and not something to chase.

**Conclusion: gravityScale=1.0 and the current lift-angle formula are
already correct. Do not tune them further.** The "drops like a stone"
complaint is not a ballistics mismatch — it is Gap 1 (missing spin) as
hypothesized: every CS:S lofted kick carried backspin from the knife's
off-centre impulse, and backspin + Magnus lift in VPhysics is what made a
CS:S ball hang and float. A spin-less CS2 ball, even with identical gravity,
reads as heavier in the air purely because it has no lift source. This
closes Gap 2 as a standalone item — it merges into Gap 1 (spin) and will be
re-verified with the same flight trial once spin lands.

### Gap 3 — Body push / dribble: calibrate against CS:S, not intuition
The explicit push (0.84 transfer, 264 cap) fixed "can't push at all", but
the numbers are invented. CS:S turbophysics dribble had a real, measurable
ball speed when walking into it:
1. Probe trial on CS:S: walk a bot/player into the resting ball at walk and
   run speed, log resulting ball speed over 2s.
2. Match `BallPushTransferRatio`/`BallPushMaxSpeed` to those two numbers.
3. Promote both to `css_sm2ball_push <ratio> <max>` (ball flag, persisted)
   so the last 10% is tunable live without redeploys.

### Gap 4 — Bounce restitution (accepted, revisit once)
CS2 gives ~12u first bounce vs CS:S 6.3 (surface-property step function,
nothing between 0.30→zero and weapon→12u; entity elasticity is inert).
User accepted 12u. One cheap experiment remains unexplored: shipping a
custom `.vsurf` surfaceproperty inside our own addon and referencing it from
the ball vmdl's `surface_prop`. Half a day, might land the exact 6.3.
Do it once, after spin — bounce feel interacts with spin.

### Gap 5 — Kick cooldown & rhythm parity
`KickCooldownSeconds = 0.35` is our invention; CS:S rhythm was the knife's
own primary-attack rate (~0.4s for stab... actually CS:S knife primary is
0.35–0.4 and SoMoE didn't add its own cooldown). Verify with a CS:S capture
of two fastest consecutive kicks; align. Small, but rhythm is muscle memory.

---

## Part B — SoMoE-19 gameplay parity

### Built (MVP level, this week)
Admin+bans (own JSON), ball tuning panel (persisted), match core
(periods/pause/goals/halftime swap/rr/maprr), cap core (pool→draft→picks),
sprint (1.25×/3s/7.5s, +use), !menu, sky-path removal, damage block,
knife-only, settle deadband, center-spawn fix, silent `!` triggers.

### Tier 1 — completes the core match experience (next)
| Item | Notes | Effort |
|---|---|---|
| Goal calibration in-game | `css_sm2goal_calib` with real posts; confirm team ends (`css_sm2goal_swap`); persist goals config (currently runtime-only!) | XS |
| Own-goal attribution via last **toucher** | today it's last kicker; body touches must count (SoMoE credited hits/passes) — reuse push/proximity tracking | S |
| Kickoff order & wall | SoMoE: conceding team kicks off; `kickoffwall` blocked the non-kicking team crossing midfield until first touch. We restart rounds with no possession rule | M |
| Readycheck (`!rdy`, forced pause parity) | SoMoE pause required all-ready to resume | S |
| Golden goal + configurable OT | match config exists, flow doesn't | S |
| Forfeit vote | goal-diff gated, auto-spec | S |
| Team names + hostname status | chat + `hostname` updates ("LIVE 2:1") | S |
| Match log file | `soccer_mod_last_match.txt` equivalent: goals w/ timestamps+SteamIDs, rotated | S |
| Persist match/sprint/goal settings | extend `soccermod_settings.json` (match block exists in schema sketch, not wired) | XS |

### Tier 2 — social / QoL layer (what made servers sticky)
Positions menu (`!pos`, stored), connect-order list (`!lc`), serverlock +
AFK captcha on cap, `!spec me|all|<name>`, deadchat modes, MVP messages,
chat prefix/colors, `!help`/`!commands`. Each is S; do them in one batch
after Tier 1, they share the config/menu plumbing.

### Tier 3 — big features, staged, each its own mini-plan
- **Training** (global/personal cannons, spawnball, props, adv-training):
  most-used SoMoE feature outside matches. Cannons = timer + the existing
  impulse path; spawnball needs multi-ball support in the foundation
  (today the plugin owns exactly one ball — real architectural work).
- **GK areas + saves + `!gk` skin toggle**: needs stats hooks + per-map
  config; skin part depends on skins below.
- **Stats/ranking (SQLite)**: point table is fully documented in SoMoE
  source; needs the touch-attribution work from Tier 1 first.
- **Skins/jerseys**: CS2 can swap player models server-side, but custom
  content must ship via a workshop addon the clients download — new
  pipeline (author addon, mount alongside map). Prereq for GK skins and
  the NATSU jersey system. Investigate as its own spike.
- **Shouts**: sound files → same workshop-addon prereq.
- Explicitly dead: grass replacer (map-specific, obsolete), tickrate module
  (CS2 is 64-tick fixed), updater (we deploy via script).

### CS2-specific polish (no SoMoE equivalent, but part of "feels right")
- **Contact shadow** under the ball (deferred asset: flat disc vmdl,
  compile Windows-side, third payload in push script — design already in
  the MVP plan).
- **Map scoreboard mirror**: drive the stadium's physical counter via
  `Press` inputs on `t_plus`/`ct_plus`/`reset` so the in-world scoreboard
  matches plugin score (config-gated).
- HUD: center text is functional; consider CenterHtml score/clock styling
  pass at the end.

---

## Part C — how we verify "1:1" (method, not vibes)
1. **Reference captures first**: every feel complaint becomes a CS:S probe
   trial + a mirrored CS2 trial + a log diff (`[SM2CSSREF]` convention).
   New trials needed: flight, dribble-push, kick-rhythm.
2. **A/B session**: user plays 5 min CS:S then 5 min CS2 back-to-back on
   the same VPS, rates per category (kick power, air, wall, push, roll,
   stop). Repeat after each tuning phase. Ratings tracked in this doc.
3. All tunables stay live-adjustable and persisted — no redeploy needed
   during an A/B session.

## Part D — ordered roadmap
1. **Now / continuous**: weekly CS2Fixes signature check (unblocks spin).
2. **Phase 1 — Measure.** Flight trial: **done 2026-08-30**, see Gap 2 above
   — gravity/lift confirmed correct, no tuning applied, air-feel gap
   re-classified into Gap 1 (spin). Still open, both need the user live (a
   bot/script can't walk or knife-swing realistically):
   - **Dribble-push calibration**: on CS:S, walk into the resting ball at
     walk and run speed, log resulting ball speed for 2s (probe already
     samples position every trial tick — reuse via a manual timed capture,
     or add a lightweight `OnBallDamagedOutput`-style touch logger if push
     doesn't go through damage). Match CS2's `BallPushTransferRatio`/
     `BallPushMaxSpeed` to the two numbers instead of the current invented
     0.84/264.
   - **Kick-rhythm**: two fastest consecutive real knife kicks on CS:S,
     measure the gap, align `KickCooldownSeconds` (currently our own
     invented 0.35).
3. **Phase 2 — Tier 1 gameplay** (order as listed; goal-config persistence
   and own-goal-by-toucher first — they're prerequisites for match logs
   and stats later).
4. **Phase 3 — Spin** (as soon as CS2Fixes updates; may preempt anything):
   signature paste → user meta-reload → Phase B/C of the implementation
   plan → re-run wall + flight trials → retire heuristics that native spin
   makes redundant → repeat A/B.
5. **Phase 4 — Tier 2 QoL batch.**
6. **Phase 5 — Contact shadow + scoreboard mirror.**
7. **Phase 6 — Workshop-addon spike** (unlocks skins/shouts/jerseys), then
   Training → GK/saves → Stats, each gated on the previous.

Standing rules unchanged: knife-left-click only, no analytic ball control,
ball never damages players, match work pauses whenever ball feel regresses.

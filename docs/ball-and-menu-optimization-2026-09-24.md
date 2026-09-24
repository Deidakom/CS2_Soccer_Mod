# Ball handling and menu optimization — 2026-09-24

Review of the ball handling/behaviour and the in-game SoccerMod menu, with
the fixes and one new mechanism that came out of it. Tuned feel values,
handling profiles, physics constants and the reverted menu layout are
unchanged. Nothing here has been deployed or tested with connected clients.

## Ball handling

### Fixed: the higher slot won every two-player body duel (legacy profile)

`ApplyPlayerBallPushFor` computes each touching player's push from the same
pre-push ball velocity. The improved and creative profiles already averaged
those pushes (`BallContactMath.CombinePushes`), but the legacy profile — the
one the live server runs — wrote each push with its own `Teleport`, so the
last writer won. Players are iterated in slot order, which means the player
with the higher slot number silently won every contested ball.

All profiles now combine simultaneous pushes. A single dribbler still gets
exactly their own push, so ordinary dribbling is unchanged; only contested
balls (two or more bodies touching the ball in the same tick) change from
"highest slot wins" to the order-independent average.

### New: lag-compensated knife contact

A client draws the ball roughly one round trip plus interpolation behind the
server. A knife press that looked like a clean touch on screen used to be
judged only against the server's current ball, which has moved on by then —
at 800 u/s and 50 ms ping that is 60–70 units, most of the knife's reach.
This matches the "knife feedback without a shot" reports.

Every tick now records a short trail (32 ticks) of each playable ball's
position (`KickRewind.cs`, `SoccerModMvpPlugin.KickRewind.cs`). When a press
misses the ball's live position, the reach and aim-cone test is repeated
against the newest trail position inside the player's window:

- window = scoreboard ping (`CCSPlayerController.Ping`) + two ticks,
  capped by the new Ball workbench control **Kick power → Lag compensation
  max (ms)**, default 100 ms, range 0–250, `0` = off (no trail is recorded);
- never across the ball's last contact by anyone (kick, body push, body
  impact, wall or landing correction) — a sample taken on the contact tick
  still shows the ball before that contact, so it is excluded too;
- never across a teleport (reset, cannon, placement): a jump no ball could
  make at the configured speed limit ends the trail;
- any ball qualifying at its live position wins, so every kick accepted
  before is accepted exactly as before;
- the kick always acts on the live ball and its live momentum; only the
  contact geometry (distance, contact point, lift, grounded test, line of
  sight) comes from the position the player saw. The experimental thruster
  kick stays attached to the physical ball.

`kick_accepted` logs `lagCompensatedMs` and `ping`; `css_sm2ball_feel`
reports the configured maximum. Settings files without the new value keep
the 100 ms default; presets saved before the control existed still load and
leave it at its current value (previously a preset with a missing control was
rejected as incompatible, which would have locked out "Before workbench").

### Log volume

Every knife press anywhere on the pitch wrote `primary_input` and usually
`kick_rejected` at Information level. Presses farther than 320 units from the
ball, presses by dead/spectating players and presses with a non-knife weapon
are now Debug. Near-ball rejections — the ones that explain "I hit it but
nothing happened" — stay at Information.

### Reviewed and deliberately not changed

- Several systems (landing limiter, wall assist, body push, body impact,
  rolling assist) can each write the ball velocity in the same tick from the
  tick's start sample. Letting later systems see earlier writes would also
  change when body impacts trigger in scrums; that is a feel change that
  needs play testing, not a silent refactor.
- Held-swing cadence stays at a 0.48 s minimum even if the kick cooldown is
  set lower, as documented in `KnifeSwingRules`.
- Physics constants, kick power, lift, soft pass/pitch, wall and rollout
  tuning are untouched.

## Menu

The reverted pre-redesign layout (fonts, colours, capacities, navigation
keys, timeout) is unchanged.

- **Escaped names in the HTML renderer.** Titles and rows carry player,
  clan, team, ban and preset names. A name containing `<`, `>` or `&` could
  break or restyle the centre panel. Only those three characters are
  escaped, so umlauts and emoji render as typed. Padding still follows the
  visible text length.
- **Page memory.** Toggling a setting on page 3 of a long menu (Misc
  settings, Unban, Player Promotion, Ball dials, referee score) re-opened
  the menu on page 1, and Back always returned to page 1 of the parent. The
  per-player `MenuPageMemory` trail now returns to the page the player left.
  It lives only while the player stays in the menus: closing with 0, expiry,
  disconnect, a fresh `!menu`/`!admin`, or an option that ends the session
  forget it. Menus whose title shows a live value use a stable key
  (`ball-dial:<key>`, `referee-score`).
- **Pages clamp instead of wrap.** A remembered page beyond a menu that has
  since shrunk lands on its last page instead of wrapping to an early one.
- **Match Log auto-refresh** now redraws directly on the page being read.
  The classic HUD, which is never redrawn on a timer, previously showed page
  one after each refresh while the keys acted on another page.
- **Render cache.** Plain/HTML redraws (every tick in HTML mode) reuse the
  rendered text until the menu, page or renderer changes.
- **Help → Ball controls** explains left/right/crouched kicks, lobs, soft
  passes, volleys, dribbling and sprint, using the server's live power values.
- **Pending chat prompts** ("type a value in chat") are cancelled when the
  player explicitly opens `!menu` or `!admin` again, so a number typed later
  as ordinary chat is not consumed as a setting.
- Ball effects and preset review show ON/OFF instead of True/False.

## Switches and rollback

```text
css_sm2ball_tune kickLagCompensationMs 0     // lag compensation off
css_sm2ball_tune kickLagCompensationMs 100   // default
```

The body-push change is a fix without a switch. The previous DLL can be
restored with the usual installer backup.

## Validation

- Release build: 0 warnings, 0 errors.
- Managed suite: all groups pass, including new checks for lag compensation
  and fair pushes (22), menu text/page memory (14), the real HTML renderer
  and page clamping (4) and loading presets that predate a control.
  The workbench now has 48 controls.
- Node: 138/138 (new `test/ball-menu-optimization.test.js` wiring checks).
- Python: 21 tests, 6 skipped.
- `git diff --check` clean.

## In-game checks still needed

1. With a normal ping, receive fast passes and rebounds: fewer "swing but
   no kick" moments; `kick_accepted ... lagCompensatedMs=` shows when the
   rewind decided a contact. Compare with `kickLagCompensationMs 0`.
2. Two players pushing the same ball from opposite sides: neither slot
   should dominate.
3. HTML menu: toggle options on later pages of Admin → Settings → Misc
   Settings; Back from sub-menus; a player named `<b>x</b>` in Punish/Spec.
4. Help → Ball controls text in chat.

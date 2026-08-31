# Session handoff — 2026-08-31

Full, self-contained handoff for continuing this project in a different tool
(Codex or otherwise). Read this before touching anything.

## 1. What this project is

A CS2 (CounterStrikeSharp) port of the CS:S SourceMod plugin **SoMoE-19**
("Soccer Mod"). A native VPhysics soccer ball, knife-kick controls, match
flow, a website-cap bridge, admin/referee tooling, stats/ranking, and an
in-game menu, all built as a single CounterStrikeSharp C# plugin.
The public plugin identity is **CS2 SoccerMod v1.0 Beta**.

- **Repo (local)**: `C:\Users\sergi\Documents\ChatGPT\Privat\cs2-soccermod`
- **Repo (GitHub)**: https://github.com/Deidakom/CS2_Soccer_Mod, branch `main`.
- **KICKOFF cap website**: https://kickoff.212-87-212-58.sslip.io/; local
  deployment source is at
  `C:\Users\sergi\Documents\ChatGPT\Privat\soccermod-cap-web` (a separate,
  currently unversioned deployment directory, not part of this Git repo).
- **Test server**: `212.87.212.58:27017`, systemd unit
  `cs2-soccermod-test.service`, map `soccer_cssl_stadium_v8` (Steam Workshop
  id `3361075564`). SSH: `ssh root@212.87.212.58` — **key auth works
  passwordless** from a Bash-tool-style shell (confirmed via
  `ssh -o BatchMode=yes`), no password needed.
- **CS:S reference server** (for measuring "how did the original do this"):
  same VPS, `cssserver.service`, port 27015. A SourceMod probe plugin
  (`soccermod_css_ball_probe`) is installed there — not autoload-persisted,
  reload after any CS:S server restart:
  `ssh root@212.87.212.58 '/root/css_rcon "sm plugins load soccermod_css_ball_probe"'`
- **Original SoMoE-19 source** (for porting reference): already cloned at
  `C:\Users\sergi\Documents\ChatGPT\Privat\ball-reference-analysis\somoe19-original\`
  (495 `.sp` files). Use this, don't re-fetch from GitHub.

## 2. Build and deploy — exact commands

The .NET SDK is **not** on PATH by default in a fresh shell on this machine
(only a bare .NET 8 runtime is). A portable **.NET 10 SDK** lives at:

```
C:\Users\sergi\Documents\ChatGPT\Privat\.codex-tmp\dotnet-sdk\
```

(a sibling of the repo, NOT inside it). Build with:

```bash
export DOTNET_ROOT="/c/Users/sergi/Documents/ChatGPT/Privat/.codex-tmp/dotnet-sdk"
export PATH="$DOTNET_ROOT:$PATH"
cd "/c/Users/sergi/Documents/ChatGPT/Privat/cs2-soccermod/src/server-plugin/SoccerModMvp"
dotnet build -c Release
```

This produces `bin/Release/net10.0/SoccerModNativeHull.dll` (the assembly
name is `SoccerModNativeHull` even though the project/namespace is
`SoccerModMvp` — this is intentional, don't "fix" it).

Deploy (builds must already be done — this script does NOT build):

```bash
cd "/c/Users/sergi/Documents/ChatGPT/Privat/cs2-soccermod"
bash deploy/testserver/push-ball-build.sh
```

This base64-embeds the DLL + ball model into one SSH session, installs them,
and **restarts the live service** — every deploy drops any players currently
on the server. It backs up the previous DLL/model to
`/home/gameserver/cs2/backups/ball-<timestamp>/` first, so rollback is a
straight `cp` if a deploy goes bad.

**Always verify after every deploy:**

```bash
ssh root@212.87.212.58 '/root/rcon "mp_maxrounds"'
# must read 999999 — this is a regression canary, see §5
ssh root@212.87.212.58 'journalctl -u cs2-soccermod-test.service --since "2 min ago" --no-pager | grep -iE "exception|unhandled"'
# must be empty
```

RCON one-liner: `ssh root@212.87.212.58 '/root/rcon "<command>"'`

Logs: `ssh root@212.87.212.58 'journalctl -u cs2-soccermod-test.service --since "N min ago" --no-pager | grep SM2DIAG'`
— the plugin logs almost everything under the `[SM2DIAG]` tag; this is the
primary debugging tool. Prefer pulling real log lines over guessing at
engine/physics behavior — see the lessons in §5.

**Never run `meta load`/`meta unload` yourself** (loading native code into
the live server process) — a harness safety classifier blocks it even in
isolation. Ask the user to run it in their own terminal if it's ever needed.

## 3. Project layout

```
src/server-plugin/SoccerModMvp/     the live plugin — partial class
                                     SoccerModMvpPlugin split across ~20 files,
                                     one per feature area (Match.cs, Cap.cs,
                                     WebCap.cs,
                                     Menu.cs, Referee.cs, Stats.cs, Health.cs,
                                     Admin.cs, Social.cs, BodyImpact.cs, ...)
                                     plus the big SoccerModMvpPlugin.cs (ball
                                     physics/kicks/core).
src/server-plugin/SoccerModBallV2/  an OLDER, superseded parallel project —
                                     not the live one, don't confuse the two.
src/ball-lab/                       a JS reference implementation (match.js,
                                     cap.js, goal.js, kick.js, vector.js) that
                                     the C# port was originally translated
                                     from — useful as a second reference
                                     alongside the real SoMoE .sp source.
src/assets/models/soccermod/        the ball's own .vmdl/.dmx physics hull
                                     source files (small, ~116K, this
                                     project's own authored content).
deploy/testserver/                  cfg files + the deploy/install scripts.
  soccermod_test.cfg                  the server's own exec'd cfg — NOTE:
                                       sv_password is now a placeholder
                                       ("REPLACE_WITH_A_PASSWORD") in the
                                       repo copy; the LIVE server's actual
                                       running password is untouched by
                                       that edit (never redeployed).
  gamemode_casual_server.cfg          runs AFTER the CS2 gamemode cfg and
                                       stomps its defaults — has an explicit
                                       comment forbidding re-adding
                                       `mp_maxrounds 0` (see §5).
docs/                                dated handoff/plan docs, read the most
                                      recent ones first; this file is the
                                      newest.
test/, tools/                        JS test suite + Python/PowerShell/mjs
                                      tooling used during the ball-hull and
                                      physics-calibration work.
```

`.gitignore` excludes `artifacts/` (94MB — a stadium `.vpk`, video captures)
and `.codex-tmp/` (decompiled CS2 game models, a crash dump) — neither is
this project's own content, deliberately kept out of the public repo.

## 4. What shipped in the 2026-08-30/31 session (most recent work)

All of this is built, deployed to the test server, and pushed to GitHub.
**None of it has been re-confirmed live by the user as of this handoff** —
see §6 for exactly what to check first.

### New feature: ball↔player physics impact (`SoccerModMvpPlugin.BodyImpact.cs`)
User wanted CS:S's behavior replicated: a ball landing on you bounces off
with real energy (letting you then knife it harder — a natural "powershot"),
and a fast ball hitting you knocks you back. Implemented as one shared
per-tick detection pass (`ApplyBallPlayerImpact`, called from `OnTick` after
the existing `ApplyPlayerBallPush`):
- Ball moving ≥`_ballImpactMinSpeed` (150 u/s) hits a nearby player →
  player knocked back in the ball's travel direction, capped.
- If the ball is genuinely *falling* onto the player
  (`-ballVelocity.Z > _ballImpactFallSpeedThreshold`, default 80) it also
  bounces off with retained velocity instead of just registering the hit.
- No separate "powershot" flag needed — kicks already **add** their delta
  onto the ball's existing velocity (never overwrite), so a follow-up knife
  hit on a now-live ball compounds naturally.
- All numbers invented (not CS:S-measured), tunable + persisted via
  `css_sm2ball_impact`, `css_sm2ball_impact_push`, `css_sm2ball_impact_bounce`.

### Kick power no longer angle-dependent for airborne balls (took 2 rounds)
User: "in the air, the angle you're looking shouldn't decide the kick's
power/speed." Round 1 gated `softPitchScale` (power reduction from steep
look-down angle) to only apply when the ball is resting on the ground
(`ballGrounded = ballOrigin.Z <= StadiumPitchPlaneZ + BallCollisionRadius +
SettleGroundToleranceZ`). User came back with "still weaker if I don't aim
directly at an overhead ball" — **pulling the actual `kick_accepted` log
lines showed the real culprit was soft PASS, not soft pitch**: real overhead
kicks were logging `softPassScale` down to 0.25–0.87 purely from where the
aim ray crossed the ball, even though the ball was fully airborne. Round 2
gated `softPassBlend` the same way. **Lesson for next time a "fixed" bug
comes back: pull the diagnostic log lines before touching code again** —
don't just re-reason about it, there were two similarly-named mechanisms
(soft PITCH = view angle, soft PASS = contact point on the ball) and the
first fix targeted the wrong one.

### Crouch left-click kick power
New independent tunable `_leftClickCrouchPowerScale` (default `1.0`,
`css_sm2ball_leftclick_crouch`), used instead of the normal
`_leftClickPowerScale` (`0.85`) when the player is crouching at the moment
of a primary (left-click) kick. Detected via a new static helper
`IsPlayerCrouching(pawn)`: `pawn.MovementServices` → cast to
`new CCSPlayer_MovementServices(movement.Handle)` → `.Ducked || .Ducking`.
Right-click kicks are unaffected.

### Menu restructured to mirror real SoMoE, and pagination rebuilt (4 iterations)
- **Structure**: root menu is now Admin (gated) / Position / Spectate / Help
  / Credits, matching SoMoE's real `menus.sp` `OpenMenuSoccer`. Match and
  Referee moved OFF the root and INTO the Admin submenu. The Cap entry and
  legacy in-game cap commands were subsequently disabled when KICKOFF became
  the only cap UI (see below). New `OpenCreditsMenu` prints version + repo link +
  "Port by Natsu" to chat.
- **Real bug fixed**: the Help menu entry called
  `ExecuteClientCommandFromServer("css_help")`, which runs the command as a
  console command — `ReplyToCommand` then replies to the player's CONSOLE,
  not chat, so selecting Help from the menu appeared to do nothing. Fixed by
  extracting a shared `PrintHelp(player)` method (always prints to chat)
  called directly by both the menu and the `!help` command handler.
- **Pagination, rebuilt from scratch across 4 rounds, all screenshot-driven** —
  do not re-guess any of these numbers, they came from actual measurements:
  1. Plain-mode center-text panel hard-clips at **~4 total rendered lines**
     (title + content) — established in an earlier session.
  2. HTML mode ALSO clips (an old code comment claiming "stable up to 9
     options" was never actually verified and was wrong — same mistake
     class as an earlier wrong "html is stable" claim). A screenshot proved
     title + 6 items + one nav line (8 lines) render fully, and a 9th line
     (a small-font "page X/Y" hint) gets cut. Fix: **dropped the hint line
     entirely**, capacities set to `MenuHtmlFirstPageCapacity=6`,
     `MenuHtmlLaterPageCapacity=7` (items+nav budget, with margin).
  3. First pagination-key design used FIXED keys (8=Back, 9=Next — the
     literal SourceMod convention) per an explicit user request. Live
     testing (screenshot) showed this produces a confusing GAP whenever a
     page has fewer than 7 items ("1..6" then a bare jump to "9. Next" —
     user read it as broken). **Reverted to CONTIGUOUS keys**:
     `MenuPage.BackKey => Items.Count+1`,
     `NextKey => Items.Count + (HasPrev?2:1)`. This is a case where the
     literal ask, once tested live, produced worse UX than the alternative,
     and was explicitly reverted with the reasoning explained to the user.
  4. **Real bug, not cosmetic**: most menus (Match, Cap, Admin, etc.) already
     end with their OWN "Back" item that returns to the PARENT menu. The
     pagination control (go to the previous PAGE of the same menu) was ALSO
     labeled "Back" — a paginated page containing both showed "2. Back /
     3. Back", identical text, different actions. Fixed by **renaming the
     pagination control to "Prev"**. `BuildMenuDisplayLines` is the single
     shared (key, text) builder both `BuildMenuHtml`/`BuildMenuPlainText`
     consume — keep it that way.
  5. The page-splitting lookahead algorithm itself (unchanged): a page
     tentatively assumes it might be the LAST page (reserving a slot for
     Prev only, never Next); only reserves both when the remaining item
     count proves that guess wrong. Keeps page count minimal.

### Ball invisible after round restart until first kick — "game-breaking," 2 attempts
Attempt 1 (wrong): added an explicit `Teleport()` right after the ball
visual's `DispatchSpawn`, theorizing the spawn-keyvalue origin plus an
identical-position first sync `Teleport()` was a no-op that skipped the
engine's render/PVS link. **User reported still broken**, with a screenshot
proving the ball's SERVER-side position was correct (dead-center) — so the
bug is really about CLIENT rendering/delivery, not server position. That
attempt was reverted. **Actual fix, two halves, shipped together:**
1. **Guaranteed per-tick delta**: `SyncOwnedBallVisual` now alternates a
   ±0.015-unit vertical epsilon every tick instead of teleporting to the
   exact same resting position every time — guarantees a genuine networked
   position delta every tick, so a client that missed the entity's creation
   message still has a live stream to recover from (0.03 units
   peak-to-peak on a 37.6-unit ball — imperceptible).
2. **Post-restart refresh**: new `RefreshOwnedBallVisual(reason)`
   (kill + immediately recreate) called via `AddTimer` 0.5s after every
   round start and 1.5s after map start. The original creation happens IN
   the same frame as the round-restart entity churn (deliberately, to avoid
   a visible pop-in) — which is exactly the frame where a client's creation
   message can get lost. Refreshing a moment later, in a quiet frame, gives
   it a second clean chance.

### KICKOFF website now runs CS2 caps; the in-game cap UI is disabled
- The existing KICKOFF site now persists the selected cap name/game/map in
  its SQLite settings and supports both CS:S (`Titan Club 2026`) and CS2
  (`soccer_cssl_stadium_v8`) all the way through `/api/match/prepare`.
- Standard CS2 caps target `212.87.212.58:27017`. When the draw is ready,
  each participant's browser attempts the `steam://connect/...` launch and
  keeps the explicit Join button as a fallback (browsers may show an
  “Open Steam?” confirmation).
- The host-only `/opt/kickoff-rcon/rcon_helper.py` service routes the
  allowlisted assignment import to the correct game. Its systemd unit is
  `kickoff-rcon.service`; `/health/cs2` performs a real, read-only RCON
  command and was healthy after deployment.
- `SoccerModMvpPlugin.WebCap.cs` registers server-console-only import
  commands (`css_sm2webcap_begin`, `assign`, `commit`, `evict`, `status`).
  It persists the selected SteamID/team/position lineup for six hours and
  applies it when those accounts connect or spawn. Home maps to Terrorist
  and away to Counter-Terrorist, matching the existing CS:S cap bridge.
  `SwitchTeam()` alone originally left a newly connected assigned player
  dead/off-field until `!rr`; the bridge now schedules `Respawn()` when the
  assigned player has no live pawn. It also mirrors the temporary CS:S cap
  plugin by setting the controller clan tag to `[GK]`, `[DEF]`, `[MID]`, or
  `[WING]`, then restores the player's previous tag when a new cap begins or
  the six-hour assignment expires.
- `CapOnLoad()` is no longer called, so `css_cap`/`!cap` and the other old
  draft commands are not registered. The Cap item was removed from the
  Admin menu and help now points players to KICKOFF.
- Deployment verification: public API returned the new metadata fields,
  both services were active, the private CS2 RCON health check passed,
  `css_sm2webcap_status` answered, and `css_cap` returned Unknown command.
  No public cap was created during verification, so the final multi-player
  draw/reconnect path still needs one real community cap test.

### GitHub push (2026-08-31)
Pushed the whole repo to https://github.com/Deidakom/CS2_Soccer_Mod (was
empty). Real secrets were found and redacted before pushing:
- `deploy/testserver/soccermod_test.cfg` had a literal
  `sv_password "<redacted>"` — now a placeholder in the repo. **The live test
  server's actual running password was never touched or redeployed.**
- `docs/2026-08-30-session-handoff.md` had three literal password mentions
  in prose — redacted.
- `.gitignore` extended to exclude `artifacts/` and `.codex-tmp/` (see §3) —
  neither is this project's own content.

**Environment quirk hit during the push, now resolved**: the repo directory
is owned by a different Windows account (`CodexSandboxOffline`) than the
shell's logged-in user (`sergi`), so git refused to operate until
`git config --global --add safe.directory <path>` was set; there was also no
git commit identity configured. Both are now set persistently on this
machine (`user.name`/`user.email` = `Natsu <samenta.mail@gmail.com>`), so a
future push from this machine should not hit either prompt again. If
working from a *different* machine/environment, expect to hit both again —
these are git-config changes, which should be flagged to the user rather
than run silently (per this session's own operating rule).

### Public v1.0 Beta release packaging and team appearance follow-up
- `deploy/release/` now contains a checksum-verifying Linux installer,
  manual Windows instructions, a verification script, and a safe example
  cfg. `tools/build-public-release.ps1` assembles a top-level-folder ZIP
  containing the compiled plugin plus the exact ball/menu/radar resources
  used by the test server. It deliberately excludes runtime JSON, passwords,
  third-party dependencies, and the Workshop map.
- Fresh public installs no longer grant the project owner's Steam account
  root automatically. Server operators must run
  `css_admin_add <steamid64> root` once through server console/RCON. Existing
  installations retain their private `soccermod_admins.json` during upgrade.
- Claude delivered the self-contained Phase A+B team-appearance spec at
  `docs/2026-08-31-teamcolor-spec.md`. The implementation is isolated in
  `SoccerModMvpPlugin.TeamColor.cs`: T uses a softened red tint plus the
  stock Phoenix model; CT uses a softened blue tint plus the stock SAS model.
  Model and tint writes are reasserted after spawn and round start, include
  bots, and never add the stock models to the precache manifest.
- `css_sm2teamcolor <on|off>` and `css_sm2teammodel <on|off>` are independent,
  persistent match-permission commands. Team RGB values can also be tuned with
  `css_sm2teamcolor <t|ct> <r> <g> <b>`. Existing match-settings JSON migrates
  safely because all new persisted fields are nullable.
- The combined source builds under the portable .NET 10 SDK with zero warnings
  and the expanded Node regression suite passes 85/85. The team appearance is
  still visually unverified; specifically check distance readability,
  WeaponPaints gloves, knives, respawn, and round restart.

## 5. Load-bearing lessons from the project's history — do not re-learn these the hard way

- **`mp_maxrounds 0` is catastrophic in this CS2 build**, not "unlimited"
  like some other Source titles. It wedges the ENTIRE match state — the
  match is considered "already over" before round 1, which under CS2's
  freeze-period movement lock manifests as: players frozen on spawn, unable
  to sprint, unable to be slain, `!rr`/`!match start` silently no-op-ing,
  and a phantom native warmup banner. This cost an entire session to
  diagnose because the symptoms looked unrelated. **Never add
  `mp_maxrounds 0` (or `mp_halftime 0`) to any cfg without testing a full
  service restart first.** `gamemode_casual_server.cfg` has an explicit
  comment forbidding this. The value `999999` is the safe stand-in and is
  checked as a regression canary after every deploy (§2).
- **`CCheckTransmitInfo.TransmitEntities.Add()` hard-crashes the server**
  (SIGSEGV, status 139, crash-loops). `Remove()` is safe (used to hide the
  physics ball from clients) — `Add()` is not, and `CheckTransmit` only even
  runs once a client is connected, so an empty server looks fine right up
  until someone joins. Never retry this.
- **Source 2's hull cooker silently mangles convex physics hulls above ~80
  faces** — feeds a wrong/lopsided shape through with no error. Any custom
  physics hull must stay ≤80 faces and be verified with
  `Source2Viewer-CLI -i <vmdl_c> --block PHYS` before trusting it.
- **CSSharp's own `ChatMenu`/`CenterHtmlMenu` don't do real number-key
  input** — `ChatMenu` requires typing `!1`/`!2`; `CenterHtmlMenu` scrolls
  with W/S/E. The plugin's own `NumberMenu` (Menu.cs) intercepts the engine
  commands `slot1`..`slot9` directly (CS2 binds number keys 1-9 to these by
  default) via `AddCommandListener(..., HookMode.Pre)`, returning
  `HookResult.Handled` to swallow the weapon switch.
- **No native spin source exists in this CS2 build reachable from
  CounterStrikeSharp.** `phys_thruster`'s "Apply Torque" flag does nothing
  without "Apply Force" also set (contrary to its FGD docs), and
  force+torque combined doesn't respect its own `forcetime` auto-shutoff.
  Direct `ApplyAbsVelocityImpulse`/`ApplyLocalAngularVelocityImpulse` via
  `AcceptInput` are silent no-ops. This blocks ball curve/spin entirely
  until CS2Fixes (a native Metamod addon) publishes an updated
  `CEntityInstance_AcceptInput` signature for this game build — check its
  release date periodically; as of the last check it was still stale
  (2026-08-24, older than the server binary).
- **Verify UI/rendering behavior in-game, not just via RCON/logs.** Several
  past sessions shipped menu/HUD changes that "worked" per
  `[SM2DIAG]` logs (proving the server-side code path ran) but were
  completely broken or invisible client-side. When a fix is reported as not
  working, prefer pulling real diagnostic log lines (or asking for a
  screenshot) over re-guessing from first principles — this session's
  soft-pass/soft-pitch mixup and the menu pagination saga (§4) are both
  examples of guesses that needed correcting against real evidence.
- **Never git add -A blindly or assume a working tree has no secrets.**
  This session found a live server password sitting in a tracked cfg file
  moments before it would have been pushed to a public GitHub repo. Always
  grep staged files for password/secret patterns before committing,
  especially the first time a repo is initialized.

## 6. What to verify first, next session

The ball-visibility fix and several feel changes were subsequently confirmed
live by the user. Remaining checks, in order of importance:

1. **Team appearance** — confirm every T/bot uses the Phoenix model with a
   visible softened-red tint and every CT/bot uses SAS with softened blue.
   Verify respawn and `mp_restartgame`, then check WeaponPaints gloves and
   knives. Test each toggle separately; model-off fully takes effect as the
   affected players respawn.
2. **First real CS2 KICKOFF cap** — publish a standard CS2 cap with real
   participants and confirm all 12 clients receive the Steam launch prompt,
   reconnect to port 27017, land on their assigned home/away teams, and see
   the assigned position in chat. The backend/helper/plugin boundaries were
   tested independently, but this public side-effect was intentionally not
   triggered during deployment verification.
3. **Menu pagination** — open a menu that splits across pages and confirm:
   no duplicate "Back" labels, Prev/Next
   keys are contiguous with the visible items (no numbering gap), and
   nothing is visually cut off in HTML mode.
4. **Crouch kick power** — knife-kick a grounded ball while crouched vs.
   standing and confirm crouched kicks feel like full power (1.0) instead
   of the normal 0.85.
5. **Airborne kick power independence** — volley/header an airborne ball
   while aiming imprecisely at it and confirm the shot doesn't come out
   weak/short anymore.
6. **Ball↔player impact feature** — this is entirely new and untested:
   check that a fast ball hitting a player knocks them back, and that a
   ball genuinely falling onto a player bounces off instead of just dying.
   All the tunable numbers are invented, not CS:S-measured — expect to
   iterate on `css_sm2ball_impact_push`/`_bounce` based on how it feels.

If any of these are still broken, the diagnostic-log-first approach (§5)
is the way to re-diagnose, not re-guessing from code inspection alone.

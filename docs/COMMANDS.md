# Command reference

Every command works both as a chat message (`!command args`) and as a
console command (`css_command args`). `!menu` covers most of this without
needing to remember any of it — this reference is for admins and anyone
who wants direct chat/console access.

## Everyone

| Command | Does |
|---|---|
| `!menu` | Opens the SoccerMod menu (Match, Cap, Training if admin, Ranking, Statistics, Positions, Help, Settings, Credits). |
| `!cap` | Opens the cap menu — join the pool, start a captain-pick fight, or (if you're the captain on turn) pick players. |
| `!pick <n>` | Pick pool member `n` (captain on turn only). |
| `!pos` | Set your cap positions (GK/LB/RB/MF/LW/RW/Spec only) — shown next to your name when captains pick. |
| `!t` / `!ct` / `!afk` / `!brb` | Join a team or move to spectator. Only outside a running match. |
| `!sprint` | Burst of speed (1.25×, 3s, 7.5s cooldown) — or just hold your `+use` key. |
| `!tp` | Toggle third-person camera. |
| `!gk` | Claim or release your team's goalkeeper skin (one per team). |
| `!kill` | Respawn yourself instantly if you get stuck. |
| `!rdy` | Mark yourself ready during a match pause — resumes automatically once everyone is. |
| `!forfeit` | Vote to forfeit the match for your team (majority of your team needed). |
| `!lc` / `!late` | List connected players in join order. |
| `!rank` | Your ranking for the current match (needs 5+ players/team). |
| `!prank` | Your all-time public ranking. |
| `!top` | Top players by points. |
| `!stats` | Your personal stats. |
| `!spec me` | Move yourself to spectator. |
| `!help` / `!commands` | Print this list in chat. |

## Match (open to everyone by default, `soccer_test.cfg`'s `publicmode` policy)

| Command | Does |
|---|---|
| `!match` | Status, or open the match menu with no arguments. |
| `!match start` | Start a match. |
| `!match stop` | Stop the running match. |
| `!match pause` / `!match unpause` | Pause/resume. |
| `!match status` | Print the current phase/score. |

## Admin (needs the `admin` flag or higher — see [Admin & permissions](#admin--permissions))

| Command | Does |
|---|---|
| `!admin` | Open the admin menu directly. |
| `!training` | Open the training menu (Cannon, Personal Cannon, Spawn/Remove Ball, Disable Goals). |
| `!ref` | Referee menu (yellow/red cards, manual score adjustment). |
| `!yellowcard <player>` / `!redcard <player>` | Card a player directly. |
| `!uncard <player>` / `!uncardall` | Clear cards. |
| `!refscore <ct\|t> <add\|remove>` | Adjust the score directly. |
| `!kick <player>` / `!slay <player>` | Kick or slay a player. |
| `!ban <player> [minutes]` | Ban a player. No minutes = permanent (**root only**, see below). |
| `!unban <steamid64>` | Remove a ban (**root only**). |
| `!spec <player\|all>` | Move a player (or everyone) to spectator. |
| `!sm2health godmode <on\|off>` / `!sm2health amount <1-500>` | Toggle damage immunity or set HP when it's off. |
| `!sm2gk_area ...` | Tune the goalkeeper save-detection box. |
| `!sm2djb <on\|off\|seconds>` | Duck-jump block toggle/timing. |
| `!sm2chat prefix\|prefixcolor\|textcolor\|deadchat ...` | Chat appearance settings. |
| `!admin_add <steamid64> <flag>` / `!admin_remove <steamid64>` | Grant/revoke an admin flag (**root only**). |
| `!admin_list` / `!banlist` | List current admins/bans. |

Match-configuration commands (need the `match` flag or higher):

| Command | Does |
|---|---|
| `!rr` | Restart the round without touching the match clock/score. |
| `!maprr` | Reload the Workshop map. Open to everyone, no flag needed. |
| `!matchrr` | Stop, then start a fresh match. |
| `!teamname <ct\|t> <name>` | Set a team's display name. |
| `!sm2match_config periods\|periodlength\|breaklength\|goldengoal <value>` | Match-length settings. |
| `!sm2goal_calib <halfWidth> <maxHeight> [lineY] [depth]` | Set the goal-detection frame directly. |
| `!sm2goal_swap` | Flip which end belongs to which team. |
| `!sm2goal_punish <on\|off>` | Toggle killing the conceding team on a goal. |
| `!sm2kickoffwall <on\|off>` | Toggle the post-kickoff possession wall. |
| `!sm2lock <on\|off>` | AFK-kicker + serverlock, arms only while a cap is running. |

Ball-tuning commands (**root only** — the whole physics feel of the mod):

`!sm2ball_spinfactor`, `!sm2ball_push`, `!sm2ball_airkick`, `!sm2ball_kicksound`,
`!sm2ball_impact`, `!sm2ball_impact_push`, `!sm2ball_impact_bounce`,
`!sm2ball_impact_feedback`, `!sm2ball_settle`, `!sm2ball_elevation`,
`!sm2ball_softpass`, `!sm2ball_softpitch`, `!sm2ball_leftclick`,
`!sm2ball_rightclick`, `!sm2ball_leftclick_crouch`, `!sm2ball_rightclick_crouch`,
`!sm2ball_center`, `!sm2ball_collision` — all reachable through
`!menu → Admin → Ball` with live value labels; console gives exact values.

## Admin & permissions

Flags, from least to most access: `match` < `admin` < `soccermod` (implies
`admin`+`match`) < `ball` < `root` (implies everything). Grant one with
`css_admin_add <steamid64> <flag>` (root only). The **Player Promotion**
entry in `!menu → Admin` lets a root admin promote/demote the `soccermod`
tier without touching the console.

A fresh install has no admin at all — grant your own SteamID64 `root`
once via RCON or server console after the first install.

## Server-console / RCON only

These never accept an in-game player and exist for calibration, one-off
diagnostics, or the KICKOFF website bridge — you will not normally need
them:

- Ball diagnostics: `sm2ball_status`, `sm2ball_mode`, `sm2ball_model`,
  `sm2ball_physics`, `sm2ball_trial`, `sm2ball_impulse`,
  `sm2ball_impulse_input`, `sm2ball_spin_input`, `sm2ball_spin_isolate`,
  `sm2ball_thrust`, `sm2ball_torque_test`, `sm2ball_kickmode`,
  `sm2ball_native_handle`, `sm2ball_replace_test`, `sm2ball_reset_center`,
  `sm2ball_restore_map`, `sm2ball_trace_arena`, `sm2ball_wallassist`,
  `sm2ball_collision`, `sm2knife_give`, `sm2_reload_settings`.
- Goal calibration: `sm2goal_measure` (traces the real crossbar/posts —
  run this once per map before tuning `sm2goal_calib`), `sm2goal_test`,
  `sm2goal_roundwin`.
- Player/entity diagnostics: `sm2_playerstatus`, `sm2_move_probe`,
  `sm2_high_geometry`, `sm2_button_probe`, `sm2_mutelanding`,
  `sm2inventory_status`.
- Sound blocking: `sm2sound_log`, `sm2sound_block`, `sm2sound_unblock`,
  `sm2sound_blocklist`.
- KICKOFF website bridge (see `kickoff/README.md`):
  `sm2webcap_begin/reference/assign/commit/clear/evict/status`.
- Menu internals: `sm2menu_hud`, `sm2menu_mode`, `sm2menu_classic_ready`.
- Stats: `wiperanks`.

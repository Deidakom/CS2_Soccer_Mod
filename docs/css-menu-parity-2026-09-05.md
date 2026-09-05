# CS:S → CS2 in-game menu audit (1.3.0 historical snapshot)

**See the [1.4.0 audit](css-menu-parity-2026-09-05-v1.4.md) for current coverage.**

Reviewed CS:S `15cdf2c` against CS2's 1.2.0 baseline and the 1.3.0 catch-up
changes. **The menus are not fully feature-equivalent.** This report distinguishes
working ports from incomplete features; a similar label is not proof of parity.

The [static CS:S inventory](css-menu-inventory.json) contains 630 distinct
file/function/key entries after removing commented-out code. Most are choices
inside submenus (177 are color choices), not 630 independent features. Runtime
player/map/model lists and conditional availability still require live checks.

## Implemented in this catch-up

| Area | Previous CS2 behavior | 1.3.0 change |
| --- | --- | --- |
| Kickoff wall | Disabled; opposing team rubber-banded at halfway; no visible circle | Configurable visible team-colored halfway outline and 252.5-unit semicircle, allowing the kicking team into the kickoff pocket and keeping the other team outside. Both teams constrained along the rest of halfway. |
| Kickoff lifecycle | Half-only check; old boundary could outlive a stop | Outline clears on touch, timeout, stop, full time, map change and unload. Opponents cannot knife/push the match ball through the restriction. |
| Menu timeout | Closed after 30 seconds | Stays open until exit, matching `MENU_TIME_FOREVER`; legacy timeout available through `KeepMenusOpen=false`. |
| Admin → Settings | Lists and public-mode toggle only | Misc, Skin, Chat, Sound and Training settings are reachable. |
| Misc settings | Working commands without menu access | Kickoff on/off and style, duck-jump block, damage feedback, health protection, sprint profile/use-button and CAP server lock. |
| Match settings | Period, break, golden goal and names | Forfeit, match-log and match-info submenus with persistence and permission checks. |
| Forfeit | Fixed public majority vote | Enable, goal-deficit condition, admin/public availability, CAP-only and auto-spectator options. Departed/switched players' votes are excluded. Forced winner is reported correctly even when score differs. |
| Sprint | Fixed 3s burst + cooldown; messages only | Sprint 2.0 stamina: 3s drain, 1s recovery delay, 7.5s recharge, hold/toggle control, early stop, exhaustion/release gate and personal HUD modes. Legacy burst remains selectable. |
| Sprint HUD | Removed because it covered menus | Opt-in HUD; hidden while menus are open and composed with the match scoreboard instead of overwriting it. Defaults off on this server. |
| Chat | Command-only prefix/colors/dead-chat settings | Prefix text input, color selection and dead-chat mode menu. Existing formatting coverage remains partial, below. |
| Skins | Command-only team tint/model/legs controls | Team tint and uniform-model toggles, goalkeeper and first-person legs entries. |
| Sound | Command-only hash block list | Diagnostic toggle and unblock entries. Exact CS:S sound-file browser is not implemented. |
| Ball profiles | Console-only improved/creative/legacy | Ball admin menu now exposes all profiles. |
| Training drills | New commands only | Direct target, wall-pass target, clear target and replay in the Training menu. |

The kickoff boundary uses server-side position constraints and beam outlines,
not a converted Source 1 collision model. It retains the pre-existing ten-second
anti-stall timeout. Tangential and vertical player velocity survive correction.
This is a gameplay equivalent for the stadium's Y-axis halves, not universal
support for every CS:S map orientation or its arbitrary wall assets.

## Coverage and remaining gaps

| CS:S area | CS2 status after 1.3.0 |
| --- | --- |
| Main menu, back/exit, pagination, help, credits | Present. Existing CS2 Match/Reload/Cap shortcuts retained. Client keybind/custom-HUD constraints remain engine-specific. |
| Match start/stop, pause, periods/break, golden goal, names | Present. Detailed ready-check/stoppage and every CS:S match-info/log category are not equivalent. |
| Kickoff wall | Restored with outline/semicircle and legacy fallback, subject to the implementation differences above. |
| Forfeit options | Main options ported. CAP-only recognizes a completed in-game draft or an active website cap when the match starts. |
| Ranking | Partial: CS2 has current-match rank, all-time rank/top10 and personal stats. CS:S top50, per-round/per-match averages, historical competitive tables and per-player reset menus are still absent. Historical competitive data cannot be reconstructed from CS2's current-match reset counters. |
| Statistics | Partial: core personal counters exist; CS:S team/round dashboards and extended chat event preferences are missing. |
| Positions/CAP | Present core position selection, fighting, weapons and picks. Not all CS:S first-12/pre-cap-join, tag, assignment and roster rules have equivalent settings. Source 1 weapon choices also require Source 2 substitutions. |
| Referee | Core cards, whistles and player actions exist; requires a separate action-by-action client validation. |
| Admin management | CS2 tier promotion/list/ban/unban exists. The full SourceMod flag editor and offline SteamID management menus differ from this server's intentional tier model. |
| Public mode | CS2 intentionally uses a restricted main-menu switch, not CS:S's three-value permission model. Existing server access rules were preserved. |
| Allowed maps / map defaults | CS2 is tied to the Workshop stadium and goal geometry; arbitrary CS:S map allowlists and per-map defaults cannot be copied as equivalent behavior. |
| Sprint | Stamina and control modes ported. Arbitrary HUD position/RGB and the original sprint sound still need the client asset/UI port. |
| Grass | Missing. CS:S per-player material replacement requires a Source 2 asset and transmission design; `.vmt/.vtf` files are not usable CS2 resources. |
| Shouts | Missing. The original categories, sounds, volume/delay/mode settings and per-player filtering need Source 2 soundevents/client delivery. |
| Training | Cannon/personal cannon/balls/goals and new shot drills present. Hoop/can/plate props, prop positioning, goal-target models, cone manager and full advanced training mode are missing. |
| Skins/jerseys | CS2 team tint/stock models and goalkeeper appearance present; original CT/T/GK model selection and jersey assets are not equivalent. |
| Chat | Prefix/color/dead-chat controls exposed, but older match/CAP messages still use their own formatting. The per-recipient CS:S dead-chat routing is approximated through server cvars. |
| Sound control | Event-hash block/unblock exists; CS:S map sound-list/file controls and dedicated shout/sprint sound preferences differ. |
| Miscellaneous | Ready modes, ragdoll/dissolve variants, class-choice, hostname option, rank cooldown/modes, celebration and all first-12 settings still need ports. Health/no-damage, duck-jump block and CAP AFK locking already have working CS2 backends. |
| Tickrate selector | CS:S's native tickrate extension cannot be ported as a CS2 menu command with the same effect. |

## Validation boundary and next dependencies

The compiler, production-math tests and source/menu wiring checks cannot prove
client display, key input, sound audibility or multi-player feel. Those require
in-game testing with a connected player. The German server was empty during the
headless checks.

The German host no longer contains `/home/gameserver/css`, so there is no live
CS:S reference menu there to operate. The repository documents a Windows
Workshop Tools installation at `E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive`;
that machine/compiler is not accessible from this Mac or the German Linux host.
Access was requested for the remaining custom client assets. Full parity remains
open; the unresolved rows above are not represented by misleading dead buttons.

## Rollback

`css_sm2kickoffwall off` disables the restored boundary. `css_sm2kickoffwall_style
legacy` selects the earlier half-only restriction. `css_sm2sprint_profile legacy`
restores the older fixed-burst sprint. `css_sm2ball_profile legacy` remains the
ball behavior fallback. Preferences live in `soccermod_menu_parity.json` and
`soccermod_sprint_prefs.json`; existing ball power/size is unchanged.

The deployment installer now also backs up/restores match and menu-parity
settings along with the binary and ball settings. It still preserves new ranks,
bans, admins and match records. The deployment report records the exact snapshot.

# CS:S → CS2 menu parity after 1.4.0

This supersedes the remaining-server-work list in the [1.3.0 audit](css-menu-parity-2026-09-05.md).
The source reference is the repository's CS:S mod. Full client-observed parity is
not yet established. Changes and operational limits are recorded below.

| Area | Current implementation | Remaining difference / validation |
| --- | --- | --- |
| Kickoff / match controls | Visible halfway/circle restriction; start/stop, pause, periods, golden goal; CS:S halfway stoppage or legacy timer ending | Ball freezes in place during pauses; stadium Y-axis geometry; connected-player tests still required |
| Ranking | Public/competitive Top 50, totals and averages, personal details/reset, last-connected view, cooldown and default view setting | Competitive history starts at 1.4; old missing match records cannot be reconstructed |
| Statistics | Team, player, round, match, possession, round MVP/MOTM, extended chat preferences | Warmup goal bookkeeping stays disabled by the prior server preference; historical play time starts when recorded |
| Ready check | Off/Auto/Manual, ready/not-ready, paused roster by SteamID and side | Native CS2 numbered menu replaces SourceMod panel; client input needs play test |
| Match log / information | Categories, days, start/stop time, overnight windows; individual match-start announcements | Server-local schedule time is shown in the menu; engine HUD differs |
| Forfeit | Enable, deficit, availability, CAP-only, auto-spec | Existing majority vote rules retained |
| CAP / positions | Fight, weapons, picks, first-player rule, pre-CAP signup, roster, team size, persistent draft assignments | CS:S weapons use existing CS2 substitutions; arbitrary SourceMod plugin flags/password rotation are not introduced |
| Admin / public | Root offline SteamID64 editor; existing supported flags; optional Admins / CAP-Match / Free-for-all controls | CS:S flags without a CS2 backend are not presented as working permissions; separate restricted-main-menu preference remains |
| Training | Cannons, balls, native CS2 cones/cans/plates, scored hoop outlines, goal targets, position editor, per-map saved layouts, advanced mode | Original hoop/can/plate/target models, physical hoop rim and target-blocker art require conversion; no claim of identical prop collisions |
| Sound control | Recent observed event hashes and direct block/unblock | Source 1 sound-file browser has no direct CS2 event-name equivalent |
| Chat | Prefix/colors now cover global match/CAP/training announcements; per-player statistics event filters | Exact per-recipient SourceMod dead-chat routing remains a cvar approximation |
| Sprint | Stamina, hold/toggle, messages, HUD modes and legacy fallback | Original sprint sound and arbitrary HUD position/RGB require client asset/UI work |
| Skins / jerseys | Stock uniform and goalkeeper appearance, team tint, first-person legs | Original jersey/model collection is not converted |
| Grass | No converted grass replacement pack | Source 1 VMT/VTF materials require a Source 2 material/transmission implementation and client validation |
| Shouts | Original custom sound collection not delivered | Requires compiled soundevents and Workshop client delivery; no silent fake menu buttons |
| Maps / defaults | Stadium calibration and map-scoped training layouts | Arbitrary maps need measured goal/reset/wall geometry and ported assets; listing them alone would not make gameplay work |
| Misc engine options | Health protection, duck-jump block, CAP lock, rank modes/cooldown, hostname update toggle, optional post-goal celebration weapons | Source 1 ragdoll/dissolve/class-selection variants are not established as equivalent CS2 behavior |
| Tickrate selector | Not offered | Source 1 tickrate extension is not a supported CS2 tickrate selector |

## Client asset dependency

The Mac and German Linux server do not contain the Source 2 resource compiler.
The documented Windows Workshop Tools installation is at
`E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive`.
Access to that machine/project has been requested but has not been supplied.
The remaining original sounds, materials, jerseys and models cannot be made
client-visible by copying their Source 1 files to the Linux server.

Native CS2 training model paths were verified in the host's `pak01_dir.vpk` and
are included in the map resource manifest. The visible hoop is a beam outline;
scoring uses swept, full-ball clearance through its oriented plane. It does not
pretend to be the unconverted physical Source 1 hoop.

## Data and rollback

`css_sm2parity_status` reports versioned runtime controls. `css_sm2training_test
cone|can|plate|off` is a server-console diagnostic outside matches; test props
expire after eight seconds.

Competitive totals are written to `soccermod_competitive_stats.json`, separate
from the older public-stats writer. Missing historical competitive totals start
empty. If an existing competitive sidecar cannot be read, writes to it are
suppressed so the corrupt file is not silently replaced by empty data.

The targeted installer backs up the complete plugin directory and generates an
explicit rollback script. Binary and gameplay settings revert; public ranks,
competitive history, admin/ban data and saved layouts remain current. Re-select
legacy stoppage, ball, kickoff or sprint settings individually when only one
behavior should revert.

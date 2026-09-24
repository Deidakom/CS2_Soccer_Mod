# awesome-cs2 evaluation (2026-09-24)

[samyycX/awesome-cs2](https://github.com/samyycX/awesome-cs2) at `5814a70`
(synced 2026-09-23) lists 162 entries in 9 sections:

- 17 Metamod plugins
- 95 CounterStrikeSharp plugins
- 9 SwiftlyS2 plugins
- frameworks, tools, status trackers and developer resources

41 entries were cloned and checked for licence file, last commit and framework
version. No football mod is listed. The value for SoccerMod lies in
infrastructure (menus and HUD, addon delivery, persistence, operations), not in
gameplay plugins.

**Licence rule:** copy code only from MIT, Apache or BSD entries and keep
their notice. GPL-3 and unlicensed entries are ideas to reimplement.

## Already applied

- **Signature tracker.** The status tracker
  [ianlucas/cs2-signatures](https://github.com/ianlucas/cs2-signatures)
  diagnosed the CS2 1.41.8.2 break. The resulting KHook migration and the
  gamedata-driven native bridge are described in
  [cs2-1.41.8.2-server-stack-2026-09-24.md](cs2-1.41.8.2-server-stack-2026-09-24.md).
  After each CS2 update, read the tracker's CounterStrikeSharp rows before
  restarting.
- **HTML flicker fix.** The flicker fix from CS2FlashingHtmlHudFix is already
  part of the menu.

## Recommended, in priority order

### 1. Clickable classic menu (effort M)

Use CounterStrikeSharp's `CustomHudLayout` API (since v1.0.374, so available
now) with [nvmxre/cs2-ui-kit](https://github.com/nvmxre/cs2-ui-kit). The kit
is MIT, so we can copy its `UIKit`, `Panel` and `BuyMenuBridge`, about 800
lines.

What it gives us:

- mouse selection instead of binds
- the stock **B** key opens the menu
- no HTML redraw flicker
- no `point_script`/`RunScriptInput` bridge

The same pipeline carries toasts, a persistent score/clock HUD and the
stamina bar.

Risks:

- **Maturity.** The kit is `2.0.0-preview.1` with a single maintainer.
- **Blank texts after a join.** Since 1.41.8.2 texts go blank after a player
  joins. Workaround: rebuild about 1.5 s after connect-full; CounterStrikeSharp
  PR #1434 is pending.
- **Buying must be on for B.** The B bridge needs `mp_buy_anywhere`/`mp_buytime`
  enabled. Block `buy`/`autobuy`/`rebuy` and verify that no weapon can be
  obtained.
- **Four panel types.** Only Panel, Label, Image and Button render.
- **Layout path.** The layout value must be the source path ending in `.xml`;
  ours ends in `.vxml`.
- **Delivery.** It needs client addon delivery (item 2).

### 2. Addon delivery with MultiAddonManager (effort M)

The server already runs
[MultiAddonManager](https://github.com/Source2ZE/MultiAddonManager) for the
menu UI and jersey addons. Its v1.5.4 is a SourceHook build with the old
offsets 344/584 instead of 376/616, so `update-server.sh` moves it to v1.6.1.
v1.6.1 is KHook, matches the server now and carries the offsets for the
2026-09-23 build.

With delivery working, add content in this order:

1. the UI layout
2. kick, goal and whistle sounds (Kandru/cs2-quake-sounds pattern,
   reimplemented; darkerz7/CSSharp-Fixes `css_fixes_emit_sound_volume_fix`)
3. kits and pitch-side billboards

### 3. Stats and player settings in a database (effort M–L)

Use one SQLite store (`Microsoft.Data.Sqlite`) keyed by SteamID64 instead of
per-feature JSON files.

- **Stats.** Copy the infrastructure of
  [NeuTroNBZh/CS2-STATPLAY](https://github.com/NeuTroNBZh/CS2-STATPLAY) (MIT):
  buffered capture, an asynchronous transactional flush, versioned migrations,
  reconnect backoff and a `server_id` column. Replace its kill events with
  goal, assist, pass and save.
- **Settings.** Menu mode, FOV, third person and sound volumes follow the
  Clientprefs idea. Clientprefs has no licence, so reimplement it.

### 4. Discord and website events, demos (effort S–M)

- **Match events.** Post kickoff, goal (scorer and assist), half-time, final
  score and cards as JSON to Discord and KICKOFF. Model it on
  [MatchZy](https://github.com/shobhit-pathak/MatchZy) `PublishEvents.cs`
  (MIT).
- **Demos.** `tv_record` per match with optional upload, as in MatchZy
  `DemoManagement.cs`. Stop recording before any map reload.
- **Chat relay.** An optional chat relay is about 50 lines.

### 5. Small tickets, all reimplemented (effort S each)

- **Native F1/F2 vote panel** for forfeit, ready check and vote-kick (idea
  from CS2MenuManager's `PanoramaVote`).
- **Per-player FOV** with an admin-capped range (idea from
  SLAYER_UnrestrictedFOV).
- **Third-person wall trace**, so the camera does not clip into walls (idea
  from ThirdPerson-Revamped).
- **Blocking and anti-flood.** Block radio, chat wheel, spray and ping, and add
  chat anti-flood (idea from cs2-Game-Manager).
- **Scoreboard rank** from our ranking points (idea from cs2-ranks' FakeRank).
- **Timed announcements**, via AutomaticAds or a timer in our plugin.

### 6. Operations, only when needed

These are all KHook builds and now installable:

- **AcceleratorCS2**, temporarily while hunting a crash. Uncaught C#
  exceptions then end in a dump and quit.
- **ServerListPlayersFix**, if the server browser shows 0 players.
- **GameBanFix**, if game-banned joiners block later joins.
- **StripperCS2**, only for edits that must happen before entities spawn.
- **CSSharp-Fixes**, with `no_block` and `movement_unlocker` kept off: they
  would change body push and the tuned movement.

Every native or signature-based plugin adds one more thing to check after each
CS2 update.

## Not recommended

- **Second menu or admin systems.** CS2MenuManager, T3Menu-API and anything
  depending on MenuManagerCS2 use W/S/E/Shift keys that clash with play.
  CS2-SimpleAdmin collides with six of our commands (`css_admin`, `css_ban`,
  `css_kick`, `css_rr`, `css_slay`, `css_unban`); css-bans only makes sense
  with it.
- **CS2Fixes as an install.** It is ZE-oriented and overlaps admin, votes and
  AFK. It stays a gamedata reference only.
- **MovementUnlocker.** It would re-open the tuned sprint and ball feel.
- **Unrelated categories:** weapon skins; bomb, competitive, gun,
  deathmatch and fun modes; movement and timer modes; Zombie Escape tooling;
  bots; economy, VIP and store; map voting; audio streaming; cosmetics
  (emotes, and a model changer that warns about GSLT bans).
- **Ports to other frameworks** (SwiftlyS2, ModSharp, Plugify).
- **Entries built for net8 or old CounterStrikeSharp** releases: reimplement
  instead of loading stale builds.

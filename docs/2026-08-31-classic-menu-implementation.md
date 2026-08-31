# CS:S-style menu implementation — 2026-08-31

## Outcome

The menu now has a renderer-independent model and restores the original
SoMoE-19 root order for every feature that currently has a working CS2
backend:

1. Admin (permission-gated)
2. Ranking
3. Statistics
4. Positions
5. Help
6. Settings
7. Credits

The Admin branch follows the original grouping: Match, Cap, Referee,
Spec Player, Reload Map, and Settings. Training, Shouts, skins, and other
features with no CS2 implementation are intentionally not displayed as dead
options.

Back is no longer a fake content item. The classic renderer uses the original
SourceMod navigation convention:

- `1`–`7`: choices
- `8`: Back/previous page
- `9`: next page
- `0`: Exit

The small plain/HTML fallback keeps contiguous navigation keys because that
was already confirmed clearer in live testing on the clipped CS2 center panel.

## Renderers

`css_sm2menu_mode <plain|html|classic>` selects the global renderer and is
persisted in `soccermod_settings.json`.

- `plain`: stable `PrintToCenter`, current safe default.
- `html`: legacy `PrintToCenterHtml`; still available, still subject to the
  engine's redraw/fade pulse.
- `classic`: Valve `custom_hud_layout`, using the companion content addon in
  `src/workshop-addon/soccermod_classic_ui`.

Classic mode is fail-safe. The plugin initially renders plain text and only
switches after the addon-side `cs_script` sends
`css_sm2menu_classic_ready`. If the content addon is missing, fails to mount,
or its script cannot load, menus remain usable in plain mode.

## Why a script bridge exists

Valve added `custom_hud_layout` in August 2026. The installed Workshop Tools
support per-player dialog variables/classes and stable CSS panels, but
CounterStrikeSharp API 1.0.373 does not yet expose the entity's native
per-player methods. Directly writing the entity's internal vectors would be a
fragile crash risk.

The plugin therefore spawns a `custom_hud_layout` and `point_script`. The
script receives encoded menu state through the point entity's supported
`RunScriptInput` input and calls Valve's official per-player HUD API. The
payload is carried in a short-lived target-name field on a helper entity.
This intentionally avoids `RegisterCheatCommand`, which is rejected on the
production server while `sv_cheats` is `0`. The HUD never captures mouse
input. Number-key input continues through the already proven
`css_1`–`css_9`/`css_0` bindings.

## Local verification completed

- .NET Release build: 0 errors, 0 warnings.
- Node test suite: 75/75 passing.
- Valve `resourcecompiler`: XML, CSS, and script all compiled; 3 compiled,
  0 failed.
- Compiled local Workshop Tools output:
  `E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\csgo_addons\soccermod_classic_ui`

Rebuild after asset changes with:

```powershell
.\tools\build-classic-menu-addon.ps1
```

## Live deployment result and remaining activation boundary

The rebuilt DLL and all three compiled HUD resources (`vxml_c`, `vcss_c`,
and `vjs_c`) were installed on the test server on 2026-08-31. The persisted
mode is `classic`, the service is healthy, the stadium and ball load, and
`mp_maxrounds` remains `999999`.

The live probe also established an important engine boundary: loose resources
under the base `game/csgo` search path can load on the initial `de_dust2`, but
the `point_script` is not allowed to execute after the third-party stadium
Workshop addon becomes the active addon context. The plugin therefore remains
on its intentional plain fallback on the stadium. Copying the same files to
the server again cannot activate the visual skin.

For the visual skin itself, the source addon must be published/installed so
clients receive its Panorama resources and the script is trusted as an active
addon. The current stadium is a third-party Workshop map, so the second addon
must be mounted alongside it through MultiAddonManager.

1. In Workshop Tools, publish/update the staged addon named
   `soccermod_classic_ui` and note its Workshop ID.
2. Follow `docs/2026-08-30-multiaddonmanager-staging.md` to arm the already
   staged MultiAddonManager binary. Loading that native module remains an
   explicit user action.
3. Put the UI addon's Workshop ID in
   `cfg/multiaddonmanager/multiaddonmanager.cfg` as `mm_extra_addons`.
4. Restart at an empty-server window and verify `meta list`, clean journal,
   ball kicks, and `!menu` in plain mode first.
5. Run `css_sm2menu_mode classic`, then reload the map so all three HUD
   resources are included in the precache manifest.
6. Confirm the journal contains:

   ```text
   [SM2DIAG] classic_menu_ready layout=panorama/layout/custom_game/soccermod_classic_menu.vxml
   ```

7. Join as a normal player and as an admin. Verify root order, fixed
   `8 Back` / `9 Next` / `0 Exit`, player-specific pages, 30-second timeout,
   and that weapon/menu binds still behave normally.

If readiness is absent, leave or switch the mode to `plain`; the fallback is
automatic and no raw HUD state is touched.

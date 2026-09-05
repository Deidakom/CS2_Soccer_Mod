import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const root = new URL("../src/server-plugin/SoccerModMvp/", import.meta.url);

test("in-game cap remains available when the website has no active cap", async () => {
  const [plugin, menu] = await Promise.all([
    readFile(new URL("SoccerModMvpPlugin.cs", root), "utf8"),
    readFile(new URL("SoccerModMvpPlugin.Menu.cs", root), "utf8"),
  ]);
  assert.match(plugin, /\bCapOnLoad\(\)/);
  assert.match(menu, /if \(_menuParity\.IngameCap && !IsWebsiteCapActive\(\) && HasPublicControl\(player\)\)\s*\{\s*menu\.Add\("Cap", OpenCapMenu\)/);
});

test("private CS2 website cap bridge persists and applies validated assignments", async () => {
  const source = await readFile(new URL("SoccerModMvpPlugin.WebCap.cs", root), "utf8");

  for (const command of ["begin", "reference", "assign", "commit", "clear", "evict", "status"]) {
    assert.match(source, new RegExp(`css_sm2webcap_${command}`));
  }
  assert.match(source, /WebsiteCapTtlSeconds = 6 \* 60 \* 60/);
  assert.match(source, /WebsiteCapHalfSeconds = new\(\) \{ 450, 600, 900 \}/);
  assert.match(source, /public int HalfSeconds \{ get; set; \}/);
  assert.match(source, /TryGetWebsiteCapReference/);
  assert.match(source, /SaveJsonAtomic\(WebsiteCapFileName/);
  assert.match(source, /player\.AuthorizedSteamID\?\.SteamId64/);
  assert.match(source, /player\.SwitchTeam\(targetTeam\)/);
  assert.match(source, /player\.Respawn\(\)/);
  assert.match(source, /SpectateWebsiteCapNonParticipant/);
  assert.match(source, /player\.ChangeTeam\(CsTeam\.Spectator\)/);
  assert.match(source, /website_cap_nonparticipant_spectated/);
  assert.doesNotMatch(source, /kickid \$\{userId\}/);
  assert.match(source, /player\.Clan = tag/);
  assert.match(source, /"CCSPlayerController", "m_szClan"/);
  assert.match(source, /ClearWebsiteCapPositionTags\(\)/);
  assert.match(source, /ClearWebsiteCapState\("website_clear"\)/);
  assert.match(source, /_playerPositions\.Remove\(playerSlot\)/);
  assert.match(source, /_playerPositions\[player\.Slot\] = assignment\.Role/);
});

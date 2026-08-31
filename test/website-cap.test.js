import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const root = new URL("../src/server-plugin/SoccerModMvp/", import.meta.url);

test("in-game cap collector is disabled in favor of KICKOFF", async () => {
  const [plugin, menu, social] = await Promise.all([
    readFile(new URL("SoccerModMvpPlugin.cs", root), "utf8"),
    readFile(new URL("SoccerModMvpPlugin.Menu.cs", root), "utf8"),
    readFile(new URL("SoccerModMvpPlugin.Social.cs", root), "utf8"),
  ]);

  assert.doesNotMatch(plugin, /\bCapOnLoad\(\)/);
  assert.doesNotMatch(menu, /OpenCapMenu|menu\.Add\("Cap"/);
  assert.doesNotMatch(social, /!cap, !join, !leave/);
  assert.match(social, /kickoff\.212-87-212-58\.sslip\.io/);
});

test("private CS2 website cap bridge persists and applies validated assignments", async () => {
  const source = await readFile(new URL("SoccerModMvpPlugin.WebCap.cs", root), "utf8");

  for (const command of ["begin", "assign", "commit", "evict", "status"]) {
    assert.match(source, new RegExp(`css_sm2webcap_${command}`));
  }
  assert.match(source, /WebsiteCapTtlSeconds = 6 \* 60 \* 60/);
  assert.match(source, /SaveJsonAtomic\(WebsiteCapFileName/);
  assert.match(source, /player\.AuthorizedSteamID\?\.SteamId64/);
  assert.match(source, /player\.SwitchTeam\(targetTeam\)/);
  assert.match(source, /player\.Respawn\(\)/);
  assert.match(source, /player\.Clan = tag/);
  assert.match(source, /"CCSPlayerController", "m_szClan"/);
  assert.match(source, /ClearWebsiteCapPositionTags\(\)/);
  assert.match(source, /_playerPositions\[player\.Slot\] = assignment\.Role/);
});

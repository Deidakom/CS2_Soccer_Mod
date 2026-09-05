import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const source = readFileSync(new URL("../src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.TeamColor.cs", import.meta.url), "utf8");

test("team tint preserves first-person leg visibility without enabling through-wall glow", () => {
  assert.match(source, /var color = !_teamColorEnabled/);
  assert.match(source, /LegsVisibleAlpha = 255/);
  assert.match(source, /LegsHiddenAlpha = 254/);
  assert.match(source, /pawn\.Render = Color\.FromArgb\(renderAlpha, color\.R, color\.G, color\.B\)/);
  assert.doesNotMatch(source, /pawn\.Glow\./);
});

test("leg visibility is permissionless and cleared when a player disconnects", () => {
  const toggle = source.slice(source.indexOf("private void OnLegsToggleCommand"), source.indexOf("private void OnTeamColorToggleCommand"));
  assert.doesNotMatch(toggle, /RequirePermission/);
  assert.match(source, /_hideLegsSlots\.Remove\(slot\)/);
  assert.match(toggle, /ApplyTeamAppearance\(player, "legs_toggle_command"\)/);
});

import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("sprint retains speed, cooldown and chat preferences without an overlapping HUD bar", async () => {
  const root = new URL("../src/server-plugin/SoccerModMvp/", import.meta.url);
  const [sprint, menu] = await Promise.all([
    readFile(new URL("SoccerModMvpPlugin.Sprint.cs", root), "utf8"),
    readFile(new URL("SoccerModMvpPlugin.Menu.cs", root), "utf8"),
  ]);
  assert.match(sprint, /SprintSpeedMultiplier = 1\.25f/);
  assert.match(sprint, /SprintDurationSeconds = 3\.0f/);
  assert.match(sprint, /SprintCooldownSeconds = 7\.5f/);
  assert.match(sprint, /state\.Phase = SprintPhase\.Cooldown/);
  assert.match(sprint, /pawn\.VelocityModifier = 1\.0f/);
  assert.match(sprint, /css_sprintset/);
  assert.match(menu, /Sprint messages: \{messages\}/);
  assert.doesNotMatch(sprint, /PrintToCenter|UserMessage|ProgressBarDuration|AddCommand\("css_sprintbar"/);
});

 test("compact sprint bar uses a private entity and never writes over center menus", async () => {
  const root = new URL("../src/server-plugin/SoccerModMvp/", import.meta.url);
  const [bar, parity, match] = await Promise.all(["SprintBar", "SprintParity", "Match"].map(name =>
    readFile(new URL(`SoccerModMvpPlugin.${name}.cs`, root), "utf8")));
  assert.match(bar, /recipient.EntityHandle.Raw != bar.Controller/);
  assert.match(bar, /info.TransmitEntities.Remove\(bar.Text\)/);
  assert.doesNotMatch(bar, /TransmitEntities.Add|PrintToCenter|ProgressBarDuration/);
  assert.match(bar, /_openMenus.ContainsKey\(player.Slot\)/);
  assert.match(bar, /Listeners.OnClientDisconnect>\(RemoveSprintBar\)/);
  assert.doesNotMatch(parity, /PrintToCenter/);
  assert.doesNotMatch(match, /SprintHud\(player\)/);
});

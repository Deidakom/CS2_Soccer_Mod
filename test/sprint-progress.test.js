import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const root = new URL("../src/server-plugin/SoccerModMvp/", import.meta.url);

test("sprint shows a subtle player-toggleable cooldown progress bar", async () => {
  const [sprint, menu] = await Promise.all([
    readFile(new URL("SoccerModMvpPlugin.Sprint.cs", root), "utf8"),
    readFile(new URL("SoccerModMvpPlugin.Menu.cs", root), "utf8"),
  ]);

  assert.match(sprint, /css_sprintbar/);
  assert.match(sprint, /public bool ProgressBar \{ get; set; \} = true/);
  assert.match(sprint, /public bool ProgressBarActive/);
  assert.match(sprint, /SprintProgressBarSegments = 10/);
  assert.match(sprint, /new string\('■', filled\) \+ new string\('·'/);
  assert.match(sprint, /player\.PrintToCenter\(BuildSprintCooldownProgressBar/);
  assert.match(sprint, /_openMenus\.ContainsKey\(player\.Slot\)/);
  assert.doesNotMatch(sprint, /ProgressBarDuration|ProgressBarStartTime/);
  assert.match(sprint, /case SprintPhase\.Cooldown:[\s\S]*ClearSprintProgressBar\(player, state\)/);
  assert.match(menu, /Sprint progress bar: \{progressBar\}/);
});

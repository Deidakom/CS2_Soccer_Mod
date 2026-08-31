import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const root = new URL("../src/server-plugin/SoccerModMvp/", import.meta.url);

test("sprint reproduces SoMoE's native cooldown progress bar", async () => {
  const [sprint, menu] = await Promise.all([
    readFile(new URL("SoccerModMvpPlugin.Sprint.cs", root), "utf8"),
    readFile(new URL("SoccerModMvpPlugin.Menu.cs", root), "utf8"),
  ]);

  assert.match(sprint, /css_sprintbar/);
  assert.match(sprint, /public bool ProgressBar \{ get; set; \} = true/);
  assert.match(sprint, /pawn\.ProgressBarStartTime = \(float\)cooldownStartTime/);
  assert.match(sprint, /pawn\.ProgressBarDuration = SprintCooldownProgressBarSeconds/);
  assert.match(sprint, /public bool ProgressBarActive/);
  assert.match(sprint, /"CCSPlayerPawnBase", "m_flProgressBarStartTime"/);
  assert.match(sprint, /"CCSPlayerPawnBase", "m_iProgressBarDuration"/);
  assert.match(sprint, /case SprintPhase\.Cooldown:[\s\S]*state\.ProgressBarActive[\s\S]*ClearSprintProgressBar\(pawn\)/);
  assert.match(menu, /Sprint progress bar: \{progressBar\}/);
});

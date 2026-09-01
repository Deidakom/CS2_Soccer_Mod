import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const root = new URL("../src/server-plugin/SoccerModMvp/", import.meta.url);

test("sprint shows one player-toggleable progress bar during sprint and cooldown", async () => {
  const [sprint, menu] = await Promise.all([
    readFile(new URL("SoccerModMvpPlugin.Sprint.cs", root), "utf8"),
    readFile(new URL("SoccerModMvpPlugin.Menu.cs", root), "utf8"),
  ]);

  assert.match(sprint, /css_sprintbar/);
  assert.match(sprint, /public bool ProgressBar \{ get; set; \} = true/);
  assert.match(sprint, /public bool ProgressBarActive/);
  assert.match(sprint, /SprintProgressBarSegments = 10/);
  assert.match(sprint, /new string\('■', filled\) \+ new string\('·'/);
  assert.match(sprint, /BuildSprintProgressBarText\(SprintPhase phase, double remainingSeconds\)/);
  assert.match(sprint, /phase == SprintPhase\.Sprinting[\s\S]*SprintDurationSeconds[\s\S]*SprintCooldownSeconds/);
  assert.match(sprint, /SPRINT ACTIVE/);
  assert.match(sprint, /SPRINT REFILL/);
  assert.match(sprint, /player\.PrintToCenterAlert\(text\)/);
  assert.doesNotMatch(sprint, /player\.PrintToCenter\(/);
  assert.doesNotMatch(sprint, /ProgressBarPhase/);
  assert.match(sprint, /AddTimer\(0\.1f,[\s\S]*ClearSprintProgressBar\(player, state\)/);
  assert.match(sprint, /UserMessage\.FromPartialName\("ResetHUD"\)/);
  assert.match(sprint, /resetHud\.Send\(player\)/);
  assert.doesNotMatch(sprint, /PrintToCenterAlert\(string\.Empty\)/);
  assert.doesNotMatch(sprint, /PrintToCenterAlert\(" "\)|PrintToCenter\(" "\)/);
  assert.doesNotMatch(sprint, /PrintToCenterHtml/);
  assert.doesNotMatch(sprint, /CGameText|game_text/);
  assert.doesNotMatch(sprint, /\{seconds:F1\}s/);
  assert.match(sprint, /_openMenus\.ContainsKey\(player\.Slot\)/);
  assert.doesNotMatch(sprint, /ProgressBarDuration|ProgressBarStartTime/);
  assert.match(sprint, /StartSprint[\s\S]*state\.Phase = SprintPhase\.Sprinting[\s\S]*DrawSprintProgressBar\(player, state, now\)/);
  assert.match(sprint, /case SprintPhase\.Sprinting:[\s\S]*else[\s\S]*DrawSprintProgressBar\(player, state, now\)/);
  assert.match(sprint, /case SprintPhase\.Cooldown:[\s\S]*ClearSprintProgressBar\(player, state\)/);
  assert.match(menu, /Sprint progress bar: \{progressBar\}/);
});

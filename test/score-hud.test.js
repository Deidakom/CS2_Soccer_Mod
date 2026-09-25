import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (p) => fs.readFileSync(path.join(root, p), 'utf8').replace(/\r\n/g, '\n');
const hud = read('src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.ScoreHud.cs');
const match = read('src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.Match.cs');
const xml = read('src/workshop-addon/soccermod_menu/panorama/layout/custom_game/soccermod_scorebug.xml');
const css = read('src/workshop-addon/soccermod_menu/panorama/styles/custom_game/soccermod_scorebug.css');

test('match HUD layout follows the custom_hud_layout rules', () => {
  assert.match(xml, /<Panel id="sm_hud" class="sm-hud" hittest="false">/);
  for (const id of ['sm_team_red', 'sm_score_red', 'sm_clock', 'sm_period', 'sm_score_blue', 'sm_team_blue', 'sm_status', 'sm_banner_main', 'sm_banner_sub'])
    assert.match(xml, new RegExp(`<Label id="${id}" class="[^"]+" text="\{s:text\}" />`), id);
  assert.match(xml, /s2r:\/\/panorama\/styles\/custom_game\/soccermod_scorebug\.vcss_c/);
  assert.doesNotMatch(xml, /style="/);
  assert.doesNotMatch(css, /^[^\n{]*,[^\n{]*\{/m, 'no comma selectors');
  for (const cls of ['st-live', 'st-kickoff', 'st-goal', 'st-break', 'st-paused', 'st-final', 'bn-goal-red', 'bn-goal-blue'])
    assert.ok(css.includes(`.${cls}`), cls);
});

test('the plugin draws the HUD, sends only changes and keeps the centre text as fallback', () => {
  assert.ok(hud.includes('CaptureInput = false'));
  assert.ok(hud.includes('if (Remember(player.Slot, id, text)) _scoreHudPanel!.SetText(player, id, text);'));
  assert.ok(match.includes('if (ScoreHudPanorama) return; // the match HUD shows it (ScoreHud.cs)'));
  for (const banner of ['"MATCH START"', '"OWN GOAL" : "GOAL!"', '"HALF-TIME"', '"GOLDEN GOAL"', '"FULL TIME"'])
    assert.ok(match.includes(banner), banner);
});

test('teams are Home (T) and Away (CT), also on the Tab scoreboard', () => {
  const config = read('src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.Config.cs');
  const main = read('src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.cs');
  assert.ok(match.includes('private const string DefaultTeamNameCt = "Away";'));
  assert.ok(match.includes('private const string DefaultTeamNameT = "Home";'));
  assert.ok(match.includes('Server.ExecuteCommand($"mp_teamname_1 \\"{Clean(_teamNameCt)}\\"");'));
  assert.ok(match.includes('Server.ExecuteCommand($"mp_teamname_2 \\"{Clean(_teamNameT)}\\"");'));
  assert.ok(config.includes('stored.TeamNameCt != "Counter-Terrorists"'), 'old stock names migrate');
  assert.ok(main.includes('AddTimer(1.0f, ApplyScoreboardTeamNames);'));
});

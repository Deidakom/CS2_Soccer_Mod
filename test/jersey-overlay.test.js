import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const jerseySource = read('src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.Jersey.cs');
const rulesSource = read('src/server-plugin/SoccerModMvp/JerseyRenderRules.cs');
const configSource = read('src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.Config.cs');
const teamColorSource = read('src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.TeamColor.cs');
const mainSource = read('src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.cs');

test('jersey text is engine-parented, non-billboarding, and never teleported', () => {
  assert.match(jerseySource, /CPointWorldText NameText/);
  assert.match(jerseySource, /CPointWorldText\? NumberText/);
  assert.match(jerseySource, /POINT_WORLD_TEXT_REORIENT_NONE/);
  assert.match(jerseySource, /AcceptInput\("SetParent"/);
  assert.match(jerseySource, /AcceptInput\("SetParentAttachment"/);
  assert.ok(jerseySource.indexOf('AcceptInput("SetParent"') < jerseySource.indexOf('AcceptInput("SetParentAttachment"'));
  assert.match(jerseySource, /PParent/);
  assert.match(jerseySource, /HierarchyAttachName/);
  assert.match(jerseySource, /text\.Enabled = false/);
  assert.match(jerseySource, /text\.Enabled = true/);
  assert.doesNotMatch(jerseySource, /\.Teleport\s*\(/);
  assert.doesNotMatch(jerseySource, /RegisterListener<Listeners\.CheckTransmit>/);
  assert.doesNotMatch(jerseySource, /ReorientMode\s*=\s*\(.*?\)\s*1/);
});

test('outfield and goalkeeper layouts are separate child plans', () => {
  assert.match(rulesSource, /public int ChildCount => NumberText is null \? 1 : 2/);
  assert.match(jerseySource, /var number = goalkeeper \? 1 : EnsureJerseyNumber/);
  assert.match(jerseySource, /if \(plan\.NumberText is not null\)/);
  assert.match(jerseySource, /css_sm2jerseyprototype/);
  assert.match(jerseySource, /"88"/);
  assert.match(jerseySource, /requested is < 2 or > 99/);
  assert.match(jerseySource, /plan\.Number\?\.ToString\(CultureInfo\.InvariantCulture\)/);
});

test('settings and model callbacks gate the renderer on the actual kit', () => {
  assert.match(configSource, /bool\? DynamicJerseysEnabled/);
  assert.match(configSource, /DynamicJerseysEnabled = _dynamicJerseysEnabled/);
  assert.match(configSource, /if \(stored\.DynamicJerseysEnabled is/);
  assert.match(configSource, /MatchSettingsOnLoad\(\);/);
  assert.match(configSource, /JerseyRefreshAll\("settings_reload"\)/);
  assert.match(jerseySource, /requestedEnabled=/);
  assert.match(jerseySource, /uncalibrated_model=/);
  assert.match(teamColorSource, /JerseyOnTeamAppearanceApplied\(player, pawn, appliedModel\)/);
  assert.match(jerseySource, /GetAppliedJerseyModel\(pawn\)/);
});

test('the renderer reconciles lifecycle boundaries at low frequency', () => {
  assert.match(jerseySource, /DynamicJerseyReconcileEveryTicks = 16/);
  assert.match(jerseySource, /JerseyOnMapEnd/);
  assert.match(jerseySource, /JerseyOnPlayerDisconnect/);
  assert.match(jerseySource, /JerseyOnPlayerSpawn/);
  assert.match(jerseySource, /JerseyOnPlayerDeath/);
  assert.match(jerseySource, /RemoveDynamicJersey/);
  assert.match(mainSource, /JerseyOnLoad\(\)/);
  assert.match(mainSource, /JerseyOnMapEnd\(\)/);
  assert.match(mainSource, /JerseyOnMapStart\(mapName\)/);
  assert.match(mainSource, /JerseyOnPlayerSpawn\(player\)/);
  assert.match(mainSource, /JerseyOnPlayerDeath\(@event\.Userid\)/);
  assert.match(mainSource, /JerseyOnTick\(\)/);
});

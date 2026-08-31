import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const moduleSource = fs.readFileSync(
  path.join(root, 'src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.TeamColor.cs'),
  'utf8',
);
const mainSource = fs.readFileSync(
  path.join(root, 'src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.cs'),
  'utf8',
);
const configSource = fs.readFileSync(
  path.join(root, 'src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.Config.cs'),
  'utf8',
);

test('team appearance applies uniform stock models before a replicated team tint', () => {
  assert.match(moduleSource, /tm_phoenix\/tm_phoenix\.vmdl/);
  assert.match(moduleSource, /ctm_sas\/ctm_sas\.vmdl/);
  assert.match(moduleSource, /pawn\.SetModel/);
  assert.match(moduleSource, /pawn\.Render = _teamColorEnabled/);
  assert.match(moduleSource, /SetStateChanged\(pawn, "CBaseModelEntity", "m_clrRender"\)/);
  assert.ok(moduleSource.indexOf('pawn.SetModel') < moduleSource.indexOf('pawn.Render = _teamColorEnabled'));
});

test('team appearance is reasserted at load, round start, and player spawn', () => {
  assert.match(mainSource, /TeamColorOnLoad\(\)/);
  assert.match(mainSource, /TeamColorOnRoundStart\(\)/);
  assert.match(mainSource, /TeamColorOnPlayerSpawn\(player\)/);
  assert.match(mainSource, /ApplyAllTeamAppearances\("round_start_plus_0_25s"\)/);
  assert.match(mainSource, /ApplyTeamAppearance\(player, "spawn_plus_0_25s"\)/);
});

test('team appearance commands and settings are independently persistent', () => {
  assert.match(moduleSource, /css_sm2teamcolor/);
  assert.match(moduleSource, /css_sm2teammodel/);
  assert.match(moduleSource, /TryParseTeamAppearanceToggle/);
  assert.match(configSource, /TeamColorEnabled/);
  assert.match(configSource, /bool\? TeamColorEnabled/);
  assert.match(configSource, /bool\? TeamModelEnabled/);
  assert.doesNotMatch(configSource, /TeamModelT/);
  assert.doesNotMatch(configSource, /TeamModelCt/);
});

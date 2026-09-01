import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const source = fs.readFileSync(
  path.join(root, 'src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.TeamColor.cs'),
  'utf8',
);

test('team glow shares the exact resolved tint and its existing toggle', () => {
  assert.match(source, /var color = !_teamColorEnabled/);
  assert.match(source, /pawn\.Render = color/);
  assert.match(source, /pawn\.Glow\.GlowColorOverride = color/);
  assert.match(source, /pawn\.Glow\.Glowing = _teamColorEnabled/);
  assert.doesNotMatch(source, /AddCommand\([^)]*glow/i);
});

test('team glow uses the documented look-at state and no team filter', () => {
  assert.match(source, /const int GlowTypeLookAt = 2/);
  assert.match(source, /const int GlowTeamAll = -1/);
  assert.match(source, /pawn\.Glow\.GlowType = GlowTypeLookAt/);
  assert.match(source, /pawn\.Glow\.GlowTeam = GlowTeamAll/);
  assert.match(source, /pawn\.Glow\.GlowRange = TeamGlowRange/);
  assert.match(source, /pawn\.Glow\.GlowRangeMin = 0/);
});

test('team glow replication is reasserted with the appearance lifecycle', () => {
  assert.match(source, /SetStateChanged\(pawn, "CBaseModelEntity", "m_Glow"\)/);
  assert.match(source, /TeamColorOnRoundStart/);
  assert.match(source, /TeamColorOnPlayerSpawn/);
});

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = name => readFileSync(new URL(`../src/server-plugin/SoccerModMvp/${name}`, import.meta.url), 'utf8');
const main = read('SoccerModMvpPlugin.cs');

test('our own stadium counts as the foundation map everywhere', () => {
  assert.match(main, /OwnStadiumMapName = "soccer_soccermod_stadium"/);
  for (const name of ['SoccerModMvpPlugin.cs', 'SoccerModMvpPlugin.BallHandling.cs', 'SoccerModMvpPlugin.Training.cs'])
    assert.doesNotMatch(read(name), /string\.Equals\(_currentMapName, FoundationMapName/, name);
  assert.ok(main.includes('IsFoundationMap(_currentMapName)'));
});

test('minimap and loading image follow the running map', () => {
  assert.match(main, /soccer_soccermod_stadium_radar_psd\.vtex/);
  assert.match(main, /1080p\/soccer_soccermod_stadium_png\.vtex/);
  assert.match(main, /string\.Equals\(Server\.MapName, OwnStadiumMapName/);
});

test('map reload uses the running map\'s Workshop ID, old stadium as fallback', () => {
  const match = read('SoccerModMvpPlugin.Match.cs');
  assert.match(match, /MapWorkshopId\(Server\.MapName\) \?\? LegacyStadiumWorkshopId/);
  assert.match(match, /LegacyStadiumWorkshopId = "3361075564"/);
  assert.doesNotMatch(match, /host_workshop_map 3361075564"/);
});

test('jersey/menu detection also reads the running map\'s own VPK', () => {
  const team = read('SoccerModMvpPlugin.TeamColor.cs');
  assert.ok(team.includes('if (MapWorkshopId(Server.MapName) is { } mapId) ids.Add(mapId);'));
  assert.ok(team.includes('entries.Contains($"maps/{mapName}.vpk"'));
});

test('a goal outside a match resets the round for everyone', () => {
  const match = read('SoccerModMvpPlugin.Match.cs');
  const warmup = match.split('private void HandleWarmupGoal')[1].split('private void RestoreGoalRespawnCvars')[0];
  assert.ok(warmup.includes('Server.ExecuteCommand("mp_restartgame 1");'));
  assert.ok(!warmup.includes('player.Respawn()'), 'no longer respawns only the conceding team');
});

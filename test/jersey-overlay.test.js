import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const jerseySource = fs.readFileSync(
  path.join(root, 'src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.Jersey.cs'),
  'utf8',
);
const mainSource = fs.readFileSync(
  path.join(root, 'src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.cs'),
  'utf8',
);

test('dynamic jersey overlay is a public Source 2 world-text entity, not the crashed transmit path', () => {
  assert.match(jerseySource, /CPointWorldText/);
  assert.match(jerseySource, /point_worldtext/);
  assert.match(jerseySource, /MessageText/);
  assert.match(jerseySource, /ReorientMode/);
  assert.doesNotMatch(jerseySource, /RegisterListener<Listeners\.CheckTransmit>/);
});

test('dynamic jerseys track live names, fixed goalkeeper #1, and unique outfield numbers', () => {
  assert.match(jerseySource, /NormalizeJerseyName/);
  assert.match(jerseySource, /IsGkSlot/);
  assert.match(jerseySource, /var number = goalkeeper \? 1 : EnsureJerseyNumber/);
  assert.match(jerseySource, /css_sm2jerseynumber/);
  assert.match(jerseySource, /requested is < 2 or > 99/);
  assert.match(jerseySource, /number\.ToString\(CultureInfo\.InvariantCulture\)/);
});

test('the overlay is cleaned and refreshed at plugin, map, spawn, death, and tick boundaries', () => {
  assert.match(jerseySource, /JerseyOnMapEnd/);
  assert.match(jerseySource, /JerseyOnPlayerDisconnect/);
  assert.match(jerseySource, /JerseyOnPlayerSpawn/);
  assert.match(jerseySource, /JerseyOnPlayerDeath/);
  assert.match(mainSource, /JerseyOnLoad\(\)/);
  assert.match(mainSource, /JerseyOnMapEnd\(\)/);
  assert.match(mainSource, /JerseyOnMapStart\(mapName\)/);
  assert.match(mainSource, /JerseyOnPlayerSpawn\(player\)/);
  assert.match(mainSource, /JerseyOnPlayerDeath\(@event\.Userid\)/);
  assert.match(mainSource, /JerseyOnTick\(\)/);
});

import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');

test('enemy name tags face the viewer and follow without parenting', () => {
  const tags = read('SoccerModMvpPlugin.NameTags.cs');
  assert.match(tags, /POINT_WORLD_TEXT_REORIENT_AROUND_UP/);
  assert.match(tags, /tag\.Text\.Teleport\(new Vector\(origin\.X, origin\.Y, origin\.Z \+ NameTagHeight\), _nameTagAngles\)/);
  assert.match(tags, /_nameTagAngles = new\(0\.0f, 270\.0f, 90\.0f\)/);
  assert.doesNotMatch(tags, /SetParent/);
});

test('each client only receives enemy tags, and only Remove is ever used on the transmit list', () => {
  const tags = read('SoccerModMvpPlugin.NameTags.cs');
  assert.match(tags, /slot == receiver\.Slot\s*\|\| \(receiverTeam is CsTeam\.Terrorist or CsTeam\.CounterTerrorist && tag\.Team == receiverTeam\)/);
  assert.match(tags, /info\.TransmitEntities\.Remove\(tag\.Text\)/);
  assert.doesNotMatch(tags, /TransmitEntities\.Add/);
});

test('name tags are wired into load, tick and unload and can be switched off', () => {
  const main = read('SoccerModMvpPlugin.cs');
  assert.match(main, /NameTagsOnLoad\(\);/);
  assert.match(main, /NameTagsOnTick\(\);/);
  assert.match(main, /NameTagsOnUnload\(\);/);
  assert.match(read('SoccerModMvpPlugin.MenuParity.cs'), /public bool EnemyNameTags \{ get; set; \} = true;/);
});

import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');

test('personal "Flashlight on F": inspect toggles the Flashlight plugin, saved per player, default off', () => {
  const key = read('SoccerModMvpPlugin.FlashlightKey.cs');
  assert.ok(key.includes('(player.Buttons & PlayerButtons.Inspect) != 0'));
  assert.ok(key.includes('player.ExecuteClientCommandFromServer("css_fl_toggle")'));
  assert.ok(key.includes('BlockInspectUntilNextGraphUpdate = true'));
  assert.ok(key.includes('_inspectHeld.Add(player.Slot)'), 'toggles once per press, not every tick');
  assert.match(read('SoccerModMvpPlugin.MenuParity.cs'), /public Dictionary<ulong, bool> FlashlightOnInspect \{ get; set; \} = new\(\);/);
  assert.ok(read('SoccerModMvpPlugin.Menu.cs').includes('menu.Add($"Flashlight on F: {(FlashlightOnInspect(player) ? "On" : "Off")}"'));
  const main = read('SoccerModMvpPlugin.cs');
  assert.ok(main.includes('FlashlightKeyOnTick();') && main.includes('FlashlightKeyOnDisconnect'));
});

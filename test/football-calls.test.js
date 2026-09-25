import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');

test('football calls: the owner\'s list, team chat plus a teammates-only marker', () => {
  const calls = read('SoccerModMvpPlugin.Calls.cs');
  for (const call of ['Pass hier!', 'Flanke!', 'Schieß!', 'Ich bin frei!', 'Doppelpass!', 'Hinten sichern!',
    'Zurück / Rückpass!', 'Klär ihn!', 'Gut gemacht!', 'Schöner Pass!', 'Sorry!'])
    assert.ok(calls.includes(`"${call}"`), call);
  assert.ok(!calls.includes('"Mann!"'), 'owner replaced Mann! with Gut gemacht!');
  assert.ok(calls.includes('AddCommand("css_calls"'));
  assert.ok(calls.includes('p.Team == player.Team'), 'team chat only');
  assert.ok(calls.includes('info.TransmitEntities.Remove(marker.Text)'));
  assert.ok(!calls.includes('TransmitEntities.Add'), 'Add crashes the server');
  assert.ok(read('SoccerModMvpPlugin.Links.cs').includes('bind v css_calls'), 'V bind is in !binds');
  assert.ok(read('SoccerModMvpPlugin.Menu.cs').includes('menu.Add("Rufe", OpenCallsMenu);'));
  const main = read('SoccerModMvpPlugin.cs');
  assert.ok(main.includes('CallsOnLoad();') && main.includes('CallsOnTick();'));
});

test('B opens nothing unless an admin turns it back on', () => {
  assert.match(read('SoccerModMvpPlugin.MenuParity.cs'), /public bool BuyKeyMenu \{ get; set; \}\n/);
  const click = read('SoccerModMvpPlugin.ClickMenu.cs');
  assert.match(click, /if \(!_menuParity\.BuyKeyMenu\)\s*\{\s*if \(hotReload\) Server\.ExecuteCommand\("mp_buytime 0"\);\s*return;\s*\}/);
  assert.ok(click.includes('AddCommand("css_sm2menu_buykey"'));
});

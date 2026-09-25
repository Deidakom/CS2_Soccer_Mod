import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');

test('health, weapon selection and money are hidden by default (owner, 2026-09-25)', () => {
  const hud = read('SoccerModMvpPlugin.HudHide.cs');
  assert.ok(hud.includes('private const uint SoccerHiddenHud = (uint)(HideHud.Weapons | HideHud.Health);'));
  assert.ok(hud.includes('Server.ExecuteCommand("mp_maxmoney 0")'));
  assert.ok(hud.includes('if ((pawn.HideHUD & SoccerHiddenHud) == SoccerHiddenHud) continue;'), 'writes only when missing');
  assert.ok(hud.includes('AddCommand("css_sm2hud_hide"'));
  assert.ok(read('SoccerModMvpPlugin.MenuParity.cs').includes('public bool HideCombatHud { get; set; } = true;'));
  const main = read('SoccerModMvpPlugin.cs');
  assert.ok(main.includes('HudHideOnLoad();') && main.includes('HudHideOnTick();'));
});

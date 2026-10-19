import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');
const skins = read('SoccerModMvpPlugin.BallSkins.cs');

test('ball skins: map ball by default, own choice only, Remove-only transmit', () => {
  assert.ok(skins.includes('s > 0 && s < BallSkinNames.Length && BallSkinAvailableFor(player) ? s : 0'));
  assert.ok(skins.includes('info.TransmitEntities.Remove(shell)'));
  assert.ok(!skins.includes('TransmitEntities.Add'));
});

test('ball skins: shells are parented, non-solid, gated on the model and loose files go to the owner only', () => {
  assert.ok(skins.includes('prop.AcceptInput("SetParent", ball, prop, "!activator")'));
  assert.ok(skins.includes('keyValues.SetInt("solid", 0)'));
  assert.ok(skins.includes('MountedAddonFiles().Contains(BallSkinModel + "_c")'));
  assert.ok(skins.includes('(!_ballSkinLooseOnly || IsOwner(player))'));
});

test('ball skins: Settings - Stadium entry and the welcome list', () => {
  assert.ok(read('SoccerModMvpPlugin.Menu.cs').includes('menu.Add($"Ball: {BallSkinNames[BallSkinOf(player)]}", OpenBallSkinMenu)'));
  assert.ok(read('SoccerModMvpPlugin.Welcome.cs').includes('ball, sounds.'));
  assert.ok(read('SoccerModMvpPlugin.cs').includes('BallSkinsOnLoad();'));
});

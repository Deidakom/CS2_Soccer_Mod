import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');

test('knife duels: first kick wins inside the window, a cleaner same-tick kick replaces it', () => {
  const duel = read('SoccerModMvpPlugin.KickDuel.cs');
  assert.match(duel, /DefaultKickDuelWindowSeconds = 0\.10f/);
  assert.match(duel, /state\.LastKickerSlot != player\.Slot/);
  assert.match(duel, /if \(!sameTick \|\| quality <= state\.LastKickQuality \|\| state\.PreKickVelocity is not \{ \} before\)/);
  assert.match(duel, /ball\.Teleport\(velocity: before\);\s*inherited = before;/);
  const main = read('SoccerModMvpPlugin.cs');
  assert.match(main, /if \(!ResolveKickDuel\(player, ball, aimDot, distance, ref duelInherited\)\)\s*\{\s*LogKickRejected\(player, "duel_lost", distance, aimDot\);\s*return;\s*\}\s*target = target with \{ Inherited = duelInherited \};\s*(\/\/.*\s*)*ApplyPreKickPlayerImpact\(player, target\);/);
});

test('duel window and ground bounce are reachable from the Ball menu and persisted', () => {
  const bench = read('SoccerModMvpPlugin.BallWorkbench.cs');
  assert.match(bench, /new\("kickDuelWindowSeconds", "Kick power"/);
  assert.match(bench, /menu\.Add\(\$"Ground bounce: /);
  const config = read('SoccerModMvpPlugin.Config.cs');
  assert.match(config, /KickDuelWindowSeconds = _kickDuelWindowSeconds,/);
});

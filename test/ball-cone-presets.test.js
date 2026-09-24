import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const source = fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.BallWorkbench.cs'), 'utf8');

test('the Ball menu offers the CS:S, middle and previous kick cone', () => {
  assert.match(source, /menu\.Add\(\$"Kick cone: \{ActiveKickConePreset\(\)\}", OpenKickConeMenu\);/);
  assert.match(source, /\("CS:S", 64f, 32f\)/);
  assert.match(source, /\("Middle", 70f, 50f\)/);
  assert.match(source, /\("Current", 81\.5f, 70f\)/);
});

test('a cone preset is root-gated and saved through the workbench (validated, undoable)', () => {
  const menu = source.slice(source.indexOf('private void OpenKickConeMenu'), source.indexOf('private void OpenBallDialGroup'));
  assert.match(menu, /if \(!BallWorkbenchAccess\(player\)\) return;/);
  assert.match(menu, /if \(!BallWorkbenchAccess\(p\)\) return;/);
  assert.match(menu, /tuning\.Values\["kickSurfaceReach"\] = reach;/);
  assert.match(menu, /tuning\.Values\["kickAimConeDegrees"\] = cone;/);
  assert.match(menu, /ApplyBallTuning\(tuning\)/);
});

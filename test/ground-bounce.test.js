import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');

test('ground bounce runs every tick in every handling profile, only on the pitch', () => {
  const main = read('SoccerModMvpPlugin.cs');
  assert.match(main, /TryApplyWallAssist\(_derivedBallVelocity, now\);\s*\}\s*TryApplyGroundBounce\(origin, _derivedBallVelocity, now\);/);
  const bounce = read('SoccerModMvpPlugin.GroundBounce.cs');
  assert.match(bounce, /DefaultGroundBounceRestitution = 0\.55f/);
  assert.match(bounce, /origin\.Z > StadiumPitchPlaneZ \+ BallCollisionRadius \+ GroundBounceGroundTolerance/);
  assert.match(bounce, /KnifeKickOwnsTick\(ball\)/);
  assert.match(bounce, /ball\.Teleport\(velocity: new Vector\(current\.X, current\.Y, rebound\)\)/);
});

test('ground bounce is a persisted Ball menu dial', () => {
  assert.match(read('SoccerModMvpPlugin.BallWorkbench.cs'), /new\("groundBounceRestitution", "Engine physics"/);
  const config = read('SoccerModMvpPlugin.Config.cs');
  assert.match(config, /public float\? GroundBounceRestitution \{ get; set; \}/);
  assert.match(config, /GroundBounceRestitution = _groundBounceRestitution,/);
});

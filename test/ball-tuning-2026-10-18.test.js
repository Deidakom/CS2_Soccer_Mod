import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');
const main = read('SoccerModMvpPlugin.cs');

test('per-tick ball velocity writes keep the sampled velocity in step', () => {
  for (const file of ['SoccerModMvpPlugin.Aero.cs', 'SoccerModMvpPlugin.Rolling.cs', 'SoccerModMvpPlugin.ContactImpact.cs',
    'SoccerModMvpPlugin.BodyImpact.cs', 'SoccerModMvpPlugin.NetPocket.cs', 'SoccerModMvpPlugin.DynamicNet.cs'])
    assert.ok(read(file).includes('SetBallVelocity(ball,'), file);
  assert.ok(main.includes('SetBallVelocity(ball, C(BallContactMath.CombinePushes(N(inherited), simultaneous)));'));
  assert.match(read('SoccerModMvpPlugin.GroundBounce.cs'), /ball\.Teleport\(velocity: velocity\);\s*SyncSampledBallVelocity\(ball, velocity\);/);
});

test('the CS:S knife area has no soft-tip cushion', () => {
  assert.ok(main.includes('var earlyContactAllowed = !KickCssArea && BallContactMath.IsIncomingContact('));
});

test('new feel dials default off, are stored, legacy-filled and switchable from the Ball menu', () => {
  const legacy = read('BallContactMath.cs');
  const config = read('SoccerModMvpPlugin.Config.cs');
  const bench = read('SoccerModMvpPlugin.BallWorkbench.cs');
  for (const [key, field, prop] of [['ballPushPlayerSlowdown', '_ballPushPlayerSlowdown', 'BallPushPlayerSlowdown'],
    ['kickGroundLiftKeep', '_kickGroundLiftKeep', 'KickGroundLiftKeep']]) {
    assert.ok(main.includes(`private float ${field};`), field);
    assert.ok(legacy.includes(`["${key}"] = 0f,`), key);
    assert.ok(config.includes(`public float? ${prop} { get; set; }`) && config.includes(`${prop} = ${field},`), prop);
    assert.ok(bench.includes(`new("${key}",`), key);
  }
  assert.ok(bench.includes('CssPushbackTransferRatio = 0.72f') && bench.includes('CssPushbackPlayerSlowdown = 0.8f'));
});

test('pushback slows the player once at push start; ground kicks keep their lift', () => {
  const start = main.indexOf('if (pushing.Add(player.Slot))');
  assert.ok(main.indexOf('_ballPushPlayerSlowdown > 0f', start) - start < 400);
  assert.ok(main.includes('elevation = MathF.Max(elevation, liftDegrees * _kickGroundLiftKeep * (MathF.PI / 180.0f));'));
});

test('compiled defaults match the owner server (2026-09-30 revert)', () => {
  assert.ok(main.includes('DefaultKickCssLaunchAngles = 0f;'));
  assert.ok(read('SoccerModMvpPlugin.Rolling.cs').includes('DefaultRollDecayPerSecond = 0f;'));
  assert.ok(read('SoccerModMvpPlugin.GroundBounce.cs').includes('DefaultGroundBounceRestitution = 0.45f;'));
  assert.ok(read('SoccerModMvpPlugin.KickDuel.cs').includes('DefaultKickDuelWindowSeconds = 0.1f;'));
});

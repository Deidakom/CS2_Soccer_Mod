import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = name => readFileSync(new URL(`../src/server-plugin/SoccerModMvp/${name}`, import.meta.url), 'utf8');
const main = read('SoccerModMvpPlugin.cs');
const rolling = read('SoccerModMvpPlugin.Rolling.cs');
test('ordinary reach power is preserved while wall-pop eligibility returns to the previous version', () => {
  assert.ok(main.includes('var deltaSpeed = _kickDeltaVelocity * ComputeGameplayMassResponse()\n            * reachPower')
    || main.replaceAll('\r\n', '\n').includes('var deltaSpeed = _kickDeltaVelocity * ComputeGameplayMassResponse()\n            * reachPower'));
  assert.ok(main.includes('!ImprovedHandling && powerScale >= 1.0f && TryApplyWallPopKick'));
  assert.ok(!main.includes('_kickSurfaceReach * 0.35f && TryApplyWallPopKick'));
  assert.ok(main.includes('BallContactMath.HorizontalKickAim'));
});
test('rollout respects contact, pause, floor, nearby players and walls', () => {
  for (const gate of ['_pausedBallHandle != 0', '_ballMotionFrozen',
    'state.LastContactTick < 4', 'speed <= 4 && !hasPrevious', 'speed > 220', 'MathF.Abs(velocity.Z) > 12',
    '!IsBallGrounded', '< BallPushContactDistance + 12', 'new[] { -1f, 1f }', 'trace.DidHit()', '_rollingSamples.Remove(key)'])
    assert.ok(rolling.includes(gate), gate);
  assert.ok(main.includes('_rollingSamples.Clear();'));
});
test('legacy spin targets local rotation and subtracts measured spin instead of stacking impulses', () => {
  const spin = main.split('private void ApplyBallTopspin')[1].split('private void OnBallSpinFactorCommand')[0];
  assert.ok(spin.includes('BallContactMath.RollingLocalSpin'));
  assert.ok(spin.includes('desired - state.MeasuredSpin'));
  assert.ok(spin.includes('!state.SpinMeasured'));
});

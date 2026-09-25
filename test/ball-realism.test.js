import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');

test('the landing limiter leaves the ground-bounce assist its rebound', () => {
  const landing = read('SoccerModMvpPlugin.Landing.cs');
  assert.ok(landing.includes('!(Server.TickedTime - State(ball).LastGroundBounceTime <= GroundBounceCooldownSeconds)'));
  assert.ok(landing.includes('BallContactMath.GroundBounceRestitutionAt(_groundBounceRestitution, -previous.Velocity.Z) : 0.55f'));
  assert.ok(landing.includes('BallContactMath.LandingVertical(previous.Velocity.Z, velocity.Z, ratio)'));
});

test('rolling resistance brakes clean slow rolls; 0 keeps the glide', () => {
  const rolling = read('SoccerModMvpPlugin.Rolling.cs');
  assert.match(rolling, /private float _rollResistance;/);
  assert.ok(rolling.includes('var decel = _rollResistance > 0 ? _rollResistance : BallContactMath.RollAssistDecel;'));
  assert.ok(rolling.includes('BallContactMath.BrakedRollSpeed(previousSpeed, speed, _rollResistance, dt)'));
});

test('curve in flight runs after the spin sample and skips contacts, walls and the ground', () => {
  const main = read('SoccerModMvpPlugin.cs');
  assert.match(main, /UpdateSharedBallHandling\(\);\s*UpdateBallAerodynamics\(\);/);
  const aero = read('SoccerModMvpPlugin.Aero.cs');
  assert.match(aero, /private float _magnusStrength; \/\/ 0 = off/);
  for (const guard of ['state.SpinMeasured', 'state.LastContactTick < 4', 'KnifeKickOwnsTick(ball)',
    'state.LastWall <= WallAssistCooldownSeconds', 'IsBallGrounded(ball, target.Origin)'])
    assert.ok(aero.includes(guard), guard);
  assert.ok(aero.includes('BallContactMath.MagnusCurveStep(velocity, spinZ, BallCollisionRadius, _magnusStrength, Server.TickInterval)'));
});

test('both are persisted Ball menu dials with top-level entries', () => {
  const bench = read('SoccerModMvpPlugin.BallWorkbench.cs');
  assert.match(bench, /new\("rollResistance", "Walls and settling"/);
  assert.match(bench, /new\("magnusStrength", "Engine physics"/);
  assert.ok(bench.includes('d.Key == "rollResistance"') && bench.includes('d.Key == "magnusStrength"'));
  const config = read('SoccerModMvpPlugin.Config.cs');
  for (const name of ['RollResistance', 'MagnusStrength']) {
    assert.ok(config.includes(`public float? ${name} { get; set; }`), name);
    assert.ok(config.includes(`${name} = _${name[0].toLowerCase()}${name.slice(1)},`), name);
  }
});

test('a ball pushes the player along the contact normal, not its travel line (CS:S parity)', () => {
  const impact = read('SoccerModMvpPlugin.ContactImpact.cs');
  assert.ok(impact.includes('BallContactMath.ImpactPushAlongNormal(incoming, impact.Normal)'));
  assert.ok(impact.includes('Math.Min(pushAlong * _ballImpactPlayerPushRatio, _ballImpactPlayerPushMax)'));
});

test('the frozen kickoff ball is released just before contact, not at it (no half-second stall)', () => {
  const main = read('SoccerModMvpPlugin.cs');
  assert.ok(main.includes('private const float FrozenBallApproachMargin = 24.0f;'));
  assert.ok(main.includes('planarDistance <= BallPushContactDistance + FrozenBallApproachMargin'));
  assert.ok(main.includes('UnfreezeBallForPlay("body_approach");'));
  const release = main.indexOf('UnfreezeBallForPlay("body_approach");');
  assert.ok(release > 0 && release < main.indexOf('if (planarDistance > BallPushContactDistance || planarDistance < 0.001f)'), 'before the contact gate');
});

test('a right-click kick waits for its stab animation (own cooldown, default 1.0 s)', () => {
  const bench = read('SoccerModMvpPlugin.BallWorkbench.cs');
  assert.ok(bench.includes('private float _kickSecondaryCooldownSeconds = 1.0f;'));
  assert.ok(bench.includes('inputMode == "secondary" ? MathF.Max(_kickCooldownSeconds, _kickSecondaryCooldownSeconds) : _kickCooldownSeconds'));
  assert.match(bench, /new\("kickSecondaryCooldownSeconds", "Kick power"/);
  const main = read('SoccerModMvpPlugin.cs');
  assert.ok(main.includes('_lastKickCooldownBySlot[player.Slot] = KickCooldownFor(kickInputMode);'));
  assert.ok(main.includes('_lastKickCooldownBySlot.TryGetValue(player.Slot, out var lastCooldown) ? lastCooldown : _kickCooldownSeconds'));
  assert.ok(read('SoccerModMvpPlugin.KnifeContact.cs').includes('KnifeSwingRules.NextHeldSwing(Server.TickedTime, KickCooldownFor(mode))'), 'held right click too');
  const config = read('SoccerModMvpPlugin.Config.cs');
  assert.ok(config.includes('KickSecondaryCooldownSeconds = _kickSecondaryCooldownSeconds,'));
});

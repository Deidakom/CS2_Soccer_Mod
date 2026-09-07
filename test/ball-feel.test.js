import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = name => readFileSync(new URL(`../src/server-plugin/SoccerModMvp/${name}`, import.meta.url), 'utf8');
const main = read('SoccerModMvpPlugin.cs');
const swing = read('SoccerModMvpPlugin.KnifeContact.cs');
const impact = read('SoccerModMvpPlugin.BodyImpact.cs');
const pulse = read('SoccerModMvpPlugin.ContactImpact.cs');

test('a swing checks immediately, preserves aim and can accept only one contact', () => {
  assert.equal(main.match(/BeginKnifeSwing\(player,/g)?.length, 2);
  assert.equal(main.match(/CompleteKnifeSwing\(player,/g)?.length, 2);
  assert.ok(swing.includes('TryApplyPrimaryKnifeKick(player, pawn, weapon, power, mode)'));
  for (const guard of ['WithinWindow', 'AimUnchanged', 'pawn.EntityHandle.Raw != swing.Pawn',
    'weapon.EntityHandle.Raw != swing.Weapon', '_knifeSwings.Remove(player.Slot, out var swing)']) assert.ok(swing.includes(guard), guard);
  assert.ok(main.includes('var eyeAngles = swingAim ?? pawn.EyeAngles'));
  assert.ok(main.includes('reason is not ("out_of_reach" or "outside_aim_cone")'));
});

test('validated knife contact keeps a genuine player push but owns ball direction', () => {
  const kick = main.split('private void TryApplyPrimaryKnifeKick')[1].split('private void PlayKickSound')[0];
  assert.ok(kick.indexOf('ApplyPreKickPlayerImpact(player, target)') > kick.indexOf('line_of_sight_blocked'));
  assert.ok(kick.indexOf('CushionEarlyKick') > kick.indexOf('AirborneKickVelocity'));
  assert.ok(!kick.includes('_pawnImpacts.Remove'));
  assert.ok(impact.includes('int? knifePlayerSlot = null'));
  assert.ok(impact.includes('if (knifePlayerSlot is null) NewBallContact(ball)'));
  assert.ok(impact.includes('SweepPlayerContact(pawn, segmentStart, origin)'));
});

test('push uses a bounded decaying target and cannot transfer to another pawn or team', () => {
  for (const guard of ['pawn.EntityHandle.Raw != key', 'pawn.TeamNum != initialTeam',
    'active != sequence', 'ImpactPulseTarget', 'frames - 1', 'impact_motion', 'displacementAlong']) assert.ok(pulse.includes(guard), guard);
  assert.ok(impact.includes('ImpactTargetAlong'));
  assert.ok(!impact.includes('FromPartialName("Shake")'));
});

test('the same incoming-body gate controls both distance penalty and tip cushioning', () => {
  assert.ok(main.includes('IsIncomingContact(N(target.Inherited), N(pawn.AbsVelocity), N(bodyPoint) - N(ballOrigin), ballGrounded)'));
  assert.ok(main.indexOf('var ballGrounded = IsBallGrounded(ball, ballOrigin)') < main.indexOf('var earlyContactAllowed'));
  assert.match(main, /ReachPower\([^;]+earlyContactAllowed\)/);
  assert.match(main, /CushionEarlyKick\([^;]+earlyContactAllowed\)/);
  assert.ok(main.includes('CompleteKnifeSwing(player, distance, earlyContactAllowed)'));
  const math = read('BallContactMath.cs');
  assert.ok(math.includes('if (!approaching) return 1;'));
  assert.ok(math.includes('if (!approaching) return fullKick;'));
});

test('landing limits preserve shots and require a nearby static flat floor', () => {
  const landing = read('SoccerModMvpPlugin.Landing.cs');
  for (const guard of ['previous.Tick == 1', 'LastKickTick > 2', 'previous.Velocity.Z < -80',
    'velocity.Z > 0', 'trace.Normal.Z >= .95f', 'IsStaticWallSurface(trace)', 'LandingVertical']) assert.ok(landing.includes(guard), guard);
  assert.ok(main.indexOf('UpdateLandingLimits();') < main.indexOf('UpdateKnifeSwings();'));
});

test('both wall profiles restore additive lift without the September 7 wall-hop limiters', () => {
  for (const source of [main, read('SoccerModMvpPlugin.BallHandling.cs')]) {
    assert.ok(source.includes('BallContactMath.AdditiveWallLift(speedLost, _wallAssistConversionRatio, _wallAssistMaxAddedVertical)'));
    assert.ok(source.includes('current.Z + addedVertical)'));
    assert.ok(!source.includes('BallContactMath.WallReboundVertical('));
    assert.ok(!source.includes('BallContactMath.WallLift('));
  }
});

test('legacy wall rollback restores XYZ separation and wall spin without overwriting fresh contacts', () => {
  const separation = main.split('private void ScheduleWallAssistSeparation(')[1].split('private float ComputeGameplayMassResponse')[0];
  for (const guard of ['_wallAssistGeneration != generation', '_ball.EntityHandle.Raw != key', '_pausedBallHandle != 0', '_ballMotionFrozen'])
    assert.ok(separation.includes(guard), guard);
  assert.ok(separation.includes('new Vector(reboundVelocity.X, reboundVelocity.Y, reboundVelocity.Z)'));
  assert.ok(main.includes('ApplyRestoredWallTopspin(_ball, boosted, _ballSpinFactor)'));
  assert.ok(main.includes('State(_ball).LastWall = now'));
  assert.ok(read('SoccerModMvpPlugin.Landing.cs').includes('State(ball).LastWall > (WallAssistSeparationFrames + 1)'));
});

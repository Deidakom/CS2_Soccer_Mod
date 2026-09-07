import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = name => readFileSync(new URL(`../src/server-plugin/SoccerModMvp/${name}`, import.meta.url), 'utf8');
const main = read('SoccerModMvpPlugin.cs');
const handling = read('SoccerModMvpPlugin.BallHandling.cs');
const impact = read('SoccerModMvpPlugin.BodyImpact.cs');

test('normal and wall-pop knife kicks invalidate pre-kick history', () => {
  assert.equal(main.match(/BeginKnifeBallContact\(ball\);/g)?.length, 2);
  const marker = handling.split('private void BeginKnifeBallContact')[1].split('private void NewBallContact')[0];
  for (const text of ['NewBallContact(ball)', 'LastKickTick = Server.TickCount',
    '_previousBallImpactOrigin = null', '_previousBallImpactVelocity = null',
    'training.PreviousImpactOrigin = null', 'training.PreviousImpactVelocity = null',
    '_settleLowSpeedTicks = 0', '_ballSettled = false']) assert.ok(marker.includes(text), text);
});

test('both body paths and settling respect kick ownership before velocity writes', () => {
  const push = main.split('private void ApplyPlayerBallPushFor')[1].split('private void UpdateDerivedMotion')[0];
  const body = impact.split('private void ApplyBallPlayerImpactFor')[1].split('private static void ApplyBallImpactKnockback')[0];
  assert.ok(push.includes('KnifeKickOwnsTick(target.Ball)'));
  assert.ok(body.includes('KnifeKickOwnsTick(ball)'));
  assert.ok(push.indexOf('KnifeKickOwnsTick(target.Ball)') < push.indexOf('Teleport('));
  assert.ok(body.indexOf('KnifeKickOwnsTick(ball)') < body.indexOf('previousOriginField ='));
  assert.ok(main.includes('!KnifeKickOwnsTick(_ball)) UpdateBallSettleState'));
  assert.ok(main.includes('!_ballSettled && !KnifeKickOwnsTick(_ball)'));
  assert.ok(handling.includes('if (KnifeKickOwnsTick(target.Ball)) continue;'));
});

test('surface-cone change retains reach, cooldown, obstruction and kickoff checks', () => {
  const kick = main.split('private void TryApplyPrimaryKnifeKick')[1].split('private bool TryApplyWallPopKick')[0];
  for (const text of ['candidateDistance > _kickSurfaceReach + BallCollisionRadius',
    'now - lastAcceptedTime < _kickCooldownSeconds', 'BallContactMath.KickSphereInCone(',
    '!IsKickoffTouchAllowed(player)', 'line_of_sight_blocked']) assert.ok(kick.includes(text), text);
});

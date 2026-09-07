import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = name => readFileSync(new URL(`../src/server-plugin/SoccerModMvp/${name}`, import.meta.url), 'utf8');
const training = read('SoccerModMvpPlugin.Training.cs');

test('goals are suppressed by either shared or any personal cannon without changing the manual flag', () => {
  assert.ok(training.includes('CannonGoalsSuppressed => _cannonTimer is not null'));
  assert.ok(training.includes('_personalCannons.Values.Any(state => state.Timer is not null)'));
  assert.ok(read('SoccerModMvpPlugin.Match.cs').includes('CannonGoalsSuppressed || (_trainingGoalsDisabled && _matchPhase == MatchPhase.Warmup)'));
  assert.ok(training.includes('Goals stay disabled until all cannons are off.'));
});

test('automatic suppression is active before the first cannon shot', () => {
  const shared = training.split('private void TrainingCannonOn(')[1].split('private void RestartCannonTimer')[0];
  const personal = training.split('private void PersonalCannonOn(')[1].split('private void RestartPersonalCannonTimer')[0];
  assert.ok(shared.indexOf('RestartCannonTimer();') < shared.indexOf('TrainingCannonShoot();'));
  assert.ok(personal.indexOf('RestartPersonalCannonTimer(slot, state);') < personal.indexOf('PersonalCannonShoot(slot);'));
  assert.ok(!shared.includes('_trainingGoalsDisabled ='));
  assert.ok(!personal.includes('_trainingGoalsDisabled ='));
});

test('wall assistance uses restored pre-today timing and retention in both profiles', () => {
  const main = read('SoccerModMvpPlugin.cs');
  assert.ok(main.includes('WallAssistCooldownSeconds = 0.35'));
  assert.ok(main.includes('DefaultWallAssistMinimumNormalRetention = 0.18f'));
  assert.ok(main.includes('now - _lastWallAssistTime < WallAssistCooldownSeconds'));
  assert.ok(read('SoccerModMvpPlugin.BallHandling.cs').includes('now - state.LastWall < WallAssistCooldownSeconds'));
});

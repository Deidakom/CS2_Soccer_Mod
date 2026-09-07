import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
const root = new URL('../src/server-plugin/SoccerModMvp/', import.meta.url);
const read = name => readFileSync(new URL(name, root), 'utf8');

test('removed trial has no commands, input interception, menu or held-ball hooks', () => {
  for (const name of readdirSync(root).filter(n => n.endsWith('.cs')))
    assert.doesNotMatch(read(name), /GoalkeeperTrial|_gkTrial|_gkHeldBall|css_gk(?:trial|test|dive|catch|throw)/, name);
});
test('keeper perk requires designated skin, playable state and own calibrated small box', () => {
  const source = read('SoccerModMvpPlugin.GoalkeeperSprint.cs');
  for (const guard of ['IsEligiblePlayer(player)', 'IsGkSlot(player.Slot, player.Team)',
    'GkBoxFor(player.Team)', 'GoalkeeperSprintRules.InBox', '!_sprintSuppressed',
    '!_capFightStarted', '!_capFightPending', 'MatchPhase.Warmup or MatchPhase.Live'])
    assert.ok(source.includes(guard), guard);
});
test('both sprint paths and commands support the perk without changing normal field speed', () => {
  const stamina = read('SoccerModMvpPlugin.SprintParity.cs');
  const sprint = read('SoccerModMvpPlugin.Sprint.cs');
  assert.ok(stamina.includes('state.Update(now, HasGoalkeeperBoxSprint(player, pawn))'));
  assert.ok(stamina.includes('SprintMovementMultiplier(state)'));
  assert.ok(sprint.includes('stamina.Update(Server.TickedTime, HasGoalkeeperBoxSprint(player, pawn))'));
  assert.ok(sprint.includes('UpdateLegacyKeeperSprint(player, pawn, state, now)'));
  assert.ok(sprint.includes('Server.TickedTime, command: true)'));
  assert.ok(sprint.includes('SprintSpeedMultiplier = 1.25f'));
  assert.ok(read('SoccerModMvpPlugin.SprintBar.cs').includes('if (keeperSprint ||'));
});
test('skin release and team changes refresh speed; halftime preserves both keepers', () => {
  const skin = read('SoccerModMvpPlugin.GkSkin.cs');
  assert.match(skin, /_gkSlotByTeam.Remove\(team\);\s+RefreshGoalkeeperSprint\(player\)/);
  assert.ok(skin.includes('swap.Controller == player.EntityHandle.Raw'));
  assert.ok(skin.includes('Server.TickedTime <= swap.Until'));
  assert.ok(skin.includes('_gkSlotByTeam[nextTeam] = keeper!.Slot'));
  assert.ok(skin.includes('_sprintStateBySlot.Remove(slot)'));
  assert.ok(read('SoccerModMvpPlugin.Match.cs').includes('GkSkinPrepareHalfSwap();'));
});

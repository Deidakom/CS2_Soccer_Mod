import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');

test('football calls: the owner\'s list, team chat plus a teammates-only marker', () => {
  const calls = read('SoccerModMvpPlugin.Calls.cs');
  // Exactly the owner's 7 (one menu page), in this order, in English.
  assert.ok(calls.includes('"Well done!", "Pass here!", "Cross!", "I\'m free!", "Pass back!", "Nice pass!", "Sorry!",'));
  assert.ok(!calls.includes('"Shoot!"'), 'owner dropped Shoot! to stay at 7');
  assert.ok(calls.includes('Title = "Calls"') && calls.includes('[Call]'));
  assert.ok(calls.includes('AddCommand("css_calls"'));
  assert.ok(calls.includes('p.Team == player.Team'), 'team chat only');
  assert.ok(calls.includes('info.TransmitEntities.Remove(marker.Text)'));
  assert.ok(!calls.includes('TransmitEntities.Add'), 'Add crashes the server');
  assert.ok(read('SoccerModMvpPlugin.Links.cs').includes('bind v css_calls'), 'V bind is in !binds');
  assert.ok(read('SoccerModMvpPlugin.Menu.cs').includes('menu.Add("Calls", OpenCallsMenu);'));
  const main = read('SoccerModMvpPlugin.cs');
  assert.ok(main.includes('CallsOnLoad();') && main.includes('CallsOnTick();'));
});

test('B opens nothing unless an admin turns it back on', () => {
  assert.match(read('SoccerModMvpPlugin.MenuParity.cs'), /public bool BuyKeyMenu \{ get; set; \}\n/);
  const click = read('SoccerModMvpPlugin.ClickMenu.cs');
  assert.match(click, /if \(!_menuParity\.BuyKeyMenu\)\s*\{\s*if \(hotReload\) Server\.ExecuteCommand\("mp_buytime 0"\);\s*return;\s*\}/);
  assert.ok(click.includes('AddCommand("css_sm2menu_buykey"'));
});

test('each call plays the owner\'s voice line for the caller\'s team (like a radio command)', () => {
  const calls = read('SoccerModMvpPlugin.Calls.cs');
  assert.ok(calls.includes('"SoccerMod.Call.WellDone", "SoccerMod.Call.PassHere", "SoccerMod.Call.Cross", "SoccerMod.Call.ImFree",'));
  assert.ok(calls.includes('"SoccerMod.Call.PassBack", "SoccerMod.Call.NicePass", "SoccerMod.Call.Sorry",'));
  assert.ok(calls.includes('manifest.AddResource(CallSoundEventsFile)'));
  assert.ok(calls.includes('pawn.EmitSound(CallSoundEvents[index], SoundRecipients(SoccerSound.Radio, p => p.Team == player.Team));'));
  assert.ok(calls.includes('internal const string CallSoundEventsFile = "soundevents/soccermod_calls.vsndevts";'),
    'own file name - the map already ships soundevents_addon.vsndevts');
});

test('radio spam protection: 1.5 s between calls and at most 3 calls per 10 s', () => {
  const calls = read('SoccerModMvpPlugin.Calls.cs');
  assert.ok(calls.includes('private const double CallCooldownSeconds = 1.5;'));
  assert.ok(calls.includes('private const int CallBurstLimit = 3;'));
  assert.ok(calls.includes('private const double CallBurstWindowSeconds = 10.0;'));
  assert.ok(calls.includes('if (recent.Count >= CallBurstLimit)'));
  assert.ok(calls.includes('Calls on cooldown'));
});

test('goal net sound: once when the ball goes in, before the reset; re-armed only by a reset or a round start', () => {
  const net = read('SoccerModMvpPlugin.GoalNetSound.cs');
  assert.ok(net.includes('internal const string GoalNetSoundEvent = "SoccerMod.Goal.Net";'));
  assert.ok(net.includes('if (_goalNetSoundPlayed || _ball is not { IsValid: true } ball) return;'));
  assert.ok(net.includes('ball.EmitSound(GoalNetSoundEvent, SoundRecipients(SoccerSound.GoalNet));'));
  const match = read('SoccerModMvpPlugin.Match.cs');
  const start = match.indexOf('private void OnGoalScored(');
  assert.ok(match.indexOf('PlayGoalNetSound();', start) < match.indexOf('if (_matchPhase == MatchPhase.Warmup)', start), 'before the reset');
  assert.ok(read('SoccerModMvpPlugin.cs').includes('_goalNetSoundPlayed = false; // a reset ball can score (and sound) again'));
  const events = fs.readFileSync(path.join(root, 'src/workshop-addon/soccermod_calls/soundevents/soccermod_calls.vsndevts'), 'utf8');
  assert.ok(events.includes('"SoccerMod.Goal.Net"') && events.includes('sounds/soccermod/goal/net.vsnd'));
});

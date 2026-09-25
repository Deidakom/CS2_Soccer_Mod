import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');
const events = fs.readFileSync(path.join(root, 'src/workshop-addon/soccermod_calls/soundevents/soccermod_calls.vsndevts'), 'utf8');

test('Settings -> Sounds: one switch per sound, every EmitSound filtered by its own switch', () => {
  const prefs = read('SoccerModMvpPlugin.SoundPrefs.cs');
  for (const label of ['Soccer radio (V menu calls)', 'Ball kick (off = CS2 knife hit)', 'Goal net', 'Post and crossbar hits', 'Sprint breathing', 'Stadium (whistles and crowd)'])
    assert.ok(prefs.includes(`"${label}"`), label);
  assert.ok(prefs.includes('menu.Add("All on"') && prefs.includes('menu.Add("All off"'));
  assert.match(read('SoccerModMvpPlugin.MenuParity.cs'), /public Dictionary<string, List<ulong>> MutedSounds \{ get; set; \} = new\(\);/);
  assert.ok(read('SoccerModMvpPlugin.Menu.cs').includes('menu.Add("Sounds", OpenPersonalSoundsMenu);'));
  assert.ok(read('SoccerModMvpPlugin.Calls.cs').includes('SoundRecipients(SoccerSound.Radio, p => p.Team == player.Team)'));
  assert.ok(read('SoccerModMvpPlugin.GoalNetSound.cs').includes('SoundRecipients(SoccerSound.GoalNet)'));
  assert.ok(read('SoccerModMvpPlugin.GoalFrameSound.cs').includes('SoundRecipients(SoccerSound.Posts)'));
  assert.ok(read('SoccerModMvpPlugin.cs').includes('ball.EmitSound(_kickSoundName, SoundRecipients(SoccerSound.Kick));'));
  assert.ok(read('SoccerModMvpPlugin.cs').includes('ball.EmitSound(KickSoundFallbackName, SoundRecipients(SoccerSound.Kick, p => !SoundOn(p, SoccerSound.Kick), ignoreMute: true));'), 'kick off = knife hit');
  assert.ok(read('SoccerModMvpPlugin.cs').includes('KickSoundFallbackName = "Weapon_Knife.HitWall"'));
  assert.ok(prefs.includes('!SoundOn(player, SoccerSound.Sprint)'));
  assert.ok(read('SoccerModMvpPlugin.StadiumSounds.cs').includes('SoundRecipients(SoccerSound.Stadium)'));
  assert.ok(prefs.includes('private void MigrateSoundGroups()'), 'keeps the first version\'s choices');
});

test('kick, post, crossbar, sprint and stadium sounds are wired to their events', () => {
  assert.ok(read('SoccerModMvpPlugin.cs').includes('private const string DefaultKickSoundName = "SoccerMod.Ball.Kick";'));
  const frame = read('SoccerModMvpPlugin.GoalFrameSound.cs');
  assert.ok(frame.includes('"SoccerMod.Goal.PostTop"') && frame.includes('"SoccerMod.Goal.PostSide"'));
  assert.ok(frame.includes('private const float GoalFrameHitMinimumSpeed = 300f;'));
  assert.ok(read('SoccerModMvpPlugin.Sprint.cs').includes('PlaySprintSound(player, pawn);'));
  assert.ok(read('SoccerModMvpPlugin.SprintParity.cs').includes('if (!wasActive && state.Active) PlaySprintSound(player, pawn);'));
  const match = read('SoccerModMvpPlugin.Match.cs');
  assert.ok(match.includes('StadiumGoal();') && match.includes('if ((wide || high) && MatchRuleMath.IsNearMiss(crossX, GoalCenterX, _goalHalfWidthX, crossZ, crossbarZ, speed))'));
  assert.ok(read('SoccerModMvpPlugin.cs').includes('StadiumKickoffTaken(reason);'));
  for (const name of ['SoccerMod.Goal.PostTop', 'SoccerMod.Goal.PostSide', 'SoccerMod.Ball.Kick', 'SoccerMod.Sprint.Start',
    'SoccerMod.Stadium.WhistleKickoff', 'SoccerMod.Stadium.Airhorn', 'SoccerMod.Stadium.WhistleGoal',
    'SoccerMod.Stadium.CrowdGoal', 'SoccerMod.Stadium.Boo'])
    assert.ok(events.includes(`"${name}"`), name);
});

test('the ball menu has no kick sound picker any more', () => {
  const effects = read('SoccerModMvpPlugin.BallWorkbench.cs').split('private void OpenBallEffectsMenu')[1].split('private void OpenBallRestoreDefaultsMenu')[0];
  assert.ok(!effects.includes('t.Sound =') && !effects.includes('Default.Land'));
  assert.ok(read('SoccerModMvpPlugin.BallWorkbench.cs').includes('menu.Add("Effects", OpenBallEffectsMenu);'));
});

test('middle-mouse map pings are dropped', () => {
  assert.ok(read('SoccerModMvpPlugin.Calls.cs').includes('AddCommandListener("player_ping", (_, _) => HookResult.Handled, HookMode.Pre);'));
});

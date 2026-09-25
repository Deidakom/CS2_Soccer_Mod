import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');
const events = fs.readFileSync(path.join(root, 'src/workshop-addon/soccermod_calls/soundevents/soccermod_calls.vsndevts'), 'utf8');

test('players mute our sounds by group in Settings; every EmitSound goes through SoundRecipients', () => {
  const prefs = read('SoccerModMvpPlugin.SoundPrefs.cs');
  assert.ok(prefs.includes('"Soccer radio sounds (V menu calls)"') && prefs.includes('"Effect sounds (goal net, posts, ball kick, sprint)"'));
  assert.match(read('SoccerModMvpPlugin.MenuParity.cs'), /public List<ulong> MutedRadioSounds \{ get; set; \} = new\(\);/);
  assert.match(read('SoccerModMvpPlugin.MenuParity.cs'), /public List<ulong> MutedEffectSounds \{ get; set; \} = new\(\);/);
  assert.ok(read('SoccerModMvpPlugin.Menu.cs').includes('ToggleSoundGroup(p, soundGroup);'));
  assert.ok(read('SoccerModMvpPlugin.Calls.cs').includes('SoundRecipients(SoccerSoundGroup.Radio, p => p.Team == player.Team)'));
  assert.ok(read('SoccerModMvpPlugin.GoalNetSound.cs').includes('SoundRecipients(SoccerSoundGroup.Effects)'));
  assert.ok(read('SoccerModMvpPlugin.GoalFrameSound.cs').includes('SoundRecipients(SoccerSoundGroup.Effects)'));
  assert.ok(read('SoccerModMvpPlugin.cs').includes('ball.EmitSound(_kickSoundName, SoundRecipients(SoccerSoundGroup.Effects));'));
  assert.ok(prefs.includes('if (!player.IsValid || player.IsBot || !pawn.IsValid || !SoundGroupOn(player, SoccerSoundGroup.Effects)) return;'));
});

test('kick, post, crossbar and sprint sounds are wired to their events', () => {
  assert.ok(read('SoccerModMvpPlugin.cs').includes('private const string DefaultKickSoundName = "SoccerMod.Ball.Kick";'));
  const frame = read('SoccerModMvpPlugin.GoalFrameSound.cs');
  assert.ok(frame.includes('"SoccerMod.Goal.PostTop"') && frame.includes('"SoccerMod.Goal.PostSide"'));
  assert.ok(frame.includes('private const float GoalFrameHitMinimumSpeed = 300f;'));
  assert.ok(read('SoccerModMvpPlugin.Sprint.cs').includes('PlaySprintSound(player, pawn);'));
  assert.ok(read('SoccerModMvpPlugin.SprintParity.cs').includes('if (!wasActive && state.Active) PlaySprintSound(player, pawn);'));
  for (const name of ['SoccerMod.Goal.PostTop', 'SoccerMod.Goal.PostSide', 'SoccerMod.Ball.Kick', 'SoccerMod.Sprint.Start'])
    assert.ok(events.includes(`"${name}"`), name);
});

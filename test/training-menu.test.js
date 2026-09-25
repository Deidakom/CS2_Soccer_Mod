import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');
const all = () => fs.readdirSync(path.join(root, 'src/server-plugin/SoccerModMvp'))
  .filter((f) => f.endsWith('.cs')).map(read).join('\n');

test('Advanced Training and Shot Drills / Replay are gone from every menu (owner, 2026-09-25)', () => {
  const source = all();
  for (const gone of ['menu.Add("Advanced Training"', 'menu.Add("Advanced training"', 'menu.Add("Shot Drills / Replay"',
    'menu.Add("Training Settings"', 'OpenAdvancedTrainingMenu', 'OpenTrainingDrillsMenu'])
    assert.ok(!source.includes(gone), gone);
  const training = read('SoccerModMvpPlugin.Training.cs');
  assert.match(training, /menu\.Add\("Props \/ Position Manager"/);
});

test('the drill commands themselves still exist', () => {
  const coach = read('SoccerModMvpPlugin.TrainingCoach.cs');
  assert.match(coach, /AddCommand\("css_ball_replay"/);
  assert.match(all(), /AddCommand\("css_ball_target"/);
});

import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');

test('Ball size: five exact sizes in the Ball menu, applied live with the SetScale input', () => {
  const size = read('SoccerModMvpPlugin.BallSize.cs');
  for (const [name, value] of [['CS2 Legacy', '1f'], ['10% smaller', '0.9f'], ['Default (12.5% smaller)', 'DefaultBallSize'],
    ['15% smaller', '0.85f'], ['20% smaller', '0.8f']])
    assert.ok(size.includes(`("${name}", ${value})`), name);
  assert.ok(read('SoccerModMvpPlugin.cs').includes('private const float DefaultBallSize = 0.875f;'), '12.5% smaller is the default');
  assert.ok(!size.includes('35% smaller'), '35% was removed (owner, 2026-09-25)');
  assert.ok(size.includes('ball.AcceptInput("SetScale", value: _ballSize.ToString("0.###", CultureInfo.InvariantCulture));'));
  assert.ok(size.includes('if (MathF.Abs(current - _ballSize) < 0.001f) return;'), 'only acts on a change');
  assert.ok(size.includes('tuning.Values["ballSize"] = size;') && size.includes('ApplyBallTuning(tuning)'));
  const bench = read('SoccerModMvpPlugin.BallWorkbench.cs');
  assert.ok(bench.includes('menu.Add($"Ball size: {BallSizeLabel()}", OpenBallSizeMenu);'));
  assert.match(bench, /new\("ballSize", "Engine physics", "Ball size \(1 = CS2 Legacy 37\.6 u, default 0\.875\)", \.5f, 1f, \.05f/);
  assert.ok(read('SoccerModMvpPlugin.cs').includes('ApplyBallSize(ball, reason);'));
  assert.ok(read('SoccerModMvpPlugin.MenuAudit.cs').includes('("Ball size", OpenBallSizeMenu)'));
});

test('everything radius-dependent follows the size', () => {
  const main = read('SoccerModMvpPlugin.cs');
  assert.match(main, /private const float DefaultBallCollisionRadius = 18\.805f;/);
  assert.match(main, /private float BallCollisionRadius => DefaultBallCollisionRadius \* _ballSize;/);
  assert.doesNotMatch(main, /const float BallCollisionRadius/);
  for (const derived of ['BallResetZ =>', 'BallPushContactDistance =>', 'WallPopWallProbeDistance =>'])
    assert.ok(main.includes(`private float ${derived}`), derived);
  assert.ok(read('SoccerModMvpPlugin.Match.cs').includes('_goalLineY + _goalDepthRequired * _ballSize'), 'whole ball over the line');
  const config = read('SoccerModMvpPlugin.Config.cs');
  assert.ok(config.includes('public float? BallSize { get; set; }') && config.includes('BallSize = _ballSize,'));
  assert.ok(config.includes('if (stored.BallSize is >= 0.5f and <= 1f) _ballSize = stored.BallSize.Value;'));
});

test('the size probe stays a console-only diagnostic', () => {
  const size = read('SoccerModMvpPlugin.BallSize.cs');
  assert.match(size, /OnBallSizeTestCommand\(CCSPlayerController\? player, CommandInfo command\)\s*\{\s*if \(!RequireServerConsole\(player, command\)\) return;/);
});

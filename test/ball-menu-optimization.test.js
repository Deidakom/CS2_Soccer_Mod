import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = name => readFileSync(new URL(`../src/server-plugin/SoccerModMvp/${name}`, import.meta.url), 'utf8').replaceAll('\r\n', '\n');
const main = read('SoccerModMvpPlugin.cs');
const menu = read('SoccerModMvpPlugin.Menu.cs');
const rewind = read('SoccerModMvpPlugin.KickRewind.cs');
const workbench = read('SoccerModMvpPlugin.BallWorkbench.cs');
const config = read('SoccerModMvpPlugin.Config.cs');
const chat = read('SoccerModMvpPlugin.ChatInput.cs');

test('every profile combines simultaneous body pushes instead of letting the last slot win', () => {
  const push = main.split('private void ApplyPlayerBallPushFor')[1].split('private void UpdateDerivedMotion')[0];
  assert.ok(push.includes('simultaneous.Add((player.Slot, N(pushedVelocity) - N(inherited)))'));
  assert.ok(!push.includes('if (ImprovedHandling) simultaneous.Add'), 'legacy must not bypass the combination');
  assert.ok(!push.includes('ball.Teleport(velocity: pushedVelocity)'), 'no per-player velocity write');
  assert.equal(push.match(/Teleport\(/g)?.length, 1);
  assert.ok(push.includes('BallContactMath.CombinePushes(N(inherited), simultaneous)'));
});

test('lag compensation only re-judges misses and never replaces a live-position kick', () => {
  const kick = main.split('private void TryApplyPrimaryKnifeKick')[1].split('private void PlayKickSound')[0];
  assert.ok(main.indexOf('RecordBallTrails();') > main.indexOf('UpdateTrainingBallMotion();'));
  assert.ok(main.indexOf('RecordBallTrails();') < main.indexOf('UpdateKnifeSwings();'));
  assert.ok(kick.indexOf('RewoundKickOrigins(player, candidate.Ball)') > kick.indexOf('if (reason is null)'));
  assert.ok(kick.includes('if (selected is null && rewoundBall is not null)'), 'live qualifiers always win');
  assert.ok(kick.includes('var ballOrigin = selectedOrigin ?? target.Origin;'));
  assert.ok(kick.includes('ApplyThrusterKick(target.Origin, launchDirection)'), 'thruster stays on the physical ball');
  assert.ok(kick.includes('lagCompensatedMs={LagCompensatedMs:F0}'));
  assert.ok(rewind.includes('State(ball).LastContactTick'), 'never rewinds across another contact');
  assert.ok(rewind.includes('_kickMaximumBallSpeed'), 'never rewinds across a teleport');
  assert.ok(rewind.includes('player.Ping'));
  assert.ok(main.split('private void ResetDerivedMotion')[1].split('private static bool IsEligiblePlayer')[0].includes('_ballTrails.Clear();'));
});

test('lag compensation is a persisted workbench control and old presets stay loadable', () => {
  assert.ok(workbench.includes('new("kickLagCompensationMs", "Kick power"'));
  assert.ok(config.includes('public float? KickLagCompensationMs { get; set; }'));
  assert.ok(config.includes('KickLagCompensationMs = _kickLagCompensationMs,'));
  assert.match(config, /stored\.KickLagCompensationMs is >= 0f and <= KickRewind\.MaximumMilliseconds/);
  const preset = workbench.split('private void OpenBallPreset(CCSPlayerController player, string name)')[1].split('private void OpenBallLiveMenu')[0];
  assert.equal(preset.match(/WithCurrentValuesForMissingDials\(stored\)/g)?.length, 2);
});

test('HTML menu escapes dynamic names without changing its line markup', () => {
  const html = menu.split('private static string BuildMenuHtml')[1].split('private static string BuildMenuPlainText')[0];
  assert.equal(html.match(/MenuText\.EscapeHtml\(/g)?.length, 3);
  assert.ok(html.includes("<font class='fontSize-m' color='#ff9900'>"));
  assert.ok(html.includes("<font class='fontSize-sm' color='#bfff00'>"));
  assert.ok(html.includes("<font class='fontSize-sm' color='#9a9a9a'>"));
  assert.ok(html.includes('Pad(widest - text.Length - 3)'), 'padding keeps using the visible length');
});

test('menus remember pages across toggles and Back, and forget them when the session ends', () => {
  const key = menu.split('private HookResult OnMenuNumberKey')[1].split('private int NormalizePageIndex')[0];
  assert.ok(key.indexOf('MenuPages(slot).Leave(menu.MemoryKey, pageIndex)') < key.indexOf('option.OnSelect(player)'));
  assert.ok(key.includes('!_chatInputBySlot.ContainsKey(slot)'), 'a pending chat prompt keeps the trail');
  const open = menu.split('private void OpenNumberMenu')[1].split('private const int MenuFirstPageCapacity')[0];
  assert.ok(open.includes('_menuPageBySlot[player.Slot] = MenuPages(player.Slot).Enter(menu.MemoryKey);'));
  const close = menu.split('private HookResult OnMenuCloseKey')[1].split('private HookResult OnMenuNumberKey')[0];
  assert.ok(close.includes('ForgetMenuPages(player.Slot)'));
  const fresh = menu.split('private void OnMenuCommand')[1].split('private void OpenMainMenu')[0];
  assert.ok(fresh.includes('ForgetMenuPages(player.Slot)') && fresh.includes('CancelPendingChatInput(player)'));
  assert.match(menu, /Math\.Clamp\(requested, 0, Math\.Max\(0, pageCount - 1\)\)/);
  assert.ok(workbench.includes('Key = "ball-dial:" + dial.Key'));
  assert.ok(read('SoccerModMvpPlugin.Referee.cs').includes('Key = "referee-score"'));
});

test('timed redraws reuse the rendered page and every exit path drops the cache', () => {
  const draw = menu.split('private void DrawMenu')[1].split('private static void PresentMenuText')[0];
  assert.ok(draw.includes('ReferenceEquals(cached.Menu, menu)'));
  assert.ok(draw.indexOf('_menuRenderCache.TryGetValue') < draw.indexOf('BuildMenuPages(menu)'));
  for (const section of ['private void CloseMenu', 'private void MenuOnPlayerDisconnect', 'private void MenuOnMapStart'])
    assert.ok(menu.split(section)[1].split('\n    }\n')[0].includes('_menuRenderCache'), section);
});

test('help explains the ball controls from live tuning and chat prompts can be abandoned', () => {
  const help = menu.split('private void OpenHelpMenu')[1].split('private void OpenClientSettingsMenu')[0];
  assert.ok(help.includes('menu.Add("Ball controls", PrintBallControls)'));
  const controls = menu.split('private void PrintBallControls')[1].split('private static void PrintProjectLinks')[0];
  for (const field of ['_leftClickPowerScale', '_rightClickPowerScale', '_leftClickCrouchPowerScale', '_crouchLiftBonusDegrees', '_sprintUseButtonTrigger'])
    assert.ok(controls.includes(field), field);
  const cancel = chat.split('private void CancelPendingChatInput')[1].split('private HookResult OnChatInputSay')[0];
  assert.ok(cancel.includes('_chatInputBySlot.Remove(player.Slot)'));
  assert.ok(!cancel.includes('OnCancel'), 'reopening the menu must not also run the prompt continuation');
});

test('far-away knife presses no longer flood the Information log', () => {
  assert.match(main, /ballDistance is <= KickDiagnosticRange \? LogLevel\.Information : LogLevel\.Debug/);
  assert.match(main, /reason is "player_ineligible" or "active_weapon_not_knife"/);
  assert.match(main, /reason == "out_of_reach" && distance is > KickDiagnosticRange/);
});

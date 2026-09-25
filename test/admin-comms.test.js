import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = name => readFileSync(new URL(`../src/server-plugin/SoccerModMvp/${name}`, import.meta.url), 'utf8');
const main = read('SoccerModMvpPlugin.cs');
const menu = read('SoccerModMvpPlugin.Menu.cs');
const adminMenu = read('SoccerModMvpPlugin.AdminMenu.cs');
const comms = read('SoccerModMvpPlugin.Comms.cs');
const referee = read('SoccerModMvpPlugin.Referee.cs');

test('!admin opens the SourceMod-style root menu', () => {
  const handler = menu.split('private void OnAdminMenuCommand')[1].split('private void OpenPunishPlayerMenu')[0];
  assert.ok(handler.includes('OpenAdminRootMenu(player)'));
  assert.ok(handler.includes('"root"'), 'root only');
  const root = adminMenu.split('private void OpenAdminRootMenu')[1].split('private string CommsTags')[0];
  for (const entry of ['"Player commands"', '"Server commands"', '"Active punishments"', '"SoccerMod admin", OpenAdminMenu'])
    assert.ok(root.includes(entry), entry);
});

test('player actions run the permission-checked commands', () => {
  const actions = adminMenu.split('private void OpenAdminPlayerActionsMenu')[1].split('private void OpenAdminDurationMenu')[0];
  for (const cmd of ['css_kick #', 'css_slay #', 'css_spec #', 'css_ban #', '$"css_un{command} #{userId}"', '$"css_{command} #{userId}"',
    'Comms("Mute (voice)...", "Unmute", "mute", muted)', 'Comms("Gag (chat)...", "Ungag", "gag", gagged)',
    'Comms("Silence (voice + chat)...", "Unsilence", "silence", muted && gagged)']) assert.ok(actions.includes(cmd), cmd);
  assert.match(adminMenu, /ExecuteClientCommandFromServer\(command\)/);
});

test('permanent punishments are root only in menu and command', () => {
  const duration = adminMenu.split('private void OpenAdminDurationMenu')[1].split('private void OpenAdminServerMenu')[0];
  assert.match(duration, /HasFlag\(SteamIdOf\(player\), "root"\)\) menu\.Add\("Permanent"/);
  assert.match(comms, /CommsRules\.IsPermanent\(minutes, untilMapChange\) && player is not null && !HasFlag\(SteamIdOf\(player\), "root"\)/);
  assert.match(comms, /RequirePermission\(player, command, "admin"\)/);
});

test('mute uses voice flags, gag blocks chat but not commands', () => {
  assert.match(comms, /VoiceFlags\.Muted/);
  const gag = comms.split('private HookResult OnGaggedSay')[1].split('private void OnCommsCommand')[0];
  assert.ok(gag.includes('CommsRules.IsChatCommand(info.GetArg(1))'));
  assert.ok(gag.includes('return HookResult.Handled'));
  assert.ok(main.indexOf('CommsOnLoad();') > main.indexOf('AdminOnLoad();'));
  assert.ok(main.indexOf('CommsOnLoad();') < main.indexOf('DeadChatOnLoad();') || !main.includes('DeadChatOnLoad();'));
  assert.ok(main.includes('CommsOnTick();'));
  assert.match(comms, /soccermod_comms\.json/);
});

test('Spec Player and Punish Player live in the Referee menu', () => {
  const admin = menu.split('private void OpenAdminMenu')[1].split('private void OnAdminMenuCommand')[0];
  assert.ok(!admin.includes('OpenSpecPlayerMenu') && !admin.includes('OpenPunishPlayerMenu'));
  const ref = referee.split('private void OpenRefereeMenu')[1].split('private void OpenGiveCardMenu')[0];
  assert.ok(ref.includes('"Spec Player", OpenSpecPlayerMenu'));
  assert.ok(ref.includes('"Punish Player", OpenPunishPlayerMenu'));
  assert.match(menu, /Title = "Referee - Punish Player", OnBack = OpenRefereeMenu/);
  assert.match(menu, /Title = "Referee - Spec Player", OnBack = OpenRefereeMenu/);
});

test('Help -> Commands lists every command group, admin groups only for admins', () => {
  const help = read('SoccerModMvpPlugin.HelpCommands.cs');
  assert.ok(menu.includes('menu.Add("Commands", OpenHelpCommandsMenu);'));
  for (const cmd of ['!menu', '!calls', '!links', '!kill', '!gk', '!rdy', '!cap', '!stats', '!elo', '!match', '!rr', '!admin', '!mute', '!ban'])
    assert.ok(help.includes(`new("${cmd}`), cmd);
  assert.ok(help.includes('if (flag is not null && !HasFlag(SteamIdOf(player), flag)) continue;'));
});

test('!links prints a big console block and a red chat pointer', () => {
  const links = read('SoccerModMvpPlugin.Links.cs');
  assert.ok(links.includes('SOCCERMOD WORKSHOP ITEM'));
  assert.ok(links.includes('Open the link, click Subscribe, then restart CS2.'));
  assert.ok(links.includes(String.raw`PrintToChat(" \x07[SM] The Workshop link is in your console`));
});

test('join and help texts: !help hint, no console bind block in !help, no chat-digit line', () => {
  const social = read('SoccerModMvpPlugin.Social.cs');
  assert.ok(menu.includes('First time here? Type !help to get all necessary commands.'));
  assert.ok(!menu.includes('!menu or B opens the menu'));
  assert.ok(!menu.includes('chat !1 to !9 also selects'));
  const help = social.split('private void PrintHelp')[1];
  assert.ok(!help.includes('MenuSendBindInstructions(player)') && !help.includes('--- menu keys ---'));
});

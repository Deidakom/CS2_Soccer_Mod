import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const plugin = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');
const addon = (name) => fs.readFileSync(path.join(root, 'src/workshop-addon/soccermod_menu/panorama', name), 'utf8');

test('the clickable menu uses the source .xml layout path and the kit panel ids', () => {
  const click = plugin('SoccerModMvpPlugin.ClickMenu.cs');
  assert.match(click, /ClickMenuLayout = "panorama\/layout\/custom_game\/soccermod_menu\.xml"/);
  assert.match(click, /new PanelOptions \{ Root = "sm_window", ShownClass = "shown" \}/);
  assert.match(click, /new BuyMenuBridge\(_clickMenuPanel, "sm_window", "sm_dim"/);
});

test('clicks go through the same key dispatch as number keys', () => {
  const click = plugin('SoccerModMvpPlugin.ClickMenu.cs');
  assert.match(click, /OnMenuNumberKey\(player, number, "click"\)/);
  assert.match(click, /OnMenuCloseKey\(player, "click"\)/);
  const menu = plugin('SoccerModMvpPlugin.Menu.cs');
  assert.match(menu, /var pages = BuildMenuPages\(menu, UsesClickMenu\(player\)\);/);
  assert.match(menu, /if \(UsesClickMenu\(player\)\)\s*\{\s*DrawClickMenu\(player, menu\);\s*return;\s*\}/);
  assert.match(menu, /if \(UsesClickMenu\(player\)\) HideClickMenu\(player\);/);
});

test('the clickable menu is on by default (layout ships in the jersey Workshop item), testers per player', () => {
  assert.match(plugin('SoccerModMvpPlugin.MenuParity.cs'), /public bool ClickMenu \{ get; set; \} = true;/);
  const click = plugin('SoccerModMvpPlugin.ClickMenu.cs');
  assert.match(click, /if \(_menuParity\.ClickMenu \|\| _clickMenuTesters\.Count > 0\) StartClickMenuBridge\(hotReload\);/);
  assert.match(click, /_menuParity\.ClickMenu \|\| _clickMenuTesters\.Contains\(SteamIdOf\(player\)\)/);
});

test('the layout follows the custom_hud_layout rules', () => {
  const xml = addon('layout/custom_game/soccermod_menu.xml');
  const css = addon('styles/custom_game/soccermod_menu.css');
  assert.match(xml, /s2r:\/\/panorama\/styles\/custom_game\/soccermod_menu\.vcss_c/);
  assert.match(xml, /<Panel class="sm-screen" hittest="false">/); // root child has no id
  for (let i = 1; i <= 7; i++) {
    assert.match(xml, new RegExp(`<Button id="sm_row_${i}" class="sm-row[^"]*">`));
    assert.match(xml, new RegExp(`<Label id="sm_row_${i}_text" class="sm-option" text="\{s:text\}" />`));
  }
  assert.doesNotMatch(xml, /sm_row_8|sm_row_9/); // navigation has its own bar
  assert.match(xml, /<Panel id="sm_nav" class="sm-nav">/);
  assert.match(xml, /<Button id="sm_back" class="sm-navbtn sm-back">/);
  assert.match(xml, /<Label id="sm_page" class="sm-page" text="\{s:text\}" \/>/);
  assert.match(xml, /<Button id="sm_next" class="sm-navbtn sm-next">/);
  assert.match(xml, /<Button id="sm_close"/);
  assert.doesNotMatch(xml, /<Image|style="/);
  assert.match(css, /\.HUD_BUYMENU_VISIBLE \.sm-window\.native/);
  assert.match(css, /\.HUD_BUYMENU_VISIBLE \.sm-dim\.native/);
  assert.doesNotMatch(css, /,\s*\n\s*\./); // no comma selectors
});

test('the vendored UI kit keeps its MIT notice and no toast/vote dependencies', () => {
  const dir = path.join(root, 'src/server-plugin/SoccerModMvp/UIKit');
  assert.ok(fs.readFileSync(path.join(dir, 'LICENSE-cs2-ui-kit.txt'), 'utf8').includes('MIT License'));
  for (const f of ['UIKit.cs', 'Panel.cs', 'BuyMenuBridge.cs']) {
    const source = fs.readFileSync(path.join(dir, f), 'utf8');
    assert.match(source, /Vendored from nvmxre\/cs2-ui-kit/);
    assert.doesNotMatch(source, /Toasts\.|Votes\./);
  }
});

test('navigation bar: Back/Previous is key 8, Next is key 9, page count in the middle', () => {
  const click = plugin('SoccerModMvpPlugin.ClickMenu.cs');
  assert.match(click, /if \(buttonId == "sm_back"\)\s*\{\s*OnMenuNumberKey\(player, 8, "click"\);/);
  assert.match(click, /if \(buttonId == "sm_next"\)\s*\{\s*OnMenuNumberKey\(player, 9, "click"\);/);
  assert.match(click, /page\.BackGoesToParent \? "\u2039 Back" : "\u2039 Previous"/);
  assert.match(click, /SetClass\(player, "sm_back", "hidden", !page\.HasBack\)/);
  assert.match(click, /SetClass\(player, "sm_next", "hidden", !page\.HasNext\)/);
  assert.match(click, /\$"Page \{page\.PageIndex \+ 1\} \/ \{page\.TotalPages\}"/);
});

test('aim options free the view and wait for a click; testers are saved', () => {
  const menu = plugin('SoccerModMvpPlugin.Menu.cs');
  assert.match(menu, /if \(option\.NeedsAim && UsesClickMenu\(player\)\)\s*\{\s*CloseMenu\(slot, "aim_pick"\);\s*BeginAimPick\(player, option, menu\);/);
  const click = plugin('SoccerModMvpPlugin.ClickMenu.cs');
  assert.match(click, /\(pressed & \(PlayerButtons\.Attack \| PlayerButtons\.Use\)\) != 0/);
  assert.match(click, /\(pressed & PlayerButtons\.Attack2\) != 0/);
  assert.match(click, /_menuParity\.ClickMenuTesters = _clickMenuTesters\.ToList\(\);/);
  assert.match(click, /foreach \(var id in _menuParity\.ClickMenuTesters\) _clickMenuTesters\.Add\(id\);/);
  for (const [file, label] of [
    ['SoccerModMvpPlugin.Training.cs', 'Set cannon position'],
    ['SoccerModMvpPlugin.Training.cs', 'Spawn/Remove Ball'],
    ['SoccerModMvpPlugin.BallWorkbench.cs', 'Place on pitch at crosshair'],
    ['SoccerModMvpPlugin.TrainingProps.cs', 'Move to crosshair'],
  ]) assert.ok(plugin(file).includes(`menu.AddAim("${label}"`), label);
});

test('keys-only players keep their mouse; !menumouse and !bind exist', () => {
  const click = plugin('SoccerModMvpPlugin.ClickMenu.cs');
  assert.ok(click.includes('if (!ClickMenuMouse(player) && !menu.ForceMouse) _clickMenuPanel.CaptureInput(player, false);'));
  assert.ok(click.includes('AddCommand("css_menumouse"'));
  assert.ok(click.includes('_menuParity.ClickMenuMouse[SteamIdOf(player)] = on;'));
  const links = plugin('SoccerModMvpPlugin.Links.cs');
  assert.ok(links.includes('AddCommand("css_bind"'));
  assert.ok(links.includes('player.PrintToConsole(MenuBindLine);'));
  for (let i = 0; i <= 9; i++) assert.ok(links.includes(`bind ${i} css_${i}`), `bind ${i}`);
});

test('first join points at !binds and !links instead of printing bind lines in chat', () => {
  const menu = plugin('SoccerModMvpPlugin.Menu.cs');
  const reminder = menu.slice(menu.indexOf('private void MenuMaybeSendBindReminder'), menu.indexOf('private void CloseMenu'));
  assert.ok(reminder.includes('!binds') && reminder.includes('!links'));
  assert.ok(!reminder.includes('MenuSendBindInstructions(player)'));
});

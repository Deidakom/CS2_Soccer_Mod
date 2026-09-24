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

test('the clickable menu is opt-in: off for everyone until switched on, testers per player', () => {
  assert.match(plugin('SoccerModMvpPlugin.MenuParity.cs'), /public bool ClickMenu \{ get; set; \}\r?\n/);
  const click = plugin('SoccerModMvpPlugin.ClickMenu.cs');
  assert.match(click, /if \(_menuParity\.ClickMenu\) StartClickMenuBridge\(hotReload\);/);
  assert.match(click, /_menuParity\.ClickMenu \|\| _clickMenuTesters\.Contains\(SteamIdOf\(player\)\)/);
});

test('the layout follows the custom_hud_layout rules', () => {
  const xml = addon('layout/custom_game/soccermod_menu.xml');
  const css = addon('styles/custom_game/soccermod_menu.css');
  assert.match(xml, /s2r:\/\/panorama\/styles\/custom_game\/soccermod_menu\.vcss_c/);
  assert.match(xml, /<Panel class="sm-screen" hittest="false">/); // root child has no id
  for (let i = 1; i <= 9; i++) {
    assert.match(xml, new RegExp(`<Button id="sm_row_${i}" class="sm-row[^"]*">`));
    assert.match(xml, new RegExp(`<Label id="sm_row_${i}_text" class="sm-option" text="\{s:text\}" />`));
  }
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

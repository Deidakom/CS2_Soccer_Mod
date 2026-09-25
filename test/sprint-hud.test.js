import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const plugin = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');
const addon = (name) => fs.readFileSync(path.join(root, 'src/workshop-addon/soccermod_menu/panorama', name), 'utf8');

test('the sprint bar layout follows the custom_hud_layout rules', () => {
  const xml = addon('layout/custom_game/soccermod_sprint.xml');
  const css = addon('styles/custom_game/soccermod_sprint.css');
  assert.match(xml, /s2r:\/\/panorama\/styles\/custom_game\/soccermod_sprint\.vcss_c/);
  assert.match(xml, /<Panel class="sm-sprint-screen" hittest="false">/); // root child has no id
  assert.match(xml, /<Panel id="sm_sprint" class="sm-sprint" hittest="false">/);
  assert.match(xml, /<Label id="sm_sprint_label" class="sm-sprint-label" text="\{s:text\}" \/>/);
  assert.match(xml, /<Panel id="sm_sprint_fill" class="sm-sprint-fill" hittest="false" \/>/);
  assert.doesNotMatch(xml, /<Image|<Button|style="/);
  for (let n = 0; n <= 100; n += 5) assert.ok(css.includes(`.sm-sprint-fill.fill-${n}\n{\n\twidth: ${n * 2}px;`), `fill-${n}`);
  for (const state of ['sprinting', 'cooldown', 'ready']) assert.ok(css.includes(`.sm-sprint.${state} .sm-sprint-fill`), state);
  assert.ok(css.includes('.sm-sprint.shown.faded'));
  assert.ok(css.includes('transition-property: width;'));
  assert.doesNotMatch(css, /,\s*\n\s*\./); // no comma selectors
});

test('the plugin draws the Panorama bar, sends only changes, and keeps the text bar as a fallback', () => {
  const hud = plugin('SoccerModMvpPlugin.SprintHud.cs');
  assert.ok(hud.includes('internal const string SprintHudLayout = "panorama/layout/custom_game/soccermod_sprint.xml";'));
  assert.ok(hud.includes('new PanelOptions { Root = "sm_sprint", ShownClass = "shown", CaptureInput = false }'));
  assert.ok(hud.includes('panel.SetVariant(player, "sm_sprint_fill", "fill-", SprintBarView.FillStep(amount).ToString());'));
  assert.ok(hud.includes('if (state.Label != label)') && hud.includes('if (state.Faded != !show)'), 'change detection');
  assert.ok(hud.includes('AddCommand("css_sm2sprint_hud"'));
  const bar = plugin('SoccerModMvpPlugin.SprintBar.cs');
  assert.ok(bar.includes('DrawSprintHud(player, amount, active, visible, cooldownLabel);'));
  assert.ok(bar.includes('player.PrintToCenterHtml(SprintBarView.Html(amount, active, score), 1);'), 'text fallback kept');
  assert.ok(plugin('SoccerModMvpPlugin.MenuParity.cs').includes('public bool SprintHudPanorama { get; set; } = true;'));
  const main = plugin('SoccerModMvpPlugin.cs');
  assert.ok(main.indexOf('ClickMenuOnLoad(hotReload);') < main.indexOf('SprintHudOnLoad();'), 'after UIKit.Init');
});

test('sprint state and percentage sit centred above the bar', () => {
  const panorama = new URL('../src/workshop-addon/soccermod_menu/panorama/', import.meta.url);
  const xml = fs.readFileSync(new URL('layout/custom_game/soccermod_sprint.xml', panorama), 'utf8');
  const css = fs.readFileSync(new URL('styles/custom_game/soccermod_sprint.css', panorama), 'utf8').replace(/\r\n/g, '\n');
  assert.ok(xml.indexOf('sm_sprint_label') < xml.indexOf('sm-sprint-track'), 'label before (above) the bar');
  assert.ok(!xml.includes('sm-sprint-spacer'));
  assert.ok(css.includes('\tflow-children: down;'));
  assert.ok(css.includes('.sm-sprint-label\n{\n\twidth: 100%;\n\theight: fit-children;\n\thorizontal-align: center;'));
  assert.ok(css.includes('\ttext-align: center;'));
  const hud = fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.SprintHud.cs'), 'utf8');
  assert.ok(hud.includes('var label = SprintBarView.HudLabel(amount, active, full, cooldownLabel);'));
});

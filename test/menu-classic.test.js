import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const menuSourcePath = new URL("../src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.Menu.cs", import.meta.url);
const configSourcePath = new URL("../src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.Config.cs", import.meta.url);
const addonRoot = new URL("../src/workshop-addon/soccermod_classic_ui/", import.meta.url);

test("classic menu keeps the plain renderer as a readiness-gated fallback", async () => {
  const [menuSource, configSource] = await Promise.all([
    readFile(menuSourcePath, "utf8"),
    readFile(configSourcePath, "utf8"),
  ]);

  assert.match(menuSource, /UseClassicMenuRenderer =>\s*\n\s*_menuRenderMode == MenuRenderMode\.Classic && _classicHudReady/);
  assert.match(menuSource, /EffectiveMenuRenderMode =>[\s\S]*MenuRenderMode\.Plain/);
  assert.match(menuSource, /css_sm2menu_classic_ready/);
  assert.match(configSource, /public string\? MenuRenderMode/);
  assert.match(configSource, /Enum\.TryParse<MenuRenderMode>/);
});

test("main menu exposes the current match, cap and administration branches", async () => {
  const source = await readFile(menuSourcePath, "utf8");
  const mainMenu = source.slice(source.indexOf("private void OpenMainMenu"), source.indexOf("private void OpenHelpMenu"));
  const labels = [...mainMenu.matchAll(/menu\.Add\("([^"]+)"/g)].map((match) => match[1]);

  assert.deepEqual(labels, [
    "Admin",
    "Match",
    "Reload Map",
    "Cap",
    "Ranking",
    "Statistics",
    "Positions",
    "Help",
    "Settings",
    "Credits",
  ]);
  assert.doesNotMatch(source, /menu\.Add\("Back"/);
});

test("classic renderer reserves SourceMod navigation keys", async () => {
  const source = await readFile(menuSourcePath, "utf8");
  assert.match(source, /BackKey => UsesClassicKeys \? 8/);
  assert.match(source, /NextKey => UsesClassicKeys \? 9/);
  assert.match(source, /MenuClassicPageCapacity = 7/);

  const layout = await readFile(new URL("panorama/layout/custom_game/soccermod_classic_menu.xml", addonRoot), "utf8");
  for (let key = 1; key <= 9; key += 1) {
    assert.match(layout, new RegExp(`id="line_${key}"`));
    assert.match(layout, new RegExp(`text="${key}\\."`));
  }
  assert.match(layout, /text="0\."/);
  assert.match(layout, /text="Exit"/);
});

test("companion HUD bridge is per-player, non-capturing, and acknowledges readiness", async () => {
  const script = await readFile(new URL("maps/scripts/soccermod_classic_menu.js", addonRoot), "utf8");
  assert.match(script, /OnScriptInput\("Apply"/);
  assert.doesNotMatch(script, /RegisterCheatCommand/);
  assert.match(script, /SetDialogVariableStringForPlayer/);
  assert.match(script, /SetHasClassForPlayer/);
  assert.match(script, /SetInputCaptureEnabled\(playerSlot, false\)/);
  assert.match(script, /ServerCommand\("css_sm2menu_classic_ready"\)/);
});

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
    "Cap",
    "Training",
    "Referee",
    "Settings",
    "ELO Ranking",
    "Statistics",
    "Reload Map",
    "Positions",
    "Calls",
    "Help",
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

test("admin menu order: Match first, Reload Map sixth, Ball last", async () => {
  const source = await readFile(menuSourcePath, "utf8");
  const adminMenu = source.slice(source.indexOf("private void OpenAdminMenu"), source.indexOf("private void OnAdminMenuCommand"));
  const labels = [...adminMenu.matchAll(/menu\.Add\("([^"]+)"/g)].map((match) => match[1]);
  assert.deepEqual(labels, ["Match", "Referee", "Training", "Settings", "Player Promotion", "Reload Map", "Ball"]);
});

test("public access (CAP / Match) opens match, training, referee and map reload to everyone", async () => {
  const source = await readFile(menuSourcePath, "utf8");
  const mainMenu = source.slice(source.indexOf("private void OpenMainMenu"), source.indexOf("private void OpenHelpMenu"));
  assert.match(mainMenu, /var publicControl = !hasAdmin && HasPublicControl\(player\);/);
  assert.match(mainMenu, /if \(HasPublicControl\(player\)\) menu\.Add\("Reload Map"/);
  const dir = new URL("../src/server-plugin/SoccerModMvp/", import.meta.url);
  const training = await readFile(new URL("SoccerModMvpPlugin.Training.cs", dir), "utf8");
  const referee = await readFile(new URL("SoccerModMvpPlugin.Referee.cs", dir), "utf8");
  const match = await readFile(new URL("SoccerModMvpPlugin.Match.cs", dir), "utf8");
  assert.match(training, /"admin"\) \|\| HasPublicControl\(player\);/);
  assert.match(referee, /HasFlag\(player\.AuthorizedSteamID\?\.SteamId64 \?\? 0, "match"\) \|\| HasPublicControl\(player\)/);
  const reload = match.slice(match.indexOf("private void OnMapReloadCommand"));
  assert.ok(reload.slice(0, 300).includes("if (!RequirePublicControl(player)) return;"));
});

test("public access has two levels: Admins and CAP / Match", async () => {
  const dir = new URL("../src/server-plugin/SoccerModMvp/", import.meta.url);
  const menu = await readFile(menuSourcePath, "utf8");
  const parity = await readFile(new URL("SoccerModMvpPlugin.MenuParity.cs", dir), "utf8");
  const rules = await readFile(new URL("SoccerModMvpPlugin.MatchRules.cs", dir), "utf8");
  assert.doesNotMatch(menu, /"Free for all"\s*}/);
  assert.match(menu, /s\.PublicAccess = s\.PublicAccess >= 1 \? 0 : 1/);
  assert.match(parity, /Math\.Clamp\(_menuParity\.PublicAccess, 0, 1\)/);
  assert.match(rules, /HasFlag\(player\.AuthorizedSteamID\?\.SteamId64 \?\? 0, "match"\) \|\| \(!settings && _menuParity\.PublicAccess >= 1\)/);
});

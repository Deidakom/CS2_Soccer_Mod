import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const menu = await readFile(new URL("../src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.Menu.cs", import.meta.url), "utf8");
const cap = await readFile(new URL("../src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.Cap.cs", import.meta.url), "utf8");
const config = await readFile(new URL("../deploy/client/soccermod_menu.cfg", import.meta.url), "utf8");

test("spectator setup disables raw-digit capture while retaining matching menu binds", () => {
  assert.match(menu, /SpectatorMenuKeysCommand = "spec_usenumberkeys_nobinds 0"/);
  assert.match(config, /^spec_usenumberkeys_nobinds 0$/m);
  for (let key = 0; key <= 9; key++) assert.match(config, new RegExp(`^bind ${key} css_${key}\\r?$`, "m"));
  assert.match(menu, /AddCommand\("css_menukeys"/);
  assert.match(menu, /_spectatorMenuHintShownBySlot.Remove\(slot\)/);
});

test("menu dispatch and CAP administration do not require a living playing-team pawn", () => {
  const dispatch = menu.slice(menu.indexOf("private HookResult OnMenuNumberKey"), menu.indexOf("private int NormalizePageIndex"));
  assert.doesNotMatch(dispatch, /IsEligiblePlayer|IsAlive|PlayerPawn|Team\s*[!=]=/);
  assert.match(dispatch, /option.OnSelect\(player\)/);
  const action = cap.slice(cap.indexOf("private void CapMenuAction"), cap.indexOf("private void OpenCapWeaponMenu"));
  assert.doesNotMatch(action, /IsEligiblePlayer|IsAlive|PlayerPawn|Team\s*[!=]=/);
  assert.match(action, /action\(player\)/);
  assert.match(action, /Server.NextFrame\(/);
  assert.match(action, /OpenCapMenu\(player\)/);
});

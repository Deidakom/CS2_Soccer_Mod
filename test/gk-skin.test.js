import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const gkSource = fs.readFileSync(
  path.join(root, 'src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.GkSkin.cs'),
  'utf8',
);
const teamColorSource = fs.readFileSync(
  path.join(root, 'src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.TeamColor.cs'),
  'utf8',
);
const mainSource = fs.readFileSync(
  path.join(root, 'src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.cs'),
  'utf8',
);

test('goalkeeper skin is a permissionless one-slot-per-team toggle', () => {
  assert.match(gkSource, /Dictionary<CsTeam, int> _gkSlotByTeam/);
  assert.match(gkSource, /css_sm2gk/);
  assert.match(gkSource, /css_gk/);
  assert.doesNotMatch(gkSource, /RequirePermission/);
  assert.match(gkSource, /only one goalkeeper skin allowed per team/);
  assert.match(gkSource, /_gkSlotByTeam\[team\] = player\.Slot/);
  assert.match(gkSource, /_gkSlotByTeam\.Remove\(team\)/);
});

test('goalkeeper colors override only an enabled team tint', () => {
  assert.match(gkSource, /Color\.FromArgb\(170, 170, 170\)/);
  assert.match(gkSource, /Color\.FromArgb\(255, 140, 0\)/);
  assert.match(teamColorSource, /var isGk = IsGkSlot\(player\.Slot, player\.Team\)/);
  assert.match(teamColorSource, /var color = !_teamColorEnabled[\s\S]*Color\.White[\s\S]*GkRenderColor\(player\.Team\)/);
  assert.match(teamColorSource, /pawn\.Render = Color\.FromArgb\(renderAlpha, color\.R, color\.G, color\.B\)/);
  assert.match(teamColorSource, /SetStateChanged\(pawn, "CBaseModelEntity", "m_clrRender"\)/);
});

test('goalkeeper slots are released on team change and disconnect', () => {
  assert.match(gkSource, /RegisterEventHandler<EventPlayerTeam>\(OnGkSkinPlayerTeam\)/);
  assert.match(gkSource, /@event\.Oldteam/);
  assert.match(mainSource, /RegisterListener<Listeners\.OnClientDisconnect>\(GkSkinOnPlayerDisconnect\)/);
  assert.match(gkSource, /reason=team_change/);
  assert.match(gkSource, /reason=disconnect/);
});

import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const moduleSource = fs.readFileSync(
  path.join(root, 'src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.ThirdPerson.cs'),
  'utf8',
);
const mainSource = fs.readFileSync(
  path.join(root, 'src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.cs'),
  'utf8',
);
const socialSource = fs.readFileSync(
  path.join(root, 'src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.Social.cs'),
  'utf8',
);

// 2026-09-09: a front-facing `!tpf` variant was tried, went through two
// failed fixes, and was removed at the user's request ("delete the !tpf
// command, it doesn't work") rather than attempting a third. This suite
// covers only the remaining `!tp` rear camera; asserting !tpf is gone stays
// below so it cannot silently come back through a merge/rebase.

test('third person uses one camera prop per opted-in slot and resets it cleanly', () => {
  assert.match(moduleSource, /HashSet<int> _thirdPersonSlots/);
  assert.match(moduleSource, /Dictionary<int, CDynamicProp> _thirdPersonCamBySlot/);
  assert.match(moduleSource, /CreateEntityByName<CDynamicProp>\("prop_dynamic"\)/);
  assert.match(moduleSource, /camProp\.DispatchSpawn\(\)/);
  assert.match(moduleSource, /ViewEntity\.Raw = camProp\.EntityHandle\.Raw/);
  assert.match(moduleSource, /ViewEntity\.Raw = uint\.MaxValue/);
  assert.equal(
    moduleSource.match(/SetStateChanged\(pawn, "CBasePlayerPawn", "m_pCameraServices"\)/g)?.length,
    2,
  );
  assert.match(moduleSource, /camProp\.AcceptInput\("Kill"\)/);
});

test('!tp is permissionless and never touches player weapons', () => {
  assert.match(moduleSource, /css_sm2thirdperson/);
  assert.match(moduleSource, /css_tp/);
  assert.match(socialSource, /!tp - toggle your third-person camera/);
  const toggle = moduleSource.slice(moduleSource.indexOf("private void OnThirdPersonToggleCommand"), moduleSource.indexOf("private bool AttachThirdPersonCamera"));
  assert.doesNotMatch(toggle, /RequirePermission/);
  const tuning = moduleSource.slice(moduleSource.indexOf("private void OnThirdPersonTuneCommand"), moduleSource.indexOf("private void ThirdPersonOnUnload"));
  assert.match(tuning, /RequirePermission/);
  assert.doesNotMatch(moduleSource, /RemoveWeapons|GiveNamedItem|WeaponServices|GivePlayerItem/);
});

test('third-person camera follows smoothly from pawn eye position and angles', () => {
  assert.match(moduleSource, /DefaultThirdPersonDistance = 100\.0f/);
  assert.match(moduleSource, /DefaultThirdPersonHeight = 16\.0f/);
  assert.match(moduleSource, /ThirdPersonSmoothingFactor = 0\.4f/);
  assert.match(moduleSource, /pawn\.ViewOffset/);
  assert.match(moduleSource, /pawn\.V_angle/);
  assert.match(moduleSource, /LerpThirdPersonPosition/);
  assert.match(moduleSource, /camProp\.Teleport\(smoothedPosition, targetAngles, new Vector\(\)\)/);
});

test('third-person lifecycle is wired for load, tick, respawn, disconnect, and unload', () => {
  assert.match(mainSource, /ThirdPersonOnLoad\(\)/);
  assert.match(mainSource, /ThirdPersonOnTick\(\)/);
  assert.match(mainSource, /ThirdPersonOnPlayerSpawn\(player\)/);
  assert.match(mainSource, /ThirdPersonReassertAfterSpawn\(player\)/);
  assert.match(mainSource, /RegisterListener<Listeners\.OnClientDisconnect>\(ThirdPersonOnPlayerDisconnect\)/);
  assert.match(mainSource, /ThirdPersonOnUnload\(\)/);
});

test('!tpf is fully removed', () => {
  assert.doesNotMatch(moduleSource, /tpf|ThirdPersonFront|FrontToggle|FrontFacing|FrontSlots|AbsRotation/i);
  assert.doesNotMatch(socialSource, /tpf/i);
});

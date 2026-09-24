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

test('the third-person camera prop is invisible to every player', () => {
  const attach = moduleSource.slice(
    moduleSource.indexOf('private bool AttachThirdPersonCamera'),
    moduleSource.indexOf('private void DisableThirdPerson'),
  );
  assert.ok(
    attach.indexOf('HideThirdPersonCamera(camProp)') > attach.indexOf('camProp.DispatchSpawn()'),
    'the camera is hidden right after it spawns',
  );
  const hide = moduleSource.slice(moduleSource.indexOf('private static void HideThirdPersonCamera'));
  assert.match(hide, /RenderMode = RenderMode_t\.kRenderTransAlpha/);
  assert.match(hide, /Render = Color\.FromArgb\(0, 255, 255, 255\)/);
  assert.match(hide, /SetStateChanged\(camProp, "CBaseModelEntity", "m_nRenderMode"\)/);
  assert.match(hide, /SetStateChanged\(camProp, "CBaseModelEntity", "m_clrRender"\)/);
  // EF_NODRAW would stop the prop being networked and break the owner's view.
  assert.doesNotMatch(moduleSource.replace(/\/\/.*$/gm, ''), /EF_NODRAW|\.Effects\b/);
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

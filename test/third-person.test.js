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

test('!tp is permissionless and never touches player weapons', () => {
  assert.match(moduleSource, /css_sm2thirdperson/);
  assert.match(moduleSource, /css_tp/);
  assert.match(socialSource, /!tp - toggle your third-person camera/);
  assert.doesNotMatch(moduleSource, /RequirePermission/);
  assert.doesNotMatch(moduleSource, /RemoveWeapons|GiveNamedItem|WeaponServices|GivePlayerItem/);
});

test('third-person camera follows smoothly from pawn eye position and angles', () => {
  assert.match(moduleSource, /ThirdPersonDistance = 110\.0f/);
  assert.match(moduleSource, /ThirdPersonHeight = 70\.0f/);
  assert.match(moduleSource, /ThirdPersonSmoothingFactor = 0\.4f/);
  assert.match(moduleSource, /pawn\.ViewOffset/);
  assert.match(moduleSource, /pawn\.EyeAngles/);
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

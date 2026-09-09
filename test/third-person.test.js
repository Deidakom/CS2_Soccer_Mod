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
  assert.match(moduleSource, /css_sm2thirdperson_front/);
  assert.match(moduleSource, /css_tpf/);
  assert.match(socialSource, /!tp - toggle your third-person camera/);
  assert.match(socialSource, /!tpf - view your player from the front/);
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
  assert.match(moduleSource, /camProp\.Teleport\(smoothedPosition, angles, new Vector\(\)\)/);
});

test('!tpf orbits on the shared view yaw, never on body rotation, and shares the third-person lifecycle', () => {
  assert.match(moduleSource, /_thirdPersonFrontSlots/);
  assert.match(moduleSource, /frontFacing/);
  assert.match(moduleSource, /front-facing third-person camera/);

  // The transform must not derive the camera's POSITION from AbsRotation -
  // the live bug this fix addresses. Diagnostic logging is allowed to read
  // AbsRotation for comparison (thirdperson_front_on/_sample), so scope the
  // assertion to the transform function itself rather than the whole file.
  const transformStart = moduleSource.indexOf('private bool TryGetThirdPersonCameraTransform');
  const transformEnd = moduleSource.indexOf('private static QAngle LookAtAngles');
  assert.ok(transformStart > -1 && transformEnd > transformStart, 'TryGetThirdPersonCameraTransform not found');
  const transformBody = moduleSource.slice(transformStart, transformEnd);
  assert.doesNotMatch(transformBody, /AbsRotation/);
  assert.match(transformBody, /flatForward = new Vector\(MathF\.Cos\(yawRadians\), MathF\.Sin\(yawRadians\), 0\.0f\)/);
  assert.match(transformBody, /Trace\.TraceEndShape\(/);
  assert.match(transformBody, /IsStaticWallSurface\(wallTrace\)/);
  assert.match(transformBody, /Masks\.Solid/);

  // Look-at angles are computed from the camera's ACTUAL (smoothed)
  // position, not the raw orbit target - the other half of the flicking fix.
  assert.match(moduleSource, /LookAtAngles\(smoothedPosition, lookAtTarget\)/);
  assert.match(moduleSource, /LookAtAngles\(position, lookAtTarget\)/);
  assert.match(moduleSource, /MathF\.Atan2\(-toFace\.Z, horizontalDistance\)/);

  // A trail proving/disproving the root cause must be in the journal.
  assert.match(moduleSource, /thirdperson_front_on/);
  assert.match(moduleSource, /thirdperson_front_sample/);
});

test('!tp switches !tpf to the rear camera instead of turning third person off', () => {
  const toggle = moduleSource.slice(
    moduleSource.indexOf('private void OnThirdPersonToggleCommand'),
    moduleSource.indexOf('private void OnThirdPersonFrontToggleCommand'),
  );
  assert.match(toggle, /_thirdPersonFrontSlots\.Contains\(player\.Slot\)/);
  assert.match(toggle, /_thirdPersonFrontSlots\.Remove\(player\.Slot\)/);
  assert.match(toggle, /third-person camera: rear/);
  assert.match(toggle, /third-person camera: off/);
});

test('third-person lifecycle is wired for load, tick, respawn, disconnect, and unload', () => {
  assert.match(mainSource, /ThirdPersonOnLoad\(\)/);
  assert.match(mainSource, /ThirdPersonOnTick\(\)/);
  assert.match(mainSource, /ThirdPersonOnPlayerSpawn\(player\)/);
  assert.match(mainSource, /ThirdPersonReassertAfterSpawn\(player\)/);
  assert.match(mainSource, /RegisterListener<Listeners\.OnClientDisconnect>\(ThirdPersonOnPlayerDisconnect\)/);
  assert.match(mainSource, /ThirdPersonOnUnload\(\)/);
});

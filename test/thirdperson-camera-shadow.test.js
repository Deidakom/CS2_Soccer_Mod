import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

const source = fs.readFileSync(new URL('../src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.ThirdPerson.cs', import.meta.url), 'utf8');

test('camera fallback rendering is suppressed before spawn and before attachment, including reuse', () => {
  const attach = source.slice(source.indexOf('private bool AttachThirdPersonCamera'), source.indexOf('private static void SuppressThirdPersonCameraRendering'));
  const spawn = attach.indexOf('camProp.DispatchSpawn()');
  const view = attach.indexOf('SetThirdPersonView(pawn, cameraServices, camProp)');
  assert.ok(attach.indexOf('SuppressThirdPersonCameraRendering(camProp)') < spawn);
  assert.ok(attach.lastIndexOf('SuppressThirdPersonCameraRendering(camProp)') > spawn);
  assert.ok(attach.lastIndexOf('SuppressThirdPersonCameraRendering(camProp)') < view);
  assert.match(attach, /SetStateChanged\(camProp, "CBaseModelEntity", "m_nRenderMode"\)/);
  assert.match(attach, /SetStateChanged\(camProp, "CBaseModelEntity", "m_flShadowStrength"\)/);
});

test('suppression affects only camera rendering and preserves client view transmission and cleanup', () => {
  const helper = source.slice(source.indexOf('private static void SuppressThirdPersonCameraRendering'), source.indexOf('private void DisableThirdPerson'));
  assert.match(helper, /camProp\.RenderMode = RenderMode_t\.kRenderNone/);
  assert.match(helper, /camProp\.ShadowStrength = 0\.0f/);
  assert.doesNotMatch(helper, /pawn\.|\.Effects\s*[|&]?=|CheckTransmit|TransmitEntities/);
  assert.match(source, /cameraServices\.ViewEntity\.Raw = camProp\.EntityHandle\.Raw/);
  assert.match(source, /cameraServices\.ViewEntity\.Raw = uint\.MaxValue/);
  assert.match(source, /if \(recreate\)\s*\{\s*RemoveThirdPersonCamera\(player\.Slot\)/);
  assert.match(source, /camProp\.AcceptInput\("Kill"\)/);
});

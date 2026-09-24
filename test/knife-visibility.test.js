import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const moduleSource = fs.readFileSync(
  path.join(root, 'src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.KnifeVisibility.cs'),
  'utf8',
);
const mainSource = fs.readFileSync(
  path.join(root, 'src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.cs'),
  'utf8',
);

test('knife world models are made fully transparent and shadowless', () => {
  assert.match(moduleSource, /RenderMode_t\.kRenderTransAlpha/);
  assert.match(moduleSource, /Color\.FromArgb\(0, 255, 255, 255\)/);
  assert.match(moduleSource, /ShadowStrength = 0\.0f/);
  assert.match(moduleSource, /SetStateChanged\(knife, "CBaseModelEntity", "m_clrRender"\)/);
  assert.match(moduleSource, /Contains\("knife", StringComparison\.OrdinalIgnoreCase\)/);
});

test('knife hiding runs from the tick loop and never uses CheckTransmit', () => {
  assert.match(mainSource, /KnifeVisibilityOnTick\(\);/);
  assert.doesNotMatch(moduleSource, /CheckTransmit\(|TransmitEntities/);
});

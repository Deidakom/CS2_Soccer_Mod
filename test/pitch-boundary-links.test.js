import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => fs.readFileSync(path.join(root, 'src/server-plugin/SoccerModMvp', name), 'utf8');

test('players are held inside the touchline, including the halfway railing gaps', () => {
  const boundary = read('SoccerModMvpPlugin.PitchBoundary.cs');
  assert.match(boundary, /var limit = FoundationWallPlaneX - PitchBoundaryPlayerRadius;/);
  assert.match(boundary, /Math\.Abs\(origin\.Y\) > PitchBoundaryHalfLength/);
  assert.match(read('SoccerModMvpPlugin.cs'), /PitchBoundaryOnTick\(\);/);
  // Only while a match runs; in warmup or after a match players may leave.
  assert.ok(boundary.includes('if (!PitchBoundaryActive) return;'));
  const active = boundary.split('PitchBoundaryActive =>')[1].split(';')[0];
  assert.ok(active.includes('MatchPhase.Live') && !active.includes('Warmup') && !active.includes('Finished'));
});

test('the kickoff ground line lies on the pitch', () => {
  const outline = read('SoccerModMvpPlugin.KickoffOutline.cs');
  assert.match(outline, /new\[\] \{ StadiumPitchPlaneZ \+ 1f, centre\.Z \+ 110f \}/);
});

test('!links prints only the served Workshop item, to the console', () => {
  const links = read('SoccerModMvpPlugin.Links.cs');
  assert.match(links, /filedetails\/\?id=3797479770/);
  assert.doesNotMatch(links, /"https:[^"]*(3807367334|3807366566)/);
  assert.match(links, /PrintToConsole\(url\)/);
  assert.match(read('SoccerModMvpPlugin.cs'), /LinksOnLoad\(\);/);
});

test('!tp is removed entirely (its camera showed as an ERROR model to others)', () => {
  assert.equal(fs.existsSync(path.join(root, 'src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.ThirdPerson.cs')), false);
  const all = fs.readdirSync(path.join(root, 'src/server-plugin/SoccerModMvp')).filter((f) => f.endsWith('.cs')).map(read).join('\n');
  assert.doesNotMatch(all, /css_tp\b|css_sm2thirdperson|ThirdPersonOn/);
});

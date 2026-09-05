import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const root = new URL("../src/server-plugin/SoccerModMvp/", import.meta.url);
const [match, outline, main] = await Promise.all(
  ["Match.cs", "KickoffOutline.cs", "cs"].map(suffix => readFile(
    new URL(suffix === "cs" ? "SoccerModMvpPlugin.cs" : `SoccerModMvpPlugin.${suffix}`, root), "utf8")),
);

test("kickoff lifecycle has no elapsed-time release and repairs round-deleted visuals", () => {
  const arm = match.slice(match.indexOf("private void StartKickoffRestriction"), match.indexOf("private void UpdateTeamScoreboard"));
  assert.doesNotMatch(arm, /AddTimer|ExpiresAt|reason=timeout|KickoffWallTimeout/);
  assert.match(arm, /Server\.NextFrame\(DrawKickoffOutline\)/);
  assert.match(main, /MatchOnTick\(\);\s*MaintainKickoffOutline\(\);/);
  assert.match(outline, /_kickoffBeams\.Count != KickoffOutlineSegmentCount \|\| _kickoffBeams\.Any\(beam => !beam\.IsValid\)/);
  // A queued round callback must consult current state, not capture active=true.
  assert.match(outline, /private void DrawKickoffOutline\(\)\s*\{\s*ClearKickoffOutline\(\);\s*if \(!_kickoffRestrictionActive \|\| !_menuParity.KickoffOutline\) return;/);
});

test("kickoff outline sits on the measured grass plane without changing wall height", () => {
  assert.match(outline, /KickoffOutlineHeight = 102\.0f/);
  assert.match(outline, /KickoffOutlineBottomZ = StadiumPitchPlaneZ \+ 1\.0f/);
  assert.match(outline, /new\[\] \{ KickoffOutlineBottomZ, KickoffOutlineBottomZ \+ KickoffOutlineHeight \}/);
  assert.doesNotMatch(outline, /centre\.Z \+ height/);
});

test("actual kickoff activation releases the same restriction as player contact", () => {
  const activate = match.slice(match.indexOf("private void ActivateKickoffClock"), match.indexOf("// Called from UpdateDerivedMotion"));
  assert.match(activate, /CompleteKickoffRestriction\("ball_activity"\)/);
  assert.match(match, /CompleteKickoffRestriction\("player_touch"\)/);
});

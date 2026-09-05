import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const mainSource = readFileSync(
  new URL("../src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.cs", import.meta.url),
  "utf8",
);
const matchSource = readFileSync(
  new URL("../src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.Match.cs", import.meta.url),
  "utf8",
);
const menuSource = readFileSync(
  new URL("../src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.Menu.cs", import.meta.url),
  "utf8",
);
const touchSource = readFileSync(
  new URL("../src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.GkAreas.cs", import.meta.url),
  "utf8",
);

test("jump-over parity uses a narrow assist without desynchronizing ball rendering and collision", () => {
  const jumpSource = readFileSync(
    new URL("../src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.DuckJumpBlock.cs", import.meta.url),
    "utf8",
  );
  assert.match(mainSource, /BallCollisionRadius = 18\.805f/);
  assert.doesNotMatch(mainSource, /BallModelScale/);
  assert.match(jumpSource, /BallJumpAssistRange = 120\.0f/);
  assert.match(jumpSource, /BallJumpAssistTargetVerticalSpeed = 325\.0f/);
  assert.match(jumpSource, /Server\.NextFrame\(\(\) => ApplyBallJumpAssist\(player\)\)/);
});

test("goals suppress respawning before punishment and restore it before the kickoff restart", () => {
  const pause = matchSource.slice(matchSource.indexOf("case MatchPhase.GoalPause:"), matchSource.indexOf("case MatchPhase.PeriodBreak:"));
  assert.ok(pause.indexOf("RestoreGoalRespawnCvars()") >= 0);
  assert.ok(pause.indexOf("RestoreGoalRespawnCvars()") < pause.indexOf('Server.ExecuteCommand("mp_restartgame 1")'));
  assert.match(pause, /if \(!_nativeGoalRestartPending\)/);
  assert.doesNotMatch(pause, /PunishConcedingTeam/);
  const goal = matchSource.slice(matchSource.indexOf("private void OnGoalScored"), matchSource.indexOf("private void HandleWarmupGoal"));
  assert.ok(goal.indexOf("SetRespawnOnDeathCvars(false)") >= 0);
  assert.ok(goal.indexOf("SetRespawnOnDeathCvars(false)") < goal.indexOf("PunishConcedingTeam(concedingTeam)"));
});

test("fresh CS2 installations use the KICKOFF ten-minute half default", () => {
  assert.match(matchSource, /DefaultPeriodLengthSeconds = 600\.0f/);
});

test("manual match start honors the website cap reference and otherwise uses the default", () => {
  assert.match(menuSource, /TryGetWebsiteCapReference\(out var capHalfSeconds\) \? capHalfSeconds : _periodLengthSeconds/);
  assert.match(menuSource, /StartMatch\(halfSeconds, capHalfSeconds > 0\.0f \? "cap_reference" : "default"\)/);
  assert.match(matchSource, /StartMatch\(capHalfSeconds, "cap_reference"\)/);
  assert.match(matchSource, /StartMatch\(_periodLengthSeconds, "default"\)/);
  assert.match(matchSource, /_pausedRemainingSeconds = _activePeriodLengthSeconds/);
});

test("kickoff clock waits for real ball activity and preserves remaining time", () => {
  assert.match(matchSource, /KickoffBallActivePlanarSpeed = 5\.0f/);
  assert.match(matchSource, /_kickoffClockWaitingForBall/);
  assert.match(matchSource, /_periodEndsAtServerTime = Server\.TickedTime \+ _pausedRemainingSeconds/);
  assert.match(matchSource, /planarSpeed >= KickoffBallActivePlanarSpeed \|\| _ball\?\.TouchedByPlayer == true/);
  assert.match(touchSource, /MatchOnBallActivity\("player_touch"\)/);
  assert.match(matchSource, /Math\.Max\(0\.0, _periodEndsAtServerTime - Server\.TickedTime\)/);
  assert.match(matchSource, /WAITING FOR BALL/);
});

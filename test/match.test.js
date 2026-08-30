import assert from "node:assert/strict";
import test from "node:test";

import {
  MatchPhase,
  advanceMatchState,
  createMatchState,
  formatMatchStatus,
  matchAllowsBallInteraction,
  matchCountsGoals,
  pauseMatch,
  recordMatchGoal,
  resumeMatch,
  startMatch,
  stopMatch,
} from "../src/ball-lab/core/match.js";

const config = Object.freeze({
  durationSeconds: 60,
  scoreLimit: 2,
  countdownSeconds: 3,
  goalPauseSeconds: 2,
});

test("match progresses from warmup through countdown and live play", () => {
  const warmup = createMatchState({ now: 10, config });
  assert.equal(matchAllowsBallInteraction(warmup), true);
  assert.equal(matchCountsGoals(warmup), false);

  const started = startMatch(warmup, 10);
  assert.equal(started.accepted, true);
  assert.equal(started.state.phase, MatchPhase.COUNTDOWN);
  assert.equal(matchAllowsBallInteraction(started.state), false);

  const waiting = advanceMatchState(started.state, 12.9);
  assert.equal(waiting.state.phase, MatchPhase.COUNTDOWN);
  const live = advanceMatchState(waiting.state, 14);
  assert.equal(live.state.phase, MatchPhase.LIVE);
  assert.equal(live.state.remainingSeconds, 59);
  assert.equal(matchAllowsBallInteraction(live.state), true);
  assert.equal(matchCountsGoals(live.state), true);
});

test("live goals score once, pause, resume, and finish at the score limit", () => {
  const initial = createMatchState({ now: 0, config: { ...config, countdownSeconds: 0 } });
  const live = startMatch(initial, 0).state;
  const first = recordMatchGoal(live, 2, 5);
  assert.equal(first.accepted, true);
  assert.equal(first.reason, "goal");
  assert.deepEqual(first.state.scores, { 2: 1, 3: 0 });
  assert.equal(first.state.phase, MatchPhase.GOAL_PAUSE);
  assert.equal(recordMatchGoal(first.state, 2, 6).reason, "match_not_live");

  const resumed = advanceMatchState(first.state, 7).state;
  assert.equal(resumed.phase, MatchPhase.LIVE);
  const winner = recordMatchGoal(resumed, 2, 8);
  assert.equal(winner.reason, "score_limit");
  assert.equal(winner.state.phase, MatchPhase.FINISHED);
  assert.equal(winner.state.winnerTeam, 2);
  assert.equal(matchAllowsBallInteraction(winner.state), false);
});

test("match clock pauses without consuming time and can return to warmup", () => {
  const initial = createMatchState({ now: 0, config: { ...config, countdownSeconds: 0 } });
  const live = startMatch(initial, 0).state;
  const paused = pauseMatch(live, 10);
  assert.equal(paused.state.phase, MatchPhase.PAUSED);
  assert.equal(paused.state.remainingSeconds, 50);

  const resumed = resumeMatch(paused.state, 30);
  assert.equal(resumed.state.phase, MatchPhase.LIVE);
  assert.equal(resumed.state.remainingSeconds, 50);
  const stopped = stopMatch(resumed.state, 31);
  assert.equal(stopped.state.phase, MatchPhase.WARMUP);
  assert.equal(stopped.state.remainingSeconds, 60);
});

test("time limit resolves a winner or draw and invalid input fails closed", () => {
  const initial = createMatchState({ now: 0, config: { ...config, countdownSeconds: 0 } });
  const live = startMatch(initial, 0).state;
  const scored = recordMatchGoal(live, 3, 1).state;
  const resumed = advanceMatchState(scored, 3).state;
  const finished = advanceMatchState(resumed, 62);
  assert.equal(finished.reason, "time_limit");
  assert.equal(finished.state.phase, MatchPhase.FINISHED);
  assert.equal(finished.state.winnerTeam, 3);
  assert.equal(recordMatchGoal(finished.state, 99, 62).reason, "invalid_team");
  assert.equal(advanceMatchState(finished.state, 61).reason, "invalid_time");
});

test("status text exposes phase, score, and bounded match time", () => {
  const state = createMatchState({ now: 0, config });
  assert.equal(formatMatchStatus(state), "SoccerMod | warmup | 0 - 0 | 1:00");
  assert.equal(formatMatchStatus(null), "SoccerMod match state unavailable");
});

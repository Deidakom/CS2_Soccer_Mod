import assert from "node:assert/strict";
import test from "node:test";

import {
  acceptGoalCandidate,
  createGoalState,
  detectGoalPlaneCrossing as detectWithContext,
  replaceBallGeneration,
  unlockAfterVerifiedReset,
} from "../src/ball-lab/core/goal.js";
import {
  createResetCommand,
  verifyResetSettledObservation,
  verifyResetWriteObservation,
} from "../src/ball-lab/core/reset.js";
import { vector } from "../src/ball-lab/core/vector.js";

const northGoal = {
  id: "north",
  axis: "y",
  plane: 1_420,
  direction: 1,
  lateralCenter: 0,
  halfWidth: 104,
  minimumHeight: 0,
  maximumHeight: 80,
  scoringTeam: 3,
};

const southGoal = {
  ...northGoal,
  id: "south",
  plane: -1_420,
  direction: -1,
  scoringTeam: 2,
};

function crossingContext(overrides = {}) {
  return {
    ballGeneration: 1,
    resetSequence: 0,
    previousThinkSeq: 100,
    currentThinkSeq: 101,
    ...overrides,
  };
}

function detectGoalPlaneCrossing(previous, current, goal, context = crossingContext()) {
  return detectWithContext(previous, current, goal, context);
}

function verifiedReset(sequence, ballGeneration = 1) {
  const command = createResetCommand(sequence, {
    ballGeneration,
    issuedThinkSeq: 10,
  });
  const write = verifyResetWriteObservation({
    ballCount: 1,
    ballGeneration,
    resetSequence: sequence,
    sampleThinkSeq: 11,
    position: vector(0, 0, 17),
    angles: { pitch: 0, yaw: 0, roll: 0 },
    velocity: vector(0, 0, 0),
    angularMotionZero: true,
  }, command);
  return verifyResetSettledObservation({
    ballCount: 1,
    ballGeneration,
    resetSequence: sequence,
    stableThinkCount: 2,
    stableFromThinkSeq: 12,
    sampleThinkSeq: 13,
    position: vector(0, 0, 17),
    velocity: vector(0, 0, 0),
    angularMotionZero: true,
  }, command, write);
}

test("detects a forward goal-plane crossing and interpolates impact", () => {
  const result = detectGoalPlaneCrossing(
    vector(-10, 1_400, 20),
    vector(10, 1_440, 40),
    northGoal,
  );
  assert.equal(result.crossed, true);
  assert.equal(result.goalId, "north");
  assert.equal(result.lateral, 0);
  assert.equal(result.height, 30);
});

test("handles symmetric goals and high-speed movement", () => {
  assert.equal(
    detectGoalPlaneCrossing(vector(0, 0, 20), vector(0, 3_000, 20), northGoal).crossed,
    true,
  );
  const south = detectGoalPlaneCrossing(
    vector(0, -1_400, 20),
    vector(0, -1_440, 20),
    southGoal,
  );
  assert.equal(south.crossed, true);
  assert.equal(south.goalId, "south");
});

test("rejects reverse, wide, over-crossbar, and plane-origin segments", () => {
  assert.equal(
    detectGoalPlaneCrossing(vector(0, 1_430, 20), vector(0, 1_410, 20), northGoal).reason,
    "no_forward_crossing",
  );
  assert.equal(
    detectGoalPlaneCrossing(vector(105, 1_400, 20), vector(105, 1_440, 20), northGoal).reason,
    "outside_width",
  );
  assert.equal(
    detectGoalPlaneCrossing(vector(0, 1_400, 81), vector(0, 1_440, 81), northGoal).reason,
    "outside_height",
  );
  assert.equal(
    detectGoalPlaneCrossing(vector(0, 1_420, 20), vector(0, 1_440, 20), northGoal).reason,
    "no_forward_crossing",
  );
});

test("center-plane aperture includes its boundary but never expands by radius", () => {
  assert.equal(
    detectGoalPlaneCrossing(vector(104, 1_400, 80), vector(104, 1_440, 80), northGoal).crossed,
    true,
  );
  assert.equal(
    detectGoalPlaneCrossing(vector(104.001, 1_400, 20), vector(104.001, 1_440, 20), northGoal).reason,
    "outside_width",
  );
  assert.equal(
    detectGoalPlaneCrossing(
      vector(118, 1_400, 20),
      vector(118, 1_440, 20),
      { ...northGoal, radiusAllowance: 15 },
    ).reason,
    "invalid_goal",
  );
});

test("rejects malformed goal geometry", () => {
  assert.equal(
    detectGoalPlaneCrossing(
      vector(0, 1_400, 20),
      vector(0, 1_440, 20),
      { ...northGoal, halfWidth: Number.NaN },
    ).reason,
    "invalid_goal",
  );
  assert.equal(
    detectGoalPlaneCrossing(
      vector(0, 1_400, 20),
      vector(0, 1_440, 20),
      { ...northGoal, minimumHeight: 81 },
    ).reason,
    "invalid_goal",
  );
});

test("goal latch unlocks only after a matching verified reset", () => {
  const candidate = detectGoalPlaneCrossing(
    vector(0, 1_400, 20),
    vector(0, 1_440, 20),
    northGoal,
  );
  const first = acceptGoalCandidate(createGoalState(), candidate);
  assert.equal(first.accepted, true);
  assert.equal(first.state.sequence, 1);

  const duplicate = acceptGoalCandidate(first.state, candidate);
  assert.equal(duplicate.reason, "goal_locked");
  assert.equal(
    unlockAfterVerifiedReset(first.state, { passed: false }).reason,
    "reset_not_verified",
  );
  assert.equal(
    unlockAfterVerifiedReset(first.state, {
      passed: true,
      stage: "settled",
      resetSequence: 1,
      ballGeneration: 1,
    }).reason,
    "reset_not_verified",
  );

  const unlocked = unlockAfterVerifiedReset(first.state, verifiedReset(1));
  assert.equal(unlocked.unlocked, true);
  const secondCandidate = detectGoalPlaneCrossing(
    vector(0, 1_400, 20),
    vector(0, 1_440, 20),
    northGoal,
    crossingContext({ resetSequence: 1, previousThinkSeq: 200, currentThinkSeq: 201 }),
  );
  const second = acceptGoalCandidate(unlocked.state, secondCandidate);
  assert.equal(second.accepted, true);
  assert.equal(second.state.sequence, 2);
});

test("goal latch rejects stale resets and another ball generation", () => {
  const state = { ...createGoalState(), locked: true };
  const firstUnlock = unlockAfterVerifiedReset(state, verifiedReset(1));
  assert.equal(firstUnlock.unlocked, true);
  assert.equal(
    unlockAfterVerifiedReset(firstUnlock.state, verifiedReset(1)).reason,
    "stale_reset",
  );
  assert.equal(
    unlockAfterVerifiedReset(state, verifiedReset(2, 2)).reason,
    "ball_generation",
  );
  assert.equal(
    unlockAfterVerifiedReset(
      { ...state, lastResetVerifiedThinkSeq: 100 },
      verifiedReset(2),
    ).reason,
    "stale_reset",
  );
});

test("goal state rejects a candidate captured before the verified reset", () => {
  const oldCandidate = detectGoalPlaneCrossing(
    vector(0, 1_400, 20),
    vector(0, 1_440, 20),
    northGoal,
  );
  const locked = acceptGoalCandidate(createGoalState(), oldCandidate).state;
  const unlocked = unlockAfterVerifiedReset(locked, verifiedReset(1));
  assert.equal(unlocked.unlocked, true);
  assert.equal(
    acceptGoalCandidate(unlocked.state, oldCandidate).reason,
    "stale_candidate",
  );

  const currentCandidate = detectGoalPlaneCrossing(
    vector(0, 1_400, 20),
    vector(0, 1_440, 20),
    northGoal,
    crossingContext({ resetSequence: 1, previousThinkSeq: 200, currentThinkSeq: 201 }),
  );
  assert.equal(acceptGoalCandidate(unlocked.state, currentCandidate).accepted, true);

  const relabeledPreResetCandidate = detectGoalPlaneCrossing(
    vector(0, 1_400, 20),
    vector(0, 1_440, 20),
    northGoal,
    crossingContext({ resetSequence: 1, previousThinkSeq: 12, currentThinkSeq: 14 }),
  );
  assert.equal(
    acceptGoalCandidate(unlocked.state, relabeledPreResetCandidate).reason,
    "stale_candidate",
  );
});

test("ball replacement preserves goal sequence and requires a new reset", () => {
  const state = { ...createGoalState(), sequence: 7 };
  const replacement = replaceBallGeneration(state, 2);
  assert.equal(replacement.replaced, true);
  assert.equal(replacement.state.sequence, 7);
  assert.equal(replacement.state.locked, true);
  assert.equal(
    unlockAfterVerifiedReset(replacement.state, verifiedReset(1, 2)).unlocked,
    true,
  );
  assert.equal(replaceBallGeneration(replacement.state, 1).replaced, false);
});

test("extreme finite endpoints fail closed when arithmetic overflows", () => {
  const result = detectGoalPlaneCrossing(
    vector(0, -Number.MAX_VALUE, 20),
    vector(0, Number.MAX_VALUE, 20),
    northGoal,
  );
  assert.equal(result.crossed, false);
  assert.equal(result.reason, "invalid_number");
});

test("goal state rejects malformed and exhausted sequences", () => {
  const candidate = {
    crossed: true,
    reason: "crossed",
    goalId: "north",
    scoringTeam: 3,
    fraction: 0.5,
    lateral: 0,
    height: 20,
    ...crossingContext(),
  };
  assert.equal(acceptGoalCandidate({}, candidate).reason, "invalid_state");
  assert.equal(
    acceptGoalCandidate(
      { ...createGoalState(), sequence: Number.MAX_SAFE_INTEGER },
      candidate,
    ).reason,
    "invalid_state",
  );
  assert.throws(() => createGoalState(null), /options/);
});

test("goal commit rejects malformed crossed candidates", () => {
  const candidate = detectGoalPlaneCrossing(
    vector(0, 1_400, 20),
    vector(0, 1_440, 20),
    northGoal,
  );
  for (const malformed of [
    { ...candidate, reason: "accepted" },
    { ...candidate, scoringTeam: 99 },
    { ...candidate, fraction: Number.NaN },
    { ...candidate, lateral: Number.NaN },
    { ...candidate, height: Number.NaN },
  ]) {
    assert.equal(
      acceptGoalCandidate(createGoalState(), malformed).reason,
      "invalid_candidate",
    );
  }
});

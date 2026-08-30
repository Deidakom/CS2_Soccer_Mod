import assert from "node:assert/strict";
import test from "node:test";

import { computeKick } from "../src/ball-lab/core/kick.js";
import {
  acceptGoalCandidate,
  createGoalState,
  detectGoalPlaneCrossing,
  unlockAfterVerifiedReset,
} from "../src/ball-lab/core/goal.js";
import {
  createResetCommand,
  verifyResetSettledObservation,
  verifyResetWriteObservation,
} from "../src/ball-lab/core/reset.js";
import { isFiniteVector, length, vector } from "../src/ball-lab/core/vector.js";

const goal = {
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

test("pure goal gate counts 100 reset-separated crossings exactly once", () => {
  let state = createGoalState();
  let accepted = 0;

  for (let sequence = 0; sequence < 100; sequence += 1) {
    const candidate = detectGoalPlaneCrossing(
      vector(0, 1_419, 20),
      vector(0, 1_421, 20),
      goal,
      {
        ballGeneration: state.ballGeneration,
        resetSequence: state.resetSequence,
        previousThinkSeq: sequence * 10 + 1,
        currentThinkSeq: sequence * 10 + 2,
      },
    );
    const first = acceptGoalCandidate(state, candidate);
    assert.equal(first.accepted, true);
    accepted += 1;

    const duplicate = acceptGoalCandidate(first.state, candidate);
    assert.equal(duplicate.accepted, false);
    const command = createResetCommand(sequence + 1, {
      ballGeneration: first.state.ballGeneration,
      issuedThinkSeq: sequence * 10 + 3,
    });
    const write = verifyResetWriteObservation({
      ballCount: 1,
      ballGeneration: first.state.ballGeneration,
      resetSequence: sequence + 1,
      sampleThinkSeq: sequence * 10 + 4,
      position: vector(0, 0, 17),
      angles: { pitch: 0, yaw: 0, roll: 0 },
      velocity: vector(0, 0, 0),
      angularMotionZero: true,
    }, command);
    const verification = verifyResetSettledObservation({
      ballCount: 1,
      ballGeneration: first.state.ballGeneration,
      resetSequence: sequence + 1,
      stableThinkCount: 2,
      stableFromThinkSeq: sequence * 10 + 5,
      sampleThinkSeq: sequence * 10 + 6,
      position: vector(0, 0, 17),
      velocity: vector(0, 0, 0),
      angularMotionZero: true,
    }, command, write);
    const unlocked = unlockAfterVerifiedReset(first.state, verification);
    assert.equal(unlocked.unlocked, true);
    state = unlocked.state;
  }

  assert.equal(accepted, 100);
  assert.equal(state.sequence, 100);
});

test("100 varied primary-kick vectors remain finite and speed-bounded", () => {
  for (let index = 0; index < 100; index += 1) {
    const yaw = -20 + (40 * index) / 99;
    const radians = yaw * Math.PI / 180;
    const ballPosition = vector(
      Math.cos(radians) * 60,
      Math.sin(radians) * 60,
      24,
    );
    const result = computeKick({
      playerAlive: true,
      playerEligible: true,
      playEnabled: true,
      eyePosition: vector(0, 0, 64),
      eyeAngles: { pitch: 20, yaw, roll: 0 },
      ballPosition,
      ballVelocity: vector(index * 100, -index * 40, index * 10),
      attackType: "primary",
      isDucking: index % 5 === 0,
      lineOfSight: true,
      now: index + 1,
      lastAcceptedKickTime: Number.NEGATIVE_INFINITY,
    });

    assert.equal(result.accepted, true);
    assert.equal(isFiniteVector(result.velocity), true);
    assert.equal(result.writeAngularVelocity, false);
    assert.equal(Object.hasOwn(result, "angularVelocity"), false);
    assert.ok(length(result.velocity) <= 1_250 + 1e-9);
  }
});

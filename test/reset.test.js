import assert from "node:assert/strict";
import test from "node:test";

import {
  createResetCommand,
  verifyResetSettledObservation,
  verifyResetWriteObservation,
} from "../src/ball-lab/core/reset.js";
import { vector } from "../src/ball-lab/core/vector.js";

function validWriteObservation(sequence = 1, overrides = {}) {
  return {
    ballCount: 1,
    ballGeneration: 1,
    resetSequence: sequence,
    sampleThinkSeq: 11,
    position: vector(0, 0, 17),
    angles: { pitch: 0, yaw: 0, roll: 0 },
    velocity: vector(0, 0, 0),
    angularMotionZero: true,
    ...overrides,
  };
}

function validSettledObservation(sequence = 1, overrides = {}) {
  return {
    ballCount: 1,
    ballGeneration: 1,
    resetSequence: sequence,
    stableThinkCount: 2,
    stableFromThinkSeq: 12,
    sampleThinkSeq: 13,
    position: vector(0, 0, 17),
    velocity: vector(0, 0, 0),
    angularMotionZero: true,
    ...overrides,
  };
}

function verifiedWrite(command, overrides = {}, tolerances = {}) {
  return verifyResetWriteObservation(
    validWriteObservation(command.sequence, overrides),
    command,
    tolerances,
  );
}

test("reset command always clears linear and angular velocity", () => {
  const command = createResetCommand(1);
  assert.equal(command.ballGeneration, 1);
  assert.deepEqual(command.position, vector(0, 0, 17));
  assert.deepEqual(command.velocity, vector(0, 0, 0));
  assert.deepEqual(command.angularVelocity, vector(0, 0, 0));
});

test("reset verification accepts separate write and settled observations", () => {
  const command = createResetCommand(2, { issuedThinkSeq: 10 });
  const write = verifiedWrite(command, {
    position: vector(0.2, -0.1, 17.1),
    angles: { pitch: 0.1, yaw: -0.1, roll: 0 },
    velocity: vector(0.01, 0, 0),
    angularMotionZero: true,
  });
  assert.equal(write.passed, true);
  assert.equal(write.stage, "write");

  const result = verifyResetSettledObservation(
    validSettledObservation(2, {
      position: vector(0.2, -0.1, 17.1),
      velocity: vector(0.01, 0, 0),
      angularMotionZero: true,
    }),
    command,
    write,
  );

  assert.equal(result.passed, true);
  assert.equal(result.stage, "settled");
  assert.equal(result.resetSequence, 2);
  assert.deepEqual(result.reasons, []);
});

test("settled verification reports duplicates and stale motion", () => {
  const command = createResetCommand(3, { issuedThinkSeq: 10 });
  const write = verifiedWrite(command);
  const result = verifyResetSettledObservation(validSettledObservation(3, {
    ballCount: 2,
    position: vector(2, 0, 17),
    velocity: vector(1, 0, 0),
    angularMotionZero: false,
  }), command, write);

  assert.equal(result.passed, false);
  assert.deepEqual(result.reasons, [
    "ball_count",
    "position",
    "velocity",
    "angular_motion",
  ]);
});

test("settled verification requires matching identity and two post-write thinks", () => {
  const command = createResetCommand(4, { issuedThinkSeq: 10 });
  const write = verifiedWrite(command);
  const result = verifyResetSettledObservation(validSettledObservation(4, {
    ballGeneration: 2,
    stableThinkCount: 1,
    stableFromThinkSeq: 11,
    sampleThinkSeq: 11,
  }), command, write);
  assert.equal(result.passed, false);
  assert.deepEqual(result.reasons, ["ball_generation", "not_settled"]);
});

test("stale observations cannot be relabeled as a newer reset", () => {
  const command = createResetCommand(9, { issuedThinkSeq: 20 });
  const write = verifyResetWriteObservation(validWriteObservation(8, {
    sampleThinkSeq: 19,
  }), command);
  assert.equal(write.passed, false);
  assert.deepEqual(write.reasons, ["reset_sequence", "stale_sample"]);

  const settled = verifyResetSettledObservation(
    validSettledObservation(8, {
      stableFromThinkSeq: 18,
      sampleThinkSeq: 19,
    }),
    command,
    { ...write, passed: true, stage: "write", resetSequence: 9 },
  );
  assert.equal(settled.passed, false);
  assert.deepEqual(settled.reasons, ["write_not_verified"]);

  const validWrite = verifiedWrite(command, { sampleThinkSeq: 21 });
  const staleSettled = verifyResetSettledObservation(
    validSettledObservation(9, {
      stableFromThinkSeq: 20,
      sampleThinkSeq: 21,
    }),
    command,
    validWrite,
  );
  assert.equal(staleSettled.passed, false);
  assert.ok(staleSettled.reasons.includes("not_settled"));

  const lateWrite = verifyResetWriteObservation(
    validWriteObservation(9, { sampleThinkSeq: 999 }),
    command,
  );
  assert.equal(lateWrite.passed, false);
  assert.ok(lateWrite.reasons.includes("stale_sample"));
});

test("an angular-only retry preserves reset identity and starts a new write window", () => {
  const first = createResetCommand(7, {
    ballGeneration: 3,
    issuedThinkSeq: 10,
    position: vector(4, 5, 20),
    restPosition: vector(4, 5, 17),
    angles: { pitch: 1, yaw: 2, roll: 3 },
  });
  const firstSample = validWriteObservation(7, {
    ballGeneration: 3,
    sampleThinkSeq: 11,
    position: first.position,
    angles: first.angles,
    angularMotionZero: false,
  });
  const firstVerification = verifyResetWriteObservation(firstSample, first);
  assert.equal(firstVerification.passed, false);
  assert.deepEqual(firstVerification.reasons, ["angular_motion"]);

  const retry = createResetCommand(first.sequence, {
    ballGeneration: first.ballGeneration,
    issuedThinkSeq: firstSample.sampleThinkSeq,
    position: first.position,
    restPosition: first.restPosition,
    angles: first.angles,
  });
  assert.equal(retry.sequence, first.sequence);
  assert.equal(retry.ballGeneration, first.ballGeneration);
  assert.deepEqual(retry.position, first.position);
  assert.deepEqual(retry.restPosition, first.restPosition);
  assert.deepEqual(retry.angles, first.angles);
  assert.deepEqual(retry.velocity, vector(0, 0, 0));
  assert.deepEqual(retry.angularVelocity, vector(0, 0, 0));

  const staleForRetry = verifyResetWriteObservation(firstSample, retry);
  assert.equal(staleForRetry.passed, false);
  assert.deepEqual(staleForRetry.reasons, ["stale_sample", "angular_motion"]);
  const otherwiseValidStaleForRetry = verifyResetWriteObservation({
    ...firstSample,
    angularMotionZero: true,
  }, retry);
  assert.equal(otherwiseValidStaleForRetry.passed, false);
  assert.deepEqual(otherwiseValidStaleForRetry.reasons, ["stale_sample"]);

  const retryVerification = verifyResetWriteObservation({
    ...firstSample,
    sampleThinkSeq: 12,
    angularMotionZero: true,
  }, retry);
  assert.equal(retryVerification.passed, true);
  assert.deepEqual(retryVerification.reasons, []);

  const settled = verifyResetSettledObservation({
    ...validSettledObservation(7),
    ballGeneration: 3,
    stableFromThinkSeq: 13,
    sampleThinkSeq: 14,
    position: retry.restPosition,
  }, retry, retryVerification);
  assert.equal(settled.passed, true);
});

test("settled verification uses the candidate-specific rest transform", () => {
  const command = createResetCommand(6, {
    issuedThinkSeq: 10,
    position: vector(0, 0, 20),
    restPosition: vector(0, 0, 17),
  });
  const write = verifiedWrite(command, { position: vector(0, 0, 20) });
  const result = verifyResetSettledObservation(
    validSettledObservation(6),
    command,
    write,
  );
  assert.equal(result.passed, true);
});

test("reset angle verification normalizes turns and fails closed at extremes", () => {
  const turns = createResetCommand(7, {
    issuedThinkSeq: 10,
    angles: { pitch: 360, yaw: 720, roll: -360 },
  });
  const equivalent = verifiedWrite(turns, {
    angles: { pitch: 0, yaw: 0, roll: 0 },
  });
  assert.equal(equivalent.passed, true);
  assert.equal(equivalent.angleError, 0);

  const extreme = createResetCommand(8, {
    issuedThinkSeq: 10,
    angles: { pitch: 0, yaw: Number.MAX_VALUE, roll: 0 },
  });
  const rejected = verifiedWrite(extreme, {
    angles: { pitch: 0, yaw: -Number.MAX_VALUE, roll: 0 },
  });
  assert.equal(Number.isFinite(rejected.angleError), true);
  assert.equal(rejected.passed, false);
  assert.ok(rejected.reasons.includes("angles"));
});

test("invalid numeric tolerances fail closed", () => {
  const command = createResetCommand(5, { issuedThinkSeq: 10 });
  for (const tolerance of [Number.NaN, Number.POSITIVE_INFINITY, -1]) {
    const result = verifyResetWriteObservation(
      validWriteObservation(5, { position: vector(999, 0, 17) }),
      command,
      { position: tolerance },
    );
    assert.equal(result.passed, false);
    assert.deepEqual(result.reasons, ["invalid_tolerance"]);
  }
});

test("invalid reset sequences and generations throw before an engine write", () => {
  assert.throws(() => createResetCommand(0), /reset sequence/);
  assert.throws(
    () => createResetCommand(1, { ballGeneration: Number.NaN }),
    /ball generation/,
  );
  assert.throws(
    () => createResetCommand(1, { issuedThinkSeq: Number.MAX_SAFE_INTEGER }),
    /next safe think/,
  );
});

test("100 settled reset observations meet the pure reset invariant", () => {
  for (let sequence = 1; sequence <= 100; sequence += 1) {
    const command = createResetCommand(sequence, { issuedThinkSeq: 10 });
    const write = verifiedWrite(command);
    const result = verifyResetSettledObservation(
      validSettledObservation(sequence),
      command,
      write,
    );
    assert.equal(result.passed, true);
  }
});

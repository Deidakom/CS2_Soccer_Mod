import assert from "node:assert/strict";
import test from "node:test";

import {
  analyzePhase1LiveRun,
  parsePhase1LiveLog,
} from "../tools/analyze-phase1-live-run.mjs";

function record(prefix, event, data, extra = {}) {
  return `[${prefix}] ${JSON.stringify({
    schema: prefix === "SM2LAB"
      ? "cs2-soccermod.balllab-smoke/1"
      : "cs2-soccermod.diagnostic-probe/1",
    seq: extra.seq ?? 1,
    event,
    mapName: "soccermod_phase1_lab",
    serverTime: extra.serverTime ?? 1,
    thinkSeq: extra.thinkSeq ?? 1,
    ...extra,
    data,
  })}`;
}

function passingTrial(resetSequence, goalSequence, attackType = "primary") {
  const edge = attackType === "primary"
    ? { primary: true, secondary: false }
    : { primary: false, secondary: true };
  const commonTail = {
    resetProfile: "contact",
    terminalReason: "settled",
    resetSequence,
    sameAuthoritativeBall: true,
    sameBallGeneration: true,
    sameResetCommandSequence: true,
    targetValid: true,
    angularMotionZero: true,
    angularMagnitudeRaw: 0,
    speed: 0,
  };
  return [
    record("SM2PROBE", "knife_callback", { attackType }),
    record("SM2PROBE", "input_edge", edge),
    record("SM2LAB", "kick_result", { accepted: true, reason: "accepted", attackType }),
    record("SM2PROBE", "kick_write_dispatched", { attackType }),
    record("SM2PROBE", "kick_write_observation", {
      sameAuthoritativeBall: true,
      sameBallGeneration: true,
      sameResetCommandSequence: true,
      elapsedThinks: 1,
      laterAcceptedWriteCount: 0,
    }),
    record("SM2LAB", "goal_candidate", { crossed: true, reason: "crossed" }),
    record("SM2LAB", "goal_commit", { accepted: true, reason: "accepted" }, { goalSequence }),
    record("SM2PROBE", "goal_reset_profile_applied", {
      resetReason: "goal",
      appliedProfile: "contact",
      diagnosticOnly: true,
      resetSequence,
    }),
    record("SM2LAB", "reset_begin", { reason: "goal", commandSequence: resetSequence }),
    record("SM2LAB", "reset_write_verify", {
      passed: true,
      reasons: [],
      writeAttempt: 1,
      angularMotionZero: true,
      resetSequence,
      positionError: 0.01,
      speed: 0,
    }),
    record("SM2LAB", "reset_settle_verify", {
      passed: true,
      reasons: [],
      angularMotionZero: true,
      resetSequence,
      positionError: 0.02,
      speed: 0,
    }),
    record("SM2LAB", "reset_end", {
      passed: true,
      reason: "settled",
      writeAttempt: 1,
      commandSequence: resetSequence,
    }),
    ...["before_write", "immediate_after_write", "next_think"].map((stage) => record(
      "SM2PROBE",
      "reset_physics_snapshot",
      { stage, resetSequence, angularMotionZero: stage === "before_write" ? false : true },
    )),
    ...Array.from({ length: 8 }, (_, index) => record(
      "SM2PROBE",
      "reset_post_terminal_sample",
      { ...commonTail, sampleIndex: index + 1 },
    )),
    record("SM2PROBE", "reset_post_terminal_complete", {
      resetProfile: "contact",
      terminalReason: "settled",
      resetSequence,
      samplesCaptured: 8,
      samplesExpected: 8,
      stoppedReason: null,
    }),
  ];
}

test("live-run parser ignores engine noise and reports malformed prefixed records", () => {
  const parsed = parsePhase1LiveLog([
    "Unable to determine cubemap texture",
    record("SM2LAB", "state_sample", { speed: 0 }),
    "[SM2PROBE] not-json",
  ].join("\n"));
  assert.equal(parsed.records.length, 1);
  assert.deepEqual(parsed.parseErrors, [{ line: 3, reason: "malformed_record_line" }]);
});

test("live-run analyzer accepts exact correlated contact cycles", () => {
  const log = [
    "unrelated engine noise",
    ...passingTrial(11, 5),
    ...passingTrial(12, 6),
  ].join("\n");
  const result = analyzePhase1LiveRun(log, {
    expectedTrials: 2,
    expectedProfile: "contact",
    expectedAttackType: "primary",
  });
  assert.equal(result.passed, true);
  assert.deepEqual(result.failures, []);
  assert.deepEqual(result.summary.resetSequences, [11, 12]);
  assert.deepEqual(result.summary.goalSequences, [5, 6]);
  assert.equal(result.summary.eventCounts.reset_post_terminal_sample, 16);
  assert.equal(result.summary.maximumTailAngularMagnitude, 0);
});

test("live-run analyzer fails closed on missing tails, retries, and failed resets", () => {
  const lines = passingTrial(21, 15);
  lines.splice(lines.findIndex((line) => line.includes('"sampleIndex":8')), 1);
  lines.push(record("SM2PROBE", "reset_write_retry", { resetSequence: 21 }));
  const resetEndIndex = lines.findIndex((line) => line.includes('"event":"reset_end"'));
  lines[resetEndIndex] = record("SM2LAB", "reset_end", {
    passed: false,
    reason: "write_not_verified",
    writeAttempt: 1,
    commandSequence: 21,
  });
  const result = analyzePhase1LiveRun(lines.join("\n"), {
    expectedTrials: 1,
    expectedProfile: "contact",
    expectedAttackType: "primary",
  });
  assert.equal(result.passed, false);
  assert.ok(result.failures.some((failure) => failure.startsWith("reset_write_retry:")));
  assert.ok(result.failures.some((failure) => failure.startsWith("reset_end:")));
  assert.ok(result.failures.some((failure) => failure.startsWith("reset_post_terminal_sample:")));
  assert.ok(result.failures.some((failure) => failure.includes("sample indexes were incomplete")));
});

import assert from "node:assert/strict";
import test from "node:test";

import { createPhase1LiveGateTracker } from "../tools/run-phase1-live-gate.mjs";

function record(prefix, event, data) {
  return `[${prefix}] ${JSON.stringify({
    schema: prefix === "SM2LAB"
      ? "cs2-soccermod.balllab-smoke/1"
      : "cs2-soccermod.diagnostic-probe/1",
    seq: 1,
    event,
    mapName: "soccermod_phase1_lab",
    serverTime: 1,
    thinkSeq: 1,
    data,
  })}`;
}

function completeCycle(resetSequence, attackType = "primary") {
  const edge = attackType === "primary"
    ? { primary: true, secondary: false, activeIsKnife: true }
    : { primary: false, secondary: true, activeIsKnife: true };
  const tail = {
    resetSequence,
    resetProfile: "contact",
    terminalReason: "settled",
    sameAuthoritativeBall: true,
    sameBallGeneration: true,
    sameResetCommandSequence: true,
    targetValid: true,
    angularMotionZero: true,
  };
  return [
    record("SM2PROBE", "knife_callback", { attackType, weaponValid: true }),
    record("SM2PROBE", "input_edge", edge),
    record("SM2LAB", "kick_result", { attackType, accepted: true, reason: "accepted" }),
    record("SM2PROBE", "kick_write_dispatched", { attackType }),
    record("SM2PROBE", "kick_write_observation", {
      attackType,
      sameAuthoritativeBall: true,
      sameBallGeneration: true,
      sameResetCommandSequence: true,
      elapsedThinks: 1,
      laterAcceptedWriteCount: 0,
    }),
    record("SM2LAB", "goal_candidate", { crossed: true, reason: "crossed" }),
    record("SM2LAB", "goal_commit", { accepted: true, reason: "accepted" }),
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
    }),
    record("SM2LAB", "reset_settle_verify", {
      passed: true,
      reasons: [],
      angularMotionZero: true,
      resetSequence,
    }),
    record("SM2LAB", "reset_end", {
      passed: true,
      reason: "settled",
      writeAttempt: 1,
      commandSequence: resetSequence,
    }),
    record("SM2PROBE", "reset_physics_snapshot", {
      resetSequence,
      resetProfile: "contact",
      stage: "before_write",
      angularMotionZero: false,
    }),
    record("SM2PROBE", "reset_physics_snapshot", {
      resetSequence,
      resetProfile: "contact",
      stage: "immediate_after_write",
      angularMotionZero: true,
    }),
    record("SM2PROBE", "reset_physics_snapshot", {
      resetSequence,
      resetProfile: "contact",
      stage: "next_think",
      angularMotionZero: true,
    }),
    ...Array.from({ length: 8 }, (_, index) => record(
      "SM2PROBE",
      "reset_post_terminal_sample",
      { ...tail, sampleIndex: index + 1 },
    )),
    record("SM2PROBE", "reset_post_terminal_complete", {
      resetSequence,
      resetProfile: "contact",
      terminalReason: "settled",
      samplesCaptured: 8,
      samplesExpected: 8,
      stoppedReason: null,
    }),
  ];
}

test("bounded live-gate tracker reaches the exact target without overshoot", () => {
  const tracker = createPhase1LiveGateTracker({
    expectedTrials: 2,
    expectedAttackType: "primary",
    expectedProfile: "contact",
  });
  tracker.consumeLine("engine noise is ignored");
  tracker.consumeLine(record("SM2LAB", "assertion", {
    assertionId: "api_smoke_ready",
    passed: true,
  }));
  tracker.consumeLine(record("SM2PROBE", "goal_reset_profile_configuration", {
    activeProfile: "contact",
    diagnosticsEnabled: true,
  }));
  for (const line of completeCycle(11)) tracker.consumeLine(line);
  assert.deepEqual(tracker.snapshot(), {
    completedTrials: 1,
    expectedTrials: 2,
    failures: [],
    stopped: false,
    passedSoFar: true,
    eventCounts: {
      assertion: 1,
      goal_candidate: 1,
      goal_commit: 1,
      goal_reset_profile_applied: 1,
      goal_reset_profile_configuration: 1,
      input_edge: 1,
      kick_result: 1,
      kick_write_dispatched: 1,
      kick_write_observation: 1,
      knife_callback: 1,
      reset_begin: 1,
      reset_end: 1,
      reset_physics_snapshot: 3,
      reset_post_terminal_complete: 1,
      reset_post_terminal_sample: 8,
      reset_settle_verify: 1,
      reset_write_verify: 1,
    },
  });
  for (const line of completeCycle(12)) tracker.consumeLine(line);
  const final = tracker.snapshot();
  assert.equal(final.completedTrials, 2);
  assert.equal(final.stopped, true);
  assert.equal(final.passedSoFar, true);
  assert.deepEqual(final.failures, []);
  assert.equal(final.eventCounts.reset_post_terminal_sample, 16);
});

test("bounded live-gate tracker accepts an exact secondary cycle", () => {
  const tracker = createPhase1LiveGateTracker({
    expectedTrials: 1,
    expectedAttackType: "secondary",
    expectedProfile: "contact",
  });
  for (const line of completeCycle(24, "secondary")) tracker.consumeLine(line);
  const final = tracker.snapshot();
  assert.equal(final.completedTrials, 1);
  assert.equal(final.stopped, true);
  assert.equal(final.passedSoFar, true);
});

test("bounded live-gate tracker fails immediately on disqualifying evidence", () => {
  const rejectedKick = createPhase1LiveGateTracker({
    expectedTrials: 100,
    expectedAttackType: "primary",
    expectedProfile: "contact",
  });
  rejectedKick.consumeLine(record("SM2LAB", "kick_result", {
    attackType: "primary",
    accepted: false,
    reason: "cooldown",
  }));
  assert.equal(rejectedKick.snapshot().stopped, true);
  assert.match(rejectedKick.snapshot().failures[0], /kick result was rejected/);

  const retry = createPhase1LiveGateTracker({
    expectedTrials: 100,
    expectedAttackType: "primary",
    expectedProfile: "contact",
  });
  retry.consumeLine(record("SM2PROBE", "reset_write_retry", { resetSequence: 1 }));
  assert.deepEqual(retry.snapshot().failures, ["formal reset required a retry"]);

  const wrongProfile = createPhase1LiveGateTracker({
    expectedTrials: 100,
    expectedAttackType: "primary",
    expectedProfile: "contact",
  });
  wrongProfile.consumeLine(record("SM2PROBE", "goal_reset_profile_configuration", {
    activeProfile: "radius_clearance",
    diagnosticsEnabled: true,
  }));
  assert.match(wrongProfile.snapshot().failures[0], /goal reset profile was not contact/);
});

test("bounded live-gate tracker enforces event count ceilings", () => {
  const tracker = createPhase1LiveGateTracker({
    expectedTrials: 1,
    expectedAttackType: "primary",
    expectedProfile: "contact",
  });
  const callback = record("SM2PROBE", "knife_callback", {
    attackType: "primary",
    weaponValid: true,
  });
  tracker.consumeLine(callback);
  assert.equal(tracker.snapshot().stopped, false);
  tracker.consumeLine(callback);
  assert.equal(tracker.snapshot().stopped, true);
  assert.deepEqual(tracker.snapshot().failures, [
    "knife_callback: exceeded formal count ceiling",
  ]);
});

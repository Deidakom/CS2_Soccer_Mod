import assert from "node:assert/strict";
import test from "node:test";

import { analyzePhase1PhysicsRun } from "../tools/analyze-phase1-physics-run.mjs";

function line(event, data, seq) {
  return `[SM2PROBE] ${JSON.stringify({
    schema: "cs2-soccermod.diagnostic-probe/1",
    seq,
    event,
    mapName: "soccermod_phase1_lab",
    serverTime: seq,
    thinkSeq: seq,
    data,
  })}`;
}

function passingTrial(profileId, trialIndex, seq) {
  return [
    line("physics_trial_begin", {
      profileId,
      suite: "drop",
      qualification: "diagnostic_only_reference_bands_unfrozen",
      trialIndex,
    }, seq),
    line("physics_trial_sample", {
      profileId,
      trialIndex,
      sameAuthoritativeBall: true,
      sameBallGeneration: true,
      sameResetSequence: true,
      targetValid: true,
    }, seq + 1),
    line("physics_trial_end", {
      profileId,
      trialIndex,
      reason: "first_bounce_apex",
      passedHardChecks: true,
      sampleCount: 10,
      maxSpeed: 500,
      maxDisplacement: 64,
      elapsedSeconds: 1,
      firstThinkDisplacement: 0,
      firstBounceHeight: 32,
      bounceTakeoffSpeed: 150,
      maximumFloorCenterPenetration: 2,
      finalSnapshot: { speed: 0.5, angularMagnitudeRaw: 3 },
    }, seq + 2),
  ];
}

test("physics analyzer accepts exact correlated repeatable trials", () => {
  const profileId = "drop_64";
  const log = [
    line("physics_trial_configuration", { accepted: true, profileId }, 1),
    ...passingTrial(profileId, 1, 10),
    ...passingTrial(profileId, 2, 20),
    line("physics_trial_run_end", {
      profileId,
      passedHardChecks: true,
      cleanupPassed: true,
      cleanupReason: "settled",
    }, 30),
  ].join("\n");
  const result = analyzePhase1PhysicsRun(log, { profileId, expectedTrials: 2 });
  assert.equal(result.passed, true);
  assert.deepEqual(result.failures, []);
  assert.equal(result.summary.sampleCount, 2);
  assert.equal(result.summary.metricStats.firstBounceHeight.coefficientOfVariationPercent, 0);
  assert.equal(result.summary.metricStats.finalSpeed.mean, 0.5);
  assert.equal(result.summary.metricStats.maximumFloorCenterPenetration.mean, 2);
  assert.equal(result.summary.cleanupPassed, true);
  assert.deepEqual(result.summary.endReasons, { first_bounce_apex: 2 });
});

test("physics analyzer fails on hard failure, lost correlation, and excessive CV", () => {
  const profileId = "drop_64";
  const first = passingTrial(profileId, 1, 10);
  const second = passingTrial(profileId, 2, 20);
  const secondSample = JSON.parse(second[1].slice("[SM2PROBE] ".length));
  secondSample.data.sameBallGeneration = false;
  second[1] = `[SM2PROBE] ${JSON.stringify(secondSample)}`;
  const secondEnd = JSON.parse(second[2].slice("[SM2PROBE] ".length));
  secondEnd.data.passedHardChecks = false;
  secondEnd.data.firstBounceHeight = 64;
  second[2] = `[SM2PROBE] ${JSON.stringify(secondEnd)}`;
  const log = [
    line("physics_trial_configuration", { accepted: true, profileId }, 1),
    ...first,
    ...second,
    line("physics_trial_run_end", { profileId, passedHardChecks: false }, 30),
  ].join("\n");
  const result = analyzePhase1PhysicsRun(log, { profileId, expectedTrials: 2 });
  assert.equal(result.passed, false);
  assert.ok(result.failures.some((failure) => /hard checks/.test(failure)));
  assert.ok(result.failures.some((failure) => /correlation/.test(failure)));
  assert.ok(result.failures.some((failure) => /CV/.test(failure)));
});

test("physics analyzer uses impact-normal retention for wall repeatability", () => {
  const profileId = "wall_y_300_0";
  const trial = (trialIndex, seq, normalRetention) => [
    line("physics_trial_begin", {
      profileId,
      suite: "walls",
      qualification: "diagnostic_only_reference_bands_unfrozen",
      trialIndex,
    }, seq),
    line("physics_trial_sample", {
      profileId,
      trialIndex,
      sameAuthoritativeBall: true,
      sameBallGeneration: true,
      sameResetSequence: true,
      targetValid: true,
    }, seq + 1),
    line("physics_trial_end", {
      profileId,
      trialIndex,
      reason: "rebound_observed",
      passedHardChecks: true,
      sampleCount: 10,
      normalRetention,
      tangentRetention: 0.8,
      totalSpeedRetention: 0.5,
      verticalSpeedDelta: 25,
      finalSnapshot: { speed: 100, angularMagnitudeRaw: 3 },
    }, seq + 2),
  ];
  const log = [
    line("physics_trial_configuration", { accepted: true, profileId }, 1),
    ...trial(1, 10, 0.46),
    ...trial(2, 20, 0.46),
    line("physics_trial_run_end", {
      profileId,
      passedHardChecks: true,
      cleanupPassed: true,
      cleanupReason: "settled",
    }, 30),
  ].join("\n");

  const result = analyzePhase1PhysicsRun(log, { profileId, expectedTrials: 2 });
  assert.equal(result.passed, true);
  assert.equal(result.summary.metricStats.normalRetention.mean, 0.46);
  assert.equal(result.summary.metricStats.tangentRetention.mean, 0.8);
  assert.equal(result.summary.metricStats.totalSpeedRetention.mean, 0.5);
  assert.equal(result.summary.metricStats.verticalSpeedDelta.mean, 25);
});

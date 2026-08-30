#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const RECORD_LINE = /^\[(SM2LAB|SM2PROBE)\]\s+(\{.*\})$/;
const ATTACK_TYPES = new Set(["primary", "secondary"]);
const PROFILES = new Set(["contact", "radius_clearance"]);

export function parsePhase1LiveLog(text) {
  const records = [];
  const parseErrors = [];
  const lines = String(text).split(/\r?\n/);

  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index];
    if (!line.startsWith("[SM2LAB]") && !line.startsWith("[SM2PROBE]")) {
      continue;
    }
    const match = RECORD_LINE.exec(line);
    if (!match) {
      parseErrors.push({ line: index + 1, reason: "malformed_record_line" });
      continue;
    }
    try {
      const record = JSON.parse(match[2]);
      records.push({ prefix: match[1], line: index + 1, ...record });
    } catch (error) {
      parseErrors.push({
        line: index + 1,
        reason: "invalid_json",
        message: error instanceof Error ? error.message : String(error),
      });
    }
  }

  return { records, parseErrors };
}

function sortedNumbers(values) {
  return [...values].sort((left, right) => left - right);
}

function sameNumbers(left, right) {
  const a = sortedNumbers(left);
  const b = sortedNumbers(right);
  return a.length === b.length && a.every((value, index) => value === b[index]);
}

function countByEvent(records) {
  const counts = {};
  for (const record of records) {
    counts[record.event] = (counts[record.event] ?? 0) + 1;
  }
  return Object.fromEntries(Object.entries(counts).sort(([a], [b]) => a.localeCompare(b)));
}

function maximum(records, selector) {
  const values = records.map(selector).filter(Number.isFinite);
  return values.length === 0 ? null : Math.max(...values);
}

export function analyzePhase1LiveRun(text, options) {
  const expectedTrials = Number(options?.expectedTrials);
  const expectedProfile = options?.expectedProfile;
  const expectedAttackType = options?.expectedAttackType;
  const expectedTailSamples = options?.expectedTailSamples ?? 8;

  if (!Number.isInteger(expectedTrials) || expectedTrials < 1) {
    throw new Error("expectedTrials must be a positive integer");
  }
  if (!PROFILES.has(expectedProfile)) {
    throw new Error(`unsupported expectedProfile: ${expectedProfile}`);
  }
  if (!ATTACK_TYPES.has(expectedAttackType)) {
    throw new Error(`unsupported expectedAttackType: ${expectedAttackType}`);
  }
  if (!Number.isInteger(expectedTailSamples) || expectedTailSamples < 1) {
    throw new Error("expectedTailSamples must be a positive integer");
  }

  const { records, parseErrors } = parsePhase1LiveLog(text);
  const failures = [];
  const byEvent = (event) => records.filter((record) => record.event === event);
  const requireCount = (event, expected) => {
    const actual = byEvent(event).length;
    if (actual !== expected) failures.push(`${event}: expected ${expected}, got ${actual}`);
    return byEvent(event);
  };
  const requireAll = (event, eventRecords, predicate, reason) => {
    const rejected = eventRecords.filter((record) => !predicate(record));
    if (rejected.length > 0) failures.push(`${event}: ${rejected.length} ${reason}`);
  };

  if (parseErrors.length > 0) {
    failures.push(`parse_errors: expected 0, got ${parseErrors.length}`);
  }

  const callbacks = requireCount("knife_callback", expectedTrials);
  requireAll(
    "knife_callback",
    callbacks,
    (record) => record.data?.attackType === expectedAttackType,
    `records did not use ${expectedAttackType}`,
  );

  const inputEdges = requireCount("input_edge", expectedTrials);
  requireAll(
    "input_edge",
    inputEdges,
    (record) => expectedAttackType === "primary"
      ? record.data?.primary === true && record.data?.secondary === false
      : record.data?.primary === false && record.data?.secondary === true,
    `records did not contain only the ${expectedAttackType} edge`,
  );

  const kicks = requireCount("kick_result", expectedTrials);
  requireAll(
    "kick_result",
    kicks,
    (record) => record.data?.accepted === true
      && record.data?.reason === "accepted"
      && record.data?.attackType === expectedAttackType,
    "records were not accepted with the expected attack type",
  );
  const dispatched = requireCount("kick_write_dispatched", expectedTrials);
  requireAll(
    "kick_write_dispatched",
    dispatched,
    (record) => record.data?.attackType === expectedAttackType,
    "records used an unexpected attack type",
  );
  const observations = requireCount("kick_write_observation", expectedTrials);
  requireAll(
    "kick_write_observation",
    observations,
    (record) => record.data?.sameAuthoritativeBall === true
      && record.data?.sameBallGeneration === true
      && record.data?.sameResetCommandSequence === true
      && record.data?.elapsedThinks === 1
      && record.data?.laterAcceptedWriteCount === 0,
    "records failed write correlation",
  );

  const candidates = requireCount("goal_candidate", expectedTrials);
  requireAll(
    "goal_candidate",
    candidates,
    (record) => record.data?.crossed === true && record.data?.reason === "crossed",
    "records were not crossed goal candidates",
  );
  const goals = requireCount("goal_commit", expectedTrials);
  requireAll(
    "goal_commit",
    goals,
    (record) => record.data?.accepted === true && record.data?.reason === "accepted",
    "records were not accepted goals",
  );

  const appliedProfiles = requireCount("goal_reset_profile_applied", expectedTrials);
  requireAll(
    "goal_reset_profile_applied",
    appliedProfiles,
    (record) => record.data?.resetReason === "goal"
      && record.data?.appliedProfile === expectedProfile
      && record.data?.diagnosticOnly === true,
    `records did not apply ${expectedProfile}`,
  );
  const begins = requireCount("reset_begin", expectedTrials);
  requireAll(
    "reset_begin",
    begins,
    (record) => record.data?.reason === "goal",
    "records were not goal resets",
  );

  const retries = byEvent("reset_write_retry");
  if (retries.length > 0) failures.push(`reset_write_retry: expected 0, got ${retries.length}`);

  const writeVerifies = requireCount("reset_write_verify", expectedTrials);
  requireAll(
    "reset_write_verify",
    writeVerifies,
    (record) => record.data?.passed === true
      && Array.isArray(record.data?.reasons)
      && record.data.reasons.length === 0
      && record.data?.writeAttempt === 1
      && record.data?.angularMotionZero === true,
    "records did not pass on the first exact-zero write",
  );
  const settleVerifies = requireCount("reset_settle_verify", expectedTrials);
  requireAll(
    "reset_settle_verify",
    settleVerifies,
    (record) => record.data?.passed === true
      && Array.isArray(record.data?.reasons)
      && record.data.reasons.length === 0
      && record.data?.angularMotionZero === true,
    "records did not pass settle verification",
  );
  const resetEnds = requireCount("reset_end", expectedTrials);
  requireAll(
    "reset_end",
    resetEnds,
    (record) => record.data?.passed === true
      && record.data?.reason === "settled"
      && record.data?.writeAttempt === 1,
    "records were not first-write settled resets",
  );

  const snapshots = requireCount("reset_physics_snapshot", expectedTrials * 3);
  const tailSamples = requireCount(
    "reset_post_terminal_sample",
    expectedTrials * expectedTailSamples,
  );
  const tailComplete = requireCount("reset_post_terminal_complete", expectedTrials);
  requireAll(
    "reset_post_terminal_complete",
    tailComplete,
    (record) => record.data?.resetProfile === expectedProfile
      && record.data?.terminalReason === "settled"
      && record.data?.samplesCaptured === expectedTailSamples
      && record.data?.samplesExpected === expectedTailSamples
      && record.data?.stoppedReason == null,
    "records did not complete the expected settled tail",
  );

  const beginSequences = begins.map((record) => record.data?.commandSequence);
  const profileSequences = appliedProfiles.map((record) => record.data?.resetSequence);
  const endSequences = resetEnds.map((record) => record.data?.commandSequence);
  const uniqueSequences = new Set(beginSequences);
  if (uniqueSequences.size !== expectedTrials || beginSequences.some((value) => !Number.isSafeInteger(value))) {
    failures.push("reset_sequence: reset_begin sequences were not unique safe integers");
  }
  if (!sameNumbers(beginSequences, profileSequences) || !sameNumbers(beginSequences, endSequences)) {
    failures.push("reset_sequence: profile/begin/end sequence sets did not match");
  }

  for (const resetSequence of uniqueSequences) {
    if (!Number.isSafeInteger(resetSequence)) continue;
    const cycleSnapshots = snapshots.filter((record) => record.data?.resetSequence === resetSequence);
    const stages = cycleSnapshots.map((record) => record.data?.stage).sort();
    if (JSON.stringify(stages) !== JSON.stringify(["before_write", "immediate_after_write", "next_think"])) {
      failures.push(`reset ${resetSequence}: snapshot stages were incomplete`);
    }
    const postWrite = cycleSnapshots.filter((record) => record.data?.stage !== "before_write");
    if (postWrite.some((record) => record.data?.angularMotionZero !== true)) {
      failures.push(`reset ${resetSequence}: post-write snapshot angular motion was nonzero`);
    }

    const cycleTail = tailSamples.filter((record) => record.data?.resetSequence === resetSequence);
    const indexes = cycleTail.map((record) => record.data?.sampleIndex).sort((a, b) => a - b);
    const expectedIndexes = Array.from({ length: expectedTailSamples }, (_, index) => index + 1);
    if (JSON.stringify(indexes) !== JSON.stringify(expectedIndexes)) {
      failures.push(`reset ${resetSequence}: terminal sample indexes were incomplete`);
    }
    if (cycleTail.some((record) => record.data?.resetProfile !== expectedProfile
      || record.data?.terminalReason !== "settled"
      || record.data?.sameAuthoritativeBall !== true
      || record.data?.sameBallGeneration !== true
      || record.data?.sameResetCommandSequence !== true
      || record.data?.targetValid !== true
      || record.data?.angularMotionZero !== true)) {
      failures.push(`reset ${resetSequence}: terminal sample correlation or angular-zero check failed`);
    }
  }

  const goalSequences = goals.map((record) => record.goalSequence);
  if (new Set(goalSequences).size !== expectedTrials
    || goalSequences.some((value) => !Number.isSafeInteger(value))) {
    failures.push("goal_sequence: committed goal sequences were not unique safe integers");
  }

  const summary = {
    expectedTrials,
    expectedProfile,
    expectedAttackType,
    expectedTailSamples,
    parsedRecords: records.length,
    eventCounts: countByEvent(records),
    resetSequences: sortedNumbers(uniqueSequences),
    goalSequences: sortedNumbers(goalSequences),
    maximumWritePositionError: maximum(writeVerifies, (record) => record.data?.positionError),
    maximumWriteSpeed: maximum(writeVerifies, (record) => record.data?.speed),
    maximumSettlePositionError: maximum(settleVerifies, (record) => record.data?.positionError),
    maximumSettleSpeed: maximum(settleVerifies, (record) => record.data?.speed),
    maximumTailAngularMagnitude: maximum(tailSamples, (record) => record.data?.angularMagnitudeRaw),
    maximumTailSpeed: maximum(tailSamples, (record) => record.data?.speed),
  };

  return {
    passed: failures.length === 0,
    failures,
    parseErrors,
    summary,
  };
}

function parseCliArgs(argv) {
  const args = [...argv];
  const take = (name) => {
    const index = args.indexOf(name);
    if (index < 0 || index + 1 >= args.length) throw new Error(`missing ${name}`);
    const value = args[index + 1];
    args.splice(index, 2);
    return value;
  };
  const expectedTrials = Number(take("--expected-trials"));
  const expectedProfile = take("--profile");
  const expectedAttackType = take("--attack");
  const expectedTailSamples = args.includes("--tail-samples")
    ? Number(take("--tail-samples"))
    : 8;
  if (args.length !== 1) {
    throw new Error("expected exactly one live VConsole log path");
  }
  return { logPath: args[0], expectedTrials, expectedProfile, expectedAttackType, expectedTailSamples };
}

const isMain = process.argv[1]
  && path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url));

if (isMain) {
  try {
    const { logPath, ...options } = parseCliArgs(process.argv.slice(2));
    const result = analyzePhase1LiveRun(fs.readFileSync(logPath, "utf8"), options);
    process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
    if (!result.passed) process.exitCode = 1;
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    console.error(
      "usage: analyze-phase1-live-run.mjs --expected-trials N --profile contact|radius_clearance --attack primary|secondary [--tail-samples N] LOG",
    );
    process.exitCode = 2;
  }
}

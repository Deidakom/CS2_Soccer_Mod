#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const PREFIXES = ["[SM2PROBE] ", "[SM2LAB] "];

function parseRecords(text) {
  const records = [];
  const parseErrors = [];
  for (const [index, line] of String(text).split(/\r?\n/).entries()) {
    const prefix = PREFIXES.find((candidate) => line.startsWith(candidate));
    if (!prefix) continue;
    try {
      const record = JSON.parse(line.slice(prefix.length));
      if (typeof record?.event !== "string") throw new Error("missing event");
      records.push(record);
    } catch (error) {
      parseErrors.push(`line ${index + 1}: ${error.message}`);
    }
  }
  return { records, parseErrors };
}

function numericStats(values) {
  const finite = values.filter(Number.isFinite);
  if (finite.length === 0) return null;
  const mean = finite.reduce((sum, value) => sum + value, 0) / finite.length;
  const variance = finite.reduce(
    (sum, value) => sum + (value - mean) ** 2,
    0,
  ) / finite.length;
  const standardDeviation = Math.sqrt(variance);
  return {
    count: finite.length,
    minimum: Math.min(...finite),
    maximum: Math.max(...finite),
    mean,
    standardDeviation,
    coefficientOfVariationPercent: Math.abs(mean) > 1e-12
      ? standardDeviation / Math.abs(mean) * 100
      : null,
  };
}

function uniqueSorted(values) {
  return [...new Set(values)].sort((left, right) => left - right);
}

export function analyzePhase1PhysicsRun(text, { profileId, expectedTrials }) {
  if (typeof profileId !== "string" || profileId.length === 0) {
    throw new Error("profileId is required");
  }
  if (!Number.isSafeInteger(expectedTrials) || expectedTrials < 1) {
    throw new Error("expectedTrials must be a positive integer");
  }
  const parsed = parseRecords(text);
  const records = parsed.records.filter((record) =>
    record.event.startsWith("physics_trial_"),
  );
  const configurations = records.filter((record) =>
    record.event === "physics_trial_configuration"
      && record.data?.profileId === profileId,
  );
  const begins = records.filter((record) =>
    record.event === "physics_trial_begin"
      && record.data?.profileId === profileId,
  );
  const samples = records.filter((record) =>
    record.event === "physics_trial_sample"
      && record.data?.profileId === profileId,
  );
  const ends = records.filter((record) =>
    record.event === "physics_trial_end"
      && record.data?.profileId === profileId,
  );
  const runEnds = records.filter((record) =>
    record.event === "physics_trial_run_end"
      && record.data?.profileId === profileId,
  );
  const cancellations = records.filter((record) =>
    record.event === "physics_trial_run_cancelled"
      && record.data?.profileId === profileId,
  );
  const failures = [];
  if (configurations.length !== 1 || configurations[0]?.data?.accepted !== true) {
    failures.push(`configuration: expected one accepted record, got ${configurations.length}`);
  }
  if (begins.length !== expectedTrials) {
    failures.push(`physics_trial_begin: expected ${expectedTrials}, got ${begins.length}`);
  }
  if (ends.length !== expectedTrials) {
    failures.push(`physics_trial_end: expected ${expectedTrials}, got ${ends.length}`);
  }
  if (runEnds.length !== 1 || runEnds[0]?.data?.passedHardChecks !== true) {
    failures.push(`run_end: expected one passing record, got ${runEnds.length}`);
  }
  if (cancellations.length > 0) failures.push("run was cancelled");
  if (ends.some((record) => record.data?.passedHardChecks !== true)) {
    failures.push("one or more trials failed hard checks");
  }
  if (ends.some((record) => !Number.isSafeInteger(record.data?.sampleCount)
      || record.data.sampleCount < 1)) {
    failures.push("one or more trials retained no per-think samples");
  }
  const beginIndexes = uniqueSorted(begins.map((record) => record.data?.trialIndex));
  const endIndexes = uniqueSorted(ends.map((record) => record.data?.trialIndex));
  const expectedIndexes = Array.from({ length: expectedTrials }, (_, index) => index + 1);
  if (JSON.stringify(beginIndexes) !== JSON.stringify(expectedIndexes)
      || JSON.stringify(endIndexes) !== JSON.stringify(expectedIndexes)) {
    failures.push("trial indexes were incomplete or duplicated");
  }
  const sampleIndexes = new Set(samples.map((record) => record.data?.trialIndex));
  if (expectedIndexes.some((index) => !sampleIndexes.has(index))) {
    failures.push("one or more trials had no correlated sample record");
  }
  if (samples.some((record) => record.data?.sameAuthoritativeBall !== true
      || record.data?.sameBallGeneration !== true
      || record.data?.sameResetSequence !== true
      || record.data?.targetValid !== true)) {
    failures.push("sample correlation or target validity failed");
  }

  const suite = begins[0]?.data?.suite ?? null;
  const runEndData = runEnds[0]?.data ?? null;
  const metricStats = {
    maxSpeed: numericStats(ends.map((record) => record.data?.maxSpeed)),
    maxDisplacement: numericStats(ends.map((record) => record.data?.maxDisplacement)),
    elapsedSeconds: numericStats(ends.map((record) => record.data?.elapsedSeconds)),
    firstThinkDisplacement: numericStats(
      ends.map((record) => record.data?.firstThinkDisplacement),
    ),
    firstBounceHeight: numericStats(ends.map((record) => record.data?.firstBounceHeight)),
    bounceTakeoffSpeed: numericStats(ends.map((record) => record.data?.bounceTakeoffSpeed)),
    maximumFloorCenterPenetration: numericStats(
      ends.map((record) => record.data?.maximumFloorCenterPenetration),
    ),
    normalRetention: numericStats(ends.map((record) => record.data?.normalRetention)),
    tangentRetention: numericStats(ends.map((record) => record.data?.tangentRetention)),
    totalSpeedRetention: numericStats(
      ends.map((record) => record.data?.totalSpeedRetention),
    ),
    verticalSpeedDelta: numericStats(ends.map((record) => record.data?.verticalSpeedDelta)),
    // Retained only so older captured runs remain analyzable. New wall runs
    // report pre/post impact components instead of this commanded-speed ratio.
    reboundSpeedRatio: numericStats(ends.map((record) => record.data?.reboundSpeedRatio)),
    normalAngleErrorDegrees: numericStats(
      ends.map((record) => record.data?.normalAngleErrorDegrees),
    ),
    finalSpeed: numericStats(ends.map((record) => record.data?.finalSnapshot?.speed)),
    finalAngularMagnitude: numericStats(
      ends.map((record) => record.data?.finalSnapshot?.angularMagnitudeRaw),
    ),
  };
  const repeatabilityMetric = suite === "drop"
    ? metricStats.firstBounceHeight
    : suite === "roll"
      ? metricStats.maxDisplacement
      : suite === "walls"
        ? metricStats.normalRetention ?? metricStats.reboundSpeedRatio
        : null;
  if (expectedTrials >= 2
      && repeatabilityMetric
      && Number.isFinite(repeatabilityMetric.coefficientOfVariationPercent)
      && repeatabilityMetric.coefficientOfVariationPercent > 5) {
    failures.push(
      `repeatability CV ${repeatabilityMetric.coefficientOfVariationPercent.toFixed(3)}% exceeded 5%`,
    );
  }

  return {
    passed: failures.length === 0 && parsed.parseErrors.length === 0,
    failures,
    parseErrors: parsed.parseErrors,
    summary: {
      profileId,
      suite,
      expectedTrials,
      configurationCount: configurations.length,
      beginCount: begins.length,
      sampleCount: samples.length,
      endCount: ends.length,
      runEndCount: runEnds.length,
      trialIndexes: endIndexes,
      qualification: begins[0]?.data?.qualification ?? null,
      trialHardChecksPassed: ends.length === expectedTrials
        && ends.every((record) => record.data?.passedHardChecks === true),
      cleanupPassed: runEndData?.cleanupPassed ?? null,
      cleanupReason: runEndData?.cleanupReason ?? null,
      runPassedHardChecks: runEndData?.passedHardChecks ?? null,
      endReasons: Object.fromEntries([...new Set(ends.map((record) => record.data?.reason))]
        .map((reason) => [
          reason,
          ends.filter((record) => record.data?.reason === reason).length,
        ])),
      metricStats,
    },
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
  const profileId = take("--profile");
  const expectedTrials = Number(take("--expected-trials"));
  if (args.length !== 1) throw new Error("expected exactly one VConsole log path");
  return { logPath: args[0], profileId, expectedTrials };
}

const isMain = process.argv[1]
  && path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url));

if (isMain) {
  try {
    const { logPath, ...options } = parseCliArgs(process.argv.slice(2));
    const result = analyzePhase1PhysicsRun(fs.readFileSync(logPath, "utf8"), options);
    process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
    if (!result.passed) process.exitCode = 1;
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    console.error(
      "usage: analyze-phase1-physics-run.mjs --profile ID --expected-trials N LOG",
    );
    process.exitCode = 2;
  }
}

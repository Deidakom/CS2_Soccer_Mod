#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";

import { analyzePhase1PhysicsRun } from "./analyze-phase1-physics-run.mjs";

const DEFAULT_TIMEOUT_MS = 2 * 60 * 1000;
const PREFIXES = ["[SM2PROBE] ", "[SM2LAB] "];

function parseRecord(line) {
  const prefix = PREFIXES.find((candidate) => line.startsWith(candidate));
  if (!prefix) return null;
  try {
    return JSON.parse(line.slice(prefix.length));
  } catch {
    return null;
  }
}

function takeOption(args, name, fallback) {
  const index = args.indexOf(name);
  if (index < 0) return fallback;
  if (index + 1 >= args.length) throw new Error(`missing value for ${name}`);
  const value = args[index + 1];
  args.splice(index, 2);
  return value;
}

function parseCliArgs(argv) {
  const args = [...argv];
  const resetTailIndex = args.indexOf("--require-reset-tail");
  const requireResetTail = resetTailIndex >= 0;
  if (requireResetTail) args.splice(resetTailIndex, 1);
  const profileId = takeOption(args, "--profile");
  const expectedTrials = Number(takeOption(args, "--trials"));
  const timeoutMs = Number(takeOption(args, "--timeout-ms", DEFAULT_TIMEOUT_MS));
  const output = takeOption(args, "--output");
  const port = Number(takeOption(args, "--port", 29000));
  if (args.length > 0) throw new Error(`unexpected arguments: ${args.join(" ")}`);
  if (!profileId) throw new Error("--profile is required");
  if (!Number.isSafeInteger(expectedTrials) || expectedTrials < 1 || expectedTrials > 100) {
    throw new Error("--trials must be an integer from 1 through 100");
  }
  if (!Number.isSafeInteger(timeoutMs) || timeoutMs < 5_000) {
    throw new Error("--timeout-ms must be an integer of at least 5000");
  }
  if (!Number.isSafeInteger(port) || port < 1 || port > 65535) {
    throw new Error("--port must be an integer from 1 through 65535");
  }
  if (requireResetTail
      && (expectedTrials !== 1
        || !["goal_east_1250", "goal_west_1250"].includes(profileId))) {
    throw new Error("--require-reset-tail requires one fixed forward-goal trial");
  }
  return { profileId, expectedTrials, timeoutMs, output, port, requireResetTail };
}

function defaultOutputPath(profileId, expectedTrials) {
  const stamp = new Date().toISOString().replaceAll(":", "-").replace(".", "-");
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
  return path.join(
    root,
    "artifacts",
    "phase1-physics",
    `${stamp}-${profileId}-${expectedTrials}.jsonl`,
  );
}

export async function runPhase1PhysicsGate(options) {
  const outputPath = path.resolve(
    options.output ?? defaultOutputPath(options.profileId, options.expectedTrials),
  );
  const summaryPath = `${outputPath}.summary.json`;
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  const metadata = {
    schema: "cs2-soccermod.phase1-physics-gate/1",
    startedAtUtc: new Date().toISOString(),
    profileId: options.profileId,
    expectedTrials: options.expectedTrials,
    timeoutMs: options.timeoutMs,
    requireResetTail: options.requireResetTail === true,
  };
  fs.writeFileSync(outputPath, `# ${JSON.stringify(metadata)}\n`, "utf8");

  const helperPath = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "cs2-vconsole.mjs");
  const child = spawn(process.execPath, [
    helperPath,
    "--port", String(options.port ?? 29000),
    "--timeout-ms", String(options.timeoutMs + 15_000),
    "--settle-ms", String(options.timeoutMs + 10_000),
    `sm2lab_status; ${options.requireResetTail ? "sm2lab_goal_reset_profile contact; " : ""}sm2lab_physics_trial ${options.profileId} ${options.expectedTrials}`,
  ], {
    stdio: ["ignore", "pipe", "pipe", "ipc"],
    windowsHide: true,
  });

  let pending = "";
  let stderr = "";
  let stopRequested = false;
  let timeoutReached = false;
  let sawReady = false;
  let sawAcceptedConfiguration = false;
  let sawRunEnd = false;
  let sawResetTailComplete = false;
  const resetTailSamples = [];
  const evidenceLines = [];
  const failures = [];
  const progress = [];

  function requestStop(reason) {
    if (stopRequested) return;
    stopRequested = true;
    progress.push(reason);
    process.stderr.write(`${reason}\n`);
    if (child.connected) child.send({ type: "stop", commands: ["sm2lab_status"] });
    setTimeout(() => {
      if (child.exitCode === null && child.signalCode === null) child.kill();
    }, 3_000).unref();
  }

  function consumeLine(line) {
    const record = parseRecord(line);
    if (!record) return;
    if (record.event !== "state_sample") {
      evidenceLines.push(line);
      fs.appendFileSync(outputPath, `${line}\n`, "utf8");
    }
    if (record.event === "assertion"
        && record.data?.assertionId === "api_smoke_ready"
        && record.data?.passed === true) {
      sawReady = true;
    }
    if (record.event === "physics_trial_configuration"
        && record.data?.profileId === options.profileId) {
      if (record.data?.accepted === true) sawAcceptedConfiguration = true;
      else {
        failures.push(`physics trial configuration rejected: ${record.data?.reason ?? "unknown"}`);
        requestStop("[physics] configuration rejected");
      }
    }
    if (record.event === "physics_trial_run_cancelled"
        && record.data?.profileId === options.profileId) {
      failures.push(`physics trial cancelled: ${record.data?.reason ?? "unknown"}`);
      requestStop("[physics] run cancelled");
    }
    if (record.event === "physics_trial_run_end"
        && record.data?.profileId === options.profileId) {
      sawRunEnd = true;
      if (record.data?.passedHardChecks !== true) {
        failures.push("live physics trial reported a hard-check failure");
      }
      if (options.requireResetTail) {
        const message = `[physics] completed ${record.data?.trialsCompleted ?? 0}/${options.expectedTrials}; waiting for reset tail`;
        progress.push(message);
        process.stderr.write(`${message}\n`);
      } else {
        requestStop(`[physics] completed ${record.data?.trialsCompleted ?? 0}/${options.expectedTrials}`);
      }
    }
    if (options.requireResetTail
        && record.event === "reset_post_terminal_cancelled") {
      failures.push(`reset terminal tail cancelled: ${record.data?.stoppedReason ?? "unknown"}`);
      requestStop("[physics] reset tail cancelled");
    }
    if (options.requireResetTail
        && record.event === "reset_post_terminal_sample") {
      resetTailSamples.push(record.data);
    }
    if (options.requireResetTail
        && record.event === "reset_post_terminal_complete") {
      const expectedIndexes = Array.from({ length: 8 }, (_, index) => index + 1);
      const sampleIndexes = resetTailSamples.map((sample) => sample?.sampleIndex);
      const samplesValid = resetTailSamples.length === 8
        && JSON.stringify(sampleIndexes) === JSON.stringify(expectedIndexes)
        && resetTailSamples.every((sample) => sample?.resetReason === "goal"
          && sample?.terminalReason === "settled"
          && sample?.sampleCount === 8
          && sample?.sameAuthoritativeBall === true
          && sample?.sameBallGeneration === true
          && sample?.sameResetCommandSequence === true
          && sample?.playEnabled === true
          && sample?.targetValid === true
          && sample?.position?.x === 512
          && sample?.position?.y === 0
          && sample?.position?.z === 15
          && sample?.speed === 0
          && sample?.angularMagnitudeRaw === 0
          && sample?.angularMotionZero === true);
      const valid = record.data?.resetReason === "goal"
        && record.data?.terminalReason === "settled"
        && record.data?.samplesCaptured === 8
        && record.data?.samplesExpected === 8
        && record.data?.stoppedReason === null
        && samplesValid;
      if (!valid) failures.push("reset terminal tail did not satisfy the exact contract");
      else sawResetTailComplete = true;
      requestStop(valid
        ? "[physics] reset tail completed 8/8"
        : "[physics] reset tail invalid");
    }
  }

  child.stdout.setEncoding("utf8");
  child.stdout.on("data", (chunk) => {
    pending += chunk;
    let newline;
    while ((newline = pending.indexOf("\n")) >= 0) {
      const line = pending.slice(0, newline).replace(/\r$/, "");
      pending = pending.slice(newline + 1);
      consumeLine(line);
    }
  });
  child.stderr.setEncoding("utf8");
  child.stderr.on("data", (chunk) => { stderr += chunk; });

  const preflightTimeout = setTimeout(() => {
    if (!sawReady || !sawAcceptedConfiguration) {
      failures.push("live preflight did not confirm readiness and accepted configuration");
      requestStop("[physics] preflight unavailable");
    }
  }, 5_000);
  const timeout = setTimeout(() => {
    timeoutReached = true;
    requestStop("[physics] timeout reached");
  }, options.timeoutMs);

  const childResult = await new Promise((resolve) => {
    child.once("error", (error) => resolve({ code: null, signal: null, error: error.message }));
    child.once("close", (code, signal) => resolve({ code, signal, error: null }));
  });
  clearTimeout(preflightTimeout);
  clearTimeout(timeout);
  if (pending.length > 0) consumeLine(pending.replace(/\r$/, ""));

  const analysis = analyzePhase1PhysicsRun(evidenceLines.join("\n"), {
    profileId: options.profileId,
    expectedTrials: options.expectedTrials,
  });
  if (!sawRunEnd) failures.push("live physics trial ended without a run-end record");
  if (options.requireResetTail && !sawResetTailComplete) {
    failures.push("live physics trial ended without a complete eight-think reset tail");
  }
  if (timeoutReached) failures.unshift("live physics trial timed out");
  if (childResult.error) failures.unshift(`VConsole child error: ${childResult.error}`);
  if (!stopRequested && childResult.code !== 0) {
    failures.unshift(`VConsole exited unexpectedly with code ${childResult.code}`);
  }
  failures.push(...analysis.failures, ...analysis.parseErrors);
  const result = {
    schema: "cs2-soccermod.phase1-physics-gate-result/1",
    passed: failures.length === 0 && analysis.passed,
    finishedAtUtc: new Date().toISOString(),
    outputPath,
    summaryPath,
    profileId: options.profileId,
    expectedTrials: options.expectedTrials,
    analysis,
    process: childResult,
    timeoutReached,
    requireResetTail: options.requireResetTail === true,
    sawResetTailComplete,
    resetTailSampleCount: resetTailSamples.length,
    progress,
    stderr: stderr.trim(),
    failures: [...new Set(failures)],
  };
  fs.writeFileSync(summaryPath, `${JSON.stringify(result, null, 2)}\n`, "utf8");
  return result;
}

const isMain = process.argv[1]
  && path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url));

if (isMain) {
  try {
    const result = await runPhase1PhysicsGate(parseCliArgs(process.argv.slice(2)));
    process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
    if (!result.passed) process.exitCode = 1;
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    console.error(
      "usage: run-phase1-physics-gate.mjs --profile ID --trials N [--require-reset-tail] [--timeout-ms N] [--output PATH]",
    );
    process.exitCode = 2;
  }
}

#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";

import {
  analyzePhase1LiveRun,
  parsePhase1LiveLog,
} from "./analyze-phase1-live-run.mjs";

const ATTACK_TYPES = new Set(["primary", "secondary"]);
const QUALIFYING_PROFILE = "contact";
const DEFAULT_TIMEOUT_MS = 15 * 60 * 1000;
const ARMED_MARKER = "SM2_PHASE1_LIVE_GATE_ARMED";
const MINIMUM_INPUT_INTERVAL_SECONDS = 1.25;
const COUNT_LIMITS = Object.freeze({
  knife_callback: 1,
  input_edge: 1,
  kick_result: 1,
  kick_write_dispatched: 1,
  kick_write_observation: 1,
  goal_candidate: 1,
  goal_commit: 1,
  goal_reset_profile_applied: 1,
  reset_begin: 1,
  reset_write_verify: 1,
  reset_settle_verify: 1,
  reset_end: 1,
  reset_physics_snapshot: 3,
  reset_post_terminal_sample: 8,
  reset_post_terminal_complete: 1,
});

function exactInputEdge(data, attackType) {
  return attackType === "primary"
    ? data?.primary === true && data?.secondary === false
    : data?.primary === false && data?.secondary === true;
}

export function createPhase1LiveGateTracker({
  expectedTrials,
  expectedAttackType,
  expectedProfile = QUALIFYING_PROFILE,
}) {
  if (!Number.isInteger(expectedTrials) || expectedTrials < 1) {
    throw new Error("expectedTrials must be a positive integer");
  }
  if (!ATTACK_TYPES.has(expectedAttackType)) {
    throw new Error(`unsupported expectedAttackType: ${expectedAttackType}`);
  }
  if (expectedProfile !== QUALIFYING_PROFILE) {
    throw new Error("only the contact profile can run a formal live gate");
  }

  const eventCounts = new Map();
  const failures = [];
  let completedTrials = 0;
  let stopped = false;

  function fail(reason) {
    if (!failures.includes(reason)) failures.push(reason);
    stopped = true;
  }

  function checkRecord(record) {
    const { event, data } = record;
    if (typeof event !== "string" || event.length === 0) {
      fail("record_without_event");
      return;
    }
    const nextCount = (eventCounts.get(event) ?? 0) + 1;
    eventCounts.set(event, nextCount);
    const multiplier = COUNT_LIMITS[event];
    if (multiplier && nextCount > expectedTrials * multiplier) {
      fail(`${event}: exceeded formal count ceiling`);
      return;
    }

    switch (event) {
      case "assertion":
        if (data?.assertionId === "api_smoke_ready" && data?.passed !== true) {
          fail("api_smoke_ready precondition failed");
        }
        break;
      case "goal_reset_profile_configuration":
        if (data?.activeProfile !== expectedProfile || data?.diagnosticsEnabled !== true) {
          fail(`goal reset profile was not ${expectedProfile} with diagnostics enabled`);
        }
        break;
      case "knife_callback":
        if (data?.attackType !== expectedAttackType || data?.weaponValid !== true) {
          fail(`knife callback did not match ${expectedAttackType}`);
        }
        break;
      case "input_edge":
        if (!exactInputEdge(data, expectedAttackType) || data?.activeIsKnife !== true) {
          fail(`input edge did not match ${expectedAttackType} knife input`);
        }
        break;
      case "kick_result":
        if (data?.attackType !== expectedAttackType
          || data?.accepted !== true
          || data?.reason !== "accepted") {
          fail("kick result was rejected or used the wrong attack type");
        }
        break;
      case "kick_write_dispatched":
        if (data?.attackType !== expectedAttackType) {
          fail("kick write used the wrong attack type");
        }
        break;
      case "kick_write_observation":
        if (data?.attackType !== expectedAttackType
          || data?.sameAuthoritativeBall !== true
          || data?.sameBallGeneration !== true
          || data?.sameResetCommandSequence !== true
          || data?.elapsedThinks !== 1
          || data?.laterAcceptedWriteCount !== 0) {
          fail("kick write observation lost formal correlation");
        }
        break;
      case "goal_candidate":
        if (data?.crossed !== true || data?.reason !== "crossed") {
          fail("goal candidate was not a valid crossing");
        }
        break;
      case "goal_commit":
        if (data?.accepted !== true || data?.reason !== "accepted") {
          fail("goal commit was rejected");
        }
        break;
      case "goal_reset_profile_applied":
        if (data?.resetReason !== "goal"
          || data?.appliedProfile !== expectedProfile
          || data?.diagnosticOnly !== true) {
          fail(`goal reset did not apply ${expectedProfile}`);
        }
        break;
      case "reset_begin":
        if (data?.reason !== "goal") fail("non-goal reset entered the formal capture");
        break;
      case "reset_write_retry":
        fail("formal reset required a retry");
        break;
      case "reset_write_verify":
        if (data?.passed !== true
          || !Array.isArray(data?.reasons)
          || data.reasons.length !== 0
          || data?.writeAttempt !== 1
          || data?.angularMotionZero !== true) {
          fail("formal reset failed first-write verification");
        }
        break;
      case "reset_settle_verify":
        if (data?.passed !== true
          || !Array.isArray(data?.reasons)
          || data.reasons.length !== 0
          || data?.angularMotionZero !== true) {
          fail("formal reset failed settle verification");
        }
        break;
      case "reset_end":
        if (data?.passed !== true || data?.reason !== "settled" || data?.writeAttempt !== 1) {
          fail("formal reset did not settle on write 1");
        }
        break;
      case "reset_physics_snapshot":
        if (data?.resetProfile !== expectedProfile
          || (data?.stage !== "before_write" && data?.angularMotionZero !== true)) {
          fail("reset snapshot profile or post-write angular state was invalid");
        }
        break;
      case "reset_post_terminal_sample":
        if (data?.resetProfile !== expectedProfile
          || data?.terminalReason !== "settled"
          || data?.sameAuthoritativeBall !== true
          || data?.sameBallGeneration !== true
          || data?.sameResetCommandSequence !== true
          || data?.targetValid !== true
          || data?.angularMotionZero !== true) {
          fail("terminal reset sample failed correlation or angular-zero checks");
        }
        break;
      case "reset_post_terminal_cancelled":
        fail("terminal reset sampling was cancelled");
        break;
      case "reset_post_terminal_complete":
        if (data?.resetProfile !== expectedProfile
          || data?.terminalReason !== "settled"
          || data?.samplesCaptured !== 8
          || data?.samplesExpected !== 8
          || data?.stoppedReason != null) {
          fail("terminal reset sampling did not complete cleanly");
          break;
        }
        completedTrials += 1;
        if (completedTrials === expectedTrials) stopped = true;
        break;
      default:
        break;
    }
  }

  function consumeLine(line) {
    if (stopped) return snapshot();
    const parsed = parsePhase1LiveLog(line);
    if (parsed.parseErrors.length > 0) {
      fail(`malformed telemetry record: ${parsed.parseErrors[0].reason}`);
      return snapshot();
    }
    for (const record of parsed.records) checkRecord(record);
    return snapshot();
  }

  function snapshot() {
    return {
      completedTrials,
      expectedTrials,
      failures: [...failures],
      stopped,
      passedSoFar: failures.length === 0,
      eventCounts: Object.fromEntries(
        [...eventCounts.entries()].sort(([a], [b]) => a.localeCompare(b)),
      ),
    };
  }

  return { consumeLine, snapshot };
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
  const expectedTrials = Number(takeOption(args, "--trials"));
  const expectedAttackType = takeOption(args, "--attack");
  const expectedProfile = takeOption(args, "--profile", QUALIFYING_PROFILE);
  const timeoutMs = Number(takeOption(args, "--timeout-ms", DEFAULT_TIMEOUT_MS));
  const output = takeOption(args, "--output", null);
  const port = Number(takeOption(args, "--port", 29000));
  if (args.length > 0) throw new Error(`unexpected arguments: ${args.join(" ")}`);
  if (!Number.isInteger(timeoutMs) || timeoutMs < 10_000) {
    throw new Error("timeout must be an integer of at least 10000 ms");
  }
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    throw new Error("port must be an integer from 1 through 65535");
  }
  return { expectedTrials, expectedAttackType, expectedProfile, timeoutMs, output, port };
}

function defaultOutputPath(expectedAttackType, expectedTrials) {
  const stamp = new Date().toISOString().replaceAll(":", "-").replace(".", "-");
  const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
  return path.join(
    projectRoot,
    "artifacts",
    "phase1-live",
    `${stamp}-${expectedAttackType}-${expectedTrials}.jsonl`,
  );
}

export async function runPhase1LiveGate(options) {
  const tracker = createPhase1LiveGateTracker(options);
  const outputPath = path.resolve(
    options.output ?? defaultOutputPath(options.expectedAttackType, options.expectedTrials),
  );
  const summaryPath = `${outputPath}.summary.json`;
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  const metadata = {
    schema: "cs2-soccermod.phase1-live-gate/1",
    startedAtUtc: new Date().toISOString(),
    expectedTrials: options.expectedTrials,
    expectedAttackType: options.expectedAttackType,
    expectedProfile: options.expectedProfile,
    timeoutMs: options.timeoutMs,
  };
  fs.writeFileSync(outputPath, `# ${JSON.stringify(metadata)}\n`, "utf8");

  const helperPath = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "cs2-vconsole.mjs");
  const child = spawn(process.execPath, [
    helperPath,
    "--port", String(options.port ?? 29000),
    "--timeout-ms", String(options.timeoutMs + 15_000),
    "--settle-ms", String(options.timeoutMs + 10_000),
    // Prepare over the same persistent connection used for capture. Source 2 can
    // leave short-lived VConsole clients in CLOSE_WAIT, so a separate setup
    // client is not reliable. These events are deliberately ignored until the
    // explicit arming marker below.
    "mp_ignore_round_win_conditions 1; mp_freezetime 0; bot_kick; sm2lab_probe_inputs off; sm2lab_goal_reset_profile contact; sm2lab_goal_reset_diagnostics on; mp_restartgame 1",
  ], {
    stdio: ["ignore", "pipe", "pipe", "ipc"],
    windowsHide: true,
  });

  let pending = "";
  let armed = false;
  let activeKnifeReady = false;
  let lastInputEdgeServerTime = null;
  let stopRequested = false;
  let timeoutReached = false;
  let stderr = "";
  const evidenceLines = [];
  const progress = [];
  const runnerFailures = [];

  function requestStop(reason) {
    if (stopRequested) return;
    stopRequested = true;
    progress.push(reason);
    if (child.connected) {
      child.send({ type: "stop", commands: ["sm2lab_probe_inputs off"] });
    }
    setTimeout(() => {
      if (child.exitCode === null && child.signalCode === null) child.kill();
    }, 3_000).unref();
  }

  function consumeLine(line) {
    if (line.includes(ARMED_MARKER)) {
      armed = true;
      const message = "[gate] armed";
      progress.push(message);
      process.stderr.write(`${message}\n`);
      return;
    }
    if (!armed) return;
    if (!line.startsWith("[SM2LAB]") && !line.startsWith("[SM2PROBE]")) return;
    const parsed = parsePhase1LiveLog(line);
    const record = parsed.records[0];
    const event = record?.event;
    if (event === "switch_requested" && record?.data?.activeIsKnife === true) {
      activeKnifeReady = true;
    }
    if (event === "input_edge" && Number.isFinite(record?.serverTime)) {
      if (lastInputEdgeServerTime !== null) {
        const interval = record.serverTime - lastInputEdgeServerTime;
        if (interval < MINIMUM_INPUT_INTERVAL_SECONDS) {
          const reason = `input interval ${interval.toFixed(3)}s was below ${MINIMUM_INPUT_INTERVAL_SECONDS.toFixed(2)}s`;
          runnerFailures.push(reason);
          requestStop(`[gate] fail-closed: ${reason}`);
        }
      }
      lastInputEdgeServerTime = record.serverTime;
    }
    if (event !== "state_sample") {
      evidenceLines.push(line);
      fs.appendFileSync(outputPath, `${line}\n`, "utf8");
    }
    const before = tracker.snapshot().completedTrials;
    const state = tracker.consumeLine(line);
    if (state.completedTrials > before) {
      const message = `[gate] completed ${state.completedTrials}/${state.expectedTrials}`;
      progress.push(message);
      process.stderr.write(`${message}\n`);
    }
    if (state.failures.length > 0) requestStop(`[gate] fail-closed: ${state.failures[0]}`);
    else if (state.completedTrials === state.expectedTrials) requestStop("[gate] target reached");
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

  const prepTimer = setTimeout(() => {
    if (!child.connected) {
      runnerFailures.push("VConsole disconnected before post-restart preparation");
      requestStop("[gate] preparation unavailable");
      return;
    }
    child.send({
      type: "commands",
      commands: [
        "sm2lab_probe_inputs off; sm2lab_goal_reset_profile contact; sm2lab_goal_reset_diagnostics on; sm2lab_ball_reset; setpos 452 0 0.031251; setang 39.199905 0 0; slot3; sensitivity 0.0001",
      ],
    });
  }, 2_500);

  const armTimer = setTimeout(() => {
    if (!child.connected) {
      runnerFailures.push("VConsole disconnected before the formal gate could arm");
      requestStop("[gate] arming unavailable");
      return;
    }
    child.send({
      type: "commands",
      commands: [
        `slot3; echoln ${ARMED_MARKER}; sm2lab_prepare_player 0; sm2lab_probe_inputs on; sm2lab_status; sm2lab_goal_reset_profile`,
      ],
    });
  }, 4_000);

  const timeout = setTimeout(() => {
    timeoutReached = true;
    requestStop("[gate] timeout reached");
  }, options.timeoutMs);
  const preflightTimeout = setTimeout(() => {
    const counts = tracker.snapshot().eventCounts;
    if (!armed
      || !activeKnifeReady
      || (counts.assertion ?? 0) < 1
      || (counts.goal_reset_profile_configuration ?? 0) < 1) {
      runnerFailures.push("live preflight did not confirm arming, knife readiness, API readiness, and contact profile");
      requestStop("[gate] preflight unavailable");
    }
  }, 10_000);

  const childResult = await new Promise((resolve) => {
    child.once("error", (error) => resolve({ code: null, signal: null, error: error.message }));
    child.once("close", (code, signal) => resolve({ code, signal, error: null }));
  });
  clearTimeout(timeout);
  clearTimeout(preflightTimeout);
  clearTimeout(prepTimer);
  clearTimeout(armTimer);
  if (pending.length > 0) consumeLine(pending.replace(/\r$/, ""));

  const trackerState = tracker.snapshot();
  const analysis = analyzePhase1LiveRun(evidenceLines.join("\n"), {
    expectedTrials: options.expectedTrials,
    expectedProfile: options.expectedProfile,
    expectedAttackType: options.expectedAttackType,
    expectedTailSamples: 8,
  });
  const failures = [
    ...runnerFailures,
    ...trackerState.failures,
    ...analysis.failures,
  ];
  if (timeoutReached) failures.unshift("live gate timed out before qualification");
  if (childResult.error) failures.unshift(`VConsole child error: ${childResult.error}`);
  if (!stopRequested && childResult.code !== 0) {
    failures.unshift(`VConsole exited unexpectedly with code ${childResult.code}`);
  }

  const result = {
    schema: "cs2-soccermod.phase1-live-gate-result/1",
    passed: failures.length === 0 && trackerState.completedTrials === options.expectedTrials,
    finishedAtUtc: new Date().toISOString(),
    outputPath,
    summaryPath,
    tracker: trackerState,
    analysis,
    process: childResult,
    timeoutReached,
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
    const options = parseCliArgs(process.argv.slice(2));
    const result = await runPhase1LiveGate(options);
    process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
    if (!result.passed) process.exitCode = 1;
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    console.error(
      "usage: run-phase1-live-gate.mjs --trials N --attack primary|secondary [--profile contact] [--timeout-ms N] [--port N] [--output PATH]",
    );
    process.exitCode = 2;
  }
}

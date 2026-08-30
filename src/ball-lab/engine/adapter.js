import {
  CSGearSlot,
  CSInputs,
  CSWeaponAttackType,
  Instance,
} from "cs_script/point_script";

import {
  acceptGoalCandidate,
  createGoalState,
  detectGoalPlaneCrossing,
  replaceBallGeneration,
  unlockAfterVerifiedReset,
} from "../core/goal.js";
import { computeKick } from "../core/kick.js";
import {
  CapPhase,
  beginCapDraft,
  cancelCap,
  capTeamForSlot,
  createCapState,
  disconnectCapPlayer,
  formatCapStatus,
  joinCap,
  leaveCap,
  openCap,
  pickCapPlayer,
} from "../core/cap.js";
import {
  advanceMatchState,
  createMatchState,
  formatMatchStatus,
  matchAllowsBallInteraction,
  matchCountsGoals,
  pauseMatch,
  recordMatchGoal,
  resumeMatch,
  startMatch,
  stopMatch,
} from "../core/match.js";
import {
  createResetCommand,
  verifyResetSettledObservation,
  verifyResetWriteObservation,
} from "../core/reset.js";
import { isFiniteVector } from "../core/vector.js";
import { LAB_LAYOUT } from "../layout.js";
import {
  PHYSICS_TRIAL_PROFILE_IDS,
  createPhysicsTrialSpec,
  parsePhysicsTrialRequest,
  summarizeDropBounce,
} from "../physics-diagnostics.js";

const SMOKE_SCHEMA = "cs2-soccermod.balllab-smoke/1";
const PROBE_SCHEMA = "cs2-soccermod.diagnostic-probe/1";
const BALL_NAME = LAB_LAYOUT.ball.entityName;
const BALL_CLASS = LAB_LAYOUT.ball.entityClass;
const BALL_CANDIDATE = "valve-dust-sphere-server-solid-scaled30-v1";
const RESET_ANGLES = Object.freeze({ pitch: 0, yaw: 0, roll: 0 });
const STATE_SAMPLE_PERIOD_SECONDS = 0.05;
const RESET_TIMEOUT_SECONDS = 2;
const MAX_RESET_WRITE_ATTEMPTS = 2;
const RESET_POST_TERMINAL_SAMPLE_COUNT = 8;
const RAW_ANGULAR_UNITS = "not_declared_by_point_script_api";
const GOAL_RESET_PROFILE_CONTACT = "contact";
const GOAL_RESET_PROFILE_RADIUS_CLEARANCE = "radius_clearance";
const GOAL_RESET_PROFILES = Object.freeze([
  GOAL_RESET_PROFILE_CONTACT,
  GOAL_RESET_PROFILE_RADIUS_CLEARANCE,
]);
const RESET_MOTION_PROFILE_TELEPORT_ONLY = "teleport_only";
const RESET_MOTION_PROFILE_DISABLE_MOTION = "disable_motion";
const RESET_MOTION_PROFILES = Object.freeze([
  RESET_MOTION_PROFILE_TELEPORT_ONLY,
  RESET_MOTION_PROFILE_DISABLE_MOTION,
]);

let telemetrySequence = 0;
let probeSequence = 0;
let thinkSequence = 0;
let ballGeneration = 1;
let resetCommandSequence = 0;
let ball;
let lastBoundBall;
let previousBallPosition;
let previousBallThinkSequence;
let resetOperation;
let resetWritePosition;
let resetRestPosition;
let labGoals = [];
let nextStateSampleTime = 0;
let stateTelemetryEnabled = false;
let playEnabled = false;
let lastBallFaultKey;
let lastBallFaultTime = Number.NEGATIVE_INFINITY;
let playerInputProbeEnabled = false;
let goalResetProfile = GOAL_RESET_PROFILE_CONTACT;
let goalResetDiagnosticsEnabled = false;
let resetMotionProfile = RESET_MOTION_PROFILE_DISABLE_MOTION;
let pendingResetTerminalTrace;
let physicsTrialRun;
let physicsTrialRunSequence = 0;
let goalState = createGoalState({ ballGeneration });
let matchState = createMatchState();
let capState = createCapState();
let lastRenderedMatchSecond;
let nextMatchHudTime = 0;
const lastAcceptedKickByPlayerSlot = new Map();
let pendingKickWriteObservations = [];

function emit(event, data = {}) {
  telemetrySequence += 1;
  const record = {
    schema: SMOKE_SCHEMA,
    seq: telemetrySequence,
    event,
    mapName: Instance.GetMapName(),
    serverTime: Instance.GetGameTime(),
    thinkSeq: thinkSequence,
    candidateId: BALL_CANDIDATE,
    ballGeneration,
    resetSequence: goalState.resetSequence,
    goalSequence: goalState.sequence,
    data,
  };
  Instance.Msg(`[SM2LAB] ${JSON.stringify(record)}`);
}

function emitProbe(event, data = {}) {
  probeSequence += 1;
  const record = {
    schema: PROBE_SCHEMA,
    seq: probeSequence,
    event,
    mapName: Instance.GetMapName(),
    serverTime: Instance.GetGameTime(),
    thinkSeq: thinkSequence,
    data,
  };
  Instance.Msg(`[SM2PROBE] ${JSON.stringify(record)}`);
  return record;
}

function matchSnapshot() {
  return {
    phase: matchState.phase,
    sequence: matchState.sequence,
    scores: { ...matchState.scores },
    remainingSeconds: matchState.remainingSeconds,
    phaseEndsAt: matchState.phaseEndsAt,
    winnerTeam: matchState.winnerTeam,
    lastScoringTeam: matchState.lastScoringTeam,
    statusText: formatMatchStatus(matchState),
  };
}

function emitMatchState(reason, details = {}) {
  emit("match_state", {
    reason,
    ...matchSnapshot(),
    ...details,
  });
}

function capSnapshot() {
  return {
    phase: capState.phase,
    sequence: capState.sequence,
    ownerSlot: capState.ownerSlot,
    players: capState.players.map(({ slot, name }) => ({ slot, name })),
    captains: { ...capState.captains },
    teams: { 2: [...capState.teams[2]], 3: [...capState.teams[3]] },
    turnTeam: capState.turnTeam,
    statusText: formatCapStatus(capState),
  };
}

function emitCapState(reason, details = {}) {
  emit("cap_state", {
    reason,
    ...capSnapshot(),
    ...details,
  });
}

function capPlayer(controller) {
  return {
    slot: controller.GetPlayerSlot(),
    name: controller.GetPlayerName(),
  };
}

function capRosterText() {
  if (capState.phase === CapPhase.IDLE) return "";
  const label = ({ slot, name }) => `${slot}:${name}`;
  if ([CapPhase.COLLECTING, CapPhase.PICKING].includes(capState.phase)) {
    const drafted = new Set([...capState.teams[2], ...capState.teams[3]]);
    const available = capState.players.filter(({ slot }) => !drafted.has(slot));
    return `Available | ${available.map(label).join(", ") || "none"}`;
  }
  const bySlot = new Map(capState.players.map((entry) => [entry.slot, entry]));
  const teamText = (team) => capState.teams[team]
    .map((slot) => label(bySlot.get(slot)))
    .join(", ");
  return `T | ${teamText(2)}    CT | ${teamText(3)}`;
}

function assignReadyCapTeams() {
  if (capState.phase !== CapPhase.READY) {
    emit("cap_team_assignment", {
      accepted: false,
      reason: "cap_not_ready",
      ...capSnapshot(),
    });
    return false;
  }
  const assignments = [];
  const missingSlots = [];
  for (const participant of capState.players) {
    const team = capTeamForSlot(capState, participant.slot);
    const controller = Instance.GetPlayerController(participant.slot);
    if (![2, 3].includes(team)
        || !controller?.IsValid()
        || !controller.IsConnected()) {
      missingSlots.push(participant.slot);
      continue;
    }
    controller.JoinTeam(team);
    assignments.push({ playerSlot: participant.slot, team });
  }
  const accepted = missingSlots.length === 0
    && assignments.length === capState.players.length;
  emit("cap_team_assignment", {
    accepted,
    reason: accepted ? "assigned" : "player_unavailable",
    assignments,
    missingSlots,
    ...capSnapshot(),
  });
  return accepted;
}

function startReadyCapMatch() {
  if (!assignReadyCapTeams()) return false;
  return runMatchAction("restart");
}

function applyCapResult(action, result, details = {}) {
  if (!result?.accepted) {
    emit("cap_action", {
      action,
      accepted: false,
      reason: result?.reason ?? "invalid_result",
      ...capSnapshot(),
      ...details,
    });
    return false;
  }
  capState = result.state;
  emit("cap_action", {
    action,
    accepted: true,
    reason: result.reason,
    ...capSnapshot(),
    ...details,
  });
  emitCapState(result.reason, details);
  if (capState.phase === CapPhase.READY) startReadyCapMatch();
  return true;
}

function handlePlayerDisconnect({ playerSlot }) {
  const result = disconnectCapPlayer(capState, playerSlot);
  if (!result.accepted && result.reason === "not_joined") return;
  const accepted = applyCapResult("disconnect", result, { playerSlot });
  if (accepted && capState.phase === CapPhase.IDLE) runMatchAction("stop");
}

function applyMatchResult(action, result, resetReason) {
  if (!result?.accepted) {
    emit("match_action", {
      action,
      accepted: false,
      reason: result?.reason ?? "invalid_result",
      ...matchSnapshot(),
    });
    return false;
  }
  matchState = result.state;
  emit("match_action", {
    action,
    accepted: true,
    reason: result.reason,
    ...matchSnapshot(),
  });
  if (resetReason && ball?.IsValid()) beginReset(resetReason);
  return true;
}

function runMatchAction(action) {
  const now = Instance.GetGameTime();
  switch (action) {
    case "start":
      return applyMatchResult(action, startMatch(matchState, now), "match_start");
    case "restart": {
      const stopped = stopMatch(matchState, now);
      if (!stopped.accepted) return applyMatchResult(action, stopped);
      matchState = stopped.state;
      return applyMatchResult(action, startMatch(matchState, now), "match_restart");
    }
    case "pause":
      return applyMatchResult(action, pauseMatch(matchState, now), "match_pause");
    case "resume":
      return applyMatchResult(action, resumeMatch(matchState, now), "match_resume");
    case "stop":
      return applyMatchResult(action, stopMatch(matchState, now), "match_stop");
    case "status":
      emitMatchState("status_requested");
      nextMatchHudTime = 0;
      return true;
    default:
      emit("match_action", {
        action,
        accepted: false,
        reason: "unsupported_action",
        ...matchSnapshot(),
      });
      return false;
  }
}

function advanceMatchRuntime() {
  const previousPhase = matchState.phase;
  const result = advanceMatchState(matchState, Instance.GetGameTime());
  if (!result.accepted) {
    emit("match_fault", {
      reason: result.reason,
      ...matchSnapshot(),
    });
    return;
  }
  matchState = result.state;
  if (matchState.phase !== previousPhase) {
    emitMatchState(result.reason, { previousPhase });
  }
}

function renderMatchHud() {
  const now = Instance.GetGameTime();
  if (now < nextMatchHudTime) return;
  nextMatchHudTime = now + 0.2;
  if (typeof Instance.DebugScreenText === "function") {
    Instance.DebugScreenText({
      text: formatMatchStatus(matchState),
      x: 0.36,
      y: 0.03,
      duration: 0.25,
      color: { r: 255, g: 255, b: 255, a: 255 },
    });
    Instance.DebugScreenText({
      text: formatCapStatus(capState),
      x: 0.31,
      y: 0.065,
      duration: 0.25,
      color: { r: 170, g: 220, b: 255, a: 255 },
    });
    const rosterText = capRosterText();
    if (rosterText) {
      Instance.DebugScreenText({
        text: rosterText,
        x: 0.2,
        y: 0.095,
        duration: 0.25,
        color: { r: 170, g: 220, b: 255, a: 255 },
      });
    }
  }
  const displaySecond = Math.ceil(matchState.remainingSeconds);
  if (displaySecond !== lastRenderedMatchSecond
      && typeof Instance.SetRoundRemainingTime === "function") {
    lastRenderedMatchSecond = displaySecond;
    Instance.SetRoundRemainingTime(Math.max(1, displaySecond));
  }
}

function handleMatchChat({ player, text }) {
  if (!player?.IsValid() || !player.IsConnected() || typeof text !== "string") return;
  const tokens = text.trim().toLowerCase().split(/\s+/);
  const command = tokens[0];
  const participant = capPlayer(player);
  switch (command) {
    case "!cap": {
      const result = openCap(capState, participant);
      const accepted = applyCapResult("open", result, { playerSlot: participant.slot });
      if (accepted) runMatchAction("stop");
      return;
    }
    case "!join":
      applyCapResult("join", joinCap(capState, participant), {
        playerSlot: participant.slot,
      });
      return;
    case "!leave":
      applyCapResult("leave", leaveCap(capState, participant.slot), {
        playerSlot: participant.slot,
      });
      return;
    case "!draft":
    case "!captains":
      applyCapResult("draft", beginCapDraft(capState, participant.slot), {
        playerSlot: participant.slot,
      });
      return;
    case "!pick": {
      const targetSlot = Number(tokens[1]);
      applyCapResult("pick", pickCapPlayer(capState, participant.slot, targetSlot), {
        playerSlot: participant.slot,
        targetSlot: Number.isSafeInteger(targetSlot) ? targetSlot : null,
      });
      return;
    }
    case "!cancelcap":
      applyCapResult("cancel", cancelCap(capState, participant.slot), {
        playerSlot: participant.slot,
      });
      return;
    case "!teams":
    case "!capstatus":
      emitCapState("status_requested", { playerSlot: participant.slot });
      nextMatchHudTime = 0;
      return;
    case "!play":
      startReadyCapMatch();
      return;
    default:
      break;
  }
  const actions = Object.freeze({
    "!match": "start",
    "!start": "start",
    "!restart": "restart",
    "!pause": "pause",
    "!resume": "resume",
    "!stop": "stop",
    "!score": "status",
    "!status": "status",
  });
  const action = actions[command];
  if (!action) return;
  emit("match_chat_command", {
    playerSlot: participant.slot,
    playerName: participant.name,
    command,
    action,
  });
  runMatchAction(action);
}

function isZeroVector(value) {
  return Boolean(value && value.x === 0 && value.y === 0 && value.z === 0);
}

function finiteVectorOrNull(value) {
  return isFiniteVector(value) ? { x: value.x, y: value.y, z: value.z } : null;
}

function finiteAnglesOrNull(value) {
  return value
      && Number.isFinite(value.pitch)
      && Number.isFinite(value.yaw)
      && Number.isFinite(value.roll)
    ? { pitch: value.pitch, yaw: value.yaw, roll: value.roll }
    : null;
}

function captureGroundDescriptor(target) {
  try {
    const groundEntity = target.GetGroundEntity();
    if (!groundEntity) return { status: "none" };
    let valid;
    try {
      valid = groundEntity.IsValid();
    } catch {
      return { status: "read_error" };
    }
    if (!valid) return { status: "invalid" };
    try {
      return {
        status: "valid",
        isWorld: groundEntity.IsWorld(),
        className: groundEntity.GetClassName(),
        entityName: groundEntity.GetEntityName(),
      };
    } catch {
      return { status: "read_error" };
    }
  } catch {
    return { status: "read_error" };
  }
}

function captureResetPhysics(target, includeGround = true) {
  const targetValid = Boolean(target?.IsValid());
  if (!targetValid) {
    return {
      targetValid: false,
      position: null,
      angles: null,
      velocity: null,
      speed: null,
      angularVelocityRaw: null,
      angularMagnitudeRaw: null,
      angularMotionZero: null,
      angularVelocityFinite: null,
      angularUnits: RAW_ANGULAR_UNITS,
      groundEntity: null,
    };
  }

  const position = finiteVectorOrNull(target.GetAbsOrigin());
  const angles = finiteAnglesOrNull(target.GetAbsAngles());
  const velocity = finiteVectorOrNull(target.GetAbsVelocity());
  const angularVelocityRaw = finiteVectorOrNull(target.GetAbsAngularVelocity());
  return {
    targetValid: true,
    position,
    angles,
    velocity,
    speed: velocity ? Math.hypot(velocity.x, velocity.y, velocity.z) : null,
    angularVelocityRaw,
    angularMagnitudeRaw: angularVelocityRaw
      ? Math.hypot(
        angularVelocityRaw.x,
        angularVelocityRaw.y,
        angularVelocityRaw.z,
      )
      : null,
    angularMotionZero: angularVelocityRaw ? isZeroVector(angularVelocityRaw) : null,
    angularVelocityFinite: Boolean(angularVelocityRaw),
    angularUnits: RAW_ANGULAR_UNITS,
    groundEntity: includeGround ? captureGroundDescriptor(target) : null,
  };
}

function subtractVectors(left, right) {
  return {
    x: left.x - right.x,
    y: left.y - right.y,
    z: left.z - right.z,
  };
}

function dotVectors(left, right) {
  return left.x * right.x + left.y * right.y + left.z * right.z;
}

function vectorMagnitude(value) {
  return Math.hypot(value.x, value.y, value.z);
}

function normalizedOrNull(value) {
  if (!isFiniteVector(value)) return null;
  const magnitude = vectorMagnitude(value);
  if (!Number.isFinite(magnitude) || magnitude <= 1e-9) return null;
  return {
    x: value.x / magnitude,
    y: value.y / magnitude,
    z: value.z / magnitude,
  };
}

function angleBetweenDegrees(left, right) {
  const leftNormal = normalizedOrNull(left);
  const rightNormal = normalizedOrNull(right);
  if (!leftNormal || !rightNormal) return null;
  const cosine = Math.max(-1, Math.min(1, dotVectors(leftNormal, rightNormal)));
  return Math.acos(cosine) * 180 / Math.PI;
}

function traceEnvironmentForPhysicsSpec(spec) {
  const environment = { floorTrace: null, wallTrace: null };
  if (["drop", "roll"].includes(spec.mode)) {
    const floorTraceStart = spec.mode === "roll"
      ? { ...spec.startPosition, z: spec.startPosition.z + 1 }
      : spec.startPosition;
    const floorTrace = Instance.TraceSphere({
      radius: LAB_LAYOUT.ball.nominalRadius,
      start: floorTraceStart,
      end: { ...floorTraceStart, z: -2048 },
      ignoreEntity: ball,
      ignorePlayers: true,
    });
    environment.floorTrace = {
      start: floorTraceStart,
      didHit: floorTrace.didHit,
      startedInSolid: floorTrace.startedInSolid,
      fraction: floorTrace.fraction,
      end: finiteVectorOrNull(floorTrace.end),
      normal: finiteVectorOrNull(floorTrace.normal),
      hitClassName: floorTrace.hitEntity?.IsValid()
        ? floorTrace.hitEntity.GetClassName()
        : null,
    };
  }
  if (spec.mode === "wall") {
    const wallTraceStart = {
      ...spec.startPosition,
      z: spec.startPosition.z + 1,
    };
    const wallTraceEnd = {
      x: wallTraceStart.x + spec.direction.x * 4096,
      y: wallTraceStart.y + spec.direction.y * 4096,
      z: wallTraceStart.z,
    };
    const wallTrace = Instance.TraceSphere({
      radius: LAB_LAYOUT.ball.nominalRadius,
      start: wallTraceStart,
      end: wallTraceEnd,
      ignoreEntity: ball,
      ignorePlayers: true,
    });
    const wallNormal = finiteVectorOrNull(wallTrace.normal);
    const normalizedWallNormal = normalizedOrNull(wallNormal);
    environment.wallTrace = {
      start: wallTraceStart,
      didHit: wallTrace.didHit,
      startedInSolid: wallTrace.startedInSolid,
      fraction: wallTrace.fraction,
      end: finiteVectorOrNull(wallTrace.end),
      normal: wallNormal,
      approachDot: normalizedWallNormal
        ? dotVectors(normalizedWallNormal, spec.direction)
        : null,
      verticalComponent: normalizedWallNormal?.z ?? null,
      hitClassName: wallTrace.hitEntity?.IsValid()
        ? wallTrace.hitEntity.GetClassName()
        : null,
    };
  }
  return environment;
}

function emitPhysicsTrialRunEnd(run, cleanupPassed, cleanupReason) {
  emitProbe("physics_trial_run_end", {
    runSequence: run.runSequence,
    profileId: run.profileId,
    suite: run.spec.suite,
    qualification: run.spec.qualification,
    trialsRequested: run.trialCount,
    trialsCompleted: run.trialsCompleted,
    passedHardChecks: run.hardFailures.length === 0 && cleanupPassed,
    hardFailures: run.hardFailures,
    cleanupPassed,
    cleanupReason,
  });
}

function cancelPhysicsTrialRun(reason) {
  if (!physicsTrialRun) return;
  const run = physicsTrialRun;
  physicsTrialRun = undefined;
  emitProbe("physics_trial_run_cancelled", {
    runSequence: run.runSequence,
    profileId: run.profileId,
    suite: run.spec.suite,
    trialsRequested: run.trialCount,
    trialsCompleted: run.trialsCompleted,
    reason,
  });
}

function startPhysicsTrialIteration(run) {
  const spec = createPhysicsTrialSpec(run.profileId);
  const environment = traceEnvironmentForPhysicsSpec(spec);
  const setupFailures = [];
  if (environment.floorTrace
      && (!environment.floorTrace.didHit
        || environment.floorTrace.startedInSolid
        || !environment.floorTrace.end)) {
    setupFailures.push("floor_trace_unavailable");
  }
  if (environment.wallTrace
      && (!environment.wallTrace.didHit
        || environment.wallTrace.startedInSolid
        || !environment.wallTrace.end
        || !environment.wallTrace.normal)) {
    setupFailures.push("wall_trace_unavailable");
  }
  if (environment.wallTrace?.didHit
      && environment.wallTrace.normal
      && (Math.abs(environment.wallTrace.verticalComponent) > 0.2
        || environment.wallTrace.approachDot > -0.65)) {
    setupFailures.push("wall_surface_not_supported");
  }
  if (setupFailures.length > 0) {
    run.hardFailures.push(...setupFailures.map((reason) => ({
      trialIndex: run.trialsCompleted + 1,
      reason,
    })));
    emitPhysicsTrialRunEnd(run, false, "setup_failed");
    physicsTrialRun = undefined;
    beginReset("physics_trial_setup_failed");
    return;
  }

  const trialIndex = run.trialsCompleted + 1;
  playEnabled = ["goal", "reverse", "near_miss"].includes(spec.mode);
  previousBallPosition = { ...spec.startPosition };
  previousBallThinkSequence = thinkSequence;
  Instance.EntFireAtTarget({ target: ball, input: "EnableMotion" });
  Instance.EntFireAtTarget({ target: ball, input: "Wake" });
  ball.Teleport({
    position: spec.startPosition,
    angles: RESET_ANGLES,
    velocity: spec.initialVelocity,
    angularVelocity: { x: 0, y: 0, z: 0 },
  });
  const immediateSnapshot = captureResetPhysics(ball);
  run.current = {
    trialIndex,
    spec,
    environment,
    targetBall: ball,
    ballGenerationAtStart: ballGeneration,
    resetSequenceAtStart: resetCommandSequence,
    goalSequenceAtStart: goalState.sequence,
    issuedThinkSeq: thinkSequence,
    issuedServerTime: Instance.GetGameTime(),
    previousVerticalVelocity: immediateSnapshot.velocity?.z ?? 0,
    sawFalling: false,
    sawBounce: false,
    bounceTakeoffSpeed: null,
    bounceImpactMinZ: null,
    bounceApexZ: null,
    maxSpeed: immediateSnapshot.speed ?? 0,
    maxZ: immediateSnapshot.position?.z ?? spec.startPosition.z,
    minZ: immediateSnapshot.position?.z ?? spec.startPosition.z,
    maxDisplacement: 0,
    firstThinkDisplacement: null,
    reboundObserved: false,
    lastWallIncomingVelocity: spec.mode === "wall"
      ? immediateSnapshot.velocity
      : null,
    lastWallIncomingElapsedThinks: spec.mode === "wall" ? 0 : null,
    sampleCount: 0,
  };
  run.pendingNextTrial = false;
  emitProbe("physics_trial_begin", {
    runSequence: run.runSequence,
    profileId: run.profileId,
    suite: spec.suite,
    mode: spec.mode,
    qualification: spec.qualification,
    trialIndex,
    trialCount: run.trialCount,
    maxThinkCount: spec.maxThinkCount,
    startPosition: spec.startPosition,
    initialVelocity: spec.initialVelocity,
    environment,
    immediateSnapshot,
  });
}

function finishPhysicsTrialIteration(
  reason,
  hardFailures = [],
  extra = {},
  deferToGoalReset = false,
) {
  const run = physicsTrialRun;
  const current = run?.current;
  if (!run || !current) return;
  const snapshot = captureResetPhysics(current.targetBall);
  const elapsedThinks = thinkSequence - current.issuedThinkSeq;
  const elapsedSeconds = Instance.GetGameTime() - current.issuedServerTime;
  const displacement = snapshot.position
    ? subtractVectors(snapshot.position, current.spec.startPosition)
    : null;
  const trialFailures = [...hardFailures];
  if (!snapshot.targetValid) trialFailures.push("target_invalid");
  for (const failure of trialFailures) {
    run.hardFailures.push({ trialIndex: current.trialIndex, reason: failure });
  }
  emitProbe("physics_trial_end", {
    runSequence: run.runSequence,
    profileId: run.profileId,
    suite: current.spec.suite,
    mode: current.spec.mode,
    qualification: current.spec.qualification,
    trialIndex: current.trialIndex,
    trialCount: run.trialCount,
    reason,
    passedHardChecks: trialFailures.length === 0,
    hardFailures: trialFailures,
    sampleCount: current.sampleCount,
    elapsedThinks,
    elapsedSeconds,
    displacement,
    displacementMagnitude: displacement ? vectorMagnitude(displacement) : null,
    maxDisplacement: current.maxDisplacement,
    maxSpeed: current.maxSpeed,
    minZ: current.minZ,
    maxZ: current.maxZ,
    firstThinkDisplacement: current.firstThinkDisplacement,
    finalSnapshot: snapshot,
    ...extra,
  });
  run.trialsCompleted += 1;
  run.current = undefined;
  if (deferToGoalReset) {
    run.waitingForReset = true;
    return;
  }
  if (run.trialsCompleted < run.trialCount) {
    run.pendingNextTrial = true;
    return;
  }
  run.waitingForReset = true;
  beginReset("physics_trial_complete");
}

function observePhysicsTrial() {
  const run = physicsTrialRun;
  if (!run) return;
  if (run.waitingForReset) {
    if (resetOperation) return;
    const cleanupPassed = playEnabled && Boolean(ball?.IsValid());
    if (cleanupPassed && run.trialsCompleted < run.trialCount) {
      run.waitingForReset = false;
      run.pendingNextTrial = true;
      return;
    }
    emitPhysicsTrialRunEnd(
      run,
      cleanupPassed,
      cleanupPassed ? "settled" : "reset_failed",
    );
    physicsTrialRun = undefined;
    return;
  }
  if (run.pendingNextTrial) {
    startPhysicsTrialIteration(run);
    return;
  }
  const current = run.current;
  if (!current) return;
  if (current.targetBall !== ball
      || current.ballGenerationAtStart !== ballGeneration
      || !current.targetBall?.IsValid()) {
    finishPhysicsTrialIteration("identity_lost", ["ball_identity_lost"]);
    return;
  }

  const elapsedThinks = thinkSequence - current.issuedThinkSeq;
  if (elapsedThinks < 1) return;
  const snapshot = captureResetPhysics(current.targetBall);
  const displacement = snapshot.position
    ? subtractVectors(snapshot.position, current.spec.startPosition)
    : null;
  const displacementMagnitude = displacement ? vectorMagnitude(displacement) : null;
  current.sampleCount += 1;
  current.maxSpeed = Math.max(current.maxSpeed, snapshot.speed ?? 0);
  current.maxZ = Math.max(current.maxZ, snapshot.position?.z ?? current.maxZ);
  current.minZ = Math.min(current.minZ, snapshot.position?.z ?? current.minZ);
  current.maxDisplacement = Math.max(
    current.maxDisplacement,
    displacementMagnitude ?? 0,
  );
  if (elapsedThinks === 1) current.firstThinkDisplacement = displacementMagnitude;
  emitProbe("physics_trial_sample", {
    runSequence: run.runSequence,
    profileId: run.profileId,
    suite: current.spec.suite,
    trialIndex: current.trialIndex,
    elapsedThinks,
    sampleIndex: current.sampleCount,
    sameAuthoritativeBall: current.targetBall === ball,
    sameBallGeneration: current.ballGenerationAtStart === ballGeneration,
    sameResetSequence: current.resetSequenceAtStart === resetCommandSequence,
    displacement,
    displacementMagnitude,
    ...snapshot,
  });

  const { spec } = current;
  if (spec.mode === "wake" && elapsedThinks >= spec.maxThinkCount) {
    const failures = current.maxDisplacement <= 0.001
      ? ["no_bounded_physics_motion"]
      : [];
    finishPhysicsTrialIteration("observation_complete", failures);
    return;
  }
  if (spec.mode === "speed_cap" && elapsedThinks >= spec.maxThinkCount) {
    const failures = current.maxSpeed > spec.maximumObservedSpeed
      ? ["observed_speed_exceeded_cap"]
      : [];
    finishPhysicsTrialIteration("observation_complete", failures, {
      maximumAllowedObservedSpeed: spec.maximumObservedSpeed,
    });
    return;
  }
  if (spec.mode === "drop") {
    const verticalVelocity = snapshot.velocity?.z;
    if (Number.isFinite(verticalVelocity)) {
      if (verticalVelocity < -1) current.sawFalling = true;
      if (current.sawFalling
          && !current.sawBounce
          && current.previousVerticalVelocity < 0
          && verticalVelocity > 0) {
        current.sawBounce = true;
        current.bounceTakeoffSpeed = verticalVelocity;
        current.bounceImpactMinZ = current.minZ;
        current.bounceApexZ = snapshot.position?.z ?? null;
      } else if (current.sawBounce && Number.isFinite(snapshot.position?.z)) {
        current.bounceApexZ = Math.max(
          current.bounceApexZ ?? snapshot.position.z,
          snapshot.position.z,
        );
      }
      if (current.sawBounce
          && current.previousVerticalVelocity > 0
          && verticalVelocity <= 0) {
        const floorZ = current.environment.floorTrace?.end?.z;
        const bounceApexZ = current.bounceApexZ;
        const bounceImpactMinZ = current.bounceImpactMinZ;
        finishPhysicsTrialIteration("first_bounce_apex", [], {
          ...summarizeDropBounce({
            floorZ,
            impactMinZ: bounceImpactMinZ,
            apexZ: bounceApexZ,
          }),
          bounceTakeoffSpeed: current.bounceTakeoffSpeed,
        });
        return;
      }
      current.previousVerticalVelocity = verticalVelocity;
    }
  }
  if (spec.mode === "roll") {
    const floorZ = current.environment.floorTrace?.end?.z;
    if (Number.isFinite(floorZ)
        && snapshot.position
        && snapshot.position.z < floorZ - 1) {
      finishPhysicsTrialIteration("floor_penetration", ["floor_penetration"]);
      return;
    }
    if (elapsedThinks >= 16 && (snapshot.speed ?? Number.POSITIVE_INFINITY) <= 1) {
      finishPhysicsTrialIteration("settled");
      return;
    }
  }
  if (spec.mode === "wall") {
    const velocityProjection = snapshot.velocity
      ? dotVectors(snapshot.velocity, spec.direction)
      : null;
    if (Number.isFinite(velocityProjection)
        && velocityProjection > 1
        && snapshot.velocity) {
      current.lastWallIncomingVelocity = { ...snapshot.velocity };
      current.lastWallIncomingElapsedThinks = elapsedThinks;
    }
    if (Number.isFinite(velocityProjection) && velocityProjection < -1) {
      const normal = normalizedOrNull(current.environment.wallTrace?.normal);
      const incoming = current.lastWallIncomingVelocity ?? spec.initialVelocity;
      const outgoing = snapshot.velocity;
      const incomingNormalProjection = normal
        ? dotVectors(incoming, normal)
        : null;
      const outgoingNormalProjection = normal && outgoing
        ? dotVectors(outgoing, normal)
        : null;
      const incomingNormalSpeed = Number.isFinite(incomingNormalProjection)
        ? Math.max(0, -incomingNormalProjection)
        : null;
      const outgoingNormalSpeed = Number.isFinite(outgoingNormalProjection)
        ? Math.max(0, outgoingNormalProjection)
        : null;
      const incomingTangent = normal && Number.isFinite(incomingNormalProjection)
        ? {
          x: incoming.x - incomingNormalProjection * normal.x,
          y: incoming.y - incomingNormalProjection * normal.y,
          z: incoming.z - incomingNormalProjection * normal.z,
        }
        : null;
      const outgoingTangent = normal
        && outgoing
        && Number.isFinite(outgoingNormalProjection)
        ? {
          x: outgoing.x - outgoingNormalProjection * normal.x,
          y: outgoing.y - outgoingNormalProjection * normal.y,
          z: outgoing.z - outgoingNormalProjection * normal.z,
        }
        : null;
      const incomingTangentSpeed = incomingTangent
        ? vectorMagnitude(incomingTangent)
        : null;
      const outgoingTangentSpeed = outgoingTangent
        ? vectorMagnitude(outgoingTangent)
        : null;
      const incomingSpeed = vectorMagnitude(incoming);
      const outgoingSpeed = outgoing ? vectorMagnitude(outgoing) : null;
      const expectedReflection = normal
        ? {
          x: incoming.x - 2 * dotVectors(incoming, normal) * normal.x,
          y: incoming.y - 2 * dotVectors(incoming, normal) * normal.y,
          z: incoming.z - 2 * dotVectors(incoming, normal) * normal.z,
        }
        : null;
      finishPhysicsTrialIteration("rebound_observed", [], {
        preImpactElapsedThinks: current.lastWallIncomingElapsedThinks,
        preImpactVelocity: incoming,
        postImpactVelocity: outgoing,
        incomingNormalSpeed,
        outgoingNormalSpeed,
        incomingTangentSpeed,
        outgoingTangentSpeed,
        normalRetention: Number.isFinite(incomingNormalSpeed)
          && incomingNormalSpeed > 1e-9
          && Number.isFinite(outgoingNormalSpeed)
          ? outgoingNormalSpeed / incomingNormalSpeed
          : null,
        tangentRetention: Number.isFinite(incomingTangentSpeed)
          && incomingTangentSpeed > 1e-9
          && Number.isFinite(outgoingTangentSpeed)
          ? outgoingTangentSpeed / incomingTangentSpeed
          : null,
        totalSpeedRetention: Number.isFinite(outgoingSpeed)
          && incomingSpeed > 1e-9
          ? outgoingSpeed / incomingSpeed
          : null,
        verticalSpeedDelta: outgoing ? outgoing.z - incoming.z : null,
        normalAngleErrorDegrees: expectedReflection && outgoing
          ? angleBetweenDegrees(expectedReflection, outgoing)
          : null,
      });
      return;
    }
    const wallEnd = current.environment.wallTrace?.end;
    if (wallEnd && snapshot.position) {
      const penetration = dotVectors(
        subtractVectors(snapshot.position, wallEnd),
        spec.direction,
      );
      if (penetration > 1) {
        finishPhysicsTrialIteration("wall_penetration", ["wall_penetration"], {
          penetration,
        });
        return;
      }
    }
  }
  if (["reverse", "near_miss"].includes(spec.mode) && snapshot.position) {
    const axisPosition = snapshot.position[spec.goalAxis];
    const crossed = spec.goalDirection > 0
      ? axisPosition > spec.goalPlane + 2
      : axisPosition < spec.goalPlane - 2;
    const reverseReturned = spec.mode === "reverse"
      ? (spec.goalDirection > 0
        ? axisPosition < spec.goalPlane - 2
        : axisPosition > spec.goalPlane + 2)
      : crossed;
    if (reverseReturned) {
      finishPhysicsTrialIteration("crossing_without_goal");
      return;
    }
  }
  if (elapsedThinks >= spec.maxThinkCount) {
    const failures = spec.mode === "drop"
      ? ["first_bounce_not_observed"]
      : spec.mode === "wall"
        ? ["rebound_not_observed"]
        : spec.mode === "goal"
          ? ["expected_goal_not_observed"]
          : [];
    finishPhysicsTrialIteration("think_limit", failures);
  }
}

function recordResetWriteSample(stage, operation, snapshot) {
  if (!operation.diagnostic) return snapshot;
  operation.diagnosticSnapshots.push({
    stage,
    resetReason: operation.reason,
    resetProfile: operation.profile,
    resetSequence: operation.command.sequence,
    ballGeneration: operation.command.ballGeneration,
    writeAttempt: operation.writeAttempt,
    maximumAttempts: MAX_RESET_WRITE_ATTEMPTS,
    writeIssuedThinkSeq: operation.command.issuedThinkSeq,
    sampleThinkSeq: thinkSequence,
    ...snapshot,
  });
  return snapshot;
}

function emitResetTerminalTrace(trace, complete, stoppedReason) {
  for (const snapshot of trace.writeSnapshots) {
    emitProbe("reset_physics_snapshot", snapshot);
  }
  for (const sample of trace.samples) {
    emitProbe("reset_post_terminal_sample", {
      resetReason: trace.resetReason,
      resetProfile: trace.resetProfile,
      terminalReason: trace.terminalReason,
      resetSequence: trace.resetSequence,
      ballGeneration: trace.ballGeneration,
      writeAttempt: trace.writeAttempt,
      maximumAttempts: MAX_RESET_WRITE_ATTEMPTS,
      terminalThinkSeq: trace.terminalThinkSeq,
      sampleCount: RESET_POST_TERMINAL_SAMPLE_COUNT,
      ...sample,
    });
  }
  emitProbe(complete
    ? "reset_post_terminal_complete"
    : "reset_post_terminal_cancelled", {
    resetReason: trace.resetReason,
    resetProfile: trace.resetProfile,
    terminalReason: trace.terminalReason,
    resetSequence: trace.resetSequence,
    ballGeneration: trace.ballGeneration,
    terminalThinkSeq: trace.terminalThinkSeq,
    samplesCaptured: trace.samples.length,
    samplesExpected: RESET_POST_TERMINAL_SAMPLE_COUNT,
    stoppedReason: stoppedReason ?? null,
  });
}

function observePendingResetTerminalTrace() {
  if (!pendingResetTerminalTrace) return;
  const trace = pendingResetTerminalTrace;
  if (trace.targetBall !== ball) {
    pendingResetTerminalTrace = undefined;
    emitResetTerminalTrace(trace, false, "authoritative_ball_changed");
    return;
  }
  if (trace.ballGeneration !== ballGeneration) {
    pendingResetTerminalTrace = undefined;
    emitResetTerminalTrace(trace, false, "ball_generation_changed");
    return;
  }
  if (!trace.targetBall?.IsValid()) {
    pendingResetTerminalTrace = undefined;
    emitResetTerminalTrace(trace, false, "target_invalid");
    return;
  }
  trace.sampleIndex += 1;
  const snapshot = captureResetPhysics(trace.targetBall);
  trace.samples.push({
    sampleThinkSeq: thinkSequence,
    elapsedThinks: thinkSequence - trace.terminalThinkSeq,
    sampleIndex: trace.sampleIndex,
    sameAuthoritativeBall: snapshot.targetValid && trace.targetBall === ball,
    sameBallGeneration: trace.ballGeneration === ballGeneration,
    sameResetCommandSequence: trace.resetSequence === resetCommandSequence,
    playEnabled,
    ...snapshot,
  });
  if (trace.sampleIndex >= RESET_POST_TERMINAL_SAMPLE_COUNT) {
    pendingResetTerminalTrace = undefined;
    emitResetTerminalTrace(trace, true);
  }
}

function scheduleResetTerminalTrace(operation, terminalReason) {
  if (!operation.diagnostic) return;
  pendingResetTerminalTrace = {
    targetBall: ball,
    resetReason: operation.reason,
    resetProfile: operation.profile,
    terminalReason,
    resetSequence: operation.command.sequence,
    ballGeneration: operation.command.ballGeneration,
    writeAttempt: operation.writeAttempt,
    terminalThinkSeq: thinkSequence,
    sampleIndex: 0,
    samples: [],
    writeSnapshots: operation.diagnosticSnapshots,
  };
}

function cancelPendingResetTerminalTrace(supersededBy) {
  if (!pendingResetTerminalTrace) return;
  const trace = pendingResetTerminalTrace;
  pendingResetTerminalTrace = undefined;
  emitResetTerminalTrace(trace, false, supersededBy);
}

function emitBallFault(event, data) {
  const now = Instance.GetGameTime();
  const key = `${event}:${JSON.stringify(data)}`;
  if (key !== lastBallFaultKey || now - lastBallFaultTime >= 5) {
    emit(event, data);
    lastBallFaultKey = key;
    lastBallFaultTime = now;
  }
}

function countValidNamedBalls() {
  return Instance.FindEntitiesByName(BALL_NAME)
    .filter((candidate) => candidate.IsValid()).length;
}

function findUniqueMarker(name) {
  const matches = Instance.FindEntitiesByName(name)
    .filter((candidate) => candidate.IsValid());
  if (matches.length !== 1
      || matches[0].GetClassName() !== "info_target"
      || !isFiniteVector(matches[0].GetAbsOrigin())) {
    return { found: false, name, matchCount: matches.length };
  }
  return { found: true, entity: matches[0], position: matches[0].GetAbsOrigin() };
}

function bindLabGeometry() {
  playEnabled = false;
  const resetMarker = findUniqueMarker(LAB_LAYOUT.reset.markerName);
  const goalMarkers = LAB_LAYOUT.goals.map((goal) => ({
    goal,
    marker: findUniqueMarker(goal.markerName),
  }));
  const invalid = [resetMarker, ...goalMarkers.map(({ marker }) => marker)]
    .filter((marker) => !marker.found);
  if (invalid.length > 0) {
    resetWritePosition = undefined;
    resetRestPosition = undefined;
    labGoals = [];
    emit("assertion", {
      assertionId: "lab_geometry_ready",
      passed: false,
      reason: "missing_measurement",
      invalidMarkers: invalid.map(({ name, matchCount }) => ({ name, matchCount })),
    });
    return false;
  }

  resetRestPosition = { ...resetMarker.position };
  resetWritePosition = {
    ...resetMarker.position,
    z: resetMarker.position.z + LAB_LAYOUT.reset.writeClearance,
  };
  labGoals = goalMarkers.map(({ goal, marker }) => {
    const lateralAxis = goal.axis === "x" ? "y" : "x";
    const halfHeight = (goal.maximumHeight - goal.minimumHeight) / 2;
    return {
      ...goal,
      plane: marker.position[goal.axis],
      lateralCenter: marker.position[lateralAxis],
      minimumHeight: marker.position.z - halfHeight,
      maximumHeight: marker.position.z + halfHeight,
    };
  });
  emit("assertion", {
    assertionId: "lab_geometry_ready",
    passed: true,
    reason: "matched",
    layoutId: LAB_LAYOUT.id,
    resetWritePosition,
    resetRestPosition,
    goals: labGoals,
  });
  return true;
}

function bindBall() {
  const matches = Instance.FindEntitiesByName(BALL_NAME)
    .filter((candidate) => candidate.IsValid());

  if (matches.length !== 1) {
    ball = undefined;
    playEnabled = false;
    emitBallFault(matches.length === 0 ? "ball_invalid" : "duplicate_ball", {
      reason: matches.length === 0 ? "missing_ball" : "multiple_balls",
      matchCount: matches.length,
    });
    return false;
  }

  const candidate = matches[0];
  if (candidate.GetClassName() !== BALL_CLASS || candidate.GetParent()) {
    ball = undefined;
    playEnabled = false;
    emitBallFault("ball_invalid", {
      reason: candidate.GetClassName() !== BALL_CLASS
        ? "wrong_class"
        : "parented_ball",
      className: candidate.GetClassName(),
      hasParent: Boolean(candidate.GetParent()),
    });
    return false;
  }

  const modelEntity = /** @type {import("cs_script/point_script").BaseModelEntity} */ (candidate);
  const modelName = modelEntity.GetModelName();
  const modelScale = modelEntity.GetModelScale();
  if (modelName !== LAB_LAYOUT.ball.model
      || !Number.isFinite(modelScale)
      || Math.abs(modelScale - LAB_LAYOUT.ball.modelScale) > 1e-6) {
    ball = undefined;
    playEnabled = false;
    emitBallFault("ball_invalid", {
      reason: modelName !== LAB_LAYOUT.ball.model
        ? "missing_model"
        : "invalid_transform",
      modelName,
      expectedModel: LAB_LAYOUT.ball.model,
      modelScale,
      expectedModelScale: LAB_LAYOUT.ball.modelScale,
    });
    return false;
  }

  if (lastBoundBall && lastBoundBall !== candidate) {
    ballGeneration += 1;
    const replacement = replaceBallGeneration(goalState, ballGeneration);
    if (!replacement.replaced) {
      emitBallFault("ball_invalid", { reason: replacement.reason });
      ball = undefined;
      playEnabled = false;
      return false;
    }
    goalState = replacement.state;
  }

  ball = candidate;
  lastBoundBall = candidate;
  lastBallFaultKey = undefined;
  lastBallFaultTime = Number.NEGATIVE_INFINITY;
  previousBallPosition = ball.GetAbsOrigin();
  previousBallThinkSequence = thinkSequence;
  emit("ball_bind", {
    reason: "accepted",
    className: ball.GetClassName(),
    entityName: ball.GetEntityName(),
    modelName,
    modelScale,
    hasParent: false,
  });
  return true;
}

function observeResetWrite(command, snapshot) {
  return {
    ballCount: countValidNamedBalls(),
    ballGeneration,
    resetSequence: command.sequence,
    sampleThinkSeq: thinkSequence,
    position: snapshot.position,
    angles: snapshot.angles,
    velocity: snapshot.velocity,
    angularMotionZero: snapshot.angularMotionZero === true,
  };
}

function observeResetSettled(command, stableFromThinkSeq, stableThinkCount) {
  return {
    ballCount: countValidNamedBalls(),
    ballGeneration,
    resetSequence: command.sequence,
    stableThinkCount,
    stableFromThinkSeq,
    sampleThinkSeq: thinkSequence,
    position: ball.GetAbsOrigin(),
    velocity: ball.GetAbsVelocity(),
    angularMotionZero: isZeroVector(ball.GetAbsAngularVelocity()),
  };
}

function applyResetWrite(command, operation) {
  if (operation.diagnostic) {
    recordResetWriteSample(
      "before_write",
      operation,
      captureResetPhysics(ball),
    );
  }
  if (operation.motionProfile === RESET_MOTION_PROFILE_DISABLE_MOTION) {
    Instance.EntFireAtTarget({ target: ball, input: "DisableMotion" });
  }
  ball.Teleport({
    position: command.position,
    angles: command.angles,
    velocity: command.velocity,
    angularVelocity: command.angularVelocity,
  });
  if (operation.diagnostic) {
    recordResetWriteSample(
      "immediate_after_write",
      operation,
      captureResetPhysics(ball),
    );
  }
}

function beginReset(reason) {
  if (physicsTrialRun
      && !physicsTrialRun.waitingForReset
      && reason !== "physics_trial_complete") {
    cancelPhysicsTrialRun(`reset:${reason}`);
  }
  cancelPendingResetTerminalTrace(reason);
  if (resetOperation) {
    emit("reset_end", {
      passed: false,
      reason: "aborted",
      commandSequence: resetOperation.command.sequence,
      commandBallGeneration: resetOperation.command.ballGeneration,
      writeAttempt: resetOperation.writeAttempt,
      maximumAttempts: MAX_RESET_WRITE_ATTEMPTS,
      supersededBy: reason,
    });
  }
  playEnabled = false;
  resetOperation = undefined;
  previousBallPosition = undefined;
  previousBallThinkSequence = undefined;
  lastAcceptedKickByPlayerSlot.clear();

  if (!ball?.IsValid() || !resetWritePosition || !resetRestPosition) {
    emit("reset_end", { passed: false, reason: "ball_invalid" });
    return;
  }

  const profile = reason === "goal"
    ? goalResetProfile
    : GOAL_RESET_PROFILE_CONTACT;
  const writeClearance = profile === GOAL_RESET_PROFILE_RADIUS_CLEARANCE
    ? LAB_LAYOUT.ball.nominalRadius
    : 0;
  const commandPosition = profile === GOAL_RESET_PROFILE_RADIUS_CLEARANCE
    ? {
      ...resetRestPosition,
      z: resetRestPosition.z + writeClearance,
    }
    : { ...resetWritePosition };
  resetCommandSequence += 1;
  const command = createResetCommand(resetCommandSequence, {
    ballGeneration,
    issuedThinkSeq: thinkSequence,
    position: commandPosition,
    restPosition: resetRestPosition,
    angles: RESET_ANGLES,
  });

  resetOperation = {
    command,
    reason,
    profile,
    motionProfile: resetMotionProfile,
    diagnostic: reason === "goal" && goalResetDiagnosticsEnabled,
    diagnosticSnapshots: [],
    stage: "write",
    writeAttempt: 1,
    writeVerification: undefined,
    stableFromThinkSeq: undefined,
    stableThinkCount: 0,
    deadline: Instance.GetGameTime() + RESET_TIMEOUT_SECONDS,
  };
  if (resetOperation.diagnostic) {
    emitProbe("goal_reset_profile_applied", {
      resetReason: reason,
      configuredGoalProfile: goalResetProfile,
      appliedProfile: profile,
      writeClearance,
      position: command.position,
      restPosition: command.restPosition,
      resetSequence: command.sequence,
      ballGeneration: command.ballGeneration,
      diagnosticOnly: true,
    });
  }
  emit("reset_begin", {
    reason,
    commandSequence: command.sequence,
    position: command.position,
    restPosition: command.restPosition,
    angles: command.angles,
    zeroLinearVelocity: true,
    zeroAngularVelocity: true,
    motionProfile: resetOperation.motionProfile,
    leavesMotionDisabled:
      resetOperation.motionProfile === RESET_MOTION_PROFILE_DISABLE_MOTION,
  });

  applyResetWrite(command, resetOperation);
}

function finishResetFailure(reason, details = {}) {
  const operation = resetOperation;
  playEnabled = false;
  emit("reset_end", {
    passed: false,
    reason,
    ...(operation ? {
      commandSequence: operation.command.sequence,
      commandBallGeneration: operation.command.ballGeneration,
      writeAttempt: operation.writeAttempt,
      maximumAttempts: MAX_RESET_WRITE_ATTEMPTS,
    } : {}),
    ...details,
  });
  if (operation) scheduleResetTerminalTrace(operation, reason);
  resetOperation = undefined;
}

function processReset() {
  if (!resetOperation) return;
  if (!ball?.IsValid()) {
    finishResetFailure("ball_invalid");
    return;
  }

  if (resetOperation.stage === "write") {
    if (resetOperation.command.issuedThinkSeq >= thinkSequence) return;
    const snapshot = captureResetPhysics(ball, resetOperation.diagnostic);
    recordResetWriteSample("next_think", resetOperation, snapshot);
    const verification = verifyResetWriteObservation(
      observeResetWrite(resetOperation.command, snapshot),
      resetOperation.command,
    );
    const retryableAngularMotion = !verification.passed
      && verification.reasons.length === 1
      && verification.reasons[0] === "angular_motion"
      && resetOperation.writeAttempt < MAX_RESET_WRITE_ATTEMPTS
      && thinkSequence < Number.MAX_SAFE_INTEGER;
    if (retryableAngularMotion) {
      const failedCommand = resetOperation.command;
      const failedAttempt = resetOperation.writeAttempt;
      emit("reset_write_verify", {
        ...verification,
        writeAttempt: failedAttempt,
        maximumAttempts: MAX_RESET_WRITE_ATTEMPTS,
        retryScheduled: true,
      });
      const retryCommand = createResetCommand(failedCommand.sequence, {
        ballGeneration: failedCommand.ballGeneration,
        issuedThinkSeq: thinkSequence,
        position: failedCommand.position,
        restPosition: failedCommand.restPosition,
        angles: failedCommand.angles,
      });
      resetOperation.command = retryCommand;
      resetOperation.writeAttempt += 1;
      applyResetWrite(retryCommand, resetOperation);
      emitProbe("reset_write_retry", {
        resetSequence: failedCommand.sequence,
        ballGeneration: failedCommand.ballGeneration,
        failedAttempt,
        nextAttempt: resetOperation.writeAttempt,
        maximumAttempts: MAX_RESET_WRITE_ATTEMPTS,
        reasons: verification.reasons,
        positionError: verification.positionError,
        angleError: verification.angleError,
        speed: verification.speed,
        angularMotionZero: verification.angularMotionZero,
        failedSampleThinkSeq: verification.sampleThinkSeq,
        retryIssuedThinkSeq: retryCommand.issuedThinkSeq,
      });
      return;
    }
    emit("reset_write_verify", {
      ...verification,
      writeAttempt: resetOperation.writeAttempt,
      maximumAttempts: MAX_RESET_WRITE_ATTEMPTS,
      retryScheduled: false,
    });
    if (!verification.passed) {
      finishResetFailure("write_not_verified", { reasons: verification.reasons });
      return;
    }
    resetOperation.stage = "settled";
    resetOperation.writeVerification = verification;
    return;
  }

  const position = ball.GetAbsOrigin();
  const velocity = ball.GetAbsVelocity();
  const angularMotionZero = isZeroVector(ball.GetAbsAngularVelocity());
  const positionError = Math.hypot(
    position.x - resetOperation.command.restPosition.x,
    position.y - resetOperation.command.restPosition.y,
    position.z - resetOperation.command.restPosition.z,
  );
  const speed = Math.hypot(velocity.x, velocity.y, velocity.z);
  const stable = Number.isFinite(positionError)
    && Number.isFinite(speed)
    && positionError <= 0.5
    && speed <= 0.1
    && angularMotionZero;

  if (stable) {
    if (resetOperation.stableThinkCount === 0) {
      resetOperation.stableFromThinkSeq = thinkSequence;
    }
    resetOperation.stableThinkCount += 1;
  } else {
    resetOperation.stableFromThinkSeq = undefined;
    resetOperation.stableThinkCount = 0;
  }

  if (resetOperation.stableThinkCount >= 2) {
    const verification = verifyResetSettledObservation(
      observeResetSettled(
        resetOperation.command,
        resetOperation.stableFromThinkSeq,
        resetOperation.stableThinkCount,
      ),
      resetOperation.command,
      resetOperation.writeVerification,
    );
    emit("reset_settle_verify", verification);
    if (!verification.passed) {
      finishResetFailure("not_settled", { reasons: verification.reasons });
      return;
    }

    const unlocked = unlockAfterVerifiedReset(goalState, verification);
    if (!unlocked.unlocked) {
      finishResetFailure("unlock_failed", { coreReason: unlocked.reason });
      return;
    }
    goalState = unlocked.state;
    const completedOperation = resetOperation;
    resetOperation = undefined;
    playEnabled = true;
    previousBallPosition = ball.GetAbsOrigin();
    previousBallThinkSequence = thinkSequence;
    emit("reset_end", {
      passed: true,
      reason: "settled",
      commandSequence: completedOperation.command.sequence,
      commandBallGeneration: completedOperation.command.ballGeneration,
      writeAttempt: completedOperation.writeAttempt,
      maximumAttempts: MAX_RESET_WRITE_ATTEMPTS,
    });
    scheduleResetTerminalTrace(completedOperation, "settled");
    return;
  }

  if (Instance.GetGameTime() > resetOperation.deadline) {
    emit("reset_settle_verify", {
      stage: "settled",
      passed: false,
      reasons: ["not_settled"],
      positionError,
      speed,
      angularMotionZero,
      sampleThinkSeq: thinkSequence,
    });
    finishResetFailure("not_settled");
  }
}

function lineOfSightToBall(player, start, end) {
  const trace = Instance.TraceLine({
    start,
    end,
    ignoreEntity: player,
    ignorePlayers: true,
  });
  return !trace.startedInSolid
    && (!trace.didHit || trace.hitEntity === ball || trace.fraction >= 0.999);
}

function parsePlayerSlot(args) {
  const text = String(args ?? "").trim();
  if (!text) return 0;
  const slot = Number(text.split(/\s+/)[0]);
  return Number.isSafeInteger(slot) && slot >= 0 ? slot : undefined;
}

function weaponSnapshot(weapon) {
  if (!weapon?.IsValid()) return undefined;
  const data = weapon.GetData();
  return {
    className: weapon.GetClassName(),
    entityName: weapon.GetEntityName(),
    dataName: data.GetName(),
    gearSlot: data.GetGearSlot(),
  };
}

function connectedPlayerData(controller, player, playerSlot) {
  const activeWeapon = player.GetActiveWeapon();
  const knife = player.FindWeaponBySlot(CSGearSlot.KNIFE);
  return {
    knife,
    data: {
      playerSlot,
      playerName: controller.GetPlayerName(),
      playerTeam: player.GetTeamNumber(),
      playerAlive: player.IsAlive(),
      position: player.GetAbsOrigin(),
      eyePosition: player.GetEyePosition(),
      eyeAngles: player.GetEyeAngles(),
      activeWeapon: weaponSnapshot(activeWeapon),
      knife: weaponSnapshot(knife),
      activeIsKnife: Boolean(activeWeapon && knife && activeWeapon === knife),
    },
  };
}

function playerSnapshot(playerSlot) {
  if (!Number.isSafeInteger(playerSlot) || playerSlot < 0) {
    return { found: false, reason: "invalid_player_slot", playerSlot: null };
  }
  const controller = Instance.GetPlayerController(playerSlot);
  if (!controller?.IsValid() || !controller.IsConnected()) {
    return { found: false, reason: "player_not_connected", playerSlot };
  }
  const player = controller.GetPlayerPawn();
  if (!player?.IsValid()) {
    return { found: false, reason: "missing_player", playerSlot };
  }
  const snapshot = connectedPlayerData(controller, player, playerSlot);
  return {
    found: true,
    controller,
    player,
    ...snapshot,
  };
}

function probePlayerInputs() {
  if (!playerInputProbeEnabled) return;
  for (const controller of Instance.GetAllPlayerControllers()) {
    if (!controller.IsValid() || !controller.IsConnected()) continue;
    const player = controller.GetPlayerPawn();
    if (!player?.IsValid()) continue;
    const primary = player.WasInputJustPressed(CSInputs.ATTACK);
    const secondary = player.WasInputJustPressed(CSInputs.ATTACK2);
    if (!primary && !secondary) continue;
    const playerSlot = controller.GetPlayerSlot();
    const snapshot = connectedPlayerData(controller, player, playerSlot);
    emitProbe("input_edge", {
      primary,
      secondary,
      ...snapshot.data,
    });
  }
}

function handleKnifeAttack({ weapon, attackType }) {
  const attack = attackType === CSWeaponAttackType.PRIMARY
    ? "primary"
    : attackType === CSWeaponAttackType.SECONDARY
      ? "secondary"
      : "invalid";
  if (playerInputProbeEnabled) {
    emitProbe("knife_callback", {
      attackType: attack,
      weaponValid: Boolean(weapon?.IsValid()),
      weapon: weaponSnapshot(weapon),
    });
  }
  const weaponValid = Boolean(weapon?.IsValid());
  const player = weaponValid ? weapon.GetOwner() : undefined;
  const playerValid = Boolean(player?.IsValid());
  const controller = playerValid ? player.GetPlayerController() : undefined;
  const controllerValid = Boolean(controller?.IsValid() && controller.IsConnected());
  const playerSlot = controllerValid ? controller.GetPlayerSlot() : undefined;
  const playerTeam = playerValid ? player.GetTeamNumber() : undefined;
  const playerEligible = playerValid
    && controllerValid
    && Number.isSafeInteger(playerSlot)
    && playerSlot >= 0
    && (playerTeam === 2 || playerTeam === 3);
  if (!ball?.IsValid()) bindBall();
  const eyePosition = playerValid ? player.GetEyePosition() : undefined;
  const ballPosition = ball?.GetAbsOrigin();
  const lineOfSight = Boolean(
    player
      && ball
      && eyePosition
      && ballPosition
      && lineOfSightToBall(player, eyePosition, ballPosition),
  );
  const lastAcceptedKickTime = Number.isSafeInteger(playerSlot)
    ? lastAcceptedKickByPlayerSlot.get(playerSlot) ?? Number.NEGATIVE_INFINITY
    : Number.NEGATIVE_INFINITY;

  const result = computeKick({
    playerAlive: playerValid && player.IsAlive(),
    playerEligible,
    playEnabled: playEnabled
      && matchAllowsBallInteraction(matchState)
      && !Instance.IsFreezePeriod(),
    eyePosition,
    eyeAngles: playerValid ? player.GetEyeAngles() : undefined,
    ballPosition,
    ballVelocity: ball?.GetAbsVelocity(),
    attackType: attack,
    isDucking: playerValid && player.IsDucking(),
    lineOfSight,
    now: Instance.GetGameTime(),
    lastAcceptedKickTime,
  });

  emit("kick_result", {
    playerSlot: Number.isSafeInteger(playerSlot) ? playerSlot : null,
    playerTeam: Number.isSafeInteger(playerTeam) ? playerTeam : null,
    playerEligible,
    attackType: attack,
    isDucking: playerValid && player.IsDucking(),
    isDucked: playerValid && player.IsDucked(),
    lineOfSight,
    ...result,
  });

  if (!result.accepted || !ball?.IsValid()) return;
  const targetBall = ball;
  const positionBeforeWrite = { ...ballPosition };
  const commandedVelocity = { ...result.velocity };
  Instance.EntFireAtTarget({ target: targetBall, input: "EnableMotion" });
  Instance.EntFireAtTarget({ target: targetBall, input: "Wake" });
  targetBall.Teleport({ velocity: commandedVelocity });
  cancelPendingResetTerminalTrace("kick_write");
  if (Number.isSafeInteger(playerSlot)) {
    lastAcceptedKickByPlayerSlot.set(playerSlot, Instance.GetGameTime());
  }
  for (const pending of pendingKickWriteObservations) {
    if (pending.targetBall === targetBall) pending.laterAcceptedWriteCount += 1;
  }
  if (playerInputProbeEnabled) {
    const dispatched = emitProbe("kick_write_dispatched", {
      playerSlot: Number.isSafeInteger(playerSlot) ? playerSlot : null,
      attackType: attack,
      positionBeforeWrite,
      commandedVelocity,
      velocityImmediatelyAfterWrite: targetBall.GetAbsVelocity(),
      writeThinkSeq: thinkSequence,
      ballGenerationAtWrite: ballGeneration,
      resetCommandSequenceAtWrite: resetCommandSequence,
    });
    pendingKickWriteObservations.push({
      writeProbeSeq: dispatched.seq,
      targetBall,
      ballGenerationAtWrite: ballGeneration,
      resetCommandSequenceAtWrite: resetCommandSequence,
      writeThinkSeq: thinkSequence,
      playerSlot: Number.isSafeInteger(playerSlot) ? playerSlot : null,
      attackType: attack,
      positionBeforeWrite,
      commandedVelocity,
      laterAcceptedWriteCount: 0,
    });
  }
}

function observePendingKickWrites() {
  if (pendingKickWriteObservations.length === 0) return;
  const observations = pendingKickWriteObservations;
  pendingKickWriteObservations = [];
  for (const pending of observations) {
    const targetValid = Boolean(pending.targetBall?.IsValid());
    const position = targetValid ? pending.targetBall.GetAbsOrigin() : null;
    const velocity = targetValid ? pending.targetBall.GetAbsVelocity() : null;
    const displacement = isFiniteVector(position)
      && isFiniteVector(pending.positionBeforeWrite)
      ? {
        x: position.x - pending.positionBeforeWrite.x,
        y: position.y - pending.positionBeforeWrite.y,
        z: position.z - pending.positionBeforeWrite.z,
      }
      : null;
    emitProbe("kick_write_observation", {
      writeProbeSeq: pending.writeProbeSeq,
      playerSlot: pending.playerSlot,
      attackType: pending.attackType,
      targetValid,
      sameAuthoritativeBall: targetValid && pending.targetBall === ball,
      sameBallGeneration: pending.ballGenerationAtWrite === ballGeneration,
      sameResetCommandSequence:
        pending.resetCommandSequenceAtWrite === resetCommandSequence,
      writeThinkSeq: pending.writeThinkSeq,
      observationThinkSeq: thinkSequence,
      elapsedThinks: thinkSequence - pending.writeThinkSeq,
      positionBeforeWrite: pending.positionBeforeWrite,
      position,
      displacement,
      displacementMagnitude: isFiniteVector(displacement)
        ? Math.hypot(displacement.x, displacement.y, displacement.z)
        : null,
      commandedVelocity: pending.commandedVelocity,
      velocity,
      speed: isFiniteVector(velocity)
        ? Math.hypot(velocity.x, velocity.y, velocity.z)
        : null,
      laterAcceptedWriteCount: pending.laterAcceptedWriteCount,
    });
  }
}

function evaluateGoal(previous, current, previousThinkSeq, currentThinkSeq) {
  for (const goal of labGoals) {
    const candidate = detectGoalPlaneCrossing(previous, current, goal, {
      ballGeneration,
      resetSequence: goalState.resetSequence,
      previousThinkSeq,
      currentThinkSeq,
    });
    if (!candidate.crossed) continue;

    emit("goal_candidate", candidate);
    const committed = acceptGoalCandidate(goalState, candidate);
    if (!committed.accepted) {
      emit("goal_ignored", {
        accepted: false,
        reason: committed.reason,
        goalId: candidate.goalId,
      });
      return false;
    }
    goalState = committed.state;
    emit("goal_commit", {
      accepted: true,
      reason: "accepted",
      goalId: candidate.goalId,
      scoringTeam: candidate.scoringTeam,
    });
    if (matchCountsGoals(matchState)) {
      const matchGoal = recordMatchGoal(
        matchState,
        candidate.scoringTeam,
        Instance.GetGameTime(),
      );
      if (matchGoal.accepted) {
        matchState = matchGoal.state;
        emit("match_goal", {
          accepted: true,
          reason: matchGoal.reason,
          goalId: candidate.goalId,
          scoringTeam: candidate.scoringTeam,
          ...matchSnapshot(),
        });
        emitMatchState(matchGoal.reason, {
          goalId: candidate.goalId,
          scoringTeam: candidate.scoringTeam,
        });
      } else {
        emit("match_goal", {
          accepted: false,
          reason: matchGoal.reason,
          goalId: candidate.goalId,
          scoringTeam: candidate.scoringTeam,
          ...matchSnapshot(),
        });
      }
    } else {
      emit("match_goal", {
        accepted: false,
        reason: "warmup_goal",
        goalId: candidate.goalId,
        scoringTeam: candidate.scoringTeam,
        ...matchSnapshot(),
      });
    }
    const activePhysicsTrial = physicsTrialRun?.current;
    if (activePhysicsTrial
        && ["goal", "reverse", "near_miss"].includes(activePhysicsTrial.spec.mode)) {
      const expected = activePhysicsTrial.spec.expectGoal === true
        && activePhysicsTrial.spec.goalId === candidate.goalId;
      finishPhysicsTrialIteration(
        "goal_commit",
        expected ? [] : ["unexpected_goal_commit"],
        {
          expectedGoal: activePhysicsTrial.spec.expectGoal,
          expectedGoalId: activePhysicsTrial.spec.goalId,
          observedGoalId: candidate.goalId,
          observedScoringTeam: candidate.scoringTeam,
        },
        true,
      );
    }
    beginReset("goal");
    return true;
  }
  return false;
}

function sampleBallState() {
  if (!stateTelemetryEnabled) return;
  if (!ball?.IsValid()) return;
  const now = Instance.GetGameTime();
  if (now < nextStateSampleTime) return;
  const position = ball.GetAbsOrigin();
  const velocity = ball.GetAbsVelocity();
  emit("state_sample", {
    source: "server",
    position,
    velocity,
    speed: Math.hypot(velocity.x, velocity.y, velocity.z),
    angularMotionZero: isZeroVector(ball.GetAbsAngularVelocity()),
  });
  do {
    nextStateSampleTime += STATE_SAMPLE_PERIOD_SECONDS;
  } while (nextStateSampleTime <= now);
}

function think() {
  thinkSequence += 1;
  Instance.SetNextThink(Instance.GetGameTime());
  advanceMatchRuntime();
  renderMatchHud();
  observePendingResetTerminalTrace();
  observePendingKickWrites();
  probePlayerInputs();

  if (!ball?.IsValid()) {
    if (!bindBall()) return;
    beginReset("rebind");
  }

  observePhysicsTrial();
  sampleBallState();
  if (resetOperation) {
    processReset();
    return;
  }

  const currentPosition = ball.GetAbsOrigin();
  if (playEnabled
      && matchAllowsBallInteraction(matchState)
      && !Instance.IsFreezePeriod()
      && previousBallPosition
      && Number.isSafeInteger(previousBallThinkSequence)) {
    const scored = evaluateGoal(
      previousBallPosition,
      currentPosition,
      previousBallThinkSequence,
      thinkSequence,
    );
    if (scored) return;
  }
  previousBallPosition = currentPosition;
  previousBallThinkSequence = thinkSequence;
}

function activate(reason) {
  configureBotFreeSession();
  emit("run_start", {
    reason,
    mode: Instance.IsDedicatedServer() ? "dedicated" : "listen",
    apiSha256: "2da5d7d10ffcea1aac52e668cf153974a3d973aeb8e7dc9a15fb8a2227b50bf9",
    kickWrite: "Teleport({velocity})",
    angularKickWrite: false,
    resetMotionProfile,
    layoutId: LAB_LAYOUT.id,
  });
  nextStateSampleTime = Instance.GetGameTime();
  nextMatchHudTime = 0;
  lastRenderedMatchSecond = undefined;
  emitMatchState(reason);
  if (bindLabGeometry() && bindBall()) beginReset(reason);
  Instance.SetNextThink(Instance.GetGameTime());
}

function configureBotFreeSession() {
  Instance.ServerCommand("bot_quota 0");
  Instance.ServerCommand("bot_kick");
}

Instance.OnActivate(() => activate("activate"));
Instance.OnScriptReload({ after: () => activate("script_reload") });
Instance.OnRoundStart(() => {
  configureBotFreeSession();
  if (labGoals.length !== LAB_LAYOUT.goals.length) bindLabGeometry();
  if (!ball?.IsValid()) bindBall();
  if (ball?.IsValid()) beginReset("round_start");
});
Instance.OnKnifeAttack(handleKnifeAttack);
Instance.OnPlayerChat(handleMatchChat);
Instance.OnPlayerDisconnect(handlePlayerDisconnect);
Instance.OnModifyPlayerDamage(() => ({ abort: true }));
Instance.OnScriptInput("phase1_smoke", () => {
  const ready = Boolean(ball?.IsValid())
    && playEnabled
    && labGoals.length === LAB_LAYOUT.goals.length;
  Instance.Msg(ready ? "SM2_PHASE1_SMOKE_OK" : "SM2_PHASE1_SMOKE_BLOCKED");
  emit("assertion", {
    assertionId: "script_input_smoke",
    passed: ready,
    reason: ready ? "matched" : "precondition_blocked",
  });
});
Instance.RegisterCheatCommand("sm2match_start", () => runMatchAction("start"));
Instance.RegisterCheatCommand("sm2match_restart", () => runMatchAction("restart"));
Instance.RegisterCheatCommand("sm2match_pause", () => runMatchAction("pause"));
Instance.RegisterCheatCommand("sm2match_resume", () => runMatchAction("resume"));
Instance.RegisterCheatCommand("sm2match_stop", () => runMatchAction("stop"));
Instance.RegisterCheatCommand("sm2match_status", () => runMatchAction("status"));
Instance.RegisterCheatCommand("sm2lab_goal_reset_profile", (args) => {
  const requestedProfile = String(args ?? "").trim().toLowerCase();
  if (requestedProfile === "") {
    emitProbe("goal_reset_profile_configuration", {
      accepted: true,
      changed: false,
      requestedProfile: null,
      activeProfile: goalResetProfile,
      diagnosticsEnabled: goalResetDiagnosticsEnabled,
      supportedProfiles: GOAL_RESET_PROFILES,
      diagnosticOnly: true,
    });
    return;
  }
  if (!GOAL_RESET_PROFILES.includes(requestedProfile)) {
    emitProbe("goal_reset_profile_configuration", {
      accepted: false,
      reason: "invalid_argument",
      requestedProfile,
      activeProfile: goalResetProfile,
      diagnosticsEnabled: goalResetDiagnosticsEnabled,
      supportedProfiles: GOAL_RESET_PROFILES,
      diagnosticOnly: true,
    });
    return;
  }
  if (resetOperation) {
    emitProbe("goal_reset_profile_configuration", {
      accepted: false,
      reason: "reset_in_progress",
      requestedProfile,
      activeProfile: goalResetProfile,
      diagnosticsEnabled: goalResetDiagnosticsEnabled,
      supportedProfiles: GOAL_RESET_PROFILES,
      diagnosticOnly: true,
    });
    return;
  }
  const changed = goalResetProfile !== requestedProfile
    || !goalResetDiagnosticsEnabled;
  goalResetProfile = requestedProfile;
  goalResetDiagnosticsEnabled = true;
  emitProbe("goal_reset_profile_configuration", {
    accepted: true,
    changed,
    requestedProfile,
    activeProfile: goalResetProfile,
    diagnosticsEnabled: true,
    supportedProfiles: GOAL_RESET_PROFILES,
    diagnosticOnly: true,
  });
});
Instance.RegisterCheatCommand("sm2lab_reset_motion_profile", (args) => {
  const requestedProfile = String(args ?? "").trim().toLowerCase();
  if (requestedProfile === "") {
    emitProbe("reset_motion_profile_configuration", {
      accepted: true,
      changed: false,
      requestedProfile: null,
      activeProfile: resetMotionProfile,
      supportedProfiles: RESET_MOTION_PROFILES,
      diagnosticOnly: true,
    });
    return;
  }
  if (!RESET_MOTION_PROFILES.includes(requestedProfile)) {
    emitProbe("reset_motion_profile_configuration", {
      accepted: false,
      reason: "invalid_argument",
      requestedProfile,
      activeProfile: resetMotionProfile,
      supportedProfiles: RESET_MOTION_PROFILES,
      diagnosticOnly: true,
    });
    return;
  }
  if (resetOperation || physicsTrialRun) {
    emitProbe("reset_motion_profile_configuration", {
      accepted: false,
      reason: resetOperation ? "reset_in_progress" : "trial_in_progress",
      requestedProfile,
      activeProfile: resetMotionProfile,
      supportedProfiles: RESET_MOTION_PROFILES,
      diagnosticOnly: true,
    });
    return;
  }
  const changed = resetMotionProfile !== requestedProfile;
  resetMotionProfile = requestedProfile;
  emitProbe("reset_motion_profile_configuration", {
    accepted: true,
    changed,
    requestedProfile,
    activeProfile: resetMotionProfile,
    supportedProfiles: RESET_MOTION_PROFILES,
    diagnosticOnly: true,
  });
});
Instance.RegisterCheatCommand("sm2lab_reset", () => beginReset("console"));
Instance.RegisterCheatCommand("sm2lab_physics_trial", (args) => {
  const request = parsePhysicsTrialRequest(args);
  if (!request.accepted) {
    emitProbe("physics_trial_configuration", {
      accepted: false,
      reason: request.reason,
      requestedProfile: request.profileId ?? null,
      supportedProfiles: PHYSICS_TRIAL_PROFILE_IDS,
    });
    return;
  }
  if (physicsTrialRun || resetOperation || !playEnabled || !ball?.IsValid()) {
    emitProbe("physics_trial_configuration", {
      accepted: false,
      reason: physicsTrialRun
        ? "trial_in_progress"
        : resetOperation
          ? "reset_in_progress"
          : "lab_not_ready",
      requestedProfile: request.profileId,
      requestedTrials: request.trialCount,
      supportedProfiles: PHYSICS_TRIAL_PROFILE_IDS,
    });
    return;
  }
  physicsTrialRunSequence += 1;
  const spec = createPhysicsTrialSpec(request.profileId);
  physicsTrialRun = {
    runSequence: physicsTrialRunSequence,
    profileId: request.profileId,
    spec,
    trialCount: request.trialCount,
    trialsCompleted: 0,
    hardFailures: [],
    current: undefined,
    pendingNextTrial: false,
    waitingForReset: false,
  };
  emitProbe("physics_trial_configuration", {
    accepted: true,
    runSequence: physicsTrialRun.runSequence,
    profileId: request.profileId,
    suite: spec.suite,
    mode: spec.mode,
    qualification: spec.qualification,
    trialCount: request.trialCount,
    supportedProfiles: PHYSICS_TRIAL_PROFILE_IDS,
  });
  physicsTrialRun.pendingNextTrial = true;
});
Instance.RegisterCheatCommand("sm2lab_probe_inputs", (args) => {
  const mode = String(args ?? "").trim().toLowerCase();
  if (mode !== "" && mode !== "on" && mode !== "off") {
    emitProbe("probe_configuration", {
      accepted: false,
      reason: "invalid_argument",
      mode,
    });
    return;
  }
  playerInputProbeEnabled = mode === ""
    ? !playerInputProbeEnabled
    : mode === "on";
  emitProbe("probe_configuration", {
    accepted: true,
    enabled: playerInputProbeEnabled,
  });
});
Instance.RegisterCheatCommand("sm2lab_telemetry", (args) => {
  const mode = String(args ?? "").trim().toLowerCase();
  if (mode !== "" && mode !== "on" && mode !== "off") {
    emitProbe("telemetry_configuration", {
      accepted: false,
      reason: "invalid_argument",
      mode,
    });
    return;
  }
  stateTelemetryEnabled = mode === ""
    ? !stateTelemetryEnabled
    : mode === "on";
  nextStateSampleTime = Instance.GetGameTime();
  emitProbe("telemetry_configuration", {
    accepted: true,
    enabled: stateTelemetryEnabled,
  });
});
Instance.RegisterCheatCommand("sm2lab_player_status", (args) => {
  const snapshot = playerSnapshot(parsePlayerSlot(args));
  emitProbe("player_status", {
    found: snapshot.found,
    reason: snapshot.reason,
    playerSlot: snapshot.playerSlot,
    ...snapshot.data,
  });
});
Instance.RegisterCheatCommand("sm2lab_prepare_player", (args) => {
  const snapshot = playerSnapshot(parsePlayerSlot(args));
  const rejectionReason = !snapshot.found
    ? snapshot.reason ?? "missing_player"
    : !snapshot.data.playerAlive
      ? "player_not_alive"
      : ![2, 3].includes(snapshot.data.playerTeam)
        ? "ineligible_team"
        : !snapshot.knife?.IsValid()
          ? "missing_knife"
          : undefined;
  if (rejectionReason) {
    emitProbe("switch_rejected", {
      reason: rejectionReason,
      playerSlot: snapshot.playerSlot,
      ...snapshot.data,
    });
    return;
  }
  snapshot.player.SwitchToWeapon(snapshot.knife);
  emitProbe("switch_requested", {
    requestedWeapon: snapshot.data.knife,
    ...snapshot.data,
  });
});
Instance.RegisterCheatCommand("sm2lab_status", () => {
  emit("assertion", {
    assertionId: "api_smoke_ready",
    passed: Boolean(ball?.IsValid()) && playEnabled,
    reason: Boolean(ball?.IsValid()) && playEnabled ? "matched" : "precondition_blocked",
    scope: "bind_and_reset_readiness_only",
    ballValid: Boolean(ball?.IsValid()),
    playEnabled,
    resetPending: Boolean(resetOperation),
    resetMotionProfile,
    match: matchSnapshot(),
  });
});
Instance.SetThink(think);

import assert from "node:assert/strict";
import path from "node:path";
import test from "node:test";
import vm from "node:vm";
import { fileURLToPath } from "node:url";

import { bundlePhase1Adapter } from "../tools/bundle-phase1-adapter.mjs";

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const STATIC_IMPORT = /^\s*import\s*\{[\s\S]*?\}\s*from\s*["']([^"']+)["'];/gm;

test("phase 1 runtime bundle is deterministic and has no relative imports", () => {
  const first = bundlePhase1Adapter(projectRoot);
  const second = bundlePhase1Adapter(projectRoot);
  assert.equal(first.code, second.code);
  assert.match(first.sourceSha256, /^[a-f0-9]{64}$/);
  assert.deepEqual(first.sources, [
    "src/ball-lab/core/vector.js",
    "src/ball-lab/core/goal.js",
    "src/ball-lab/core/kick.js",
    "src/ball-lab/core/reset.js",
    "src/ball-lab/core/cap.js",
    "src/ball-lab/core/match.js",
    "src/ball-lab/layout.js",
    "src/ball-lab/physics-diagnostics.js",
    "src/ball-lab/engine/adapter.js",
  ]);
  const specifiers = [...first.code.matchAll(STATIC_IMPORT)]
    .map((match) => match[1]);
  assert.deepEqual(specifiers, ["cs_script/point_script"]);
  assert.match(
    first.code,
    /import\s*\{\s*CSGearSlot,\s*CSInputs,\s*CSWeaponAttackType,\s*Instance,?\s*\}\s*from\s*"cs_script\/point_script";/,
  );
  assert.doesNotMatch(first.code, /\bfrom\s*["']\./);
  assert.doesNotMatch(first.code, /^\s*export\b/m);
  assert.match(first.code, /Instance\.ServerCommand\("bot_quota 0"\)/);
  assert.match(first.code, /Instance\.ServerCommand\("bot_kick"\)/);
});

test("phase 1 runtime bundle exercises the audited point_script probe surface", () => {
  const registrations = {
    commands: new Map(),
    inputs: new Map(),
  };
  const messages = [];
  let switchRequests = 0;
  let playerSlot = 0;
  let wakeRequests = 0;
  let enableMotionRequests = 0;
  let disableMotionRequests = 0;
  let velocityWrites = 0;
  const teamAssignments = [];
  const teleportCalls = [];
  let exposeTestBallByName = false;
  let ballPosition = { x: 60, y: 0, z: 24 };
  let ballVelocity = { x: 0, y: 0, z: 0 };
  let ballAngles = { pitch: 0, yaw: 0, roll: 0 };
  let ballAngularVelocity = { x: 0, y: 0, z: 0 };
  let ballGroundEntity;
  let groundReadThrows = false;
  const worldGround = {
    IsValid: () => true,
    IsWorld: () => true,
    GetClassName: () => "worldspawn",
    GetEntityName: () => "",
  };
  const testBall = {
    IsValid: () => true,
    GetAbsOrigin: () => ({ ...ballPosition }),
    GetAbsVelocity: () => ({ ...ballVelocity }),
    GetAbsAngles: () => ({ ...ballAngles }),
    GetAbsAngularVelocity: () => ({ ...ballAngularVelocity }),
    GetGroundEntity: () => {
      if (groundReadThrows) throw new Error("diagnostic ground read failed");
      return ballGroundEntity;
    },
    Teleport: ({ position, angles, velocity, angularVelocity }) => {
      assert.ok(velocity);
      velocityWrites += 1;
      teleportCalls.push({
        position: position ? { ...position } : undefined,
        angles: angles ? { ...angles } : undefined,
        velocity: { ...velocity },
        angularVelocity: angularVelocity ? { ...angularVelocity } : undefined,
      });
      if (position) ballPosition = { ...position };
      if (angles) ballAngles = { ...angles };
      ballVelocity = { ...velocity };
      if (angularVelocity) ballAngularVelocity = { ...angularVelocity };
    },
  };
  const knifeData = {
    GetName: () => "weapon_knife_t",
    GetGearSlot: () => 2,
  };
  const gunData = {
    GetName: () => "weapon_glock",
    GetGearSlot: () => 1,
  };
  const gun = {
    IsValid: () => true,
    GetClassName: () => "weapon_glock",
    GetEntityName: () => "",
    GetData: () => gunData,
  };
  const knife = {
    IsValid: () => true,
    GetClassName: () => "weapon_knife",
    GetEntityName: () => "",
    GetData: () => knifeData,
  };
  const player = {
    IsValid: () => true,
    IsAlive: () => true,
    IsDucking: () => false,
    IsDucked: () => false,
    GetTeamNumber: () => 2,
    GetAbsOrigin: () => ({ x: 100, y: 0, z: 0 }),
    GetEyePosition: () => ({ x: 100, y: 0, z: 64 }),
    GetEyeAngles: () => ({ pitch: 0, yaw: 180, roll: 0 }),
    GetActiveWeapon: () => gun,
    FindWeaponBySlot: (slot) => (slot === 2 ? knife : undefined),
    WasInputJustPressed: (input) => input === (1 << 8),
    SwitchToWeapon: (weapon) => {
      assert.equal(weapon, knife);
      switchRequests += 1;
    },
  };
  const controller = {
    IsValid: () => true,
    IsConnected: () => true,
    GetPlayerPawn: () => player,
    GetPlayerSlot: () => playerSlot,
    GetPlayerName: () => "Test Player",
    JoinTeam: (team) => teamAssignments.push({ playerSlot, team }),
  };
  const controller2 = {
    IsValid: () => true,
    IsConnected: () => true,
    GetPlayerSlot: () => 2,
    GetPlayerName: () => "Second Player",
    JoinTeam: (team) => teamAssignments.push({ playerSlot: 2, team }),
  };
  const disconnectedController = {
    IsValid: () => true,
    IsConnected: () => false,
    GetPlayerPawn: () => { throw new Error("disconnected pawn must not be read"); },
  };
  player.GetPlayerController = () => controller;
  knife.GetOwner = () => player;
  const Instance = {
    OnActivate: (callback) => { registrations.activate = callback; },
    OnScriptReload: (callbacks) => { registrations.reload = callbacks; },
    OnRoundStart: (callback) => { registrations.roundStart = callback; },
    OnKnifeAttack: (callback) => { registrations.knifeAttack = callback; },
    OnPlayerChat: (callback) => { registrations.playerChat = callback; },
    OnPlayerDisconnect: (callback) => { registrations.playerDisconnect = callback; },
    OnModifyPlayerDamage: (callback) => { registrations.modifyPlayerDamage = callback; },
    OnScriptInput: (name, callback) => registrations.inputs.set(name, callback),
    RegisterCheatCommand: (name, callback) => registrations.commands.set(name, callback),
    SetThink: (callback) => { registrations.think = callback; },
    SetNextThink: () => {},
    GetMapName: () => "soccermod_phase1_lab",
    GetGameTime: () => 10,
    GetPlayerController: (slot) => (
      slot === 0
        ? controller
        : slot === 1
          ? disconnectedController
          : slot === 2
            ? controller2
            : undefined
    ),
    GetAllPlayerControllers: () => [disconnectedController, controller],
    FindEntitiesByName: (name) => (
      exposeTestBallByName && name === "sm_ball" ? [testBall] : []
    ),
    IsFreezePeriod: () => false,
    TraceLine: () => ({ startedInSolid: false, didHit: false, fraction: 1 }),
    EntFireAtTarget: ({ target, input }) => {
      assert.equal(target, testBall);
      assert.ok(["Wake", "EnableMotion", "DisableMotion"].includes(input));
      if (input === "Wake") wakeRequests += 1;
      if (input === "EnableMotion") enableMotionRequests += 1;
      if (input === "DisableMotion") {
        disableMotionRequests += 1;
        ballVelocity = { x: 0, y: 0, z: 0 };
        ballAngularVelocity = { x: 0, y: 0, z: 0 };
      }
    },
    Msg: (message) => messages.push(message),
  };
  const bundled = bundlePhase1Adapter(projectRoot).code.replace(
    /^import\s*\{[\s\S]*?\}\s*from\s*"cs_script\/point_script";$/m,
    "const { CSGearSlot, CSInputs, CSWeaponAttackType, Instance } = globalThis.__csScript;",
  );
  const context = vm.createContext({
    __csScript: {
      CSGearSlot: Object.freeze({ KNIFE: 2 }),
      CSInputs: Object.freeze({ ATTACK: 1 << 8, ATTACK2: 1 << 9 }),
      CSWeaponAttackType: Object.freeze({ PRIMARY: 0, SECONDARY: 1 }),
      Instance,
    },
  });
  vm.runInContext(bundled, context);
  assert.equal(typeof registrations.activate, "function");
  assert.equal(typeof registrations.reload?.after, "function");
  assert.equal(typeof registrations.roundStart, "function");
  assert.equal(typeof registrations.knifeAttack, "function");
  assert.equal(typeof registrations.playerChat, "function");
  assert.equal(typeof registrations.playerDisconnect, "function");
  assert.equal(typeof registrations.modifyPlayerDamage, "function");
  assert.equal(typeof registrations.think, "function");
  assert.equal(typeof registrations.inputs.get("phase1_smoke"), "function");
  assert.equal(typeof registrations.commands.get("sm2match_start"), "function");
  assert.equal(typeof registrations.commands.get("sm2match_restart"), "function");
  assert.equal(typeof registrations.commands.get("sm2match_pause"), "function");
  assert.equal(typeof registrations.commands.get("sm2match_resume"), "function");
  assert.equal(typeof registrations.commands.get("sm2match_stop"), "function");
  assert.equal(typeof registrations.commands.get("sm2match_status"), "function");
  assert.equal(typeof registrations.commands.get("sm2lab_goal_reset_profile"), "function");
  assert.equal(typeof registrations.commands.get("sm2lab_reset_motion_profile"), "function");
  assert.equal(typeof registrations.commands.get("sm2lab_reset"), "function");
  assert.equal(typeof registrations.commands.get("sm2lab_physics_trial"), "function");
  assert.equal(typeof registrations.commands.get("sm2lab_probe_inputs"), "function");
  assert.equal(typeof registrations.commands.get("sm2lab_telemetry"), "function");
  assert.equal(typeof registrations.commands.get("sm2lab_player_status"), "function");
  assert.equal(typeof registrations.commands.get("sm2lab_prepare_player"), "function");
  assert.equal(typeof registrations.commands.get("sm2lab_status"), "function");
  assert.equal(registrations.modifyPlayerDamage({}).abort, true);

  const beforeMatchStatus = messages.length;
  registrations.playerChat({ player: controller, text: "!status", team: 0 });
  const matchStatusRecords = messages.slice(beforeMatchStatus)
    .filter((message) => message.startsWith("[SM2LAB] "))
    .map((message) => JSON.parse(message.slice("[SM2LAB] ".length)));
  assert.deepEqual(
    matchStatusRecords.map(({ event }) => event),
    ["match_chat_command", "match_state"],
  );
  assert.equal(matchStatusRecords[1].data.phase, "warmup");
  assert.deepEqual(matchStatusRecords[1].data.scores, { 2: 0, 3: 0 });

  const beforeCapStatus = messages.length;
  registrations.playerChat({ player: controller, text: "!capstatus", team: 0 });
  const capStatusRecords = messages.slice(beforeCapStatus)
    .filter((message) => message.startsWith("[SM2LAB] "))
    .map((message) => JSON.parse(message.slice("[SM2LAB] ".length)));
  assert.deepEqual(capStatusRecords.map(({ event }) => event), ["cap_state"]);
  assert.equal(capStatusRecords[0].data.phase, "idle");

  registrations.commands.get("sm2lab_player_status")("0");
  registrations.commands.get("sm2lab_player_status")("1");
  registrations.commands.get("sm2lab_prepare_player")("0");
  registrations.commands.get("sm2lab_probe_inputs")("on");
  registrations.think();
  const beforeKnifeCallback = messages.length;
  registrations.knifeAttack({
    weapon: knife,
    attackType: 0,
  });

  assert.equal(switchRequests, 1);
  const probeRecords = messages
    .filter((message) => message.startsWith("[SM2PROBE] "))
    .map((message) => JSON.parse(message.slice("[SM2PROBE] ".length)));
  assert.deepEqual(
    probeRecords.map(({ event }) => event),
    [
      "player_status",
      "player_status",
      "switch_requested",
      "probe_configuration",
      "input_edge",
      "knife_callback",
    ],
  );
  assert.ok(probeRecords.every(
    ({ schema }) => schema === "cs2-soccermod.diagnostic-probe/1",
  ));
  assert.equal(probeRecords[0].data.activeIsKnife, false);
  assert.equal(probeRecords[0].data.activeWeapon.className, "weapon_glock");
  assert.equal(probeRecords[1].data.found, false);
  assert.equal(probeRecords[1].data.reason, "player_not_connected");
  assert.equal(probeRecords[2].data.requestedWeapon.gearSlot, 2);
  assert.equal(probeRecords[2].data.activeIsKnife, false);
  assert.equal(Object.hasOwn(probeRecords[2].data, "passed"), false);
  assert.equal(probeRecords[3].data.enabled, true);
  assert.equal(probeRecords[4].data.primary, true);
  assert.equal(probeRecords[4].data.playerName, "Test Player");
  assert.equal(probeRecords[5].data.attackType, "primary");
  assert.equal(probeRecords[5].data.weapon.className, "weapon_knife");
  assert.match(messages[beforeKnifeCallback], /^\[SM2PROBE\] /);
  assert.match(messages[beforeKnifeCallback], /"event":"knife_callback"/);
  assert.ok(messages.slice(beforeKnifeCallback + 1).some(
    (message) => message.startsWith("[SM2LAB] ") && message.includes('"event":"kick_result"'),
  ));

  context.__testBall = testBall;
  vm.runInContext("playEnabled = true; ball = globalThis.__testBall;", context);
  const beforeAcceptedWrites = messages.length;
  playerSlot = 0;
  registrations.knifeAttack({ weapon: knife, attackType: 0 });
  playerSlot = 1;
  registrations.knifeAttack({ weapon: knife, attackType: 0 });

  const acceptedWriteMessages = messages.slice(beforeAcceptedWrites);
  const acceptedResults = acceptedWriteMessages
    .filter((message) => message.startsWith("[SM2LAB] "))
    .map((message) => JSON.parse(message.slice("[SM2LAB] ".length)))
    .filter(({ event }) => event === "kick_result");
  const dispatched = acceptedWriteMessages
    .filter((message) => message.startsWith("[SM2PROBE] "))
    .map((message) => JSON.parse(message.slice("[SM2PROBE] ".length)))
    .filter(({ event }) => event === "kick_write_dispatched");
  assert.equal(acceptedResults.length, 2);
  assert.ok(acceptedResults.every(({ data }) => data.accepted === true));
  assert.equal(wakeRequests, 2);
  assert.equal(enableMotionRequests, 2);
  assert.equal(velocityWrites, 2);
  assert.equal(dispatched.length, 2);
  assert.equal(acceptedWriteMessages.some(
    (message) => message.includes('"event":"kick_write_observation"'),
  ), false);

  registrations.commands.get("sm2lab_probe_inputs")("off");
  playerSlot = 2;
  registrations.knifeAttack({ weapon: knife, attackType: 0 });
  assert.equal(wakeRequests, 3);
  assert.equal(enableMotionRequests, 3);
  assert.equal(velocityWrites, 3);
  ballPosition = { x: 51, y: 2, z: 23 };
  ballVelocity = { x: -444, y: 12, z: 35 };
  const beforeObservation = messages.length;
  registrations.think();
  const observations = messages.slice(beforeObservation)
    .filter((message) => message.startsWith("[SM2PROBE] "))
    .map((message) => JSON.parse(message.slice("[SM2PROBE] ".length)))
    .filter(({ event }) => event === "kick_write_observation");
  assert.equal(observations.length, 2);
  assert.deepEqual(
    observations.map(({ data }) => data.writeProbeSeq),
    dispatched.map(({ seq }) => seq),
  );
  assert.deepEqual(
    observations.map(({ data }) => data.laterAcceptedWriteCount),
    [2, 1],
  );
  assert.ok(observations.every(({ data }) => data.elapsedThinks === 1));
  assert.ok(observations.every(({ data }) => data.targetValid === true));
  assert.ok(observations.every(({ data }) => data.sameAuthoritativeBall === true));
  assert.ok(observations.every(({ data }) => data.sameBallGeneration === true));
  assert.ok(observations.every(({ data }) => data.sameResetCommandSequence === true));
  assert.ok(observations.every(({ data }) => (
    data.position.x === 51 && data.position.y === 2 && data.position.z === 23
  )));
  assert.ok(observations.every(({ data }) => (
    data.velocity.x === -444 && data.velocity.y === 12 && data.velocity.z === 35
  )));
  assert.ok(observations.every(({ data }) => Object.hasOwn(data, "passed") === false));

  const beforeInvalidWeapon = messages.length;
  assert.doesNotThrow(() => registrations.knifeAttack({
    weapon: { IsValid: () => false },
    attackType: 0,
  }));
  const invalidWeaponResult = messages.slice(beforeInvalidWeapon)
    .filter((message) => message.startsWith("[SM2LAB] "))
    .map((message) => JSON.parse(message.slice("[SM2LAB] ".length)))
    .find(({ event }) => event === "kick_result");
  assert.equal(invalidWeaponResult.data.accepted, false);
  assert.equal(invalidWeaponResult.data.reason, "player_not_alive");

  exposeTestBallByName = true;
  context.__resetWritePosition = { x: 512, y: 0, z: 15 };
  context.__resetRestPosition = { x: 512, y: 0, z: 15 };
  vm.runInContext(
    "resetWritePosition = globalThis.__resetWritePosition; resetRestPosition = globalThis.__resetRestPosition;",
    context,
  );
  ballPosition = { x: 700, y: 20, z: 24 };
  ballVelocity = { x: 400, y: 10, z: 5 };
  ballAngularVelocity = { x: 20, y: 0, z: 0 };
  const beforeResetRetry = messages.length;
  const writesBeforeReset = velocityWrites;
  registrations.commands.get("sm2lab_reset")();
  assert.equal(velocityWrites, writesBeforeReset + 1);
  const initialResetSequence = vm.runInContext("resetOperation.command.sequence", context);
  const initialBallGeneration = vm.runInContext("resetOperation.command.ballGeneration", context);
  const initialIssuedThinkSeq = vm.runInContext("resetOperation.command.issuedThinkSeq", context);
  const initialDeadline = vm.runInContext("resetOperation.deadline", context);
  const initialTransform = vm.runInContext(
    "JSON.stringify({ position: resetOperation.command.position, restPosition: resetOperation.command.restPosition, angles: resetOperation.command.angles })",
    context,
  );

  // Simulate the one contact/physics step observed after the live moving-goal
  // reset: transform and linear zero remain valid, but angular motion returns.
  ballAngularVelocity = { x: 0.01, y: 0, z: 0 };
  registrations.think();
  assert.equal(velocityWrites, writesBeforeReset + 2);
  assert.equal(vm.runInContext("resetOperation.command.sequence", context), initialResetSequence);
  assert.equal(vm.runInContext("resetOperation.command.ballGeneration", context), initialBallGeneration);
  assert.equal(vm.runInContext("resetOperation.command.issuedThinkSeq", context), initialIssuedThinkSeq + 1);
  assert.equal(vm.runInContext("resetOperation.deadline", context), initialDeadline);
  assert.equal(vm.runInContext("resetOperation.reason", context), "console");
  assert.equal(
    vm.runInContext(
      "JSON.stringify({ position: resetOperation.command.position, restPosition: resetOperation.command.restPosition, angles: resetOperation.command.angles })",
      context,
    ),
    initialTransform,
  );
  assert.deepEqual(teleportCalls.at(-1).velocity, { x: 0, y: 0, z: 0 });
  assert.deepEqual(teleportCalls.at(-1).angularVelocity, { x: 0, y: 0, z: 0 });
  registrations.think();
  registrations.think();
  registrations.think();

  const resetMessages = messages.slice(beforeResetRetry);
  const resetProbeRecords = resetMessages
    .filter((message) => message.startsWith("[SM2PROBE] "))
    .map((message) => JSON.parse(message.slice("[SM2PROBE] ".length)));
  const resetLabRecords = resetMessages
    .filter((message) => message.startsWith("[SM2LAB] "))
    .map((message) => JSON.parse(message.slice("[SM2LAB] ".length)));
  const retry = resetProbeRecords.find(({ event }) => event === "reset_write_retry");
  assert.deepEqual(retry.data.reasons, ["angular_motion"]);
  assert.equal(retry.data.resetSequence, initialResetSequence);
  assert.equal(retry.data.ballGeneration, initialBallGeneration);
  assert.equal(retry.data.failedAttempt, 1);
  assert.equal(retry.data.nextAttempt, 2);
  assert.equal(retry.data.maximumAttempts, 2);
  assert.equal(retry.data.failedSampleThinkSeq, retry.data.retryIssuedThinkSeq);
  assert.equal(Object.hasOwn(retry.data, "passed"), false);
  const writeVerifications = resetLabRecords
    .filter(({ event }) => event === "reset_write_verify");
  assert.equal(writeVerifications.length, 2);
  assert.equal(writeVerifications[0].data.passed, false);
  assert.deepEqual(writeVerifications[0].data.reasons, ["angular_motion"]);
  assert.equal(writeVerifications[0].data.writeAttempt, 1);
  assert.equal(writeVerifications[0].data.maximumAttempts, 2);
  assert.equal(writeVerifications[0].data.retryScheduled, true);
  assert.equal(writeVerifications[1].data.passed, true);
  assert.equal(writeVerifications[1].data.writeAttempt, 2);
  assert.equal(writeVerifications[1].data.maximumAttempts, 2);
  assert.equal(writeVerifications[1].data.retryScheduled, false);
  assert.equal(writeVerifications[1].data.resetSequence, initialResetSequence);
  assert.equal(writeVerifications[1].data.ballGeneration, initialBallGeneration);
  const resetEnd = resetLabRecords.find(({ event }) => event === "reset_end");
  assert.equal(resetEnd.data.passed, true);
  assert.equal(resetEnd.data.reason, "settled");
  assert.equal(resetEnd.data.commandSequence, initialResetSequence);
  assert.equal(resetEnd.data.commandBallGeneration, initialBallGeneration);
  assert.equal(resetEnd.data.writeAttempt, 2);

  const beforeBoundedFailure = messages.length;
  const writesBeforeBoundedFailure = velocityWrites;
  registrations.commands.get("sm2lab_reset")();
  const boundedFailureSequence = vm.runInContext("resetOperation.command.sequence", context);
  assert.equal(boundedFailureSequence, initialResetSequence + 1);
  ballAngularVelocity = { x: Number.EPSILON, y: 0, z: 0 };
  registrations.think();
  assert.equal(velocityWrites, writesBeforeBoundedFailure + 2);
  ballAngularVelocity = { x: Number.EPSILON, y: 0, z: 0 };
  registrations.think();
  const writesAfterTerminalFailure = velocityWrites;
  assert.equal(writesAfterTerminalFailure, writesBeforeBoundedFailure + 2);
  registrations.think();
  assert.equal(velocityWrites, writesAfterTerminalFailure);
  assert.equal(vm.runInContext("resetOperation", context), undefined);
  assert.equal(vm.runInContext("playEnabled", context), false);

  const boundedFailureMessages = messages.slice(beforeBoundedFailure);
  const boundedFailureProbeRecords = boundedFailureMessages
    .filter((message) => message.startsWith("[SM2PROBE] "))
    .map((message) => JSON.parse(message.slice("[SM2PROBE] ".length)));
  const boundedFailureLabRecords = boundedFailureMessages
    .filter((message) => message.startsWith("[SM2LAB] "))
    .map((message) => JSON.parse(message.slice("[SM2LAB] ".length)));
  const boundedRetries = boundedFailureProbeRecords
    .filter(({ event }) => event === "reset_write_retry");
  assert.equal(boundedRetries.length, 1);
  assert.equal(boundedRetries[0].data.resetSequence, boundedFailureSequence);
  const boundedWriteVerifications = boundedFailureLabRecords
    .filter(({ event }) => event === "reset_write_verify");
  assert.equal(boundedWriteVerifications.length, 2);
  assert.equal(boundedWriteVerifications[0].data.passed, false);
  assert.deepEqual(boundedWriteVerifications[0].data.reasons, ["angular_motion"]);
  assert.equal(boundedWriteVerifications[0].data.writeAttempt, 1);
  assert.equal(boundedWriteVerifications[0].data.retryScheduled, true);
  assert.equal(boundedWriteVerifications[1].data.passed, false);
  assert.deepEqual(boundedWriteVerifications[1].data.reasons, ["angular_motion"]);
  assert.equal(boundedWriteVerifications[1].data.writeAttempt, 2);
  assert.equal(boundedWriteVerifications[1].data.retryScheduled, false);
  assert.equal(boundedFailureLabRecords.some(
    ({ event }) => event === "reset_settle_verify",
  ), false);
  const boundedResetEnds = boundedFailureLabRecords
    .filter(({ event }) => event === "reset_end");
  assert.equal(boundedResetEnds.length, 1);
  assert.equal(boundedResetEnds[0].data.passed, false);
  assert.equal(boundedResetEnds[0].data.reason, "write_not_verified");
  assert.deepEqual(boundedResetEnds[0].data.reasons, ["angular_motion"]);
  assert.equal(boundedResetEnds[0].data.commandSequence, boundedFailureSequence);
  assert.equal(boundedResetEnds[0].data.writeAttempt, 2);

  const beforeMixedFailure = messages.length;
  const writesBeforeMixedFailure = velocityWrites;
  registrations.commands.get("sm2lab_reset")();
  ballVelocity = { x: 1, y: 0, z: 0 };
  ballAngularVelocity = { x: Number.EPSILON, y: 0, z: 0 };
  registrations.think();
  assert.equal(velocityWrites, writesBeforeMixedFailure + 1);
  const mixedFailureMessages = messages.slice(beforeMixedFailure);
  assert.equal(mixedFailureMessages.some(
    (message) => message.startsWith("[SM2PROBE] ")
      && message.includes('"event":"reset_write_retry"'),
  ), false);
  const mixedWriteVerification = mixedFailureMessages
    .filter((message) => message.startsWith("[SM2LAB] "))
    .map((message) => JSON.parse(message.slice("[SM2LAB] ".length)))
    .find(({ event }) => event === "reset_write_verify");
  assert.equal(mixedWriteVerification.data.passed, false);
  assert.deepEqual(mixedWriteVerification.data.reasons, ["velocity", "angular_motion"]);
  assert.equal(mixedWriteVerification.data.writeAttempt, 1);
  assert.equal(mixedWriteVerification.data.retryScheduled, false);

  assert.equal(messages.some(
    (message) => message.startsWith("[SM2PROBE] ")
      && message.includes('"event":"reset_physics_snapshot"'),
  ), false);

  const profileCommand = registrations.commands.get("sm2lab_goal_reset_profile");
  const beforeProfileQueries = messages.length;
  profileCommand("");
  profileCommand("radius_clearance 15");
  const profileQueries = messages.slice(beforeProfileQueries)
    .filter((message) => message.startsWith("[SM2PROBE] "))
    .map((message) => JSON.parse(message.slice("[SM2PROBE] ".length)))
    .filter(({ event }) => event === "goal_reset_profile_configuration");
  assert.equal(profileQueries.length, 2);
  assert.equal(profileQueries[0].data.accepted, true);
  assert.equal(profileQueries[0].data.activeProfile, "contact");
  assert.equal(profileQueries[0].data.diagnosticsEnabled, false);
  assert.equal(profileQueries[1].data.accepted, false);
  assert.equal(profileQueries[1].data.reason, "invalid_argument");
  assert.equal(profileQueries[1].data.activeProfile, "contact");
  assert.ok(profileQueries.every(({ data }) => !Object.hasOwn(data, "passed")));

  let invalidGroundMetadataReads = 0;
  ballGroundEntity = {
    IsValid: () => false,
    IsWorld: () => { invalidGroundMetadataReads += 1; throw new Error("must not read"); },
    GetClassName: () => { invalidGroundMetadataReads += 1; throw new Error("must not read"); },
    GetEntityName: () => { invalidGroundMetadataReads += 1; throw new Error("must not read"); },
  };
  profileCommand("contact");
  const writesBeforeContactGoal = velocityWrites;
  vm.runInContext("beginReset('goal')", context);
  assert.equal(velocityWrites, writesBeforeContactGoal + 1);
  assert.equal(teleportCalls.at(-1).position.z, 15);
  assert.equal(vm.runInContext("resetOperation.command.position.z", context), 15);
  assert.equal(vm.runInContext("resetOperation.command.restPosition.z", context), 15);
  assert.equal(vm.runInContext("resetOperation.profile", context), "contact");
  assert.equal(invalidGroundMetadataReads, 0);

  const writesBeforeRejectedProfileChange = velocityWrites;
  profileCommand("radius_clearance");
  assert.equal(velocityWrites, writesBeforeRejectedProfileChange);
  assert.equal(vm.runInContext("goalResetProfile", context), "contact");
  const rejectedWhileActive = messages
    .filter((message) => message.startsWith("[SM2PROBE] "))
    .map((message) => JSON.parse(message.slice("[SM2PROBE] ".length)))
    .filter(({ event }) => event === "goal_reset_profile_configuration")
    .at(-1);
  assert.equal(rejectedWhileActive.data.accepted, false);
  assert.equal(rejectedWhileActive.data.reason, "reset_in_progress");

  registrations.commands.get("sm2lab_reset")();
  assert.equal(teleportCalls.at(-1).position.z, 15);
  assert.equal(vm.runInContext("resetOperation.profile", context), "contact");
  ballAngularVelocity = { x: 0, y: 0, z: 0 };
  ballVelocity = { x: 0, y: 0, z: 0 };
  registrations.think();
  registrations.think();
  registrations.think();
  assert.equal(vm.runInContext("resetOperation", context), undefined);
  assert.equal(vm.runInContext("playEnabled", context), true);

  profileCommand("radius_clearance");
  assert.equal(vm.runInContext("goalResetProfile", context), "radius_clearance");
  ballGroundEntity = worldGround;
  ballPosition = { x: 512, y: 0, z: 15 };
  ballVelocity = { x: 0, y: 0, z: 0 };
  ballAngularVelocity = { x: 0, y: 0, z: 0 };
  const beforeRadiusGoal = messages.length;
  const writesBeforeRadiusGoal = velocityWrites;
  vm.runInContext("beginReset('goal')", context);
  const radiusResetSequence = vm.runInContext(
    "resetOperation.command.sequence",
    context,
  );
  assert.equal(velocityWrites, writesBeforeRadiusGoal + 1);
  assert.deepEqual(teleportCalls.at(-1).position, { x: 512, y: 0, z: 30 });
  assert.equal(vm.runInContext("resetOperation.command.restPosition.z", context), 15);
  assert.equal(vm.runInContext("resetOperation.profile", context), "radius_clearance");
  assert.equal(vm.runInContext("resetOperation.diagnostic", context), true);

  profileCommand("contact");
  assert.equal(vm.runInContext("goalResetProfile", context), "radius_clearance");
  groundReadThrows = true;
  ballAngularVelocity = { x: 1e-40, y: 0, z: 0 };
  registrations.think();
  assert.equal(velocityWrites, writesBeforeRadiusGoal + 2);

  groundReadThrows = false;
  ballGroundEntity = worldGround;
  ballAngularVelocity = { x: 1e-40, y: 0, z: 0 };
  registrations.think();
  assert.equal(velocityWrites, writesBeforeRadiusGoal + 2);
  assert.equal(vm.runInContext("resetOperation", context), undefined);
  assert.equal(vm.runInContext("playEnabled", context), false);

  for (let sampleIndex = 1; sampleIndex <= 8; sampleIndex += 1) {
    ballPosition = { x: 512, y: 0, z: 30 - sampleIndex };
    ballVelocity = { x: 0, y: 0, z: -sampleIndex };
    ballAngularVelocity = sampleIndex === 1
      ? { x: 1e-40, y: 0, z: 0 }
      : { x: 0, y: 0, z: 0 };
    registrations.think();
  }

  const radiusMessages = messages.slice(beforeRadiusGoal);
  const radiusProbeRecords = radiusMessages
    .filter((message) => message.startsWith("[SM2PROBE] "))
    .map((message) => JSON.parse(message.slice("[SM2PROBE] ".length)));
  const appliedProfile = radiusProbeRecords
    .find(({ event }) => event === "goal_reset_profile_applied");
  assert.equal(appliedProfile.data.appliedProfile, "radius_clearance");
  assert.equal(appliedProfile.data.writeClearance, 15);
  assert.equal(appliedProfile.data.position.z, 30);
  assert.equal(appliedProfile.data.restPosition.z, 15);
  assert.equal(appliedProfile.data.resetSequence, radiusResetSequence);
  assert.equal(appliedProfile.data.diagnosticOnly, true);

  const physicsSnapshots = radiusProbeRecords
    .filter(({ event }) => event === "reset_physics_snapshot");
  assert.equal(physicsSnapshots.length, 6);
  assert.deepEqual(
    physicsSnapshots.map(({ data }) => data.stage),
    [
      "before_write",
      "immediate_after_write",
      "next_think",
      "before_write",
      "immediate_after_write",
      "next_think",
    ],
  );
  assert.deepEqual(
    physicsSnapshots.map(({ data }) => data.writeAttempt),
    [1, 1, 1, 2, 2, 2],
  );
  assert.ok(physicsSnapshots.every(
    ({ data }) => data.maximumAttempts === 2
      && data.resetSequence === radiusResetSequence
      && data.angularUnits === "not_declared_by_point_script_api"
      && !Object.hasOwn(data, "passed"),
  ));
  assert.equal(physicsSnapshots[2].data.angularVelocityRaw.x, 1e-40);
  assert.equal(physicsSnapshots[2].data.angularMotionZero, false);
  assert.equal(physicsSnapshots[2].data.groundEntity.status, "read_error");
  assert.deepEqual(
    physicsSnapshots[2].data.angularVelocityRaw,
    physicsSnapshots[3].data.angularVelocityRaw,
  );
  assert.equal(physicsSnapshots[3].data.groundEntity.status, "read_error");
  assert.equal(physicsSnapshots[1].data.angularMotionZero, true);
  assert.equal(physicsSnapshots[4].data.angularMotionZero, true);
  assert.equal(physicsSnapshots[5].data.groundEntity.status, "valid");
  assert.equal(physicsSnapshots[5].data.groundEntity.isWorld, true);
  assert.equal(
    physicsSnapshots[2].data.sampleThinkSeq,
    physicsSnapshots[0].data.writeIssuedThinkSeq + 1,
  );
  assert.equal(
    physicsSnapshots[5].data.sampleThinkSeq,
    physicsSnapshots[3].data.writeIssuedThinkSeq + 1,
  );

  const postTerminalSamples = radiusProbeRecords
    .filter(({ event }) => event === "reset_post_terminal_sample");
  assert.equal(postTerminalSamples.length, 8);
  assert.deepEqual(
    postTerminalSamples.map(({ data }) => data.sampleIndex),
    [1, 2, 3, 4, 5, 6, 7, 8],
  );
  assert.deepEqual(
    postTerminalSamples.map(({ data }) => data.elapsedThinks),
    [1, 2, 3, 4, 5, 6, 7, 8],
  );
  assert.equal(postTerminalSamples[0].data.angularVelocityRaw.x, 1e-40);
  assert.equal(postTerminalSamples[0].data.angularMotionZero, false);
  assert.ok(postTerminalSamples.every(
    ({ data }) => data.sampleCount === 8
      && data.sameAuthoritativeBall === true
      && data.sameBallGeneration === true
      && data.sameResetCommandSequence === true
      && data.playEnabled === false
      && data.groundEntity.status === "valid"
      && !Object.hasOwn(data, "passed"),
  ));
  const terminalComplete = radiusProbeRecords
    .find(({ event }) => event === "reset_post_terminal_complete");
  assert.equal(terminalComplete.data.samplesCaptured, 8);
  assert.equal(terminalComplete.data.samplesExpected, 8);
  assert.equal(terminalComplete.data.stoppedReason, null);
  assert.equal(Object.hasOwn(terminalComplete.data, "passed"), false);

  const radiusLabRecords = radiusMessages
    .filter((message) => message.startsWith("[SM2LAB] "))
    .map((message) => JSON.parse(message.slice("[SM2LAB] ".length)));
  const radiusWriteVerifications = radiusLabRecords
    .filter(({ event }) => event === "reset_write_verify");
  assert.equal(radiusWriteVerifications.length, 2);
  assert.deepEqual(radiusWriteVerifications[0].data.reasons, ["angular_motion"]);
  assert.deepEqual(radiusWriteVerifications[1].data.reasons, ["angular_motion"]);
  assert.equal(radiusWriteVerifications[0].data.retryScheduled, true);
  assert.equal(radiusWriteVerifications[1].data.retryScheduled, false);
  const radiusResetEnd = radiusLabRecords
    .filter(({ event }) => event === "reset_end")
    .at(-1);
  assert.equal(radiusResetEnd.data.passed, false);
  assert.equal(radiusResetEnd.data.reason, "write_not_verified");
  assert.equal(radiusResetEnd.data.writeAttempt, 2);

  const terminalEventsBeforeNinthThink = messages
    .filter((message) => message.includes('"event":"reset_post_terminal_')).length;
  registrations.think();
  const terminalEventsAfterNinthThink = messages
    .filter((message) => message.includes('"event":"reset_post_terminal_')).length;
  assert.equal(terminalEventsAfterNinthThink, terminalEventsBeforeNinthThink);
  assert.equal(velocityWrites, writesBeforeRadiusGoal + 2);

  vm.runInContext("resetOperation = undefined; physicsTrialRun = undefined;", context);
  const motionProfileCommand = registrations.commands.get("sm2lab_reset_motion_profile");
  const beforeMotionProfile = messages.length;
  motionProfileCommand("");
  motionProfileCommand("unsupported");
  motionProfileCommand("teleport_only");
  motionProfileCommand("disable_motion");
  const motionProfileRecords = messages.slice(beforeMotionProfile)
    .filter((message) => message.startsWith("[SM2PROBE] "))
    .map((message) => JSON.parse(message.slice("[SM2PROBE] ".length)))
    .filter(({ event }) => event === "reset_motion_profile_configuration");
  assert.equal(motionProfileRecords.length, 4);
  assert.equal(motionProfileRecords[0].data.activeProfile, "disable_motion");
  assert.equal(motionProfileRecords[1].data.accepted, false);
  assert.equal(motionProfileRecords[2].data.activeProfile, "teleport_only");
  assert.equal(motionProfileRecords[3].data.accepted, true);
  assert.equal(motionProfileRecords[3].data.activeProfile, "disable_motion");
  const disablesBeforeCandidateReset = disableMotionRequests;
  registrations.commands.get("sm2lab_reset")();
  assert.equal(disableMotionRequests, disablesBeforeCandidateReset + 1);
  assert.equal(vm.runInContext("resetOperation.motionProfile", context), "disable_motion");

  vm.runInContext(
    "ball = undefined; resetOperation = undefined; playEnabled = false;",
    context,
  );
  playerSlot = 0;
  const beforeCapFlow = messages.length;
  registrations.playerChat({ player: controller, text: "!cap", team: 0 });
  registrations.playerChat({ player: controller2, text: "!join", team: 0 });
  registrations.playerChat({ player: controller, text: "!draft", team: 0 });
  assert.deepEqual(teamAssignments, [
    { playerSlot: 0, team: 2 },
    { playerSlot: 2, team: 3 },
  ]);
  assert.equal(vm.runInContext("capState.phase", context), "ready");
  assert.equal(vm.runInContext("matchState.phase", context), "countdown");
  const capFlowEvents = messages.slice(beforeCapFlow)
    .filter((message) => message.startsWith("[SM2LAB] "))
    .map((message) => JSON.parse(message.slice("[SM2LAB] ".length)))
    .map(({ event }) => event);
  assert.ok(capFlowEvents.includes("cap_team_assignment"));
  assert.ok(capFlowEvents.includes("match_action"));
  registrations.playerDisconnect({ playerSlot: 2 });
  assert.equal(vm.runInContext("capState.phase", context), "idle");
  assert.equal(vm.runInContext("matchState.phase", context), "warmup");

  const beforeLiveGoal = messages.length;
  const liveGoalAccepted = vm.runInContext(`
    ball = globalThis.__testBall;
    resetWritePosition = { x: 512, y: 0, z: 15 };
    resetRestPosition = { x: 512, y: 0, z: 15 };
    resetOperation = undefined;
    playEnabled = true;
    labGoals = [...LAB_LAYOUT.goals];
    goalState = createGoalState({ ballGeneration });
    matchState = startMatch(createMatchState({
      now: 10,
      config: {
        durationSeconds: 60,
        scoreLimit: 2,
        countdownSeconds: 0,
        goalPauseSeconds: 2,
      },
    }), 10).state;
    evaluateGoal(
      { x: 500, y: 0, z: 20 },
      { x: 700, y: 0, z: 20 },
      thinkSequence,
      thinkSequence + 1,
    );
  `, context);
  assert.equal(liveGoalAccepted, true);
  assert.equal(vm.runInContext("matchState.scores[3]", context), 1);
  assert.equal(vm.runInContext("matchState.phase", context), "goal_pause");
  const liveGoalEvents = messages.slice(beforeLiveGoal)
    .filter((message) => message.startsWith("[SM2LAB] "))
    .map((message) => JSON.parse(message.slice("[SM2LAB] ".length)))
    .map(({ event }) => event);
  assert.ok(liveGoalEvents.includes("goal_commit"));
  assert.ok(liveGoalEvents.includes("match_goal"));
  assert.ok(liveGoalEvents.includes("match_state"));
});

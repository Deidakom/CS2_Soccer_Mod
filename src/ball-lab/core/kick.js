import {
  add,
  clampMagnitude,
  dot,
  isFiniteVector,
  length,
  normalize,
  qAngleToForward,
  scale,
  subtract,
  vector,
} from "./vector.js";

export const KickKind = Object.freeze({
  PASS: "pass",
});

export const DEFAULT_KICK_CONFIG = Object.freeze({
  maximumReach: 96,
  minimumAimDot: Math.cos(55 * Math.PI / 180),
  cooldownSeconds: 0.16,
  passSpeed: 520,
  passLift: 55,
  inheritedVelocityRatio: 0.15,
  maximumBallSpeed: 1_250,
});

function rejection(reason, details = {}) {
  return { accepted: false, reason, ...details };
}

function isFiniteQAngle(value) {
  return Boolean(
    value
      && Number.isFinite(value.pitch)
      && Number.isFinite(value.yaw)
      && Number.isFinite(value.roll),
  );
}

function isValidConfig(config) {
  const nonnegative = [
    config.maximumReach,
    config.cooldownSeconds,
    config.passSpeed,
    config.passLift,
  ];
  return nonnegative.every((value) => Number.isFinite(value) && value >= 0)
    && Number.isFinite(config.minimumAimDot)
    && config.minimumAimDot >= -1
    && config.minimumAimDot <= 1
    && Number.isFinite(config.inheritedVelocityRatio)
    && config.inheritedVelocityRatio >= 0
    && config.inheritedVelocityRatio <= 1
    && Number.isFinite(config.maximumBallSpeed)
    && config.maximumBallSpeed > 0;
}

export function selectKickKind(attackType) {
  if (attackType === "primary") {
    return KickKind.PASS;
  }
  return undefined;
}

export function computeKick(input, overrides = {}) {
  if (!overrides || typeof overrides !== "object" || Array.isArray(overrides)) {
    return rejection("invalid_config");
  }
  const config = { ...DEFAULT_KICK_CONFIG, ...overrides };

  if (!isValidConfig(config)) {
    return rejection("invalid_config");
  }
  if (!input || typeof input !== "object") {
    return rejection("invalid_input");
  }

  if (input.playerAlive !== true) {
    return rejection("player_not_alive");
  }
  if (typeof input.playerEligible !== "boolean") {
    return rejection("invalid_input");
  }
  if (!input.playerEligible) {
    return rejection("player_ineligible");
  }
  if (input.playEnabled !== true) {
    return rejection("play_disabled");
  }
  if (!isFiniteVector(input.eyePosition)
      || !isFiniteQAngle(input.eyeAngles)
      || !isFiniteVector(input.ballPosition)
      || !isFiniteVector(input.ballVelocity)) {
    return rejection("invalid_vector");
  }
  if (!Number.isFinite(input.now)
      || !(Number.isFinite(input.lastAcceptedKickTime)
        || input.lastAcceptedKickTime === Number.NEGATIVE_INFINITY)
      || input.lastAcceptedKickTime > input.now) {
    return rejection("invalid_time");
  }
  if (typeof input.isDucking !== "boolean") {
    return rejection("invalid_input");
  }

  const kind = selectKickKind(input.attackType);
  if (!kind) {
    return rejection("unsupported_attack");
  }

  const elapsed = input.now - input.lastAcceptedKickTime;
  if (Number.isFinite(input.lastAcceptedKickTime)
      && elapsed < config.cooldownSeconds) {
    return rejection("cooldown", { remaining: config.cooldownSeconds - elapsed });
  }

  const eyeToBall = subtract(input.ballPosition, input.eyePosition);
  if (!isFiniteVector(eyeToBall)) {
    return rejection("invalid_vector");
  }
  const distance = length(eyeToBall);
  if (!Number.isFinite(distance) || distance === 0) {
    return rejection("invalid_vector");
  }
  if (distance > config.maximumReach) {
    return rejection("out_of_reach", { distance });
  }

  const forward = qAngleToForward(input.eyeAngles);
  const aimDot = dot(forward, normalize(eyeToBall));
  if (!Number.isFinite(aimDot)) {
    return rejection("invalid_vector");
  }
  if (aimDot < config.minimumAimDot) {
    return rejection("outside_aim_cone", { aimDot });
  }
  if (input.lineOfSight !== true) {
    return rejection("obstructed");
  }

  const planarMagnitude = Math.hypot(forward.x, forward.y);
  if (!Number.isFinite(planarMagnitude) || planarMagnitude < 1e-6) {
    return rejection("invalid_aim_direction");
  }
  const planarForward = vector(
    forward.x / planarMagnitude,
    forward.y / planarMagnitude,
    0,
  );

  const speed = config.passSpeed;
  const lift = config.passLift;

  const intendedVelocity = add(scale(planarForward, speed), vector(0, 0, lift));
  const inheritedVelocity = scale(
    input.ballVelocity,
    config.inheritedVelocityRatio,
  );
  if (!isFiniteVector(intendedVelocity) || !isFiniteVector(inheritedVelocity)) {
    return rejection("invalid_vector");
  }
  const combinedVelocity = add(intendedVelocity, inheritedVelocity);
  if (!isFiniteVector(combinedVelocity)) {
    return rejection("invalid_vector");
  }
  const unclampedSpeed = length(combinedVelocity);
  if (!Number.isFinite(unclampedSpeed)) {
    return rejection("invalid_vector");
  }
  const velocity = clampMagnitude(
    combinedVelocity,
    config.maximumBallSpeed,
  );
  if (!isFiniteVector(velocity)) {
    return rejection("invalid_vector");
  }
  const finalSpeed = length(velocity);
  if (!Number.isFinite(finalSpeed)) {
    return rejection("invalid_vector");
  }

  return {
    accepted: true,
    reason: "accepted",
    kind,
    distance,
    aimDot,
    velocity,
    unclampedSpeed,
    finalSpeed,
    maximumBallSpeed: config.maximumBallSpeed,
    wasClamped: unclampedSpeed > config.maximumBallSpeed,
    writeAngularVelocity: false,
  };
}

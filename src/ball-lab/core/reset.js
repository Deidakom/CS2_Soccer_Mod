import {
  isFiniteVector,
  length,
  subtract,
  vector,
} from "./vector.js";

export const DEFAULT_RESET_POSITION = Object.freeze(vector(0, 0, 17));
export const DEFAULT_RESET_ANGLES = Object.freeze({ pitch: 0, yaw: 0, roll: 0 });

function isSafeSequence(value, minimum = 0) {
  return Number.isSafeInteger(value) && value >= minimum;
}

function isFiniteQAngle(value) {
  return Boolean(
    value
      && Number.isFinite(value.pitch)
      && Number.isFinite(value.yaw)
      && Number.isFinite(value.roll),
  );
}

function angleDifference(left, right) {
  const normalizeDegrees = (value) => {
    const remainder = value % 360;
    return remainder < 0 ? remainder + 360 : remainder;
  };
  let difference = normalizeDegrees(left) - normalizeDegrees(right);
  if (difference > 180) difference -= 360;
  if (difference <= -180) difference += 360;
  return difference;
}

function qAngleError(left, right) {
  return Math.hypot(
    angleDifference(left.pitch, right.pitch),
    angleDifference(left.yaw, right.yaw),
    angleDifference(left.roll, right.roll),
  );
}

function validTolerances(values) {
  return values.every((value) => Number.isFinite(value) && value >= 0);
}

function validateCommand(command) {
  return Boolean(
    command
      && isSafeSequence(command.sequence, 1)
      && isSafeSequence(command.ballGeneration, 1)
      && isSafeSequence(command.issuedThinkSeq)
      && command.issuedThinkSeq < Number.MAX_SAFE_INTEGER
      && isFiniteVector(command.position)
      && isFiniteVector(command.restPosition)
      && isFiniteQAngle(command.angles)
      && isFiniteVector(command.velocity)
      && isFiniteVector(command.angularVelocity)
      && length(command.velocity) <= 1e-9
      && length(command.angularVelocity) <= 1e-9,
  );
}

function identityReasons(observation, command, writeStage = false) {
  const reasons = [];
  if (observation.ballCount !== 1) reasons.push("ball_count");
  if (observation.ballGeneration !== command.ballGeneration) {
    reasons.push("ball_generation");
  }
  if (observation.resetSequence !== command.sequence) {
    reasons.push("reset_sequence");
  }
  const sampleValid = isSafeSequence(observation.sampleThinkSeq);
  const sampleInOrder = sampleValid && (writeStage
    ? observation.sampleThinkSeq === command.issuedThinkSeq + 1
    : observation.sampleThinkSeq > command.issuedThinkSeq);
  if (!sampleValid || !sampleInOrder) {
    reasons.push("stale_sample");
  }
  return reasons;
}

export function createResetCommand(sequence, overrides = {}) {
  if (!overrides || typeof overrides !== "object") {
    throw new Error("reset overrides must be an object");
  }
  if (!isSafeSequence(sequence, 1)) {
    throw new Error("reset sequence must be a positive safe integer");
  }

  const position = overrides.position ?? DEFAULT_RESET_POSITION;
  const restPosition = overrides.restPosition ?? position;
  const angles = overrides.angles ?? DEFAULT_RESET_ANGLES;
  const ballGeneration = overrides.ballGeneration ?? 1;
  const issuedThinkSeq = overrides.issuedThinkSeq ?? 0;
  if (!isFiniteVector(position)
      || !isFiniteVector(restPosition)
      || !isFiniteQAngle(angles)) {
    throw new Error("reset transform must be finite");
  }
  if (!isSafeSequence(ballGeneration, 1)) {
    throw new Error("ball generation must be a positive safe integer");
  }
  if (!isSafeSequence(issuedThinkSeq)
      || issuedThinkSeq >= Number.MAX_SAFE_INTEGER) {
    throw new Error("issued think sequence must allow a next safe think");
  }

  return {
    sequence,
    ballGeneration,
    issuedThinkSeq,
    position: { ...position },
    restPosition: { ...restPosition },
    angles: { ...angles },
    velocity: vector(0, 0, 0),
    angularVelocity: vector(0, 0, 0),
  };
}

export function verifyResetWriteObservation(observation, command, tolerances = {}) {
  if (!validateCommand(command)) {
    return { stage: "write", passed: false, reasons: ["invalid_command"] };
  }
  if (!tolerances || typeof tolerances !== "object") {
    return { stage: "write", passed: false, reasons: ["invalid_tolerance"] };
  }
  const positionTolerance = tolerances.position ?? 0.5;
  const angleTolerance = tolerances.angles ?? 0.5;
  const velocityTolerance = tolerances.velocity ?? 0.1;
  if (!validTolerances([
    positionTolerance,
    angleTolerance,
    velocityTolerance,
  ])) {
    return { stage: "write", passed: false, reasons: ["invalid_tolerance"] };
  }
  if (!observation || typeof observation !== "object") {
    return { stage: "write", passed: false, reasons: ["invalid_observation"] };
  }

  const reasons = identityReasons(observation, command, true);
  if (!isFiniteVector(observation.position)
      || !isFiniteQAngle(observation.angles)
      || !isFiniteVector(observation.velocity)) {
    reasons.push("invalid_vector");
  }
  if (typeof observation.angularMotionZero !== "boolean") {
    reasons.push("invalid_observation");
  }
  if (reasons.includes("invalid_vector")
      || reasons.includes("invalid_observation")) {
    return {
      stage: "write",
      passed: false,
      reasons,
      resetSequence: command.sequence,
      ballGeneration: command.ballGeneration,
      sampleThinkSeq: observation.sampleThinkSeq,
    };
  }

  const positionError = length(subtract(observation.position, command.position));
  const angleError = qAngleError(observation.angles, command.angles);
  const speed = length(observation.velocity);
  if (positionError > positionTolerance) reasons.push("position");
  if (angleError > angleTolerance) reasons.push("angles");
  if (speed > velocityTolerance) reasons.push("velocity");
  if (!observation.angularMotionZero) reasons.push("angular_motion");

  return {
    stage: "write",
    passed: reasons.length === 0,
    reasons,
    positionError,
    angleError,
    speed,
    angularMotionZero: observation.angularMotionZero,
    resetSequence: command.sequence,
    ballGeneration: command.ballGeneration,
    sampleThinkSeq: observation.sampleThinkSeq,
  };
}

export function verifyResetSettledObservation(
  observation,
  command,
  writeVerification,
  tolerances = {},
) {
  if (!validateCommand(command)) {
    return { stage: "settled", passed: false, reasons: ["invalid_command"] };
  }
  if (writeVerification?.passed !== true
      || writeVerification.stage !== "write"
      || writeVerification.resetSequence !== command.sequence
      || writeVerification.ballGeneration !== command.ballGeneration
      || !Array.isArray(writeVerification.reasons)
      || writeVerification.reasons.length !== 0
      || !isSafeSequence(writeVerification.sampleThinkSeq)
      || writeVerification.sampleThinkSeq !== command.issuedThinkSeq + 1) {
    return { stage: "settled", passed: false, reasons: ["write_not_verified"] };
  }
  if (!tolerances || typeof tolerances !== "object") {
    return { stage: "settled", passed: false, reasons: ["invalid_tolerance"] };
  }
  const positionTolerance = tolerances.position ?? 0.5;
  const velocityTolerance = tolerances.velocity ?? 0.1;
  if (!validTolerances([
    positionTolerance,
    velocityTolerance,
  ])) {
    return { stage: "settled", passed: false, reasons: ["invalid_tolerance"] };
  }
  if (!observation || typeof observation !== "object") {
    return { stage: "settled", passed: false, reasons: ["invalid_observation"] };
  }

  const reasons = identityReasons(observation, command);
  const stableCountValid = isSafeSequence(observation.stableThinkCount, 2);
  const stableFromValid = isSafeSequence(observation.stableFromThinkSeq);
  const settledSampleValid = isSafeSequence(observation.sampleThinkSeq);
  const minimumSettledSample = stableCountValid && stableFromValid
    ? observation.stableFromThinkSeq + observation.stableThinkCount - 1
    : Number.NaN;
  if (!stableCountValid
      || !stableFromValid
      || observation.stableFromThinkSeq <= writeVerification.sampleThinkSeq
      || !Number.isSafeInteger(minimumSettledSample)
      || !settledSampleValid
      || observation.sampleThinkSeq < minimumSettledSample) {
    reasons.push("not_settled");
  }
  if (!isFiniteVector(observation.position)
      || !isFiniteVector(observation.velocity)) {
    reasons.push("invalid_vector");
  }
  if (typeof observation.angularMotionZero !== "boolean") {
    reasons.push("invalid_observation");
  }
  if (reasons.includes("invalid_vector")
      || reasons.includes("invalid_observation")) {
    return {
      stage: "settled",
      passed: false,
      reasons,
      resetSequence: command.sequence,
      ballGeneration: command.ballGeneration,
      sampleThinkSeq: observation.sampleThinkSeq,
    };
  }

  const positionError = length(subtract(observation.position, command.restPosition));
  const speed = length(observation.velocity);
  if (positionError > positionTolerance) reasons.push("position");
  if (speed > velocityTolerance) reasons.push("velocity");
  if (!observation.angularMotionZero) reasons.push("angular_motion");

  return {
    stage: "settled",
    passed: reasons.length === 0,
    reasons,
    positionError,
    speed,
    angularMotionZero: observation.angularMotionZero,
    resetSequence: command.sequence,
    ballGeneration: command.ballGeneration,
    sampleThinkSeq: observation.sampleThinkSeq,
  };
}

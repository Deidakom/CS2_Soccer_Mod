import { LAB_LAYOUT } from "./layout.js";

const MAX_TRIAL_COUNT = 100;
const MAX_BALL_SPEED = 1250;

function vector(x, y, z) {
  return { x, y, z };
}

function scaled(direction, speed) {
  return vector(
    direction.x * speed,
    direction.y * speed,
    direction.z * speed,
  );
}

function wallDirection(angleDegrees) {
  const radians = angleDegrees * Math.PI / 180;
  return vector(Math.sin(radians), Math.cos(radians), 0);
}

function dropProfile(height) {
  const center = LAB_LAYOUT.physicsFixtures.dropCenter;
  return {
    suite: "drop",
    mode: "drop",
    qualification: "diagnostic_only_reference_bands_unfrozen",
    startPosition: vector(
      center.x,
      center.y,
      center.z + height,
    ),
    initialVelocity: vector(0, 0, 0),
    maxThinkCount: 512,
    dropHeight: height,
  };
}

function rollProfile(axis, speed) {
  const startPosition = axis === "x"
    ? LAB_LAYOUT.physicsFixtures.rollXStart
    : LAB_LAYOUT.physicsFixtures.rollYStart;
  return {
    suite: "roll",
    mode: "roll",
    qualification: "diagnostic_only_reference_bands_unfrozen",
    startPosition: { ...startPosition },
    initialVelocity: vector(axis === "x" ? speed : 0, axis === "y" ? speed : 0, 0),
    maxThinkCount: 640,
    axis,
    commandedSpeed: speed,
  };
}

function wallProfile(speed, angleDegrees) {
  const direction = wallDirection(angleDegrees);
  return {
    suite: "walls",
    mode: "wall",
    qualification: "diagnostic_only_reference_bands_unfrozen",
    startPosition: { ...LAB_LAYOUT.physicsFixtures.wallStart },
    initialVelocity: scaled(direction, speed),
    maxThinkCount: 640,
    direction,
    commandedSpeed: speed,
    incidenceAngleDegrees: angleDegrees,
  };
}

function goalProfile(goal, reverse = false, nearMiss = false) {
  const direction = goal.direction;
  const startOffset = reverse ? 10 * direction : -10 * direction;
  const velocityDirection = reverse ? -direction : direction;
  const lateralAxis = goal.axis === "x" ? "y" : "x";
  const position = {
    ...LAB_LAYOUT.reset.restPosition,
    [goal.axis]: goal.plane + startOffset,
    [lateralAxis]: nearMiss
      ? goal.lateralCenter + goal.halfWidth + 1
      : goal.lateralCenter,
  };
  const velocity = vector(0, 0, 0);
  velocity[goal.axis] = velocityDirection * MAX_BALL_SPEED;
  return {
    suite: nearMiss ? "near_misses" : reverse ? "reverse_crossing" : "goals",
    mode: nearMiss ? "near_miss" : reverse ? "reverse" : "goal",
    qualification: "hard_gate",
    startPosition: position,
    initialVelocity: velocity,
    maxThinkCount: 32,
    goalId: goal.id,
    goalAxis: goal.axis,
    goalPlane: goal.plane,
    goalDirection: goal.direction,
    expectGoal: !reverse && !nearMiss,
  };
}

const [WEST_GOAL, EAST_GOAL] = LAB_LAYOUT.goals;

const PROFILE_BUILDERS = Object.freeze({
  wake_y_200: () => ({
    suite: "wake_write",
    mode: "wake",
    qualification: "hard_gate",
    startPosition: { ...LAB_LAYOUT.reset.restPosition },
    initialVelocity: vector(0, 200, 0),
    maxThinkCount: 2,
    commandedSpeed: 200,
  }),
  speed_cap_y_1250: () => ({
    suite: "speed_cap",
    mode: "speed_cap",
    qualification: "hard_gate",
    startPosition: { ...LAB_LAYOUT.reset.restPosition },
    initialVelocity: vector(0, MAX_BALL_SPEED, 0),
    maxThinkCount: 2,
    commandedSpeed: MAX_BALL_SPEED,
    maximumObservedSpeed: MAX_BALL_SPEED * 1.01,
  }),
  drop_64: () => dropProfile(64),
  drop_128: () => dropProfile(128),
  drop_256: () => dropProfile(256),
  roll_x_200: () => rollProfile("x", 200),
  roll_x_400: () => rollProfile("x", 400),
  roll_x_800: () => rollProfile("x", 800),
  roll_y_200: () => rollProfile("y", 200),
  roll_y_400: () => rollProfile("y", 400),
  roll_y_800: () => rollProfile("y", 800),
  wall_y_300_0: () => wallProfile(300, 0),
  wall_y_600_30: () => wallProfile(600, 30),
  wall_y_1000_45: () => wallProfile(1000, 45),
  goal_east_1250: () => goalProfile(EAST_GOAL),
  goal_west_1250: () => goalProfile(WEST_GOAL),
  reverse_east_1250: () => goalProfile(EAST_GOAL, true),
  reverse_west_1250: () => goalProfile(WEST_GOAL, true),
  near_miss_east_1250: () => goalProfile(EAST_GOAL, false, true),
});

export const PHYSICS_TRIAL_PROFILE_IDS = Object.freeze(Object.keys(PROFILE_BUILDERS));

export function parsePhysicsTrialRequest(args) {
  const parts = String(args ?? "").trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (parts.length < 1 || parts.length > 2) {
    return { accepted: false, reason: "invalid_argument_count" };
  }
  const [profileId, countText] = parts;
  if (!Object.hasOwn(PROFILE_BUILDERS, profileId)) {
    return { accepted: false, reason: "unsupported_profile", profileId };
  }
  const trialCount = countText === undefined ? 1 : Number(countText);
  if (!Number.isSafeInteger(trialCount) || trialCount < 1 || trialCount > MAX_TRIAL_COUNT) {
    return { accepted: false, reason: "invalid_trial_count", profileId };
  }
  return { accepted: true, profileId, trialCount };
}

export function createPhysicsTrialSpec(profileId) {
  const builder = PROFILE_BUILDERS[profileId];
  if (!builder) throw new Error(`unsupported physics profile: ${profileId}`);
  return { profileId, ...builder() };
}

export function summarizeDropBounce({ floorZ, impactMinZ, apexZ }) {
  const hasImpactAndApex = Number.isFinite(impactMinZ) && Number.isFinite(apexZ);
  return {
    floorCenterZ: Number.isFinite(floorZ) ? floorZ : null,
    firstBounceApexCenterZ: Number.isFinite(apexZ) ? apexZ : null,
    bounceImpactCenterZ: Number.isFinite(impactMinZ) ? impactMinZ : null,
    firstBounceHeight: hasImpactAndApex ? apexZ - impactMinZ : null,
    maximumFloorCenterPenetration: Number.isFinite(floorZ)
        && Number.isFinite(impactMinZ)
      ? Math.max(0, floorZ - impactMinZ)
      : null,
  };
}

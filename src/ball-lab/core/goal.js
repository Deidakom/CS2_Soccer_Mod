import { isFiniteVector } from "./vector.js";

function isSafeSequence(value, minimum = 0) {
  return Number.isSafeInteger(value) && value >= minimum;
}

function isValidGoal(goal) {
  return Boolean(
    goal
      && typeof goal.id === "string"
      && goal.id.length > 0
      && ["x", "y"].includes(goal.axis)
      && [-1, 1].includes(goal.direction)
      && Number.isFinite(goal.plane)
      && Number.isFinite(goal.lateralCenter ?? 0)
      && Number.isFinite(goal.halfWidth)
      && goal.halfWidth >= 0
      && Number.isFinite(goal.minimumHeight)
      && Number.isFinite(goal.maximumHeight)
      && goal.minimumHeight <= goal.maximumHeight
      && [2, 3].includes(goal.scoringTeam)
      && (goal.radiusAllowance === undefined || goal.radiusAllowance === 0),
  );
}

function isValidGoalState(state) {
  return Boolean(
    state
      && typeof state.locked === "boolean"
      && isSafeSequence(state.sequence)
      && isSafeSequence(state.ballGeneration, 1)
      && isSafeSequence(state.resetSequence)
      && isSafeSequence(state.lastResetVerifiedThinkSeq),
  );
}

function isValidCrossedCandidate(candidate) {
  return Boolean(
    candidate
      && candidate.crossed === true
      && candidate.reason === "crossed"
      && typeof candidate.goalId === "string"
      && candidate.goalId.length > 0
      && [2, 3].includes(candidate.scoringTeam)
      && Number.isFinite(candidate.fraction)
      && candidate.fraction >= 0
      && candidate.fraction <= 1
      && Number.isFinite(candidate.lateral)
      && Number.isFinite(candidate.height)
      && isValidCrossingContext(candidate),
  );
}

function isValidCrossingContext(context) {
  return Boolean(
    context
      && isSafeSequence(context.ballGeneration, 1)
      && isSafeSequence(context.resetSequence)
      && isSafeSequence(context.previousThinkSeq)
      && isSafeSequence(context.currentThinkSeq)
      && context.currentThinkSeq > context.previousThinkSeq,
  );
}

export function detectGoalPlaneCrossing(previous, current, goal, context) {
  if (!isFiniteVector(previous) || !isFiniteVector(current)) {
    return { crossed: false, reason: "invalid_vector" };
  }
  if (!isValidGoal(goal)) {
    return { crossed: false, reason: "invalid_goal" };
  }
  if (!isValidCrossingContext(context)) {
    return { crossed: false, reason: "invalid_context" };
  }

  const before = previous[goal.axis];
  const after = current[goal.axis];
  const delta = after - before;
  if (!Number.isFinite(delta)) {
    return { crossed: false, reason: "invalid_number" };
  }
  if (Math.abs(delta) < 1e-9) {
    return { crossed: false, reason: "parallel" };
  }

  const crossedForward = goal.direction > 0
    ? before < goal.plane && after >= goal.plane
    : before > goal.plane && after <= goal.plane;
  if (!crossedForward) {
    return { crossed: false, reason: "no_forward_crossing" };
  }

  const fraction = (goal.plane - before) / delta;
  if (!Number.isFinite(fraction) || fraction < 0 || fraction > 1) {
    return { crossed: false, reason: "outside_segment" };
  }

  const lateralAxis = goal.axis === "x" ? "y" : "x";
  const lateral = previous[lateralAxis]
    + (current[lateralAxis] - previous[lateralAxis]) * fraction;
  const height = previous.z + (current.z - previous.z) * fraction;
  if (!Number.isFinite(lateral) || !Number.isFinite(height)) {
    return { crossed: false, reason: "invalid_number" };
  }

  // Phase 1 uses center-plane semantics. Whole-ball and legacy trigger-overlap
  // timing are separate diagnostics; radius must never expand this aperture.
  if (Math.abs(lateral - (goal.lateralCenter ?? 0)) > goal.halfWidth) {
    return { crossed: false, reason: "outside_width", fraction, lateral, height };
  }
  if (height < goal.minimumHeight || height > goal.maximumHeight) {
    return { crossed: false, reason: "outside_height", fraction, lateral, height };
  }

  return {
    crossed: true,
    reason: "crossed",
    goalId: goal.id,
    scoringTeam: goal.scoringTeam,
    fraction,
    lateral,
    height,
    ballGeneration: context.ballGeneration,
    resetSequence: context.resetSequence,
    previousThinkSeq: context.previousThinkSeq,
    currentThinkSeq: context.currentThinkSeq,
  };
}

export function acceptGoalCandidate(state, candidate) {
  if (!isValidGoalState(state) || state.sequence >= Number.MAX_SAFE_INTEGER) {
    return { accepted: false, reason: "invalid_state", state };
  }
  if (!candidate || candidate.crossed !== true) {
    return {
      accepted: false,
      reason: candidate?.reason ?? "invalid_candidate",
      state,
    };
  }
  if (!isValidCrossedCandidate(candidate)) {
    return { accepted: false, reason: "invalid_candidate", state };
  }
  if (candidate.ballGeneration !== state.ballGeneration
      || candidate.resetSequence !== state.resetSequence
      || candidate.previousThinkSeq < state.lastResetVerifiedThinkSeq) {
    return { accepted: false, reason: "stale_candidate", state };
  }
  if (state.locked) {
    return { accepted: false, reason: "goal_locked", state };
  }

  return {
    accepted: true,
    reason: "accepted",
    state: {
      ...state,
      locked: true,
      sequence: state.sequence + 1,
      lastGoalId: candidate.goalId,
    },
  };
}

export function createGoalState(options = {}) {
  if (!options || typeof options !== "object") {
    throw new Error("goal state options must be an object");
  }
  const ballGeneration = options.ballGeneration ?? 1;
  const resetSequence = options.resetSequence ?? 0;
  const lastResetVerifiedThinkSeq = options.lastResetVerifiedThinkSeq ?? 0;
  if (!isSafeSequence(ballGeneration, 1)
      || !isSafeSequence(resetSequence)
      || !isSafeSequence(lastResetVerifiedThinkSeq)) {
    throw new Error("goal state counters must be safe nonnegative integers");
  }
  return {
    locked: false,
    sequence: 0,
    ballGeneration,
    resetSequence,
    lastResetVerifiedThinkSeq,
    lastGoalId: undefined,
  };
}

export function unlockAfterVerifiedReset(state, verification) {
  if (!isValidGoalState(state)) {
    return { unlocked: false, reason: "invalid_state", state };
  }
  if (verification?.passed !== true
      || verification.stage !== "settled"
      || !Array.isArray(verification.reasons)
      || verification.reasons.length !== 0
      || !isSafeSequence(verification.sampleThinkSeq, 1)) {
    return { unlocked: false, reason: "reset_not_verified", state };
  }
  if (!isSafeSequence(verification.resetSequence, 1)
      || verification.resetSequence <= state.resetSequence
      || verification.sampleThinkSeq <= state.lastResetVerifiedThinkSeq) {
    return { unlocked: false, reason: "stale_reset", state };
  }
  if (verification.ballGeneration !== state.ballGeneration) {
    return { unlocked: false, reason: "ball_generation", state };
  }

  return {
    unlocked: true,
    reason: "unlocked",
    state: {
      ...state,
      locked: false,
      resetSequence: verification.resetSequence,
      lastResetVerifiedThinkSeq: verification.sampleThinkSeq,
      lastGoalId: undefined,
    },
  };
}

export function replaceBallGeneration(state, ballGeneration) {
  if (!isValidGoalState(state)) {
    return { replaced: false, reason: "invalid_state", state };
  }
  if (!isSafeSequence(ballGeneration, 1)
      || ballGeneration <= state.ballGeneration) {
    return { replaced: false, reason: "invalid_ball_generation", state };
  }

  return {
    replaced: true,
    reason: "replaced",
    state: {
      ...state,
      locked: true,
      ballGeneration,
      lastGoalId: undefined,
    },
  };
}

export const MatchPhase = Object.freeze({
  WARMUP: "warmup",
  COUNTDOWN: "countdown",
  LIVE: "live",
  GOAL_PAUSE: "goal_pause",
  PAUSED: "paused",
  FINISHED: "finished",
});

export const DEFAULT_MATCH_CONFIG = Object.freeze({
  durationSeconds: 10 * 60,
  scoreLimit: 10,
  countdownSeconds: 3,
  goalPauseSeconds: 2,
});

const PHASES = Object.freeze(Object.values(MatchPhase));

function validNow(value) {
  return Number.isFinite(value) && value >= 0;
}

function validConfig(config) {
  return Boolean(
    config
      && Number.isFinite(config.durationSeconds)
      && config.durationSeconds > 0
      && Number.isSafeInteger(config.scoreLimit)
      && config.scoreLimit > 0
      && Number.isFinite(config.countdownSeconds)
      && config.countdownSeconds >= 0
      && Number.isFinite(config.goalPauseSeconds)
      && config.goalPauseSeconds >= 0,
  );
}

function validScores(scores) {
  return Boolean(
    scores
      && Number.isSafeInteger(scores[2])
      && scores[2] >= 0
      && Number.isSafeInteger(scores[3])
      && scores[3] >= 0,
  );
}

export function isValidMatchState(state) {
  return Boolean(
    state
      && PHASES.includes(state.phase)
      && Number.isSafeInteger(state.sequence)
      && state.sequence >= 0
      && validScores(state.scores)
      && validConfig(state.config)
      && validNow(state.remainingSeconds)
      && validNow(state.lastUpdatedAt)
      && (state.phaseEndsAt === null || validNow(state.phaseEndsAt))
      && (state.winnerTeam === null || [2, 3].includes(state.winnerTeam))
      && (state.lastScoringTeam === null || [2, 3].includes(state.lastScoringTeam)),
  );
}

function rejection(reason, state) {
  return { accepted: false, reason, state };
}

function success(reason, state, details = {}) {
  return { accepted: true, reason, state, ...details };
}

function finishForTime(state, now) {
  const score2 = state.scores[2];
  const score3 = state.scores[3];
  return {
    ...state,
    phase: MatchPhase.FINISHED,
    remainingSeconds: 0,
    lastUpdatedAt: now,
    phaseEndsAt: null,
    winnerTeam: score2 === score3 ? null : score2 > score3 ? 2 : 3,
  };
}

function advanceLive(state, now, liveStartedAt = state.lastUpdatedAt) {
  const elapsed = now - liveStartedAt;
  if (!Number.isFinite(elapsed) || elapsed < 0) {
    return rejection("invalid_time", state);
  }
  const remainingSeconds = Math.max(0, state.remainingSeconds - elapsed);
  const advanced = {
    ...state,
    phase: MatchPhase.LIVE,
    phaseEndsAt: null,
    remainingSeconds,
    lastUpdatedAt: now,
  };
  return success(
    remainingSeconds === 0 ? "time_limit" : "advanced",
    remainingSeconds === 0 ? finishForTime(advanced, now) : advanced,
    { changed: elapsed > 0 || state.phase !== MatchPhase.LIVE },
  );
}

export function createMatchState(options = {}) {
  if (!options || typeof options !== "object" || Array.isArray(options)) {
    throw new Error("match options must be an object");
  }
  const now = options.now ?? 0;
  const config = {
    ...DEFAULT_MATCH_CONFIG,
    ...(options.config ?? {}),
  };
  if (!validNow(now) || !validConfig(config)) {
    throw new Error("match options are invalid");
  }
  return {
    phase: MatchPhase.WARMUP,
    sequence: 0,
    scores: { 2: 0, 3: 0 },
    config,
    remainingSeconds: config.durationSeconds,
    lastUpdatedAt: now,
    phaseEndsAt: null,
    winnerTeam: null,
    lastScoringTeam: null,
  };
}

export function startMatch(state, now) {
  if (!isValidMatchState(state)) return rejection("invalid_state", state);
  if (!validNow(now) || now < state.lastUpdatedAt) {
    return rejection("invalid_time", state);
  }
  if (![MatchPhase.WARMUP, MatchPhase.FINISHED].includes(state.phase)) {
    return rejection("match_already_active", state);
  }
  if (state.sequence >= Number.MAX_SAFE_INTEGER) {
    return rejection("sequence_exhausted", state);
  }
  const countdown = state.config.countdownSeconds;
  return success("started", {
    ...state,
    phase: countdown > 0 ? MatchPhase.COUNTDOWN : MatchPhase.LIVE,
    sequence: state.sequence + 1,
    scores: { 2: 0, 3: 0 },
    remainingSeconds: state.config.durationSeconds,
    lastUpdatedAt: now,
    phaseEndsAt: countdown > 0 ? now + countdown : null,
    winnerTeam: null,
    lastScoringTeam: null,
  });
}

export function stopMatch(state, now) {
  if (!isValidMatchState(state)) return rejection("invalid_state", state);
  if (!validNow(now) || now < state.lastUpdatedAt) {
    return rejection("invalid_time", state);
  }
  return success("stopped", {
    ...state,
    phase: MatchPhase.WARMUP,
    remainingSeconds: state.config.durationSeconds,
    lastUpdatedAt: now,
    phaseEndsAt: null,
    winnerTeam: null,
    lastScoringTeam: null,
  });
}

export function pauseMatch(state, now) {
  const advanced = advanceMatchState(state, now);
  if (!advanced.accepted) return advanced;
  if (advanced.state.phase !== MatchPhase.LIVE) {
    return rejection("match_not_live", advanced.state);
  }
  return success("paused", {
    ...advanced.state,
    phase: MatchPhase.PAUSED,
    phaseEndsAt: null,
  });
}

export function resumeMatch(state, now) {
  if (!isValidMatchState(state)) return rejection("invalid_state", state);
  if (!validNow(now) || now < state.lastUpdatedAt) {
    return rejection("invalid_time", state);
  }
  if (state.phase !== MatchPhase.PAUSED) {
    return rejection("match_not_paused", state);
  }
  return success("resumed", {
    ...state,
    phase: MatchPhase.LIVE,
    lastUpdatedAt: now,
    phaseEndsAt: null,
  });
}

export function advanceMatchState(state, now) {
  if (!isValidMatchState(state)) return rejection("invalid_state", state);
  if (!validNow(now) || now < state.lastUpdatedAt) {
    return rejection("invalid_time", state);
  }
  if (state.phase === MatchPhase.LIVE) return advanceLive(state, now);
  if ([MatchPhase.COUNTDOWN, MatchPhase.GOAL_PAUSE].includes(state.phase)) {
    if (state.phaseEndsAt === null) return rejection("invalid_state", state);
    if (now < state.phaseEndsAt) {
      return success("waiting", state, { changed: false });
    }
    return advanceLive(state, now, state.phaseEndsAt);
  }
  return success("unchanged", state, { changed: false });
}

export function recordMatchGoal(state, scoringTeam, now) {
  if (![2, 3].includes(scoringTeam)) {
    return rejection("invalid_team", state);
  }
  const advanced = advanceMatchState(state, now);
  if (!advanced.accepted) return advanced;
  if (advanced.state.phase !== MatchPhase.LIVE) {
    return rejection("match_not_live", advanced.state);
  }
  const currentScore = advanced.state.scores[scoringTeam];
  if (currentScore >= Number.MAX_SAFE_INTEGER) {
    return rejection("score_exhausted", advanced.state);
  }
  const scores = {
    ...advanced.state.scores,
    [scoringTeam]: currentScore + 1,
  };
  if (scores[scoringTeam] >= advanced.state.config.scoreLimit) {
    return success("score_limit", {
      ...advanced.state,
      phase: MatchPhase.FINISHED,
      scores,
      lastUpdatedAt: now,
      phaseEndsAt: null,
      winnerTeam: scoringTeam,
      lastScoringTeam: scoringTeam,
    });
  }
  const goalPause = advanced.state.config.goalPauseSeconds;
  return success("goal", {
    ...advanced.state,
    phase: goalPause > 0 ? MatchPhase.GOAL_PAUSE : MatchPhase.LIVE,
    scores,
    lastUpdatedAt: now,
    phaseEndsAt: goalPause > 0 ? now + goalPause : null,
    winnerTeam: null,
    lastScoringTeam: scoringTeam,
  });
}

export function matchAllowsBallInteraction(state) {
  return isValidMatchState(state)
    && [MatchPhase.WARMUP, MatchPhase.LIVE].includes(state.phase);
}

export function matchCountsGoals(state) {
  return isValidMatchState(state) && state.phase === MatchPhase.LIVE;
}

export function formatMatchStatus(state) {
  if (!isValidMatchState(state)) return "SoccerMod match state unavailable";
  const remaining = Math.ceil(state.remainingSeconds);
  const minutes = Math.floor(remaining / 60);
  const seconds = String(remaining % 60).padStart(2, "0");
  const score = `${state.scores[2]} - ${state.scores[3]}`;
  const winner = state.phase === MatchPhase.FINISHED
    ? state.winnerTeam === null ? " | draw" : ` | team ${state.winnerTeam} wins`
    : "";
  return `SoccerMod | ${state.phase} | ${score} | ${minutes}:${seconds}${winner}`;
}

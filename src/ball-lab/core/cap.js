export const CapPhase = Object.freeze({
  IDLE: "idle",
  COLLECTING: "collecting",
  PICKING: "picking",
  READY: "ready",
});

export const DEFAULT_CAP_CONFIG = Object.freeze({
  minPlayers: 2,
  maxPlayers: 10,
});

const PHASES = Object.freeze(Object.values(CapPhase));

function validPlayer(player) {
  return Boolean(
    player
      && Number.isSafeInteger(player.slot)
      && player.slot >= 0
      && typeof player.name === "string"
      && player.name.length > 0,
  );
}

function validConfig(config) {
  return Boolean(
    config
      && Number.isSafeInteger(config.minPlayers)
      && config.minPlayers >= 2
      && Number.isSafeInteger(config.maxPlayers)
      && config.maxPlayers >= config.minPlayers,
  );
}

function uniqueSafeSlots(slots) {
  return Array.isArray(slots)
    && slots.every((slot) => Number.isSafeInteger(slot) && slot >= 0)
    && new Set(slots).size === slots.length;
}

export function isValidCapState(state) {
  if (!state
      || !PHASES.includes(state.phase)
      || !Number.isSafeInteger(state.sequence)
      || state.sequence < 0
      || !validConfig(state.config)
      || !Array.isArray(state.players)
      || state.players.length > state.config.maxPlayers
      || !state.players.every(validPlayer)
      || new Set(state.players.map(({ slot }) => slot)).size !== state.players.length
      || !state.captains
      || !state.teams
      || !uniqueSafeSlots(state.teams[2])
      || !uniqueSafeSlots(state.teams[3])) {
    return false;
  }
  const playerSlots = new Set(state.players.map(({ slot }) => slot));
  const draftedSlots = [...state.teams[2], ...state.teams[3]];
  if (new Set(draftedSlots).size !== draftedSlots.length
      || draftedSlots.some((slot) => !playerSlots.has(slot))) {
    return false;
  }
  if (state.phase === CapPhase.IDLE) {
    return state.ownerSlot === null
      && state.captains[2] === null
      && state.captains[3] === null
      && state.turnTeam === null
      && state.players.length === 0
      && draftedSlots.length === 0;
  }
  if (!playerSlots.has(state.ownerSlot)) return false;
  if (state.phase === CapPhase.COLLECTING) {
    return state.captains[2] === null
      && state.captains[3] === null
      && state.turnTeam === null
      && draftedSlots.length === 0;
  }
  if (!Number.isSafeInteger(state.captains[2])
      || !Number.isSafeInteger(state.captains[3])
      || !playerSlots.has(state.captains[2])
      || !playerSlots.has(state.captains[3])
      || state.captains[2] === state.captains[3]
      || state.teams[2][0] !== state.captains[2]
      || state.teams[3][0] !== state.captains[3]) {
    return false;
  }
  if (state.phase === CapPhase.PICKING) {
    return [2, 3].includes(state.turnTeam)
      && draftedSlots.length < state.players.length;
  }
  return state.turnTeam === null && draftedSlots.length === state.players.length;
}

function rejection(reason, state) {
  return { accepted: false, reason, state };
}

function success(reason, state, details = {}) {
  return { accepted: true, reason, state, ...details };
}

function clonePlayer(player) {
  return { slot: player.slot, name: player.name };
}

export function createCapState(options = {}) {
  if (!options || typeof options !== "object" || Array.isArray(options)) {
    throw new Error("cap options must be an object");
  }
  const config = { ...DEFAULT_CAP_CONFIG, ...(options.config ?? {}) };
  if (!validConfig(config)) throw new Error("cap options are invalid");
  return {
    phase: CapPhase.IDLE,
    sequence: 0,
    config,
    ownerSlot: null,
    players: [],
    captains: { 2: null, 3: null },
    teams: { 2: [], 3: [] },
    turnTeam: null,
  };
}

export function openCap(state, owner) {
  if (!isValidCapState(state)) return rejection("invalid_state", state);
  if (!validPlayer(owner)) return rejection("invalid_player", state);
  if (state.phase !== CapPhase.IDLE) return rejection("cap_already_open", state);
  if (state.sequence >= Number.MAX_SAFE_INTEGER) {
    return rejection("sequence_exhausted", state);
  }
  return success("opened", {
    ...state,
    phase: CapPhase.COLLECTING,
    sequence: state.sequence + 1,
    ownerSlot: owner.slot,
    players: [clonePlayer(owner)],
  });
}

export function joinCap(state, player) {
  if (!isValidCapState(state)) return rejection("invalid_state", state);
  if (!validPlayer(player)) return rejection("invalid_player", state);
  if (state.phase !== CapPhase.COLLECTING) {
    return rejection("cap_not_collecting", state);
  }
  if (state.players.some(({ slot }) => slot === player.slot)) {
    return rejection("already_joined", state);
  }
  if (state.players.length >= state.config.maxPlayers) {
    return rejection("cap_full", state);
  }
  return success("joined", {
    ...state,
    players: [...state.players, clonePlayer(player)],
  });
}

export function leaveCap(state, playerSlot) {
  if (!isValidCapState(state)) return rejection("invalid_state", state);
  if (!Number.isSafeInteger(playerSlot) || playerSlot < 0) {
    return rejection("invalid_player", state);
  }
  if (state.phase !== CapPhase.COLLECTING) {
    return rejection("cap_not_collecting", state);
  }
  if (!state.players.some(({ slot }) => slot === playerSlot)) {
    return rejection("not_joined", state);
  }
  const players = state.players.filter(({ slot }) => slot !== playerSlot);
  if (playerSlot === state.ownerSlot) {
    if (players.length === 0) return cancelCap(state, playerSlot, true);
    return success("owner_left", {
      ...state,
      ownerSlot: players[0].slot,
      players,
    });
  }
  return success("left", { ...state, players });
}

export function beginCapDraft(state, requesterSlot) {
  if (!isValidCapState(state)) return rejection("invalid_state", state);
  if (state.phase !== CapPhase.COLLECTING) {
    return rejection("cap_not_collecting", state);
  }
  if (requesterSlot !== state.ownerSlot) return rejection("not_owner", state);
  if (state.players.length < state.config.minPlayers) {
    return rejection("not_enough_players", state);
  }
  const captain2 = state.players[0].slot;
  const captain3 = state.players[1].slot;
  const allAssigned = state.players.length === 2;
  return success(allAssigned ? "ready" : "draft_started", {
    ...state,
    phase: allAssigned ? CapPhase.READY : CapPhase.PICKING,
    captains: { 2: captain2, 3: captain3 },
    teams: { 2: [captain2], 3: [captain3] },
    turnTeam: allAssigned ? null : 2,
  });
}

export function pickCapPlayer(state, captainSlot, targetSlot) {
  if (!isValidCapState(state)) return rejection("invalid_state", state);
  if (state.phase !== CapPhase.PICKING) return rejection("cap_not_picking", state);
  if (!Number.isSafeInteger(captainSlot) || !Number.isSafeInteger(targetSlot)) {
    return rejection("invalid_player", state);
  }
  const team = state.turnTeam;
  if (state.captains[team] !== captainSlot) return rejection("not_pick_turn", state);
  if (!state.players.some(({ slot }) => slot === targetSlot)) {
    return rejection("player_not_found", state);
  }
  if (state.teams[2].includes(targetSlot) || state.teams[3].includes(targetSlot)) {
    return rejection("already_picked", state);
  }
  const teams = {
    2: team === 2 ? [...state.teams[2], targetSlot] : [...state.teams[2]],
    3: team === 3 ? [...state.teams[3], targetSlot] : [...state.teams[3]],
  };
  const ready = teams[2].length + teams[3].length === state.players.length;
  return success(ready ? "ready" : "picked", {
    ...state,
    phase: ready ? CapPhase.READY : CapPhase.PICKING,
    teams,
    turnTeam: ready ? null : team === 2 ? 3 : 2,
  }, { pickedSlot: targetSlot, pickedTeam: team });
}

export function cancelCap(state, requesterSlot, ownerAlreadyValidated = false) {
  if (!isValidCapState(state)) return rejection("invalid_state", state);
  if (state.phase === CapPhase.IDLE) return rejection("cap_not_open", state);
  if (!ownerAlreadyValidated && requesterSlot !== state.ownerSlot) {
    return rejection("not_owner", state);
  }
  return success("cancelled", {
    ...createCapState({ config: state.config }),
    sequence: state.sequence,
  });
}

export function disconnectCapPlayer(state, playerSlot) {
  if (!isValidCapState(state)) return rejection("invalid_state", state);
  if (!Number.isSafeInteger(playerSlot) || playerSlot < 0) {
    return rejection("invalid_player", state);
  }
  if (!state.players.some(({ slot }) => slot === playerSlot)) {
    return rejection("not_joined", state);
  }
  if (state.phase === CapPhase.COLLECTING) return leaveCap(state, playerSlot);
  return success("participant_disconnected", {
    ...createCapState({ config: state.config }),
    sequence: state.sequence,
  });
}

export function capTeamForSlot(state, playerSlot) {
  if (!isValidCapState(state)) return null;
  if (state.teams[2].includes(playerSlot)) return 2;
  if (state.teams[3].includes(playerSlot)) return 3;
  return null;
}

export function formatCapStatus(state) {
  if (!isValidCapState(state)) return "Cap state unavailable";
  if (state.phase === CapPhase.IDLE) return "Cap | idle | type !cap to open";
  if (state.phase === CapPhase.COLLECTING) {
    return `Cap | joining ${state.players.length}/${state.config.maxPlayers} | !join, owner: !draft`;
  }
  const score = `${state.teams[2].length}v${state.teams[3].length}`;
  if (state.phase === CapPhase.PICKING) {
    return `Cap | picking ${score} | team ${state.turnTeam} captain: !pick <slot>`;
  }
  return `Cap | ready ${score} | match starting`;
}

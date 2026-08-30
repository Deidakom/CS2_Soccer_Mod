import assert from "node:assert/strict";
import test from "node:test";

import {
  CapPhase,
  beginCapDraft,
  cancelCap,
  capTeamForSlot,
  createCapState,
  disconnectCapPlayer,
  formatCapStatus,
  isValidCapState,
  joinCap,
  leaveCap,
  openCap,
  pickCapPlayer,
} from "../src/ball-lab/core/cap.js";

const player = (slot) => ({ slot, name: `Player ${slot}` });

test("a cap opens, collects unique players, and transfers ownership on leave", () => {
  const idle = createCapState({ config: { minPlayers: 2, maxPlayers: 4 } });
  assert.equal(isValidCapState(idle), true);
  assert.equal(formatCapStatus(idle), "Cap | idle | type !cap to open");

  const opened = openCap(idle, player(4));
  assert.equal(opened.accepted, true);
  assert.equal(opened.state.phase, CapPhase.COLLECTING);
  const joined = joinCap(opened.state, player(8));
  assert.equal(joinCap(joined.state, player(8)).reason, "already_joined");

  const ownerLeft = leaveCap(joined.state, 4);
  assert.equal(ownerLeft.reason, "owner_left");
  assert.equal(ownerLeft.state.ownerSlot, 8);
  assert.deepEqual(ownerLeft.state.players, [player(8)]);
});

test("the owner starts a deterministic alternating draft", () => {
  let state = openCap(createCapState(), player(0)).state;
  for (let slot = 1; slot < 6; slot += 1) {
    state = joinCap(state, player(slot)).state;
  }
  assert.equal(beginCapDraft(state, 1).reason, "not_owner");
  state = beginCapDraft(state, 0).state;
  assert.equal(state.phase, CapPhase.PICKING);
  assert.deepEqual(state.captains, { 2: 0, 3: 1 });
  assert.equal(state.turnTeam, 2);

  assert.equal(pickCapPlayer(state, 1, 2).reason, "not_pick_turn");
  state = pickCapPlayer(state, 0, 2).state;
  assert.equal(capTeamForSlot(state, 2), 2);
  state = pickCapPlayer(state, 1, 3).state;
  state = pickCapPlayer(state, 0, 4).state;
  const ready = pickCapPlayer(state, 1, 5);
  assert.equal(ready.reason, "ready");
  assert.equal(ready.state.phase, CapPhase.READY);
  assert.deepEqual(ready.state.teams, { 2: [0, 2, 4], 3: [1, 3, 5] });
  assert.match(formatCapStatus(ready.state), /ready 3v3/);
});

test("a two-player cap becomes ready without picks and can be cancelled", () => {
  const idle = createCapState();
  const opened = openCap(idle, player(10)).state;
  assert.equal(beginCapDraft(opened, 10).reason, "not_enough_players");
  const joined = joinCap(opened, player(11)).state;
  const ready = beginCapDraft(joined, 10);
  assert.equal(ready.reason, "ready");
  assert.equal(ready.state.phase, CapPhase.READY);
  assert.deepEqual(ready.state.teams, { 2: [10], 3: [11] });
  assert.equal(cancelCap(ready.state, 11).reason, "not_owner");
  const cancelled = cancelCap(ready.state, 10);
  assert.equal(cancelled.state.phase, CapPhase.IDLE);
  assert.equal(cancelled.state.sequence, 1);
});

test("a disconnect removes a collecting player or safely cancels a draft", () => {
  let collecting = openCap(createCapState(), player(0)).state;
  collecting = joinCap(collecting, player(1)).state;
  collecting = joinCap(collecting, player(2)).state;
  const removed = disconnectCapPlayer(collecting, 2);
  assert.equal(removed.reason, "left");
  assert.deepEqual(removed.state.players.map(({ slot }) => slot), [0, 1]);

  const drafting = beginCapDraft(collecting, 0).state;
  const cancelled = disconnectCapPlayer(drafting, 1);
  assert.equal(cancelled.reason, "participant_disconnected");
  assert.equal(cancelled.state.phase, CapPhase.IDLE);
});

test("cap capacity and malformed inputs fail closed", () => {
  const idle = createCapState({ config: { minPlayers: 2, maxPlayers: 2 } });
  const opened = openCap(idle, player(0)).state;
  const full = joinCap(opened, player(1)).state;
  assert.equal(joinCap(full, player(2)).reason, "cap_full");
  assert.equal(openCap(idle, { slot: -1, name: "bad" }).reason, "invalid_player");
  assert.equal(isValidCapState({}), false);
  assert.throws(
    () => createCapState({ config: { minPlayers: 1, maxPlayers: 10 } }),
    /invalid/,
  );
});

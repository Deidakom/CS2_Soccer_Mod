import assert from "node:assert/strict";
import test from "node:test";

import { computeKick, KickKind } from "../src/ball-lab/core/kick.js";
import { length, vector } from "../src/ball-lab/core/vector.js";

function validInput(overrides = {}) {
  return {
    playerAlive: true,
    playerEligible: true,
    playEnabled: true,
    eyePosition: vector(0, 0, 64),
    eyeAngles: { pitch: 20, yaw: 0, roll: 0 },
    ballPosition: vector(60, 0, 24),
    ballVelocity: vector(),
    attackType: "primary",
    isDucking: false,
    lineOfSight: true,
    now: 10,
    lastAcceptedKickTime: Number.NEGATIVE_INFINITY,
    ...overrides,
  };
}

test("accepts an unobstructed in-range primary pass", () => {
  const result = computeKick(validInput());
  assert.equal(result.accepted, true);
  assert.equal(result.kind, KickKind.PASS);
  assert.ok(result.velocity.x > 0);
  assert.ok(result.velocity.z > 0, "a normal pass must not be driven into the floor");
  assert.equal(result.writeAngularVelocity, false);
  assert.equal(Object.hasOwn(result, "angularVelocity"), false);
  assert.equal(result.wasClamped, false);
  assert.equal(result.unclampedSpeed, result.finalSpeed);
});

test("rejects remote, obstructed, behind-view, and cooling-down kicks", () => {
  assert.equal(computeKick(validInput({ ballPosition: vector(200, 0, 24) })).reason, "out_of_reach");
  assert.equal(computeKick(validInput({ lineOfSight: false })).reason, "obstructed");
  assert.equal(computeKick(validInput({ eyeAngles: { pitch: 0, yaw: 180, roll: 0 } })).reason, "outside_aim_cone");
  assert.equal(computeKick(validInput({ lastAcceptedKickTime: 9.9 })).reason, "cooldown");
});

test("rejects spectators, unassigned pawns, and malformed eligibility", () => {
  assert.equal(
    computeKick(validInput({ playerEligible: false })).reason,
    "player_ineligible",
  );
  assert.equal(
    computeKick(validInput({ playerEligible: "true" })).reason,
    "invalid_input",
  );
});

test("secondary attack is not a SoccerMod kick", () => {
  const result = computeKick(validInput({ attackType: "secondary" }));
  assert.equal(result.accepted, false);
  assert.equal(result.reason, "unsupported_attack");
});

test("all accepted kicks obey the maximum speed", () => {
  const result = computeKick(
    validInput({ ballVelocity: vector(10_000, 10_000, 10_000) }),
  );
  assert.equal(result.accepted, true);
  assert.equal(result.wasClamped, true);
  assert.ok(result.unclampedSpeed > result.maximumBallSpeed);
  assert.ok(Math.abs(result.finalSpeed - 1_250) <= 1e-9);
  assert.ok(length(result.velocity) <= 1_250 + 1e-9);
});

test("typical ball-facing pitch keeps the primary kick floor-safe", () => {
  const pitchToBall = Math.atan2(40, 60) * 180 / Math.PI;
  const pass = computeKick(validInput({
    eyeAngles: { pitch: pitchToBall, yaw: 0, roll: 0 },
  }));
  assert.equal(pass.accepted, true);
  assert.ok(pass.velocity.z >= 0);
});

test("invalid time and configuration values fail closed", () => {
  assert.equal(computeKick(validInput({ now: Number.NaN })).reason, "invalid_time");
  assert.equal(
    computeKick(validInput({ lastAcceptedKickTime: 11 })).reason,
    "invalid_time",
  );
  assert.equal(
    computeKick(validInput(), { maximumBallSpeed: -100 }).reason,
    "invalid_config",
  );
  assert.equal(
    computeKick(validInput(), { inheritedVelocityRatio: Number.POSITIVE_INFINITY }).reason,
    "invalid_config",
  );
  assert.equal(computeKick(null).reason, "invalid_input");
  assert.equal(computeKick(validInput(), null).reason, "invalid_config");
});

test("malformed booleans and overflowing finite arithmetic fail closed", () => {
  assert.equal(
    computeKick(validInput({ isDucking: "false" })).reason,
    "invalid_input",
  );
  assert.equal(
    computeKick(validInput({
      ballVelocity: vector(Number.MAX_VALUE, Number.MAX_VALUE, Number.MAX_VALUE),
    }), {
      passSpeed: Number.MAX_VALUE,
      inheritedVelocityRatio: 1,
    }).reason,
    "invalid_vector",
  );
  assert.equal(
    computeKick(validInput({
      eyePosition: vector(-Number.MAX_VALUE, 0, 64),
      ballPosition: vector(Number.MAX_VALUE, 0, 24),
    })).reason,
    "invalid_vector",
  );
});

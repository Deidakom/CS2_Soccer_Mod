import assert from "node:assert/strict";
import test from "node:test";

import {
  PHYSICS_TRIAL_PROFILE_IDS,
  createPhysicsTrialSpec,
  parsePhysicsTrialRequest,
  summarizeDropBounce,
} from "../src/ball-lab/physics-diagnostics.js";

test("physics diagnostic profiles are fixed, finite, and bounded", () => {
  assert.equal(PHYSICS_TRIAL_PROFILE_IDS.length, 19);
  for (const profileId of PHYSICS_TRIAL_PROFILE_IDS) {
    const spec = createPhysicsTrialSpec(profileId);
    assert.equal(spec.profileId, profileId);
    assert.ok(Number.isSafeInteger(spec.maxThinkCount));
    assert.ok(spec.maxThinkCount >= 2 && spec.maxThinkCount <= 640);
    for (const value of [
      ...Object.values(spec.startPosition),
      ...Object.values(spec.initialVelocity),
    ]) assert.equal(Number.isFinite(value), true);
    assert.ok(Math.hypot(...Object.values(spec.initialVelocity)) <= 1250 + 1e-9);
  }
});

test("physics trial requests reject free-form and unbounded input", () => {
  assert.deepEqual(parsePhysicsTrialRequest("wake_y_200"), {
    accepted: true,
    profileId: "wake_y_200",
    trialCount: 1,
  });
  assert.deepEqual(parsePhysicsTrialRequest("DROP_64 10"), {
    accepted: true,
    profileId: "drop_64",
    trialCount: 10,
  });
  assert.equal(parsePhysicsTrialRequest("custom 1").accepted, false);
  assert.equal(parsePhysicsTrialRequest("drop_64 0").accepted, false);
  assert.equal(parsePhysicsTrialRequest("drop_64 101").accepted, false);
  assert.equal(parsePhysicsTrialRequest("drop_64 1 extra").accepted, false);
});

test("goal profiles encode forward, reverse, and aperture behavior", () => {
  const forward = createPhysicsTrialSpec("goal_east_1250");
  assert.equal(forward.expectGoal, true);
  assert.ok(forward.startPosition.x < forward.goalPlane);
  assert.ok(forward.initialVelocity.x > 0);

  const reverse = createPhysicsTrialSpec("reverse_east_1250");
  assert.equal(reverse.expectGoal, false);
  assert.ok(reverse.startPosition.x > reverse.goalPlane);
  assert.ok(reverse.initialVelocity.x < 0);

  const nearMiss = createPhysicsTrialSpec("near_miss_east_1250");
  assert.equal(nearMiss.expectGoal, false);
  assert.ok(nearMiss.startPosition.y > 104);
});

test("drop, roll, and wall profiles use only their dedicated fixtures", () => {
  const drop = createPhysicsTrialSpec("drop_64");
  assert.deepEqual(drop.startPosition, { x: 4096, y: -512, z: 79 });

  const rollX = createPhysicsTrialSpec("roll_x_200");
  const rollY = createPhysicsTrialSpec("roll_y_200");
  assert.deepEqual(rollX.startPosition, { x: 2560, y: -1024, z: 15 });
  assert.deepEqual(rollY.startPosition, { x: 5600, y: -1536, z: 15 });

  for (const profileId of ["wall_y_300_0", "wall_y_600_30", "wall_y_1000_45"]) {
    assert.deepEqual(createPhysicsTrialSpec(profileId).startPosition, {
      x: 3584,
      y: 1200,
      z: 15,
    });
  }
});

test("drop bounce summary excludes the original release height", () => {
  assert.deepEqual(summarizeDropBounce({
    floorZ: 15.03125,
    impactMinZ: 12.099943161010742,
    apexZ: 14.422845840454102,
  }), {
    floorCenterZ: 15.03125,
    firstBounceApexCenterZ: 14.422845840454102,
    bounceImpactCenterZ: 12.099943161010742,
    firstBounceHeight: 2.3229026794433594,
    maximumFloorCenterPenetration: 2.931306838989258,
  });
  assert.deepEqual(summarizeDropBounce({
    floorZ: null,
    impactMinZ: null,
    apexZ: null,
  }), {
    floorCenterZ: null,
    firstBounceApexCenterZ: null,
    bounceImpactCenterZ: null,
    firstBounceHeight: null,
    maximumFloorCenterPenetration: null,
  });
});

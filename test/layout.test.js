import assert from "node:assert/strict";
import test from "node:test";

import { LAB_LAYOUT } from "../src/ball-lab/layout.js";

test("template lab layout keeps the nominal ball at verified floor contact", () => {
  const scaledRadius = 7.9 * LAB_LAYOUT.ball.modelScale;
  assert.ok(Math.abs(scaledRadius - LAB_LAYOUT.ball.nominalRadius) <= 1e-9);
  assert.equal(LAB_LAYOUT.reset.restPosition.z, LAB_LAYOUT.ball.nominalRadius);
  assert.equal(LAB_LAYOUT.reset.writeClearance, 0);
  assert.ok(LAB_LAYOUT.reset.restPosition.x >= -256);
  assert.ok(LAB_LAYOUT.reset.restPosition.x <= 1_280);
  assert.ok(LAB_LAYOUT.reset.restPosition.y >= -976);
  assert.ok(LAB_LAYOUT.reset.restPosition.y <= 960);
});

test("template lab goals bracket reset on the audited team axis", () => {
  const [west, east] = LAB_LAYOUT.goals;
  assert.equal(west.axis, "x");
  assert.equal(east.axis, "x");
  assert.equal(west.direction, -1);
  assert.equal(east.direction, 1);
  assert.equal(west.scoringTeam, 2);
  assert.equal(east.scoringTeam, 3);
  assert.ok(west.plane < LAB_LAYOUT.reset.restPosition.x);
  assert.ok(east.plane > LAB_LAYOUT.reset.restPosition.x);
  assert.deepEqual(
    new Set([
      LAB_LAYOUT.reset.markerName,
      west.markerName,
      east.markerName,
    ]).size,
    3,
  );
});

test("dedicated physics fixtures isolate symmetric roll lanes and wall paths", () => {
  const fixtures = LAB_LAYOUT.physicsFixtures;
  assert.equal(fixtures.floor.origin.z + fixtures.floor.halfExtents.z, fixtures.floor.topZ);
  assert.equal(fixtures.dropCenter.z, fixtures.floor.topZ + LAB_LAYOUT.ball.nominalRadius);
  assert.equal(fixtures.rollXStart.z, fixtures.dropCenter.z);
  assert.equal(fixtures.rollYStart.z, fixtures.dropCenter.z);
  assert.equal(fixtures.wallStart.z, fixtures.dropCenter.z);

  const floorMinX = fixtures.floor.origin.x - fixtures.floor.halfExtents.x;
  const floorMaxX = fixtures.floor.origin.x + fixtures.floor.halfExtents.x;
  const floorMinY = fixtures.floor.origin.y - fixtures.floor.halfExtents.y;
  const floorMaxY = fixtures.floor.origin.y + fixtures.floor.halfExtents.y;
  for (const position of [
    fixtures.dropCenter,
    fixtures.rollXStart,
    fixtures.rollYStart,
    fixtures.wallStart,
  ]) {
    assert.ok(position.x > floorMinX && position.x < floorMaxX);
    assert.ok(position.y > floorMinY && position.y < floorMaxY);
  }

  const nearWallY = fixtures.wall.origin.y - fixtures.wall.halfExtents.y;
  assert.ok(fixtures.wallStart.y < nearWallY);
  assert.ok(fixtures.wallStart.x >= fixtures.wall.origin.x - fixtures.wall.halfExtents.x);
  assert.ok(fixtures.wallStart.x <= fixtures.wall.origin.x + fixtures.wall.halfExtents.x);
  assert.ok(floorMinX > 1280, "fixture must not overlap the audited template bounds");
});

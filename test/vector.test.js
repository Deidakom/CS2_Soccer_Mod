import assert from "node:assert/strict";
import test from "node:test";

import {
  clampMagnitude,
  length,
  qAngleToForward,
  vector,
} from "../src/ball-lab/core/vector.js";

const close = (actual, expected, epsilon = 1e-9) => {
  assert.ok(Math.abs(actual - expected) <= epsilon, `${actual} != ${expected}`);
};

test("qAngleToForward follows Source yaw axes", () => {
  const east = qAngleToForward({ pitch: 0, yaw: 0, roll: 0 });
  close(east.x, 1);
  close(east.y, 0);
  close(east.z, 0);

  const north = qAngleToForward({ pitch: 0, yaw: 90, roll: 0 });
  close(north.x, 0, 1e-8);
  close(north.y, 1);
  close(north.z, 0);
});

test("positive pitch points downward in Source coordinates", () => {
  const down = qAngleToForward({ pitch: 90, yaw: 0, roll: 0 });
  close(down.z, -1);
});

test("clampMagnitude preserves direction and caps length", () => {
  const result = clampMagnitude(vector(3, 4, 0), 2.5);
  close(length(result), 2.5);
  close(result.x / result.y, 3 / 4);
});

test("clampMagnitude handles sub-epsilon and overflow-sized vectors", () => {
  const tiny = clampMagnitude(vector(5e-10, 0, 0), 1e-12);
  close(length(tiny), 1e-12, 1e-24);

  const huge = clampMagnitude(
    vector(Number.MAX_VALUE, Number.MAX_VALUE, 0),
    1_250,
  );
  assert.equal(Number.isFinite(huge.x), true);
  assert.equal(Number.isFinite(huge.y), true);
  close(length(huge), 1_250, 1e-9);
});

test("clampMagnitude rejects negative and non-finite caps", () => {
  assert.throws(() => clampMagnitude(vector(1, 0, 0), -1), /nonnegative/);
  assert.throws(
    () => clampMagnitude(vector(1, 0, 0), Number.NaN),
    /nonnegative/,
  );
});

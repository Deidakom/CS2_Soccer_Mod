export function vector(x = 0, y = 0, z = 0) {
  return { x, y, z };
}

export function isFiniteVector(value) {
  return Boolean(
    value
      && Number.isFinite(value.x)
      && Number.isFinite(value.y)
      && Number.isFinite(value.z),
  );
}

export function add(left, right) {
  return vector(left.x + right.x, left.y + right.y, left.z + right.z);
}

export function subtract(left, right) {
  return vector(left.x - right.x, left.y - right.y, left.z - right.z);
}

export function scale(value, multiplier) {
  return vector(value.x * multiplier, value.y * multiplier, value.z * multiplier);
}

export function dot(left, right) {
  return left.x * right.x + left.y * right.y + left.z * right.z;
}

export function lengthSquared(value) {
  return dot(value, value);
}

export function length(value) {
  return Math.hypot(value.x, value.y, value.z);
}

export function normalize(value) {
  if (!isFiniteVector(value)) {
    throw new Error("vector must be finite");
  }
  const maximumComponent = Math.max(
    Math.abs(value.x),
    Math.abs(value.y),
    Math.abs(value.z),
  );
  if (maximumComponent === 0) return vector();
  const scaled = vector(
    value.x / maximumComponent,
    value.y / maximumComponent,
    value.z / maximumComponent,
  );
  const scaledMagnitude = Math.hypot(scaled.x, scaled.y, scaled.z);
  return scale(scaled, 1 / scaledMagnitude);
}

export function clampMagnitude(value, maximum) {
  if (!isFiniteVector(value)) {
    throw new Error("vector must be finite");
  }
  if (!Number.isFinite(maximum) || maximum < 0) {
    throw new Error("maximum magnitude must be finite and nonnegative");
  }
  const maximumComponent = Math.max(
    Math.abs(value.x),
    Math.abs(value.y),
    Math.abs(value.z),
  );
  if (maximumComponent === 0) return vector();
  const scaledMagnitude = Math.hypot(
    value.x / maximumComponent,
    value.y / maximumComponent,
    value.z / maximumComponent,
  );
  const maximumRatio = maximum / maximumComponent;
  if (scaledMagnitude <= maximumRatio) {
    return vector(value.x, value.y, value.z);
  }
  return scale(value, maximumRatio / scaledMagnitude);
}

export function qAngleToForward(angles) {
  const degreesToRadians = Math.PI / 180;
  const pitch = angles.pitch * degreesToRadians;
  const yaw = angles.yaw * degreesToRadians;
  const cosPitch = Math.cos(pitch);

  return normalize(vector(
    cosPitch * Math.cos(yaw),
    cosPitch * Math.sin(yaw),
    -Math.sin(pitch),
  ));
}

#!/usr/bin/env node

import fs from "node:fs";

function findMatchingBrace(text, openIndex) {
  let depth = 0;
  let quoted = false;
  let escaped = false;
  for (let index = openIndex; index < text.length; index += 1) {
    const character = text[index];
    if (quoted) {
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === '"') quoted = false;
      continue;
    }
    if (character === '"') quoted = true;
    else if (character === "{") depth += 1;
    else if (character === "}" && --depth === 0) return index;
  }
  throw new Error(`unclosed brace at byte ${openIndex}`);
}

function vector(text) {
  const values = text.trim().split(/\s+/).map(Number);
  return values.length === 3 && values.every(Number.isFinite) ? values : null;
}

function bounds(points) {
  const minimum = [Infinity, Infinity, Infinity];
  const maximum = [-Infinity, -Infinity, -Infinity];
  for (const point of points) {
    for (let axis = 0; axis < 3; axis += 1) {
      minimum[axis] = Math.min(minimum[axis], point[axis]);
      maximum[axis] = Math.max(maximum[axis], point[axis]);
    }
  }
  return {
    minimum,
    maximum,
    size: maximum.map((value, axis) => value - minimum[axis]),
  };
}

function inspectMesh(block, ordinal) {
  const id = block.match(/"id"\s+"elementid"\s+"([^"]+)"/)?.[1] ?? null;
  const nodeId = Number(block.match(/"nodeID"\s+"int"\s+"(-?\d+)"/)?.[1]);
  const origin = vector(block.match(/"origin"\s+"vector3"\s+"([^"]+)"/)?.[1] ?? "");
  const scales = vector(block.match(/"scales"\s+"vector3"\s+"([^"]+)"/)?.[1] ?? "");
  const material = block.match(/"materials"\s+"string_array"\s*\[\s*"([^"]+)"/)?.[1] ?? null;
  const positionStream = block.match(
    /"standardAttributeName"\s+"string"\s+"position"[\s\S]*?"data"\s+"vector3_array"\s*\[([\s\S]*?)\]/,
  );
  const points = [...(positionStream?.[1] ?? "").matchAll(/"([^"]+)"/g)]
    .map((match) => vector(match[1]))
    .filter(Boolean);
  return {
    ordinal,
    id,
    nodeId: Number.isFinite(nodeId) ? nodeId : null,
    origin,
    scales,
    material,
    vertexCount: points.length,
    bounds: points.length > 0 ? bounds(points) : null,
  };
}

const [, , inputPath] = process.argv;
if (!inputPath) throw new Error("usage: inspect-vmap-meshes.mjs <text.vmap>");
const text = fs.readFileSync(inputPath, "utf8");
const meshes = [];
const marker = '"CMapMesh"';
let searchIndex = 0;
while (true) {
  const markerIndex = text.indexOf(marker, searchIndex);
  if (markerIndex < 0) break;
  const openIndex = text.indexOf("{", markerIndex + marker.length);
  if (openIndex < 0) throw new Error(`mesh at byte ${markerIndex} has no body`);
  const closeIndex = findMatchingBrace(text, openIndex);
  meshes.push(inspectMesh(text.slice(openIndex, closeIndex + 1), meshes.length + 1));
  searchIndex = closeIndex + 1;
}
process.stdout.write(`${JSON.stringify(meshes, null, 2)}\n`);

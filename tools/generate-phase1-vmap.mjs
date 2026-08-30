import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { LAB_LAYOUT } from "../src/ball-lab/layout.js";

function formatVector(value) {
  return `${value.x} ${value.y} ${value.z}`;
}

function goalMarkerPosition(goal) {
  const lateralAxis = goal.axis === "x" ? "y" : "x";
  return {
    x: goal.axis === "x" ? goal.plane : goal.lateralCenter,
    y: lateralAxis === "y" ? goal.lateralCenter : goal.plane,
    z: (goal.minimumHeight + goal.maximumHeight) / 2,
  };
}

const RESET_WRITE_POSITION = Object.freeze({
  ...LAB_LAYOUT.reset.restPosition,
  z: LAB_LAYOUT.reset.restPosition.z + LAB_LAYOUT.reset.writeClearance,
});
const [WEST_GOAL, EAST_GOAL] = LAB_LAYOUT.goals;
const DOCUMENT_ELEMENT_ID = "5c325137-f715-49e0-99e8-0e14bdb6c001";
const FIXTURE_SOURCE_MESH_ID = "b8e18729-69bc-4417-94eb-4690a2615c4a";
const FIXTURE_MATERIAL = "materials/dev/reflectivity_30.vmat";

const SCRIPT_ASSETS = Object.freeze([
  "maps/scripts/ball_lab/adapter.vjs",
  LAB_LAYOUT.ball.model,
]);

const PHYSICS_FIXTURE_SPECS = Object.freeze([
  Object.freeze({
    name: "floor",
    namespace: 1,
    nodeId: "10006",
    referenceId: "0x51ab200000000006",
    origin: LAB_LAYOUT.physicsFixtures.floor.origin,
    halfExtents: LAB_LAYOUT.physicsFixtures.floor.halfExtents,
  }),
  Object.freeze({
    name: "wall",
    namespace: 2,
    nodeId: "10007",
    referenceId: "0x51ab200000000007",
    origin: LAB_LAYOUT.physicsFixtures.wall.origin,
    halfExtents: LAB_LAYOUT.physicsFixtures.wall.halfExtents,
  }),
]);

function fixtureElementId(namespace, index) {
  return `5c325137-f715-49e0-99e8-0e14bdb7${namespace.toString(16)}${index.toString(16).padStart(3, "0")}`;
}

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
  throw new Error(`unclosed fixture source mesh at byte ${openIndex}`);
}

function removeWorldEntityByClass(source, className) {
  const classToken = `"classname" "string" "${className}"`;
  let output = source;
  let classIndex = output.indexOf(classToken);
  while (classIndex >= 0) {
    const entityIndex = output.lastIndexOf('"CMapEntity"', classIndex);
    const openIndex = output.indexOf("{", entityIndex);
    if (entityIndex < 0 || openIndex < 0 || openIndex > classIndex) {
      throw new Error(`template ${className} entity is malformed`);
    }
    const closeIndex = findMatchingBrace(output, openIndex);
    if (classIndex > closeIndex) {
      throw new Error(`template ${className} classname escaped its entity`);
    }
    let endIndex = closeIndex + 1;
    while (output[endIndex] === " " || output[endIndex] === "\t") endIndex += 1;
    if (output[endIndex] === ",") endIndex += 1;
    if (output.slice(endIndex, endIndex + 2) === "\r\n") endIndex += 2;
    else if (output[endIndex] === "\n") endIndex += 1;
    output = `${output.slice(0, entityIndex)}${output.slice(endIndex)}`;
    classIndex = output.indexOf(classToken);
  }
  return output;
}

function extractFixtureSourceMesh(source) {
  const idToken = `"id" "elementid" "${FIXTURE_SOURCE_MESH_ID}"`;
  const idIndex = source.indexOf(idToken);
  if (idIndex < 0) throw new Error("pinned template fixture source mesh is missing");
  const meshIndex = source.lastIndexOf('"CMapMesh"', idIndex);
  const openIndex = source.indexOf("{", meshIndex);
  if (meshIndex < 0 || openIndex < 0 || openIndex > idIndex) {
    throw new Error("pinned template fixture source mesh is malformed");
  }
  return source.slice(meshIndex, findMatchingBrace(source, openIndex) + 1);
}

function resizeFixturePositions(mesh, spec) {
  const positionPattern = /(\"standardAttributeName\"\s+\"string\"\s+\"position\"[\s\S]*?\"data\"\s+\"vector3_array\"\s*\[)([\s\S]*?)(\])/;
  const match = positionPattern.exec(mesh);
  if (!match) throw new Error("pinned template fixture source has no position stream");
  let vertexCount = 0;
  const positions = match[2].replace(/"([^"]+)"/g, (quoted, value) => {
    const coordinates = value.trim().split(/\s+/).map(Number);
    if (coordinates.length !== 3
        || coordinates.some((coordinate) => !Number.isFinite(coordinate)
          || Math.abs(Math.abs(coordinate) - 8) > 1e-9)) {
      throw new Error(`pinned fixture cube vertex drifted: ${value}`);
    }
    vertexCount += 1;
    const halfExtents = [spec.halfExtents.x, spec.halfExtents.y, spec.halfExtents.z];
    return `"${coordinates.map((coordinate, axis) => (
      Math.sign(coordinate) * halfExtents[axis]
    )).join(" ")}"`;
  });
  if (vertexCount !== 8) {
    throw new Error(`pinned fixture cube must have 8 vertices, found ${vertexCount}`);
  }
  return `${mesh.slice(0, match.index)}${match[1]}${positions}${match[3]}${mesh.slice(match.index + match[0].length)}`;
}

function createPhysicsFixtureMesh(sourceMesh, spec) {
  const sourceIds = [...sourceMesh.matchAll(/"id"\s+"elementid"\s+"([^"]+)"/g)];
  if (sourceIds.length < 2 || sourceIds.length > 32) {
    throw new Error(`pinned fixture source element count is unsafe: ${sourceIds.length}`);
  }
  let idIndex = 0;
  let mesh = sourceMesh.replace(
    /("id"\s+"elementid"\s+")[^"]+(")/g,
    (_, prefix, suffix) => `${prefix}${fixtureElementId(spec.namespace, ++idIndex)}${suffix}`,
  );
  if (idIndex !== sourceIds.length) {
    throw new Error("fixture element id replacement count drifted");
  }
  mesh = mesh.replace(
    /("nodeID"\s+"int"\s+")[^"]+"/,
    `$1${spec.nodeId}"`,
  ).replace(
    /("referenceID"\s+"uint64"\s+")[^"]+"/,
    `$1${spec.referenceId}"`,
  ).replace(
    /("origin"\s+"vector3"\s+")[^"]+"/,
    `$1${formatVector(spec.origin)}"`,
  ).replace(
    /("scales"\s+"vector3"\s+")[^"]+"/,
    (_, prefix) => `${prefix}1 1 1"`,
  ).replace(
    "materials/models/props_junk/trashclusters01.vmat",
    FIXTURE_MATERIAL,
  );
  mesh = resizeFixturePositions(mesh, spec);
  return `${mesh},\n`;
}

function createPhysicsFixtureMeshes(source) {
  const sourceMesh = extractFixtureSourceMesh(source);
  return PHYSICS_FIXTURE_SPECS
    .map((spec) => createPhysicsFixtureMesh(sourceMesh, spec))
    .join("");
}

function createMarkerEntity(spec) {
  return `\t\t\t"CMapEntity"
\t\t\t{
\t\t\t\t"id" "elementid" "${spec.ids[0]}"
\t\t\t\t"nodeID" "int" "${spec.nodeId}"
\t\t\t\t"referenceID" "uint64" "${spec.referenceId}"
\t\t\t\t"children" "element_array"
\t\t\t\t[
\t\t\t\t]
\t\t\t\t"variableTargetKeys" "string_array"
\t\t\t\t[
\t\t\t\t]
\t\t\t\t"variableNames" "string_array"
\t\t\t\t[
\t\t\t\t]
\t\t\t\t"relayPlugData" "DmePlugList"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "${spec.ids[1]}"
\t\t\t\t\t"names" "string_array"
\t\t\t\t\t[
\t\t\t\t\t]
\t\t\t\t\t"dataTypes" "int_array"
\t\t\t\t\t[
\t\t\t\t\t]
\t\t\t\t\t"plugTypes" "int_array"
\t\t\t\t\t[
\t\t\t\t\t]
\t\t\t\t\t"descriptions" "string_array"
\t\t\t\t\t[
\t\t\t\t\t]
\t\t\t\t}
\t\t\t\t"connectionsData" "element_array"
\t\t\t\t[
\t\t\t\t]
\t\t\t\t"entity_properties" "EditGameClassProps"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "${spec.ids[2]}"
\t\t\t\t\t"classname" "string" "info_target"
\t\t\t\t\t"targetname" "string" "${spec.targetName}"
\t\t\t\t}
\t\t\t\t"hitNormal" "vector3" "0 0 1"
\t\t\t\t"isProceduralEntity" "bool" "0"
\t\t\t\t"origin" "vector3" "${spec.origin}"
\t\t\t\t"angles" "qangle" "0 0 0"
\t\t\t\t"scales" "vector3" "1 1 1"
\t\t\t\t"transformLocked" "bool" "0"
\t\t\t\t"transformPin" "DmElement"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "${spec.ids[3]}"
\t\t\t\t\t"name" "string" "transformPin"
\t\t\t\t\t"referenceName" "string" ""
\t\t\t\t\t"targetReferenceID" "uint64" "0x0"
\t\t\t\t\t"offsetOrigin" "vector3" "0 0 0"
\t\t\t\t\t"offsetAngles" "qangle" "0 0 0"
\t\t\t\t\t"pinAngles" "bool" "1"
\t\t\t\t\t"twoWay" "bool" "0"
\t\t\t\t}
\t\t\t\t"force_hidden" "bool" "0"
\t\t\t\t"editorOnly" "bool" "0"
\t\t\t\t"customVisGroup" "string" ""
\t\t\t\t"randomSeed" "int" "${spec.randomSeed}"
\t\t\t},
`;
}

const MARKER_SPECS = Object.freeze([
  Object.freeze({
    targetName: LAB_LAYOUT.reset.markerName,
    origin: formatVector(LAB_LAYOUT.reset.restPosition),
    nodeId: "10003",
    referenceId: "0x51ab200000000003",
    ids: Object.freeze([
      "5c325137-f715-49e0-99e8-0e14bdb6d001",
      "5c325137-f715-49e0-99e8-0e14bdb6d002",
      "5c325137-f715-49e0-99e8-0e14bdb6d003",
      "5c325137-f715-49e0-99e8-0e14bdb6d004",
    ]),
    randomSeed: "17012003",
  }),
  Object.freeze({
    targetName: WEST_GOAL.markerName,
    origin: formatVector(goalMarkerPosition(WEST_GOAL)),
    nodeId: "10004",
    referenceId: "0x51ab200000000004",
    ids: Object.freeze([
      "5c325137-f715-49e0-99e8-0e14bdb6d101",
      "5c325137-f715-49e0-99e8-0e14bdb6d102",
      "5c325137-f715-49e0-99e8-0e14bdb6d103",
      "5c325137-f715-49e0-99e8-0e14bdb6d104",
    ]),
    randomSeed: "17012004",
  }),
  Object.freeze({
    targetName: EAST_GOAL.markerName,
    origin: formatVector(goalMarkerPosition(EAST_GOAL)),
    nodeId: "10005",
    referenceId: "0x51ab200000000005",
    ids: Object.freeze([
      "5c325137-f715-49e0-99e8-0e14bdb6d201",
      "5c325137-f715-49e0-99e8-0e14bdb6d202",
      "5c325137-f715-49e0-99e8-0e14bdb6d203",
      "5c325137-f715-49e0-99e8-0e14bdb6d204",
    ]),
    randomSeed: "17012005",
  }),
]);

const MARKER_ENTITIES = MARKER_SPECS.map(createMarkerEntity).join("");

const RESERVED_IDENTIFIERS = Object.freeze([
  DOCUMENT_ELEMENT_ID,
  "5c325137-f715-49e0-99e8-0e14bdb6ce01",
  "5c325137-f715-49e0-99e8-0e14bdb6ce02",
  "5c325137-f715-49e0-99e8-0e14bdb6ce03",
  "5c325137-f715-49e0-99e8-0e14bdb6ce04",
  "5c325137-f715-49e0-99e8-0e14bdb6cf01",
  "5c325137-f715-49e0-99e8-0e14bdb6cf02",
  "5c325137-f715-49e0-99e8-0e14bdb6cf03",
  "5c325137-f715-49e0-99e8-0e14bdb6cf04",
  "0x51ab200000000001",
  "0x51ab200000000002",
  "10001",
  "10002",
  ...PHYSICS_FIXTURE_SPECS.flatMap((spec) => [
    spec.referenceId,
    spec.nodeId,
    ...Array.from({ length: 32 }, (_, index) => fixtureElementId(spec.namespace, index + 1)),
  ]),
  ...MARKER_SPECS.flatMap((spec) => [
    ...spec.ids,
    spec.referenceId,
    spec.nodeId,
  ]),
]);

function normalizeDocumentElementId(source) {
  const pattern = /(\"\$prefix_element\$\"\s*\{\s*\"id\"\s+\"elementid\"\s+)\"[^\"]+\"/;
  if (!pattern.test(source)) {
    throw new Error("template map has no prefix element id");
  }
  return source.replace(
    pattern,
    (_, prefix) => `${prefix}"${DOCUMENT_ELEMENT_ID}"`,
  );
}

const POINT_SCRIPT_ENTITY = `\t\t\t"CMapEntity"
\t\t\t{
\t\t\t\t"id" "elementid" "5c325137-f715-49e0-99e8-0e14bdb6ce01"
\t\t\t\t"nodeID" "int" "10001"
\t\t\t\t"referenceID" "uint64" "0x51ab200000000001"
\t\t\t\t"children" "element_array"
\t\t\t\t[
\t\t\t\t]
\t\t\t\t"variableTargetKeys" "string_array"
\t\t\t\t[
\t\t\t\t]
\t\t\t\t"variableNames" "string_array"
\t\t\t\t[
\t\t\t\t]
\t\t\t\t"relayPlugData" "DmePlugList"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "5c325137-f715-49e0-99e8-0e14bdb6ce02"
\t\t\t\t\t"names" "string_array"
\t\t\t\t\t[
\t\t\t\t\t]
\t\t\t\t\t"dataTypes" "int_array"
\t\t\t\t\t[
\t\t\t\t\t]
\t\t\t\t\t"plugTypes" "int_array"
\t\t\t\t\t[
\t\t\t\t\t]
\t\t\t\t\t"descriptions" "string_array"
\t\t\t\t\t[
\t\t\t\t\t]
\t\t\t\t}
\t\t\t\t"connectionsData" "element_array"
\t\t\t\t[
\t\t\t\t]
\t\t\t\t"entity_properties" "EditGameClassProps"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "5c325137-f715-49e0-99e8-0e14bdb6ce03"
\t\t\t\t\t"classname" "string" "point_script"
\t\t\t\t\t"targetname" "string" "sm_ball_lab_script"
\t\t\t\t\t"cs_script" "string" "maps/scripts/ball_lab/adapter.vjs"
\t\t\t\t}
\t\t\t\t"hitNormal" "vector3" "0 0 1"
\t\t\t\t"isProceduralEntity" "bool" "0"
\t\t\t\t"origin" "vector3" "${LAB_LAYOUT.reset.restPosition.x} ${LAB_LAYOUT.reset.restPosition.y} 64"
\t\t\t\t"angles" "qangle" "0 0 0"
\t\t\t\t"scales" "vector3" "1 1 1"
\t\t\t\t"transformLocked" "bool" "0"
\t\t\t\t"transformPin" "DmElement"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "5c325137-f715-49e0-99e8-0e14bdb6ce04"
\t\t\t\t\t"name" "string" "transformPin"
\t\t\t\t\t"referenceName" "string" ""
\t\t\t\t\t"targetReferenceID" "uint64" "0x0"
\t\t\t\t\t"offsetOrigin" "vector3" "0 0 0"
\t\t\t\t\t"offsetAngles" "qangle" "0 0 0"
\t\t\t\t\t"pinAngles" "bool" "1"
\t\t\t\t\t"twoWay" "bool" "0"
\t\t\t\t}
\t\t\t\t"force_hidden" "bool" "0"
\t\t\t\t"editorOnly" "bool" "0"
\t\t\t\t"customVisGroup" "string" ""
\t\t\t\t"randomSeed" "int" "17012001"
\t\t\t},
`;

const BALL_ENTITY = `\t\t\t"CMapEntity"
\t\t\t{
\t\t\t\t"id" "elementid" "5c325137-f715-49e0-99e8-0e14bdb6cf01"
\t\t\t\t"nodeID" "int" "10002"
\t\t\t\t"referenceID" "uint64" "0x51ab200000000002"
\t\t\t\t"children" "element_array"
\t\t\t\t[
\t\t\t\t]
\t\t\t\t"variableTargetKeys" "string_array"
\t\t\t\t[
\t\t\t\t]
\t\t\t\t"variableNames" "string_array"
\t\t\t\t[
\t\t\t\t]
\t\t\t\t"relayPlugData" "DmePlugList"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "5c325137-f715-49e0-99e8-0e14bdb6cf02"
\t\t\t\t\t"names" "string_array"
\t\t\t\t\t[
\t\t\t\t\t]
\t\t\t\t\t"dataTypes" "int_array"
\t\t\t\t\t[
\t\t\t\t\t]
\t\t\t\t\t"plugTypes" "int_array"
\t\t\t\t\t[
\t\t\t\t\t]
\t\t\t\t\t"descriptions" "string_array"
\t\t\t\t\t[
\t\t\t\t\t]
\t\t\t\t}
\t\t\t\t"connectionsData" "element_array"
\t\t\t\t[
\t\t\t\t]
\t\t\t\t"entity_properties" "EditGameClassProps"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "5c325137-f715-49e0-99e8-0e14bdb6cf03"
\t\t\t\t\t"classname" "string" "prop_physics_multiplayer"
\t\t\t\t\t"targetname" "string" "${LAB_LAYOUT.ball.entityName}"
\t\t\t\t\t"model" "string" "${LAB_LAYOUT.ball.model}"
\t\t\t\t\t"skin" "string" "default"
\t\t\t\t\t"solid" "string" "6"
\t\t\t\t\t"spawnflags" "string" "2"
\t\t\t\t\t"massScale" "string" "0"
\t\t\t\t\t"inertiaScale" "string" "1"
\t\t\t\t\t"nodamageforces" "string" "1"
\t\t\t\t\t"physicsmode" "string" "1"
\t\t\t\t\t"disableshadows" "string" "2"
\t\t\t\t\t"rendertocubemaps" "string" "0"
\t\t\t\t}
\t\t\t\t"hitNormal" "vector3" "0 0 1"
\t\t\t\t"isProceduralEntity" "bool" "0"
\t\t\t\t"origin" "vector3" "${formatVector(RESET_WRITE_POSITION)}"
\t\t\t\t"angles" "qangle" "0 0 0"
\t\t\t\t"scales" "vector3" "${LAB_LAYOUT.ball.modelScale} ${LAB_LAYOUT.ball.modelScale} ${LAB_LAYOUT.ball.modelScale}"
\t\t\t\t"transformLocked" "bool" "0"
\t\t\t\t"transformPin" "DmElement"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "5c325137-f715-49e0-99e8-0e14bdb6cf04"
\t\t\t\t\t"name" "string" "transformPin"
\t\t\t\t\t"referenceName" "string" ""
\t\t\t\t\t"targetReferenceID" "uint64" "0x0"
\t\t\t\t\t"offsetOrigin" "vector3" "0 0 0"
\t\t\t\t\t"offsetAngles" "qangle" "0 0 0"
\t\t\t\t\t"pinAngles" "bool" "1"
\t\t\t\t\t"twoWay" "bool" "0"
\t\t\t\t}
\t\t\t\t"force_hidden" "bool" "0"
\t\t\t\t"editorOnly" "bool" "0"
\t\t\t\t"customVisGroup" "string" ""
\t\t\t\t"randomSeed" "int" "17012002"
\t\t\t},
`;

function insertAssetReferences(source) {
  const marker = '"map_asset_references" "string_array"';
  const markerIndex = source.indexOf(marker);
  if (markerIndex < 0) throw new Error("template map has no asset reference list");
  const openIndex = source.indexOf("[", markerIndex);
  const closeIndex = source.indexOf("\n\t]", openIndex);
  if (openIndex < 0 || closeIndex < 0) {
    throw new Error("template asset reference list is malformed");
  }

  let body = source.slice(openIndex + 1, closeIndex).trimEnd();
  const additions = SCRIPT_ASSETS.filter((asset) => !body.includes(`"${asset}"`));
  if (additions.length === 0) return source;
  if (body.trim().length > 0 && !body.trimEnd().endsWith(",")) body += ",";
  body += additions.map((asset) => `\n\t\t"${asset}"`).join(",");
  return `${source.slice(0, openIndex + 1)}${body}${source.slice(closeIndex)}`;
}

function insertWorldEntities(source) {
  const worldIndex = source.indexOf('"world" "CMapWorld"');
  if (worldIndex < 0) throw new Error("template map has no CMapWorld");
  const childrenIndex = source.indexOf('"children" "element_array"', worldIndex);
  const openIndex = source.indexOf("[", childrenIndex);
  if (childrenIndex < 0 || openIndex < 0) {
    throw new Error("template world children are malformed");
  }
  const insertionIndex = source.indexOf("\n", openIndex) + 1;
  if (insertionIndex <= 0) throw new Error("template world children have no body");
  const fixtureMeshes = createPhysicsFixtureMeshes(source);
  return `${source.slice(0, insertionIndex)}${fixtureMeshes}${POINT_SCRIPT_ENTITY}${BALL_ENTITY}${MARKER_ENTITIES}${source.slice(insertionIndex)}`;
}

export function injectPhase1Entities(templateText) {
  if (typeof templateText !== "string"
      || !templateText.startsWith("<!-- dmx encoding keyvalues2")
      || !templateText.includes("format vmap 40")) {
    throw new Error("input must be a keyvalues2 vmap 40 document");
  }
  if (templateText.includes('"targetname" "string" "sm_ball"')) {
    throw new Error("phase 1 entities are already present");
  }
  for (const identifier of RESERVED_IDENTIFIERS) {
    if (templateText.includes(`"${identifier}"`)) {
      throw new Error(`template collides with reserved identifier ${identifier}`);
    }
  }
  const normalized = normalizeDocumentElementId(
    templateText.replaceAll("\r\n", "\n"),
  );
  const quietTemplate = removeWorldEntityByClass(normalized, "env_cubemap_fog");
  return insertWorldEntities(insertAssetReferences(quietTemplate));
}

function runCli() {
  const [, , inputPath, outputPath] = process.argv;
  if (!inputPath || !outputPath) {
    throw new Error("usage: node generate-phase1-vmap.mjs <template-text.vmap> <output.vmap>");
  }
  const templateText = fs.readFileSync(inputPath, "utf8");
  const generated = injectPhase1Entities(templateText);
  fs.mkdirSync(path.dirname(path.resolve(outputPath)), { recursive: true });
  fs.writeFileSync(outputPath, generated, "utf8");
}

const invokedPath = process.argv[1] ? path.resolve(process.argv[1]) : "";
if (invokedPath === fileURLToPath(import.meta.url)) runCli();

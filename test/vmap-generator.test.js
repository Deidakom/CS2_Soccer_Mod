import assert from "node:assert/strict";
import test from "node:test";

import { injectPhase1Entities } from "../tools/generate-phase1-vmap.mjs";

const template = `<!-- dmx encoding keyvalues2 4 format vmap 40 -->
"$prefix_element$"
{
\t"id" "elementid" "11111111-2222-4333-8444-555555555555"
\t"map_asset_references" "string_array"
\t[
\t\t"materials/dev/example.vmat"
\t]
}
"CMapRootElement"
{
\t"world" "CMapWorld"
\t{
\t\t"children" "element_array"
\t\t[
\t\t\t"CMapMesh"
\t\t\t{
\t\t\t\t"id" "elementid" "b8e18729-69bc-4417-94eb-4690a2615c4a"
\t\t\t\t"nodeID" "int" "88"
\t\t\t\t"referenceID" "uint64" "0x5a7df08a06974b9c"
\t\t\t\t"meshData" "CDmePolygonMesh"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "2f1f8881-a6e4-426e-98a3-7b4122031de8"
\t\t\t\t\t"materials" "string_array"
\t\t\t\t\t[
\t\t\t\t\t\t"materials/models/props_junk/trashclusters01.vmat"
\t\t\t\t\t]
\t\t\t\t\t"vertexData" "CDmePolygonMeshDataArray"
\t\t\t\t\t{
\t\t\t\t\t\t"id" "elementid" "fcb7041b-0ab9-40b4-84c7-ce1240da715d"
\t\t\t\t\t\t"standardAttributeName" "string" "position"
\t\t\t\t\t\t"data" "vector3_array"
\t\t\t\t\t\t[
\t\t\t\t\t\t\t"-8 -8 8",
\t\t\t\t\t\t\t"8 -8 8",
\t\t\t\t\t\t\t"-8 8 8",
\t\t\t\t\t\t\t"8 8 -8",
\t\t\t\t\t\t\t"-8 8 -8",
\t\t\t\t\t\t\t"8 8 8",
\t\t\t\t\t\t\t"8 -8 -8",
\t\t\t\t\t\t\t"-8 -8 -8"
\t\t\t\t\t\t]
\t\t\t\t\t}
\t\t\t\t}
\t\t\t\t"origin" "vector3" "512 112 16"
\t\t\t\t"scales" "vector3" "1 1 1"
\t\t\t},
\t\t\t"CMapEntity"
\t\t\t{
\t\t\t\t"id" "elementid" "33333333-4444-4555-8666-777777777777"
\t\t\t\t"entity_properties" "EditGameClassProps"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "88888888-9999-4aaa-8bbb-cccccccccccc"
\t\t\t\t\t"classname" "string" "env_cubemap_fog"
\t\t\t\t}
\t\t\t},
\t\t\t"element" "00000000-0000-0000-0000-000000000000"
\t\t]
\t}
}
`;

test("phase 1 vmap generator inserts one script and one authoritative ball", () => {
  const output = injectPhase1Entities(template);
  assert.equal((output.match(/"classname" "string" "point_script"/g) ?? []).length, 1);
  assert.equal((output.match(/"classname" "string" "prop_physics_multiplayer"/g) ?? []).length, 1);
  assert.equal((output.match(/"targetname" "string" "sm_ball"/g) ?? []).length, 1);
  assert.equal((output.match(/"classname" "string" "info_target"/g) ?? []).length, 3);
  assert.equal((output.match(/"targetname" "string" "sm_ball_reset_marker"/g) ?? []).length, 1);
  assert.equal((output.match(/"targetname" "string" "sm_goal_west_marker"/g) ?? []).length, 1);
  assert.equal((output.match(/"targetname" "string" "sm_goal_east_marker"/g) ?? []).length, 1);
  assert.match(output, /maps\/scripts\/ball_lab\/adapter\.vjs/);
  assert.match(output, /dust_soccer_ball001\.vmdl/);
  assert.match(output, /"physicsmode" "string" "1"/);
  assert.match(output, /"origin" "vector3" "512 0 15"/);
  assert.equal((output.match(/"nodeID" "int" "10006"/g) ?? []).length, 1);
  assert.equal((output.match(/"nodeID" "int" "10007"/g) ?? []).length, 1);
  assert.match(output, /"origin" "vector3" "4096 0 -8"/);
  assert.match(output, /"origin" "vector3" "4096 1536 256"/);
  assert.match(output, /"-2048 -2048 8"/);
  assert.match(output, /"-1024 -8 256"/);
  assert.equal((output.match(/materials\/dev\/reflectivity_30\.vmat/g) ?? []).length, 2);
  assert.doesNotMatch(output, /0e14bdb71001[\s\S]*0e14bdb71001/);
  assert.doesNotMatch(output, /maps\/scripts\/ball_lab\/layout\.vjs/);
  assert.doesNotMatch(output, /maps\/scripts\/ball_lab\/core\//);
  assert.doesNotMatch(output, /env_cubemap_fog/);
});

test("phase 1 vmap generator rejects malformed and repeated input", () => {
  assert.throws(() => injectPhase1Entities("not a vmap"), /keyvalues2/);
  assert.throws(
    () => injectPhase1Entities(injectPhase1Entities(template)),
    /already present/,
  );
  assert.throws(
    () => injectPhase1Entities(template.replace(
      '"element" "00000000-0000-0000-0000-000000000000"',
      '"element" "5c325137-f715-49e0-99e8-0e14bdb6ce01"',
    )),
    /reserved identifier/,
  );
});

test("phase 1 vmap generator normalizes DMXConvert's document id", () => {
  const alternate = template.replace(
    "11111111-2222-4333-8444-555555555555",
    "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee",
  );
  assert.equal(injectPhase1Entities(template), injectPhase1Entities(alternate));
});

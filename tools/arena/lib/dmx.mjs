// Static model writers: keyvalues2 DMX (position, texcoord, normal, tangent per face corner)
// and the ModelDoc .vmdl that compiles a render mesh plus a mesh collision shape.
import crypto from "node:crypto";

const id = () => crypto.randomUUID();
const f4 = (x) => { const v = Number(x.toFixed(4)); return Object.is(v, -0) ? "0" : v.toString(); };

// faces: [{ material, pts, uvs, n }]; one face set per material
export function staticDmx(name, faces) {
  const pos = [], uv = [], nrm = [], tan = [], sets = new Map();
  for (const f of faces) {
    const base = pos.length, t = tangent(f);
    f.pts.forEach((p, k) => { pos.push(p); uv.push(f.uvs[k]); nrm.push(f.n); tan.push(t); });
    if (!sets.has(f.material)) sets.set(f.material, []);
    sets.get(f.material).push(f.pts.map((_, k) => base + k));
  }
  const out = [];
  const I = { model: id(), dag: id(), bind: id() };
  const xf = () => `"DmeTransform"\n\t{\n\t\t"id" "elementid" "${id()}"\n\t\t"position" "vector3" "0 0 0"\n\t\t"orientation" "quaternion" "0 0 0 1"\n\t}`;
  out.push(`<!-- dmx encoding keyvalues2 4 format model 22 -->
"DmElement"
{
\t"id" "elementid" "${id()}"
\t"name" "string" "root"
\t"skeleton" "element" "${I.model}"
\t"model" "element" "${I.model}"
}

"DmeModel"
{
\t"id" "elementid" "${I.model}"
\t"name" "string" "${name}"
\t"transform" ${xf()}
\t"shape" "element" ""
\t"visible" "bool" "1"
\t"children" "element_array"
\t[
\t\t"element" "${I.dag}"
\t]
\t"jointList" "element_array"
\t[
\t\t"element" "${I.dag}"
\t]
\t"axisSystem" "DmeAxisSystem"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"upAxis" "int" "3"
\t\t"forwardParity" "int" "1"
\t\t"coordSys" "int" "0"
\t}
}

"DmeDag"
{
\t"id" "elementid" "${I.dag}"
\t"name" "string" "${name}"
\t"transform" ${xf()}
\t"shape" "DmeMesh"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "${name}"
\t\t"bindState" "element" ""
\t\t"currentState" "element" "${I.bind}"
\t\t"baseStates" "element_array"
\t\t[
\t\t\t"element" "${I.bind}"
\t\t]
\t\t"deltaStates" "element_array"
\t\t[
\t\t]
\t\t"faceSets" "element_array"
\t\t[
`);
  const setTexts = [...sets].map(([material, list]) => {
    const idx = []; for (const f of list) { for (const v of f) idx.push(v); idx.push(-1); }
    return `\t\t\t"DmeFaceSet"
\t\t\t{
\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t"name" "string" "${material.split("/").pop().replace(".vmat", "")}"
\t\t\t\t"faces" "int_array"
\t\t\t\t[
${joinList(idx, "\t\t\t\t\t")}
\t\t\t\t]
\t\t\t\t"material" "DmeMaterial"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t\t"name" "string" "material"
\t\t\t\t\t"mtlName" "string" "${material}"
\t\t\t\t}
\t\t\t}`;
  });
  out.push(setTexts.join(",\n"));
  const seq = joinList(pos.map((_, i) => i), "\t\t");
  const stream = (key, type, values) => `\t"${key}" "${type}"\n\t[\n${joinList(values, "\t\t")}\n\t]\n\t"${key}Indices" "int_array"\n\t[\n${seq}\n\t]\n`;
  out.push(`
\t\t]
\t\t"visible" "bool" "1"
\t}
\t"visible" "bool" "1"
\t"children" "element_array"
\t[
\t]
}

"DmeVertexData"
{
\t"id" "elementid" "${I.bind}"
\t"name" "string" "bind"
\t"vertexFormat" "string_array"
\t[
\t\t"position$0",
\t\t"texcoord$0",
\t\t"normal$0",
\t\t"tangent$0"
\t]
\t"jointCount" "int" "0"
\t"flipVCoordinates" "bool" "0"
${stream("position$0", "vector3_array", pos.map((p) => p.map(f4).join(" ")))}${stream("texcoord$0", "vector2_array", uv.map((p) => p.map(f4).join(" ")))}${stream("normal$0", "vector3_array", nrm.map((p) => p.map(f4).join(" ")))}${stream("tangent$0", "vector4_array", tan.map((p) => p.map(f4).join(" ")))}}
`);
  return out.join("");
}
function joinList(values, indent) {
  const parts = new Array(values.length);
  for (let i = 0; i < values.length; i++) parts[i] = `${indent}"${values[i]}"`;
  return parts.join(",\n");
}
// tangent = direction of growing u on the face, w = handedness
function tangent(f) {
  const [p0, p1, p2] = f.pts, [t0, t1, t2] = f.uvs;
  const e1 = [p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]], e2 = [p2[0] - p0[0], p2[1] - p0[1], p2[2] - p0[2]];
  const du1 = t1[0] - t0[0], dv1 = t1[1] - t0[1], du2 = t2[0] - t0[0], dv2 = t2[1] - t0[1];
  const det = du1 * dv2 - du2 * dv1;
  if (Math.abs(det) < 1e-12) return [1, 0, 0, 1];
  let t = [0, 1, 2].map((k) => (e1[k] * dv2 - e2[k] * dv1) / det), b = [0, 1, 2].map((k) => (e2[k] * du1 - e1[k] * du2) / det);
  const n = f.n, d = t[0] * n[0] + t[1] * n[1] + t[2] * n[2];
  t = t.map((v, k) => v - n[k] * d);
  const l = Math.hypot(...t) || 1; t = t.map((v) => v / l);
  const c = [n[1] * t[2] - n[2] * t[1], n[2] * t[0] - n[0] * t[2], n[0] * t[1] - n[1] * t[0]];
  const w = c[0] * b[0] + c[1] * b[1] + c[2] * b[2] < 0 ? -1 : 1;
  return [...t, w];
}

export const staticVmdl = (dir, name, hasPhysics, surface = "concrete") => `<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc28:version{fb63b6ca-f435-4aa0-a2c7-c66ddc651dca} -->
{
\trootNode =
\t{
\t\t_class = "RootNode"
\t\tchildren =
\t\t[
\t\t\t{
\t\t\t\t_class = "RenderMeshList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "RenderMeshFile"
\t\t\t\t\t\tname = "${name}"
\t\t\t\t\t\tfilename = "${dir}/${name}.dmx"
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},${hasPhysics ? `
\t\t\t{
\t\t\t\t_class = "PhysicsShapeList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "PhysicsMeshFile"
\t\t\t\t\t\tname = "${name}_collision"
\t\t\t\t\t\tfilename = "${dir}/${name}_phys.dmx"
\t\t\t\t\t\tsurface_prop = "${surface}"
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},` : ""}
\t\t]
\t}
}
`;

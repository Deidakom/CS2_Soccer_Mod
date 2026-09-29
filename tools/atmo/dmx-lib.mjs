// Shared keyvalues2 DMX writers for skinned, bone-animated models (same
// layout as tools/net/generate-dynamic-net.mjs, which is proven in game):
// a flat skeleton (root + children at absolute positions), one mesh with up to
// two bone weights per vertex, and position-only animation clips.
import crypto from "node:crypto";

const id = () => crypto.randomUUID();
export const f4 = (n) => Number(n.toFixed(4)).toString();
const list = (vals, ind) => vals.map((v) => `${ind}"${v}"`).join(",\n");
const xf = (xid, pos) => `"DmeTransform"\n{\n\t"id" "elementid" "${xid}"\n\t"name" "string" "transform"\n\t"position" "vector3" "${pos.map(f4).join(" ")}"\n\t"orientation" "quaternion" "0 0 0 1"\n}`;
const indent = (s, t) => s.split("\n").map((l) => t + l).join("\n");

function skeleton(bones, withDag) {
  const ids = bones.map(() => ({ joint: id(), xf: id(), base: id() }));
  const dag = withDag ? { id: id(), xf: id(), base: id() } : null;
  const jointElems = bones.map((bn, b) => `"DmeJoint"\n{\n\t"id" "elementid" "${ids[b].joint}"\n\t"name" "string" "${bn.name}"\n${indent(`"transform" ${xf(ids[b].xf, bn.pos)}`, "\t")}\n\t"shape" "element" ""\n\t"visible" "bool" "1"\n\t"children" "element_array"\n\t[\n${b === 0 ? bones.slice(1).map((_, k) => `\t\t"element" "${ids[k + 1].joint}"`).join(",\n") : ""}\n\t]\n}`);
  const jointList = [...ids.map((x) => x.joint), ...(dag ? [dag.id] : [])];
  const baseXfs = [...bones.map((bn, b) => xf(ids[b].base, bn.pos)), ...(dag ? [xf(dag.base, [0, 0, 0])] : [])];
  const model = (modelId, name) => `"DmeModel"\n{\n\t"id" "elementid" "${modelId}"\n\t"name" "string" "${name}"\n${indent(`"transform" ${xf(id(), [0, 0, 0])}`, "\t")}\n\t"shape" "element" ""\n\t"visible" "bool" "1"\n\t"children" "element_array"\n\t[\n\t\t"element" "${ids[0].joint}"${dag ? `,\n\t\t"element" "${dag.id}"` : ""}\n\t]\n\t"jointList" "element_array"\n\t[\n${jointList.map((j) => `\t\t"element" "${j}"`).join(",\n")}\n\t]\n\t"baseStates" "element_array"\n\t[\n\t\t"DmeTransformsList"\n\t\t{\n\t\t\t"id" "elementid" "${id()}"\n\t\t\t"name" "string" "bind"\n\t\t\t"transforms" "element_array"\n\t\t\t[\n${baseXfs.map((x) => indent(x, "\t\t\t\t")).join(",\n")}\n\t\t\t]\n\t\t}\n\t]\n\t"axisSystem" "DmeAxisSystem"\n\t{\n\t\t"id" "elementid" "${id()}"\n\t\t"upAxis" "int" "3"\n\t\t"forwardParity" "int" "1"\n\t\t"coordSys" "int" "0"\n\t}\n}`;
  return { ids, dag, jointElems, model };
}

const rootElement = (source, pairs) => `<!-- dmx encoding keyvalues2 4 format model 22 -->
"DmElement"
{
\t"id" "elementid" "${id()}"
\t"name" "string" "root"
${pairs.map(([k, v]) => `\t"${k}" "element" "${v}"`).join("\n")}
\t"exportTags" "DmeExportTags"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "exportTags"
\t\t"source" "string" "${source}"
\t}
}
`;

// mesh = { name, material, positions [[x,y,z]], normals, uvs [[u,v]], weights [[w0,w1]], indices [[b0,b1]], faces [[i,...]] }
export function meshDmx(source, bones, mesh) {
  const sk = skeleton(bones, true), modelId = id(), vd = id(), seq = mesh.positions.map((_, i) => i);
  return `${rootElement(source, [["skeleton", modelId], ["model", modelId]])}
${sk.model(modelId, mesh.name)}

${sk.jointElems.join("\n\n")}

"DmeDag"
{
\t"id" "elementid" "${sk.dag.id}"
\t"name" "string" "${mesh.name}"
${indent(`"transform" ${xf(sk.dag.xf, [0, 0, 0])}`, "\t")}
\t"shape" "DmeMesh"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "${mesh.name}"
\t\t"bindState" "element" "${vd}"
\t\t"currentState" "element" "${vd}"
\t\t"baseStates" "element_array"
\t\t[
\t\t\t"element" "${vd}"
\t\t]
\t\t"deltaStates" "element_array"
\t\t[
\t\t]
\t\t"faceSets" "element_array"
\t\t[
\t\t\t"DmeFaceSet"
\t\t\t{
\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t"name" "string" "faces"
\t\t\t\t"faces" "int_array"
\t\t\t\t[
${list(mesh.faces.flatMap((f) => [...f, -1]), "\t\t\t\t\t")}
\t\t\t\t]
\t\t\t\t"material" "DmeMaterial"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t\t"name" "string" "material"
\t\t\t\t\t"mtlName" "string" "${mesh.material}"
\t\t\t\t}
\t\t\t}
\t\t]
\t\t"deltaStateWeights" "vector2_array"
\t\t[
\t\t]
\t\t"deltaStateWeightsLagged" "vector2_array"
\t\t[
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
\t"id" "elementid" "${vd}"
\t"name" "string" "bind"
\t"vertexFormat" "string_array"
\t[
\t\t"position$0",
\t\t"texcoord$0",
\t\t"normal$0",
\t\t"tangent$0",
\t\t"blendweights$0",
\t\t"blendindices$0"
\t]
\t"jointCount" "int" "2"
\t"flipVCoordinates" "bool" "0"
\t"position$0" "vector3_array"
\t[
${list(mesh.positions.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"position$0Indices" "int_array"
\t[
${list(seq, "\t\t")}
\t]
\t"texcoord$0" "vector2_array"
\t[
${list(mesh.uvs.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"texcoord$0Indices" "int_array"
\t[
${list(seq, "\t\t")}
\t]
\t"normal$0" "vector3_array"
\t[
${list(mesh.normals.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"normal$0Indices" "int_array"
\t[
${list(seq, "\t\t")}
\t]
\t"tangent$0" "vector4_array"
\t[
${list(mesh.positions.map(() => "1 0 0 1"), "\t\t")}
\t]
\t"tangent$0Indices" "int_array"
\t[
${list(seq, "\t\t")}
\t]
\t"blendweights$0" "float_array"
\t[
${list(mesh.weights.flatMap((w) => w.map(f4)), "\t\t")}
\t]
\t"blendindices$0" "int_array"
\t[
${list(mesh.indices.flat(), "\t\t")}
\t]
}
`;
}

// frames: array of Map(boneIndex -> [dx,dy,dz]) offsets from the bone's rest position.
export function animDmx(source, bones, meshName, clipName, frames, fps) {
  const sk = skeleton(bones, false), modelId = id(), listId = id(), clipId = id();
  const times = frames.map((_, f) => f4(f / fps));
  const channels = bones.map((bn, b) => {
    const moves = b > 0 && frames.some((m) => m.has(b));
    const pos = moves ? frames.map((m) => { const d = m.get(b) || [0, 0, 0]; return bn.pos.map((v, i) => f4(v + d[i])).join(" "); }) : "const";
    const xid = sk.ids[b].xf;
    const chan = (suffix, attr, logType, valType, values, def) => `\t\t"DmeChannel"
\t\t{
\t\t\t"id" "elementid" "${id()}"
\t\t\t"name" "string" "${bn.name}_${suffix}"
\t\t\t"fromElement" "element" "${xid}"
\t\t\t"fromAttribute" "string" "${attr}"
\t\t\t"fromIndex" "int" "0"
\t\t\t"toElement" "element" "${xid}"
\t\t\t"toAttribute" "string" "${attr}"
\t\t\t"toIndex" "int" "0"
\t\t\t"mode" "int" "3"
\t\t\t"log" "${logType}"
\t\t\t{
\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t"name" "string" "${valType} log"
\t\t\t\t"layers" "element_array"
\t\t\t\t[
\t\t\t\t\t"${logType}Layer"
\t\t\t\t\t{
\t\t\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t\t\t"name" "string" "${valType} log"
\t\t\t\t\t\t"times" "time_array"
\t\t\t\t\t\t[
${list(values === "const" ? [times[0]] : times, "\t\t\t\t\t\t\t")}
\t\t\t\t\t\t]
\t\t\t\t\t\t"curvetypes" "int_array"
\t\t\t\t\t\t[
\t\t\t\t\t\t]
\t\t\t\t\t\t"values" "${valType}_array"
\t\t\t\t\t\t[
${list(values === "const" ? [def] : values, "\t\t\t\t\t\t\t")}
\t\t\t\t\t\t]
\t\t\t\t\t}
\t\t\t\t]
\t\t\t\t"curveinfo" "element" ""
\t\t\t\t"usedefaultvalue" "bool" "0"
\t\t\t\t"defaultvalue" "${valType}" "${def}"
\t\t\t}
\t\t}`;
    return [chan("p", "position", "DmeVector3Log", "vector3", pos, bn.pos.map(f4).join(" ")), chan("o", "orientation", "DmeQuaternionLog", "quaternion", "const", "0 0 0 1")].join(",\n");
  });
  return `${rootElement(source, [["skeleton", modelId], ["animationList", listId]])}
${sk.model(modelId, meshName)}

${sk.jointElems.join("\n\n")}

"DmeAnimationList"
{
\t"id" "elementid" "${listId}"
\t"name" "string" "animationList"
\t"animations" "element_array"
\t[
\t\t"element" "${clipId}"
\t]
}

"DmeChannelsClip"
{
\t"id" "elementid" "${clipId}"
\t"name" "string" "${clipName}"
\t"timeFrame" "DmeTimeFrame"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "timeFrame"
\t\t"start" "time" "0"
\t\t"duration" "time" "${f4((frames.length - 1) / fps)}"
\t\t"offset" "time" "0"
\t\t"scale" "float" "1"
\t}
\t"color" "color" "0 0 0 0"
\t"text" "string" ""
\t"mute" "bool" "0"
\t"trackGroups" "element_array"
\t[
\t]
\t"displayScale" "float" "1"
\t"channels" "element_array"
\t[
${channels.join(",\n")}
\t]
\t"frameRate" "int" "${fps}"
}
`;
}

// vmdl: one render mesh, material groups [{name, remaps:[[from,to]]}], looping flags per clip.
export function vmdlText(model, meshName, groups, clips) {
  const clipNode = (c) => `\t\t\t\t\t{
\t\t\t\t\t\t_class = "AnimFile"
\t\t\t\t\t\tname = "${c.name}"
\t\t\t\t\t\tsource_filename = "${model}_anims/${c.name}.dmx"
\t\t\t\t\t\tfade_in_time = ${c.looping ? "0.2" : "0.1"}
\t\t\t\t\t\tfade_out_time = ${c.looping ? "0.2" : "0.2"}
\t\t\t\t\t\tlooping = ${c.looping}
\t\t\t\t\t\tdelta = false
\t\t\t\t\t\tworldSpace = false
\t\t\t\t\t\thidden = false
\t\t\t\t\t},`;
  const groupNode = (g) => `\t\t\t\t\t{
\t\t\t\t\t\t_class = "MaterialGroup"
\t\t\t\t\t\tname = "${g.name}"
\t\t\t\t\t\tremaps =
\t\t\t\t\t\t[
${g.remaps.map(([from, to]) => `\t\t\t\t\t\t\t{\n\t\t\t\t\t\t\t\tfrom = "${from}"\n\t\t\t\t\t\t\t\tto = "${to}"\n\t\t\t\t\t\t\t},`).join("\n")}
\t\t\t\t\t\t]
\t\t\t\t\t},`;
  return `<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc28:version{fb63b6ca-f435-4aa0-a2c7-c66ddc651dca} -->
{
\trootNode =
\t{
\t\t_class = "RootNode"
\t\tchildren =
\t\t[
\t\t\t{
\t\t\t\t_class = "MaterialGroupList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "DefaultMaterialGroup"
\t\t\t\t\t\tremaps = [  ]
\t\t\t\t\t\tuse_global_default = false
\t\t\t\t\t\tglobal_default_material = ""
\t\t\t\t\t},
${groups.map(groupNode).join("\n")}
\t\t\t\t]
\t\t\t},
\t\t\t{
\t\t\t\t_class = "RenderMeshList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "RenderMeshFile"
\t\t\t\t\t\tname = "${meshName}"
\t\t\t\t\t\tfilename = "${model}.dmx"
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},
\t\t\t{
\t\t\t\t_class = "AnimationList"
\t\t\t\tchildren =
\t\t\t\t[
${clips.map(clipNode).join("\n")}
\t\t\t\t]
\t\t\t},
\t\t]
\t}
}
`;
}

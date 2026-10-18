// Minimal reader for binary DMX (encoding binary 9, Source 2 model exports): returns
// elements [{type, name, attrs:{name: value}}] with element references as indices.
// Used by generate-ball-skins.mjs to read the map ball's render mesh (positions, UVs).
import fs from "node:fs";

export function readBinaryDmx(file) {
  const b = fs.readFileSync(file);
  let p = b.indexOf(0) + 1;
  const i32 = () => { const v = b.readInt32LE(p); p += 4; return v; };
  const f32 = () => { const v = b.readFloatLE(p); p += 4; return v; };
  const cstr = () => { const e = b.indexOf(0, p); const s = b.toString("latin1", p, e); p = e + 1; return s; };
  const prefix = i32();
  if (prefix !== 0) throw new Error(`prefix attributes not supported (${prefix})`);
  const strings = Array.from({ length: i32() }, cstr);
  const count = i32();
  const elements = [];
  for (let n = 0; n < count; n++) { const type = strings[i32()], name = strings[i32()]; p += 16; elements.push({ type, name, attrs: {} }); }
  const scalar = (t) => {
    switch (t) {
      case 1: return i32();                                   // element index (-1 = null)
      case 2: return i32();
      case 3: return f32();
      case 4: return b[p++] !== 0;
      case 5: return strings[i32()];
      case 6: { const len = i32(); const v = b.subarray(p, p + len); p += len; return v; }
      case 7: return i32() / 10000;
      case 8: { const v = [b[p], b[p + 1], b[p + 2], b[p + 3]]; p += 4; return v; }
      case 9: return [f32(), f32()];
      case 10: return [f32(), f32(), f32()];
      case 11: return [f32(), f32(), f32(), f32()];
      case 12: return [f32(), f32(), f32()];
      case 13: return [f32(), f32(), f32(), f32()];
      case 14: return Array.from({ length: 16 }, f32);
      case 15: { const v = b.readBigUInt64LE(p); p += 8; return v; }
      case 16: return b[p++];
      default: throw new Error(`attribute type ${t} at ${p}`);
    }
  };
  for (const el of elements) {
    const n = i32();
    for (let k = 0; k < n; k++) {
      const name = strings[i32()], t = b[p++];
      if (t >= 33) {
        const len = i32(), st = t - 32;
        // string arrays hold inline strings
        el.attrs[name] = Array.from({ length: len }, () => (st === 5 ? cstr() : scalar(st)));
      } else el.attrs[name] = scalar(t);
    }
  }
  return elements;
}

// The first DmeVertexData "bind": positions, uvs and the triangle corners (indices into
// the per-corner index arrays) from every DmeFaceSet.
export function readMesh(file) {
  const els = readBinaryDmx(file);
  const vd = els.find((e) => e.type === "DmeVertexData");
  const a = vd.attrs;
  const faces = [];
  for (const fs_ of els.filter((e) => e.type === "DmeFaceSet")) {
    let poly = [];
    for (const v of fs_.attrs.faces) { if (v === -1) { faces.push(poly); poly = []; } else poly.push(v); }
  }
  return {
    positions: a["position$0"], positionIndices: a["position$0Indices"],
    uvs: a["texcoord$0"], uvIndices: a["texcoord$0Indices"], faces,
    flipV: a.flipVCoordinates,
  };
}

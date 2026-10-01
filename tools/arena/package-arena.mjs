#!/usr/bin/env node
// SoccerMod Arena: builds the Workshop item of the map as one single-file VPK - the compiled map,
// every compiled resource it uses from our addon, the radar, and the models the plugin spawns
// on this map (crowd, LED ring segment, pitch designs).
// What goes in is found by following the references inside the compiled files, starting at the
// map; anything that is not in the addon folder comes from the game itself and stays out.
//
//   node tools/arena/package-arena.mjs --addon <game\csgo_addons\cs2sm_stadium_v1> --out <item.vpk> [--list <file>]
//        [--map <map name> --models <folder,folder>]   another map of the addon, e.g. the indoor hall
import fs from "node:fs";
import path from "node:path";

const args = Object.fromEntries(process.argv.slice(2).reduce((p, a, i, all) => { if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
if (!args.addon || !args.out) throw new Error("usage: --addon <game addon dir> --out <item.vpk> [--list <file>]");
const MAP = args.map ?? "ka_soccermod_stadium";   // the stadium: keep in sync with MAP_NAME in layout.mjs
const exists = (rel) => fs.existsSync(path.join(args.addon, rel));
// start: the map, its radar and overview ...
const seeds = [`maps/${MAP}.vpk`, `panorama/images/overheadmaps/${MAP}_radar_psd.vtex_c`, `resource/overviews/${MAP}.txt`];
// ... every compiled model in the folders named with --models (comma separated, e.g. the indoor hall: models/soccermod_hall) ...
for (const dir of (args.models ?? "").split(",").filter(Boolean)) for (const f of fs.readdirSync(path.join(args.addon, dir))) if (f.endsWith(".vmdl_c")) seeds.push(`${dir}/${f}`);
// ... and for the stadium what the plugin spawns there
if (MAP === "ka_soccermod_stadium") {
  seeds.push("models/soccermod_arena/light_ring.vmdl_c",
    ...["stripes", "lengthwise", "diamond", "circles"].map((d) => `models/soccermod_arena/pitch_design_${d}.vmdl_c`),
    ...["end_red", "end_blue", "side_east", "side_west"].flatMap((s) => ["lower", "upper"].map((t) => `models/soccermod/atmo/crowd_arena/${s}_${t}.vmdl_c`)),
    // bench and coach in the fans look (generate-arena-bench.mjs)
    ...["bench_red", "bench_blue", "coach_red", "coach_blue"].map((n) => `models/soccermod/atmo/crowd_arena/${n}.vmdl_c`));
  // 2026-10-01: the lit 3D grass tiles this map uses (Grass.cs, "fine" set), rebuilt with every line
  // blade facing up (tools/grass/generate-shell-grass.mjs). Copied into the addon models/soccermod
  // from soccermod_menu; they replace the Feature Package copies when the item is merged.
  for (let ty = 0; ty < 20; ty++) for (let tx = 0; tx < 16; tx++) {
    const tileModel = `models/soccermod/grass_fine_${tx}_${ty}.vmdl_c`;
    if (exists(tileModel)) seeds.push(tileModel);
  }
}
const REF = /[a-z0-9_\-./]+\.(?:vmat|vmdl|vtex|vmesh|vphys|vanim|vseq|vagrp|vmorf|vpcf|vsnd|vpost|vrman|vsvg|vxml|vcss|vjs)/g;
const files = new Set(), missing = new Set(), queue = [...seeds];
while (queue.length) {
  const rel = queue.pop().replace(/\\/g, "/").toLowerCase();
  if (files.has(rel)) continue;
  if (!exists(rel)) { missing.add(rel); continue; }
  files.add(rel);
  if (rel.endsWith(".txt")) continue;
  const text = fs.readFileSync(path.join(args.addon, rel)).toString("latin1").toLowerCase();
  for (const m of text.match(REF) ?? []) {
    const ref = m.replace(/^[^a-z]+/, "") + "_c";
    if (!files.has(ref) && exists(ref)) queue.push(ref);
  }
}
for (const s of seeds) if (missing.has(s.toLowerCase())) throw new Error(`missing in the addon: ${s}`);
const list = [...files].sort();
if (args.list) fs.writeFileSync(args.list, list.join("\n") + "\n");

// ---- single-file VPK v2 (all data after the tree, archive index 0x7fff) ----------------------------
const crcTable = new Int32Array(256).map((_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c; });
const crc32 = (buf) => { let c = -1; for (let i = 0; i < buf.length; i++) c = crcTable[(c ^ buf[i]) & 255] ^ (c >>> 8); return (c ^ -1) >>> 0; };
const tree = new Map();
for (const rel of list) {
  const ext = rel.slice(rel.lastIndexOf(".") + 1), base = rel.slice(0, rel.lastIndexOf("."));
  const dir = base.includes("/") ? base.slice(0, base.lastIndexOf("/")) : " ", name = base.slice(base.lastIndexOf("/") + 1);
  if (!tree.has(ext)) tree.set(ext, new Map());
  if (!tree.get(ext).has(dir)) tree.get(ext).set(dir, []);
  tree.get(ext).get(dir).push({ rel, name });
}
const cstr = (s) => Buffer.concat([Buffer.from(s, "latin1"), Buffer.from([0])]);
const parts = [], datas = [];
let offset = 0;
for (const [ext, dirs] of [...tree].sort()) {
  parts.push(cstr(ext));
  for (const [dir, entries] of [...dirs].sort()) {
    parts.push(cstr(dir));
    for (const e of entries.sort((a, b) => a.name.localeCompare(b.name))) {
      const data = fs.readFileSync(path.join(args.addon, e.rel)), entry = Buffer.alloc(18);
      entry.writeUInt32LE(crc32(data), 0); entry.writeUInt16LE(0, 4); entry.writeUInt16LE(0x7fff, 6);
      entry.writeUInt32LE(offset, 8); entry.writeUInt32LE(data.length, 12); entry.writeUInt16LE(0xffff, 16);
      parts.push(cstr(e.name), entry); datas.push(data); offset += data.length;
    }
    parts.push(cstr(""));
  }
  parts.push(cstr(""));
}
parts.push(cstr(""));
const treeBuf = Buffer.concat(parts), header = Buffer.alloc(28);
header.writeUInt32LE(0x55aa1234, 0); header.writeUInt32LE(2, 4); header.writeUInt32LE(treeBuf.length, 8); header.writeUInt32LE(offset, 12);
fs.mkdirSync(path.dirname(args.out), { recursive: true });
fs.writeFileSync(args.out, Buffer.concat([header, treeBuf, ...datas]));
const byDir = {};
for (const rel of list) { const top = rel.split("/").slice(0, 2).join("/"); byDir[top] = (byDir[top] ?? 0) + 1; }
console.log(`${args.out}: ${list.length} files, ${(fs.statSync(args.out).size / 1048576).toFixed(1)} MB`);
console.log(byDir);

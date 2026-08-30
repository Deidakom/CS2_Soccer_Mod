#!/usr/bin/env node

import { readFileSync } from "node:fs";

function usage() {
  console.error("usage: node tools/summarize-entity-lump.mjs <decompiled.vents> [regex]");
  process.exitCode = 2;
}

const [, , inputPath, pattern = ""] = process.argv;
if (!inputPath) {
  usage();
} else {
  const text = readFileSync(inputPath, "utf8");
  const marker = /^====(\d+)====\r?$/gm;
  const markers = [...text.matchAll(marker)];
  const matcher = pattern ? new RegExp(pattern, "i") : null;
  const entities = [];

  for (let offset = 0; offset < markers.length; offset += 1) {
    const current = markers[offset];
    const start = current.index + current[0].length;
    const end = markers[offset + 1]?.index ?? text.length;
    const raw = text.slice(start, end).trim();
    if (matcher && !matcher.test(raw)) continue;

    const values = new Map();
    const outputs = [];
    for (const line of raw.split(/\r?\n/)) {
      const trimmed = line.trim();
      if (!trimmed) continue;
      const separator = trimmed.search(/\s/);
      const key = separator < 0 ? trimmed : trimmed.slice(0, separator);
      const value = separator < 0 ? "" : trimmed.slice(separator).trim();
      if (key.startsWith("@")) {
        outputs.push(`${key} ${value}`.trim());
        continue;
      }
      const list = values.get(key) ?? [];
      list.push(value.replace(/^"|"$/g, ""));
      values.set(key, list);
    }

    const first = (key) => values.get(key)?.[0];
    entities.push({
      index: Number(current[1]),
      classname: first("classname"),
      targetname: first("targetname"),
      origin: first("origin"),
      model: first("model"),
      parentname: first("parentname"),
      filtername: first("filtername"),
      sourceentityname: first("sourceentityname"),
      outputs,
      values: Object.fromEntries(values),
    });
  }

  console.log(JSON.stringify(entities, null, 2));
}

// Materials of the Blender props (materials/soccermod_world/<name>.vmat), merged from every
// tools/blender/materials/<family>.mjs (default export { name: { make, rough, metal?, normal?, illum?, ... } },
// same fields as the arena's MATERIALS in tools/arena/generate-arena-textures.mjs). A prop names
// them by its Blender material name; build-props.mjs refuses a name that is not listed. One file
// per prop family, so families can be worked on side by side.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const dir = path.join(path.dirname(fileURLToPath(import.meta.url)), "materials");
export const MATERIALS = {};
for (const file of fs.readdirSync(dir).filter((f) => f.endsWith(".mjs")).sort()) {
  const { default: family } = await import(pathToFileURL(path.join(dir, file)));
  for (const [name, m] of Object.entries(family)) {
    if (MATERIALS[name]) throw new Error(`material ${name} is defined twice (${file})`);
    MATERIALS[name] = m;
  }
}

const fs = require("node:fs/promises");
const path = require("node:path");
const sharp = require("sharp");

const repoRoot = path.resolve(__dirname, "..");
const addonRoot = path.join(repoRoot, "src", "workshop-addon", "soccermod_stadium_radar");

const renders = [
  {
    source: path.join(addonRoot, "panorama", "images", "overheadmaps", "soccer_cssl_stadium_v8_radar.svg"),
    destination: path.join(addonRoot, "panorama", "images", "overheadmaps", "soccer_cssl_stadium_v8_radar.png"),
    width: 1024,
    height: 1024,
  },
  {
    source: path.join(addonRoot, "panorama", "images", "map_icons", "screenshots", "1080p", "soccer_cssl_stadium_v8.svg"),
    destination: path.join(addonRoot, "panorama", "images", "map_icons", "screenshots", "1080p", "soccer_cssl_stadium_v8.png"),
    width: 1920,
    height: 1080,
  },
];

async function main() {
  for (const render of renders) {
    const svg = await fs.readFile(render.source);
    await sharp(svg, { density: 144 })
      .resize(render.width, render.height, { fit: "fill" })
      .png({ compressionLevel: 9, adaptiveFiltering: true })
      .toFile(render.destination);
    const info = await sharp(render.destination).metadata();
    if (info.width !== render.width || info.height !== render.height) {
      throw new Error(`Unexpected render size for ${render.destination}: ${info.width}x${info.height}`);
    }
    process.stdout.write(`rendered ${path.relative(repoRoot, render.destination)} (${info.width}x${info.height})\n`);
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});

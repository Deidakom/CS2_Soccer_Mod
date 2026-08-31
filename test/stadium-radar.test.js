import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const pluginPath = new URL("../src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.cs", import.meta.url);
const addonRoot = new URL("../src/workshop-addon/soccermod_stadium_radar/", import.meta.url);

test("stadium radar uses measured arena bounds and the exact CS2 resource names", async () => {
  const [plugin, overview, texture, loadingTexture] = await Promise.all([
    readFile(pluginPath, "utf8"),
    readFile(new URL("resource/overviews/soccer_cssl_stadium_v8.txt", addonRoot), "utf8"),
    readFile(new URL("panorama/images/overheadmaps/soccer_cssl_stadium_v8_radar_psd.vtex", addonRoot), "utf8"),
    readFile(new URL("panorama/images/map_icons/screenshots/1080p/soccer_cssl_stadium_v8_png.vtex", addonRoot), "utf8"),
  ]);

  assert.match(plugin, /FoundationWallPlaneX = 1279\.97f/);
  assert.match(plugin, /FoundationWallPlaneY = 1663\.97f/);
  assert.match(overview, /"pos_x"\s+"-1715"/);
  assert.match(overview, /"pos_y"\s+"1715"/);
  assert.match(overview, /"scale"\s+"3\.349609375"/);
  assert.match(texture, /soccer_cssl_stadium_v8_radar\.png/);
  assert.match(loadingTexture, /soccer_cssl_stadium_v8\.png/);
});

test("plugin precaches both compiled client-facing stadium textures", async () => {
  const plugin = await readFile(pluginPath, "utf8");

  assert.match(plugin, /manifest\.AddResource\(StadiumRadarTextureResource\)/);
  assert.match(plugin, /manifest\.AddResource\(StadiumLoadingScreenResource\)/);
  assert.match(plugin, /panorama\/images\/overheadmaps\/soccer_cssl_stadium_v8_radar_psd\.vtex/);
  assert.match(plugin, /panorama\/images\/map_icons\/screenshots\/1080p\/soccer_cssl_stadium_v8_png\.vtex/);
  assert.doesNotMatch(plugin, /manifest\.AddResource\([^)]*Overview/);
});

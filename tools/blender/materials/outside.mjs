// Outside the stadium: car park and plaza dressing (tools/arena/world.mjs).
import { Img, makeNoise, shade, flat } from "../matlib.mjs";

// fine-grained asphalt with a few darker patches and tyre wear
const asphalt = () => {
  const n = makeNoise(301), img = new Img(512, 512);
  img.fill((x, y, u, v) => shade([66, 67, 70], 0.88 + (n(u, v, 128) - 0.5) * 0.18 + (n.fbm(u, v, 3, 4) - 0.5) * 0.22));
  return { color: img, height: (x, y, u, v) => n(u, v, 128) * 1.2 };
};
export default {
  asphalt: { make: asphalt, rough: 0.85, normal: 1 },
  line_white: { make: () => flat([226, 226, 220], 302, 0.08), rough: 0.7 },
};

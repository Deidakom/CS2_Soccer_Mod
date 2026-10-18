// Cars in the car park (cars.py). car_paint is the base (silver); the colour variants swap it.
import { flat, steel } from "../matlib.mjs";

const paint = (rgb, seed) => ({ make: () => flat(rgb, seed, 0.02), rough: 0.22, metal: 0.35 });
export default {
  car_paint: paint([150, 154, 160], 201),
  car_red: paint([150, 16, 20], 202),
  car_blue: paint([24, 54, 130], 203),
  car_white: paint([226, 228, 230], 204),
  car_black: paint([20, 21, 24], 205),
  car_green: paint([28, 76, 52], 206),
  car_glass: { make: () => flat([16, 22, 30], 207, 0.02), rough: 0.05, metal: 0.6 },
  car_tyre: { make: () => flat([22, 22, 23], 208, 0.06), rough: 0.9 },
  car_rim: { make: () => steel([170, 172, 176], 209), rough: 0.3, metal: 0.8 },
  car_chrome: { make: () => steel([210, 212, 216], 210), rough: 0.15, metal: 1 },
  car_trim: { make: () => flat([18, 18, 20], 211, 0.04), rough: 0.6 },
  car_grille: { make: () => flat([10, 10, 11], 212, 0.04), rough: 0.5, metal: 0.3 },
  car_arch: { make: () => flat([8, 8, 9], 213, 0), rough: 0.9 },
  car_headlight: { make: () => flat([235, 238, 240], 214, 0), rough: 0.1, metal: 0.5 },
  car_taillight: { make: () => flat([150, 10, 12], 215, 0), rough: 0.15 },
  car_plate: { make: () => flat([232, 232, 226], 216, 0), rough: 0.5 },
};

# Parametric cars for the car park (props/car_*.py). Front towards +X, origin on the ground at the
# middle. Scale: 39 units = 1 m (a player is 72). Paint is "car_paint"; the prop scripts export
# colour variants that swap it (materials/cars.mjs).
import math
from lib import *

COLOURS = {c: {"car_paint": f"car_{c}"} for c in ("red", "blue", "white", "black", "green")}


def curve(points, steps=6):
    """Catmull-Rom through the given (x, z) points: smooth body lines from a few key points."""
    out = []
    p = [points[0]] + points + [points[-1]]
    steps *= DETAIL
    for i in range(1, len(p) - 2):
        for s in range(steps):
            t = s / steps
            out.append(tuple(0.5 * ((2 * p[i][k]) + (-p[i - 1][k] + p[i + 1][k]) * t + (2 * p[i - 1][k] - 5 * p[i][k] + 4 * p[i + 1][k] - p[i + 2][k]) * t * t
                                    + (-p[i - 1][k] + 3 * p[i][k] - 3 * p[i + 1][k] + p[i + 2][k]) * t ** 3) for k in range(2)))
    out.append(points[-1])
    return out


def strut(material, a, b, thick, depth, y):
    """A pillar from (x, z) a to b at side y."""
    dx, dz = b[0] - a[0], b[1] - a[1]
    ln = math.hypot(dx, dz)
    box(material, (ln, depth, thick), ((a[0] + b[0]) / 2, y, (a[1] + b[1]) / 2), rot=(0, -math.degrees(math.atan2(dz, dx)), 0))


def car(kind="sedan"):
    L = {"sedan": 178, "hatch": 156, "suv": 186}[kind]
    H = {"sedan": 1.0, "hatch": 1.06, "suv": 1.28}[kind]
    R = {"sedan": 12.5, "hatch": 12, "suv": 15.5}[kind]        # wheel radius
    W = 70 if kind != "suv" else 76
    hx, z = L / 2, (lambda v: v * H)
    belt = z(37)
    # lower body: sill line, nose, hood rising to the windscreen, tail, all as smooth curves
    if kind == "sedan":
        top = [(hx - 1, z(30)), (hx - 20, z(34)), (hx - 52, belt), (-hx + 30, belt), (-hx + 6, z(36)), (-hx, z(30))]
        glass = [(hx - 54, belt), (hx - 70, z(47)), (hx - 86, z(54.5)), (-hx + 58, z(55)), (-hx + 44, z(48)), (-hx + 28, belt)]
    elif kind == "hatch":
        top = [(hx - 1, z(30)), (hx - 18, z(34)), (hx - 46, belt), (-hx + 6, belt), (-hx, z(32))]
        glass = [(hx - 48, belt), (hx - 64, z(47)), (hx - 80, z(54.5)), (-hx + 14, z(55)), (-hx + 6, z(46)), (-hx + 4, belt)]
    else:
        top = [(hx - 1, z(31)), (hx - 16, z(34)), (hx - 40, belt), (-hx + 4, belt), (-hx, z(32))]
        glass = [(hx - 42, belt), (hx - 54, z(46)), (hx - 68, z(54)), (-hx + 8, z(54.5)), (-hx + 4, z(46)), (-hx + 3, belt)]
    body = [(-hx + 2, z(11)), (hx - 2, z(11)), (hx + 2, z(18)), (hx + 1, z(26))] + curve(top, 5) + [(-hx - 2, z(22)), (-hx - 1, z(15))]
    profile("car_paint", body, W, bevel=5, segments=4, name="body")
    gl = curve(glass, 6)
    profile("car_glass", gl, W - 7, bevel=2, segments=2, name="cabin")
    # roof skin over the glass top, pillars at the screen and rear window edges and in the middle
    roof_pts = [p for p in gl if p[1] >= z(53)]
    if len(roof_pts) >= 2:
        r0, r1 = roof_pts[0], roof_pts[-1]
        profile("car_paint", [(r1[0] - 2, r1[1] - 0.5), (r0[0] + 2, r0[1] - 0.5), (r0[0], r0[1] + 2.2), (r1[0], r1[1] + 2.2)], W - 5, bevel=1.5, segments=2, name="roof")
    a_base, c_base = glass[0], glass[-1]
    a_top, c_top = roof_pts[0] if roof_pts else glass[2], roof_pts[-1] if roof_pts else glass[-3]
    for s in (-1, 1):
        y = s * (W / 2 - 3.2)
        strut("car_paint", a_base, a_top, 3.2, 1.6, y)
        strut("car_paint", c_base, c_top, 6 if kind != "sedan" else 4, 1.6, y)
        bx = (a_top[0] + c_top[0]) / 2 + 6
        box("car_paint", (4.5, 1.6, z(55) - belt), (bx, y, (belt + z(55)) / 2))                  # B pillar
        box("car_trim", (L * 0.6, 0.6, 0.8), (-4, s * (W / 2 + 0.3), z(27)))                     # door crease
        for hxp in (bx + 20, bx - 24):
            box("car_chrome", (3.5, 0.9, 1.1), (hxp, s * (W / 2 + 0.4), z(32.5)), bevel=0.3)       # handles
        box("car_paint", (7, 5.5, 4), (a_base[0] - 3, s * (W / 2 + 2.4), z(41)), bevel=1.4)     # mirrors
        box("car_glass", (0.6, 4.5, 3), (a_base[0] - 6.6, s * (W / 2 + 2.4), z(41)))
        box("car_trim", (L - 40, 1.2, 3), (0, s * (W / 2 - 0.4), z(12.5)))                       # sill
    # wheels: tyre with rounded shoulders, five-spoke rim, hub cap, dark wheel arches
    for x in (hx - 36, -hx + 34):
        for s in (-1, 1):
            y = s * (W / 2 - 4.5)
            cyl("car_tyre", R, 10, (x, y, R), rot=(90, 0, 0), verts=32, bevel=2)
            cyl("car_rim", R * 0.66, 10.4, (x, y, R), rot=(90, 0, 0), verts=24)
            cyl("car_trim", R * 0.5, 10.6, (x, y, R), rot=(90, 0, 0), verts=24)
            for k in range(5):
                a = k * 72
                box("car_rim", (R * 0.95, 1.1, 2.2), (x, y + s * 5.4, R), rot=(0, a, 0))
            cyl("car_chrome", R * 0.16, 11.4, (x, y, R), rot=(90, 0, 0), verts=12)
            cyl("car_arch", R * 1.16, 2, (x, s * (W / 2 - 1.2), R + 1), rot=(90, 0, 0), verts=32)
    # lights, grille, plates, bumpers
    for s in (-1, 1):
        box("car_headlight", (2.4, 13, 4.5), (hx + 1.2, s * (W / 2 - 12), z(26.5)), bevel=1.2)
        box("car_taillight", (2.4, 15, 4.5), (-hx - 1.6, s * (W / 2 - 10), z(28)), bevel=1.2)
        box("car_trim", (2, 8, 3), (hx + 2.2, s * (W / 2 - 13), z(14)), bevel=0.8)               # fog light housings
    box("car_grille", (2, W * 0.36, 7), (hx + 1.8, 0, z(23)), bevel=1)
    box("car_plate", (0.8, 20, 4.4), (hx + 3.2, 0, z(15.5)))
    box("car_plate", (0.8, 20, 4.4), (-hx - 2.6, 0, z(19)))
    box("car_trim", (3.5, W - 6, 4.5), (hx + 1.6, 0, z(12.5)), bevel=1.5)
    box("car_trim", (3.5, W - 6, 4.5), (-hx - 1.4, 0, z(12.5)), bevel=1.5)
    collision((L + 4, W + 8, z(57)), (0, 0, z(57) / 2))
    PREVIEW_COLOURS.update(car_paint=(0.55, 0.05, 0.05), car_glass=(0.04, 0.06, 0.09), car_tyre=(0.03, 0.03, 0.03), car_rim=(0.7, 0.7, 0.72),
                           car_trim=(0.06, 0.06, 0.07), car_headlight=(1, 1, 0.9), car_taillight=(0.8, 0.05, 0.05), car_plate=(0.9, 0.9, 0.9),
                           car_chrome=(0.85, 0.85, 0.88), car_grille=(0.03, 0.03, 0.03), car_arch=(0.02, 0.02, 0.02))
    export(variants=COLOURS)

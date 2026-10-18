# Pretzel kiosk: baskets of pretzels, a giant pretzel on the roof.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from kiosk import *
reset()

def pretzel(x, y, z, s):
    # the rope: right end resting on the belly, up through the twist, round the left lobe, along the
    # belly, round the right lobe, back through the twist down to the left end
    path = [(0.38, -0.5, 0), (0.18, -0.05, 0.1), (0, 0.32, 0.14), (-0.3, 0.72, 0.06), (-0.72, 0.82, 0), (-0.98, 0.5, 0),
            (-0.92, -0.05, 0), (-0.62, -0.62, 0), (-0.2, -0.86, 0), (0.2, -0.86, 0), (0.62, -0.62, 0), (0.92, -0.05, 0),
            (0.98, 0.5, 0), (0.72, 0.82, 0), (0.3, 0.72, -0.06), (0, 0.32, -0.14), (-0.18, -0.05, -0.1), (-0.38, -0.5, 0)]
    pts = [(x + px * s, y + py * s, z + pz * s) for px, pz, py in path]
    small = s < 20
    tube("pretzel", pts, 0.12 * s, resolution=4 if small else 12, bevel_res=1 if small else 4)
    if not small:
        for sx, sz in ((-0.5, 0.3), (0.5, 0.3), (0, -0.7), (-0.75, -0.2), (0.75, -0.2)):
            sphere("paint_white", 0.03 * s, (x + sx * s, y - 0.12 * s, z + sz * s), segments=6, rings=4)

def inside(hc, front):
    for i in range(3):
        cyl("wood_counter", 11, 5, (-50 + i * 26, front + 6, hc + 2.5), verts=16)
        for k in range(3):
            pretzel(-56 + i * 26 + k * 6, front + 6, hc + 8, 7)
    box("glass_clear", (70, 24, 26), (40, front + 26, hc + 13), bevel=0.6)

def topper(rz):
    pretzel(0, 10, rz + 66, 44)
    for x in (-22, 22):
        cyl("paint_white", 3, 14, (x, 10, rz + 30), verts=12)

kiosk("PRETZELS", body="paint_green", awning=("awning_green", "awning_white"), topper=topper, inside=inside)
export()

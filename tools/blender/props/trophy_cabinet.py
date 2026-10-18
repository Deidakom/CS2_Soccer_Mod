# Glass trophy cabinet with trophies and pennants, front -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("frame_wood", (110, 36, 6), (0, 0, 3), bevel=1)
box("frame_wood", (110, 36, 6), (0, 0, 123), bevel=1)
for x in (-53, 53):
    box("frame_wood", (4, 36, 120), (x, 0, 63))
box("frame_wood", (110, 2, 120), (0, 17, 63))
for z in (40, 80):
    box("glass_shelf", (102, 32, 1.2), (0, 0, z))
box("shop_glass", (104, 1, 116), (0, -18, 63))
def cup(x, z, s, mat="trophy_gold"):
    cyl(mat, 4 * s, 3 * s, (x, 0, z + 1.5 * s), verts=20)
    cyl(mat, 1.4 * s, 8 * s, (x, 0, z + 7 * s), verts=12)
    sphere(mat, 6 * s, (x, 0, z + 15 * s), scale=(1, 1, 1.1), segments=20, rings=10)
    for sx in (-1, 1):
        tube(mat, [(x + sx * 5 * s, 0, z + 18 * s), (x + sx * 9 * s, 0, z + 16 * s), (x + sx * 6 * s, 0, z + 11 * s)], 0.7 * s, resolution=2, bevel_res=1)
for k, (x, z, s, m) in enumerate([(-30, 6, 1.4, "trophy_gold"), (0, 6, 1.8, "trophy_gold"), (30, 6, 1.4, "steel_brushed"), (-25, 41, 1.1, "trophy_gold"), (25, 41, 1.1, "bronze"), (0, 81, 1.3, "trophy_gold")]):
    cup(x, z, s, m)
box("team_paint", (24, 0.6, 16), (-32, 15.5, 100), rot=(0, 0, 0))
box("paint_blue", (24, 0.6, 16), (32, 15.5, 100))
collision((110, 38, 126), (0, 0, 63))
export()

# TV outside-broadcast truck with a satellite dish and a camera mast, front towards +X.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
L, W = 420, 96
box("bus_white", (L - 90, W, 150), (-45, 0, 95), bevel=4)
profile("bus_white", [(L / 2 - 90, 20), (L / 2, 20), (L / 2 + 2, 70), (L / 2 - 20, 128), (L / 2 - 90, 128)], W - 4, bevel=5, segments=3)
box("car_glass", (4, W - 14, 40), (L / 2 - 12, 0, 100), rot=(0, -30, 0))
box("tv_blue", (L - 100, W + 1.2, 30), (-45, 0, 70))
for s in (-1, 1):
    text("bus_white", "SOCCERMOD TV", 18, (-45, s * (W / 2 + 1), 70), rot=(90, 0, 0 if s < 0 else 180), depth=0.5)
for x in (L / 2 - 60, -L / 2 + 70, -L / 2 + 130):
    for s in (-1, 1):
        cyl("car_tyre", 19, 16, (x, s * (W / 2 - 8), 19), rot=(90, 0, 0), verts=28, bevel=2)
        cyl("car_rim", 11, 16.6, (x, s * (W / 2 - 8), 19), rot=(90, 0, 0), verts=20)
cyl("steel_dark", 4, 30, (-120, 0, 185), verts=12)
dish = sphere("bus_white", 1, (-120, 0, 212), scale=(34, 34, 10), segments=24, rings=8)
dish.rotation_euler = (0, math.radians(-50), 0)
cyl("steel_dark", 1.4, 40, (-104, 0, 222), rot=(0, -50, 0), verts=8)
cyl("steel_brushed", 3, 120, (60, 30, 230), verts=10)
box("steel_dark", (14, 10, 10), (60, 30, 292), bevel=1)
box("car_headlight", (2, 16, 7), (L / 2 + 1.5, 30, 40), bevel=1)
box("car_headlight", (2, 16, 7), (L / 2 + 1.5, -30, 40), bevel=1)
collision((L + 4, W + 4, 172), (0, 0, 86))
PREVIEW_COLOURS.update(bus_white=(0.95, 0.95, 0.95), car_glass=(0.04, 0.06, 0.09), tv_blue=(0.05, 0.15, 0.5), car_tyre=(0.03, 0.03, 0.03),
                       car_rim=(0.7, 0.7, 0.72), steel_dark=(0.1, 0.1, 0.1), steel_brushed=(0.55, 0.56, 0.58), car_headlight=(1, 1, 0.9))
export()

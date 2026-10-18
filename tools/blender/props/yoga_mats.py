# Stretching mats: two laid out, three rolled in a basket, foam rollers.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
for k, x in enumerate((-40, 0)):
    box("team_paint" if k == 0 else "paint_blue", (30, 72, 0.8), (x, 0, 0.4), bevel=0.3)
for k in range(3):
    cyl(["team_paint", "paint_blue", "rubber_black"][k], 4.5, 30, (40 + k * 10, -20, 4.5), rot=(90, 0, 0), verts=20)
for k in range(2):
    cyl("rubber_black", 5, 30, (40 + k * 12, 20, 5), rot=(0, 90, 0), verts=20)
export()

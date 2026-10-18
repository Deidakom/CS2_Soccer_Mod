# Water cooler with a blue bottle.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("paint_white", (24, 24, 70), (0, 0, 35), bevel=2)
cyl("water_blue", 10, 34, (0, 0, 88), verts=24, bevel=2)
box("steel_dark", (14, 2, 10), (0, -12.5, 52), bevel=0.6)
for x in (-4, 4):
    box("team_paint" if x < 0 else "paint_blue", (2.4, 2, 2.4), (x, -14, 55), bevel=0.4)
collision((24, 24, 106), (0, 0, 53))
export()

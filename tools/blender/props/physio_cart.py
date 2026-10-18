# Physio trolley with kit bag, tape and bottles.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("steel_brushed", (50, 30, 2), (0, 0, 40), bevel=0.6)
box("steel_brushed", (50, 30, 2), (0, 0, 14), bevel=0.6)
for x in (-23, 23):
    for y in (-13, 13):
        cyl("steel_brushed", 1, 40, (x, y, 22), verts=8)
        sphere("rubber_black", 2.4, (x, y, 2.4), segments=8, rings=4)
box("team_paint", (34, 20, 16), (0, 0, 49), bevel=3)
for k in range(4):
    cyl("paint_white", 2, 2.4, (-18 + k * 5, 10, 16.5), verts=12)
for k in range(3):
    cyl("water_blue", 2.4, 14, (16 + k * 5, -8, 22), verts=12)
collision((52, 32, 58), (0, 0, 29))
export()

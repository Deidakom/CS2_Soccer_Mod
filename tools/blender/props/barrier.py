# Crowd control barrier, 86 long, feet along Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
for x in (-42, 42):
    cyl("steel_brushed", 1.4, 42, (x, 0, 24), verts=10)
    box("steel_brushed", (2.5, 30, 2.5), (x, 0, 2), bevel=0.6)
for z in (8, 44):
    cyl("steel_brushed", 1.4, 84, (0, 0, z), rot=(0, 90, 0), verts=10)
for k in range(13):
    cyl("steel_brushed", 0.6, 36, (-36 + k * 6, 0, 26), verts=6)
box("barrier_sign", (60, 0.6, 14), (0, -1.6, 30))
collision((86, 6, 46), (0, 0, 23))
PREVIEW_COLOURS.update(steel_brushed=(0.6, 0.62, 0.64), barrier_sign=(0.05, 0.15, 0.5))
export()

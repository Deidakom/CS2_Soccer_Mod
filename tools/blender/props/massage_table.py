# Physio table with headrest and towel roll.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("leather_dark", (76, 28, 7), (0, 0, 32), bevel=2.5, segments=3)
box("leather_dark", (14, 26, 6), (44, 0, 31), bevel=2, segments=3)
for x in (-32, 32):
    for y in (-10, 10):
        cyl("steel_brushed", 1.5, 28, (x, y, 14), verts=10)
cyl("towel", 4, 26, (-30, 0, 39), rot=(90, 0, 0), verts=14)
collision((92, 30, 36), (4, 0, 18))
PREVIEW_COLOURS.update(leather_dark=(0.08, 0.1, 0.18), steel_brushed=(0.55, 0.56, 0.58), towel=(0.95, 0.95, 0.95))
export()

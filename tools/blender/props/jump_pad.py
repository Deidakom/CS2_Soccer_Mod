# Jump pad: a round glowing launch plate in a steel ring (the push is a trigger in the map).
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
cyl("steel_dark", 46, 6, (0, 0, 3), verts=40, bevel=1.5)
cyl("pad_glow", 40, 1, (0, 0, 6.2), verts=40)
for k in range(3):
    pts = [((14 + k * 9) * math.cos(math.radians(a)), (14 + k * 9) * math.sin(math.radians(a)), 6.9) for a in range(0, 361, 15)]
    tube("steel_brushed", pts, 0.8, closed=True, resolution=2, bevel_res=1)
for a in (0, 120, 240):
    box("paint_yellow", (10, 3, 0.6), (34 * math.cos(math.radians(a)), 34 * math.sin(math.radians(a)), 6.6), rot=(0, 0, a))
export()

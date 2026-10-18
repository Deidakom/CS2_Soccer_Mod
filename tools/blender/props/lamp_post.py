# Double street lamp, 300 tall, arms along X.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
cyl("steel_dark", 4, 300, (0, 0, 150), verts=16, radius2=2.6)
cyl("steel_dark", 9, 10, (0, 0, 5), verts=16)
for s in (-1, 1):
    tube("steel_dark", [(0, 0, 280), (s * 20, 0, 296), (s * 44, 0, 298)], 1.6)
    box("steel_dark", (26, 12, 5), (s * 52, 0, 296), bevel=1.5)
    box("ceiling_light", (22, 9, 1), (s * 52, 0, 293.2))
collision((10, 10, 300), (0, 0, 150))
PREVIEW_COLOURS.update(steel_dark=(0.12, 0.12, 0.13), ceiling_light=(1, 1, 0.9))
export()

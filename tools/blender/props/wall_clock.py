# Round wall clock, facing -Y, back on the wall.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
cyl("frame_black", 12, 3, (0, -1.5, 0), rot=(90, 0, 0), verts=32, bevel=0.6)
cyl("paint_white", 11, 0.4, (0, -3.2, 0), rot=(90, 0, 0), verts=32)
for k in range(12):
    a = math.radians(k * 30)
    box("frame_black", (0.8, 0.3, 2.2), (math.sin(a) * 9.4, -3.5, math.cos(a) * 9.4), rot=(0, -k * 30, 0))
box("frame_black", (0.8, 0.3, 7), (1.6, -3.7, 2.4), rot=(0, -40, 0))
box("frame_black", (0.6, 0.3, 9), (-2.6, -3.8, 2.8), rot=(0, 60, 0))
box("team_paint", (0.3, 0.3, 9), (0, -3.9, -2), rot=(0, 160, 0))
export()

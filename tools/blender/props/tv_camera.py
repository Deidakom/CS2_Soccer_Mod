# Broadcast camera on a tripod with a long lens, lens towards -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
for a in (0, 120, 240):
    r = math.radians(a)
    tube("steel_dark", [(0, 0, 44), (math.cos(r) * 18, math.sin(r) * 18, 0)], 1)
cyl("steel_dark", 2, 8, (0, 0, 46), verts=10)
box("camera_body", (16, 28, 16), (0, 2, 58), bevel=2)
cyl("camera_body", 5, 34, (0, -26, 60), rot=(90, 0, 0), verts=16)
cyl("car_glass", 4.2, 1, (0, -43.5, 60), rot=(90, 0, 0), verts=16)
box("steel_dark", (10, 8, 6), (0, 14, 70), bevel=1)
box("ticket_glow", (2, 1, 1), (6, 18, 68))
tube("steel_dark", [(0, 16, 54), (0, 30, 46)], 0.8)
collision((40, 40, 70), (0, 0, 35))
PREVIEW_COLOURS.update(steel_dark=(0.1, 0.1, 0.1), camera_body=(0.15, 0.15, 0.16), car_glass=(0.04, 0.06, 0.09), ticket_glow=(1, 0.1, 0.1))
export()

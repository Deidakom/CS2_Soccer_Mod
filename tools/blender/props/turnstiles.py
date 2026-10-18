# Row of waist-high tripod turnstiles across a gate (350 wide), entry towards +Y. Only the
# cabinets collide, the 46-unit lanes between them stay open.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
N = 6
for k in range(N):
    x = -175 + k * 70
    box("steel_brushed", (16, 60, 40), (x, 0, 20), bevel=2)
    box("steel_dark", (17, 30, 4), (x, 0, 41), bevel=1)
    box("ticket_glow", (6, 4, 2), (x, -18, 43), bevel=0.5)
    if k < N - 1:
        cyl("steel_dark", 4, 6, (x + 10, -6, 34), rot=(0, 90, 0), verts=12)
        for a in (0, 120, 240):
            arm = cyl("steel_brushed", 1.2, 26, (x + 22, -6, 34), rot=(0, 90, 0), verts=8)
            arm.rotation_euler = (math.radians(a), math.radians(90), 0)
    collision((16, 60, 40), (x, 0, 20))
PREVIEW_COLOURS.update(steel_brushed=(0.55, 0.56, 0.58), steel_dark=(0.1, 0.1, 0.1), ticket_glow=(0.1, 0.9, 0.3))
export()

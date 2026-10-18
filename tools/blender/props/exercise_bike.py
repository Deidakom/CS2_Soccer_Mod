# Exercise bike for the warm-up room, front towards +X. Home; variant away.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("steel_dark", (60, 20, 4), (0, 0, 2), bevel=1)
cyl("steel_dark", 12, 6, (18, 0, 16), rot=(90, 0, 0), verts=24, bevel=1)
box("team_paint", (40, 6, 6), (4, 0, 22), rot=(0, -30, 0), bevel=1.5)
box("steel_dark", (4, 4, 30), (-12, 0, 22), bevel=0.8)
box("leather_dark", (14, 9, 4), (-12, 0, 38), bevel=1.6)
box("steel_dark", (4, 4, 26), (20, 0, 32), rot=(0, 20, 0), bevel=0.8)
box("steel_brushed", (4, 24, 3), (24, 0, 45), bevel=1)
box("ticket_glow", (6, 8, 1), (22, 0, 47), rot=(0, -30, 0))
collision((60, 24, 46), (2, 0, 23))
PREVIEW_COLOURS.update(steel_dark=(0.1, 0.1, 0.1), team_paint=(0.7, 0.05, 0.06), leather_dark=(0.08, 0.1, 0.18), steel_brushed=(0.55, 0.56, 0.58), ticket_glow=(0.1, 0.9, 0.3))
export(variants={"away": {"team_paint": "team_paint_away"}})

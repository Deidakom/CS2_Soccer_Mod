# Litter bin with team-coloured band. Home; variant away.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
cyl("steel_dark", 10, 34, (0, 0, 17), verts=20, bevel=1)
cyl("team_paint", 10.4, 6, (0, 0, 26), verts=20)
cyl("steel_brushed", 11, 2, (0, 0, 35), verts=20, bevel=0.6)
collision((22, 22, 36), (0, 0, 18))
PREVIEW_COLOURS.update(steel_dark=(0.1, 0.1, 0.1), team_paint=(0.7, 0.05, 0.06), steel_brushed=(0.55, 0.56, 0.58))
export(variants={"away": {"team_paint": "team_paint_away"}})

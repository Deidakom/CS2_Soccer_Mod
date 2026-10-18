# Shower on the wall: pipe, mixer and a round head, facing -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
tube("steel_brushed", [(0, -2, -40), (0, -2, 30), (0, -12, 34)], 0.8)
cyl("steel_brushed", 6, 1.4, (0, -14, 32), rot=(20, 0, 0), verts=24)
cyl("steel_brushed", 2.2, 3, (0, -3, -6), rot=(90, 0, 0), verts=16)
box("steel_brushed", (5, 1.6, 1.6), (0, -5, -6), bevel=0.4)
export()

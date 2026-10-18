# Ball boy stool with a spare ball and a towel.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
for x in (-8, 8):
    for y in (-8, 8):
        cyl("steel_dark", 0.8, 18, (x, y, 9), verts=6)
cyl("leather_dark", 11, 2.5, (0, 0, 19), verts=20, bevel=0.8)
sphere("jersey_trim", 4.4, (16, 0, 4.4), segments=16, rings=8)
box("towel", (10, 8, 1), (0, 0, 21), bevel=0.4)
collision((24, 24, 22), (0, 0, 11))
PREVIEW_COLOURS.update(steel_dark=(0.1, 0.1, 0.1), leather_dark=(0.08, 0.1, 0.18), jersey_trim=(0.95, 0.95, 0.95), towel=(0.95, 0.95, 0.95))
export()

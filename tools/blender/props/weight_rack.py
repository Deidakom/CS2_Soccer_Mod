# Dumbbell rack with a row of dumbbells and a few plates, front -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
for x in (-50, 50):
    box("steel_dark", (4, 30, 40), (x, 0, 20), bevel=0.8)
for z, y in ((22, -6), (36, 6)):
    box("steel_dark", (104, 12, 2), (0, y, z), rot=(-15, 0, 0), bevel=0.4)
for k in range(8):
    x = -42 + k * 12
    for z, y in ((26, -6), (40, 6)):
        cyl("rubber_black", 3.4 + k * 0.15, 3, (x - 3.5, y, z), rot=(0, 90, 0), verts=16)
        cyl("rubber_black", 3.4 + k * 0.15, 3, (x + 3.5, y, z), rot=(0, 90, 0), verts=16)
        cyl("steel_brushed", 1, 5, (x, y, z), rot=(0, 90, 0), verts=8)
for k in range(3):
    cyl("rubber_black", 14 - k * 2, 3, (70, 0, 14 - k * 0), rot=(90, 0, 0), verts=28)
collision((110, 32, 44), (0, 0, 22))
export()

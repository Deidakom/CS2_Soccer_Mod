# Crate of water bottles for the bench.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("crate_blue", (40, 28, 14), (0, 0, 7), bevel=1)
for i in range(4):
    for j in range(3):
        cyl("water_blue", 2.6, 22, (-15 + i * 10, -9 + j * 9, 15), verts=12)
        cyl("paint_white", 1.4, 3, (-15 + i * 10, -9 + j * 9, 27.5), verts=10)
collision((40, 28, 28), (0, 0, 14))
export()

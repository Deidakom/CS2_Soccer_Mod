# Training cones: a stack and a few laid out, plus agility poles.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
for k in range(6):
    cyl("cone_orange", 6, 12, (0, 0, 6 + k * 2.2), verts=20, radius2=1.4)
for k, (x, y) in enumerate([(30, 10), (50, -14), (70, 10), (90, -14)]):
    cyl("cone_orange" if k % 2 == 0 else "cone_yellow", 6, 12, (x, y, 6), verts=20, radius2=1.4)
    cyl("cone_orange", 7, 0.8, (x, y, 0.4), verts=20)
for x in (-30, -20):
    cyl("cone_yellow", 1.2, 60, (x, 20, 30), verts=10)
export()

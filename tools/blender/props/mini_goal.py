# Small goal (100 x 60) with a back net, mouth towards -Y. Frame and net collide.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
def net_panel(x0, x1, z0, z1, y, cell=6, cord=0.35, mat="net_cord"):
    nx, nz = int((x1 - x0) / cell), int((z1 - z0) / cell)
    for i in range(nx + 1):
        box(mat, (cord, cord, z1 - z0), (x0 + i * (x1 - x0) / nx, y, (z0 + z1) / 2))
    for j in range(nz + 1):
        box(mat, (x1 - x0, cord, cord), ((x0 + x1) / 2, y, z0 + j * (z1 - z0) / nz))
for x in (-50, 50):
    cyl("paint_white", 2.4, 60, (x, 0, 30), verts=16)
cyl("paint_white", 2.4, 104, (0, 0, 60), rot=(0, 90, 0), verts=16)
for x in (-50, 50):
    tube("paint_white", [(x, 0, 60), (x, 30, 40), (x, 34, 0)], 1.4)
tube("paint_white", [(-50, 34, 1), (50, 34, 1)], 1.4)
net_panel(-50, 50, 0, 60, 34, 5)
for x in (-50, 50):
    collision((5, 5, 60), (x, 0, 30))
collision((104, 5, 5), (0, 0, 60))
collision((100, 4, 60), (0, 36, 30))
for x in (-52, 52):
    collision((3, 36, 60), (x, 18, 30))
export()

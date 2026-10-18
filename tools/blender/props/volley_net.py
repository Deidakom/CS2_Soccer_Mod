# Beach volleyball net across X (posts at +-205), top tape at 95 units (2.43 m). The net blocks.
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
for x in (-205, 205):
    cyl("steel_brushed", 2.6, 104, (x, 0, 52), verts=16)
    cyl("paint_white", 2.8, 30, (x, 0, 20), verts=16)
    tube("net_cord", [(x, 0, 104), (x * 1.25, 0, 0)], 0.4, resolution=2, bevel_res=1)
net_panel(-195, 195, 60, 92, 0, 6)
box("paint_white", (392, 1.2, 4), (0, 0, 94), bevel=0.3)
box("paint_white", (392, 1.2, 2.4), (0, 0, 59), bevel=0.3)
for x in (-196, 196):
    box("team_paint", (1.4, 1.4, 50), (x, 0, 116), bevel=0.2)
collision((392, 4, 36), (0, 0, 78))
for x in (-205, 205):
    collision((6, 6, 104), (x, 0, 52))
export()

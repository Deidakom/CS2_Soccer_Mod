# Full-size goal like the stadium's (254 x 96, 80 deep), mouth towards -Y, net and frame collide.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
# Variant lit: the crossbar glows for the crossbar challenge.
def net_x(x0, x1, z0, z1, y, cell=8, cord=0.45, m="net_cord"):
    nx, nz = max(1, int((x1 - x0) / cell)), max(1, int((z1 - z0) / cell))
    for i in range(nx + 1):
        box(m, (cord, cord, z1 - z0), (x0 + i * (x1 - x0) / nx, y, (z0 + z1) / 2))
    for j in range(nz + 1):
        box(m, (x1 - x0, cord, cord), ((x0 + x1) / 2, y, z0 + j * (z1 - z0) / nz))
def net_y(y0, y1, z0, z1, x, cell=8, cord=0.45, m="net_cord"):
    ny, nz = max(1, int((y1 - y0) / cell)), max(1, int((z1 - z0) / cell))
    for i in range(ny + 1):
        box(m, (cord, cord, z1 - z0), (x, y0 + i * (y1 - y0) / ny, (z0 + z1) / 2))
    for j in range(nz + 1):
        box(m, (cord, y1 - y0, cord), (x, (y0 + y1) / 2, z0 + j * (z1 - z0) / nz))
W, Hh, Dp = 127, 96, 80
for x in (-W, W):
    cyl("paint_white", 3.2, Hh, (x, 0, Hh / 2), verts=20)
cyl("goal_bar", 3.2, 2 * W + 6, (0, 0, Hh), rot=(0, 90, 0), verts=20)
for x in (-W, W):
    tube("paint_white", [(x, 0, Hh), (x, Dp * 0.5, Hh - 10), (x, Dp, 0)], 1.6, resolution=2, bevel_res=1)
tube("paint_white", [(-W, Dp, 1), (W, Dp, 1)], 1.6, resolution=2, bevel_res=1)
net_x(-W, W, 0, Hh - 12, Dp, 9)
for x in (-W, W):
    net_y(0, Dp, 0, Hh - 6, x, 9)
for x in (-W, W):
    collision((7, 7, Hh), (x, 0, Hh / 2))
collision((2 * W + 6, 7, 7), (0, 0, Hh))
collision((2 * W, 4, Hh), (0, Dp + 2, Hh / 2))
for x in (-W - 2, W + 2):
    collision((4, Dp, Hh), (x, Dp / 2, Hh / 2))
collision((2 * W, Dp, 4), (0, Dp / 2, Hh + 2))
export(variants={"lit": {"goal_bar": "bar_lit"}})

# 1v1 cage: steel posts, rails and mesh round a 560 x 360 floor, 132 high, open roof.
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
L, W, Hh = 560, 360, 132
for sx in (-1, 1):
    for x in [-L / 2 + k * L / 7 for k in range(8)]:
        pass
posts = [(-L / 2 + k * L / 7, s * W / 2) for k in range(8) for s in (-1, 1)] + [(s * L / 2, -W / 2 + k * W / 4) for k in range(1, 4) for s in (-1, 1)]
for x, y in posts:
    box("steel_dark", (6, 6, Hh), (x, y, Hh / 2), bevel=1)
for s in (-1, 1):
    for z in (2, 40, Hh - 2):
        box("steel_dark", (L, 4, 4), (0, s * W / 2, z), bevel=0.6)
        box("steel_dark", (4, W, 4), (s * L / 2, 0, z), bevel=0.6)
    box("board_white", (L, 3, 38), (0, s * (W / 2 - 3), 20))
    box("board_white", (3, W, 38), (s * (L / 2 - 3), 0, 20))
    net_panel(-L / 2, L / 2, 40, Hh, s * W / 2, 10, 0.5, "steel_dark")
for s in (-1, 1):
    me_x = s * L / 2
    for yy in range(int(-W / 2), int(W / 2) + 1, 10):
        box("steel_dark", (0.5, 0.5, Hh - 40), (me_x, yy, (40 + Hh) / 2))
    for zz in range(40, Hh + 1, 10):
        box("steel_dark", (0.5, W, 0.5), (me_x, 0, zz))
text("paint_white", "1 V 1", 18, (0, -W / 2 - 2, 20), depth=0.6)
for s in (-1, 1):
    collision((L + 8, 8, Hh), (0, s * (W / 2 + 1), Hh / 2))
    collision((8, W + 8, Hh), (s * (L / 2 + 1), 0, Hh / 2))
export()

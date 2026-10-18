# 1v1 cage at the size of soccer_1v1_cage: 600 x 880 pitch (Y long), 120-high boards that are in
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
# play, mesh up to 300 with a ball lid, 156 x 62 goals (72 deep) set into the end boards. Origin on the
# pitch floor at the centre.
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
HX, HY, WALL, TOP = 300, 440, 120, 300
GH, GZ, GD = 78, 62, 72
T = 10
for s in (-1, 1):
    box("board_white", (T, 2 * HY, WALL), (s * (HX + T / 2), 0, WALL / 2), bevel=1)            # long side boards
    for sx in (-1, 1):                                                                         # end boards beside the goal
        w = HX - GH
        box("board_white", (w, T, WALL), (sx * (GH + w / 2), s * (HY + T / 2), WALL / 2), bevel=1)
    box("board_white", (2 * GH, T, WALL - GZ), (0, s * (HY + T / 2), GZ + (WALL - GZ) / 2), bevel=1)   # over the goal
    # the goal box behind the mouth
    yb = s * (HY + GD)
    box("steel_dark", (2 * GH + 8, GD, 4), (0, s * (HY + GD / 2), GZ + 2))                     # roof
    for sx in (-1, 1):
        box("steel_dark", (4, GD, GZ), (sx * (GH + 2), s * (HY + GD / 2), GZ / 2))
    net_x(-GH, GH, 0, GZ, yb, 7)
    for x in (-GH, GH):
        cyl("paint_white", 2.6, GZ, (x, s * HY, GZ / 2), verts=16)
    cyl("paint_white", 2.6, 2 * GH, (0, s * HY, GZ), rot=(0, 90, 0), verts=16)
    box("team_paint" if s > 0 else "paint_blue", (2 * HX, 2, 6), (0, s * (HY - 1), WALL - 3))
# posts and the mesh from the boards up to the lid, the lid as a net
for x in (-HX - T, HX + T):
    for k in range(9):
        y = -HY - T + k * (2 * HY + 2 * T) / 8
        box("steel_dark", (8, 8, TOP), (x, y, TOP / 2), bevel=1)
for y in (-HY - T, HY + T):
    for k in range(5):
        x = -HX - T + k * (2 * HX + 2 * T) / 4
        box("steel_dark", (8, 8, TOP), (x, y, TOP / 2), bevel=1)
for s in (-1, 1):
    net_y(-HY - T, HY + T, WALL, TOP, s * (HX + T), 16, 0.5, "steel_dark")
    net_x(-HX - T, HX + T, WALL, TOP, s * (HY + T), 16, 0.5, "steel_dark")
    box("steel_dark", (2 * HX + 2 * T, 4, 4), (0, s * (HY + T), TOP))
    box("steel_dark", (4, 2 * HY + 2 * T, 4), (s * (HX + T), 0, TOP))
for k in range(9):
    box("steel_dark", (2 * HX + 2 * T, 0.5, 0.5), (0, -HY + k * 2 * HY / 8, TOP))
text("paint_white", "1 V 1  CAGE", 28, (0, -HY - T - 1, WALL - 30), depth=0.6)
# collision: boards, goal boxes, mesh walls, the lid
for s in (-1, 1):
    collision((T, 2 * HY + 2 * T, TOP), (s * (HX + T / 2), 0, TOP / 2))
    for sx in (-1, 1):
        collision((HX - GH, T, TOP), (sx * (GH + (HX - GH) / 2), s * (HY + T / 2), TOP / 2))
    collision((2 * GH, T, TOP - GZ), (0, s * (HY + T / 2), GZ + (TOP - GZ) / 2))
    collision((2 * GH + 8, 4, GZ), (0, s * (HY + GD + 2), GZ / 2))
    collision((2 * GH + 8, GD, 4), (0, s * (HY + GD / 2), GZ + 2))
    for sx in (-1, 1):
        collision((4, GD, GZ), (sx * (GH + 2), s * (HY + GD / 2), GZ / 2))
collision((2 * HX + 2 * T, 2 * HY + 2 * T, 4), (0, 0, TOP + 2))
export()

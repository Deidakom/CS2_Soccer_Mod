# Torwand: a goal-sized board with two round holes (bottom left, top right), front -Y. The board
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
# collides everywhere except the holes (boxes round them).
W, Hh, T = 300, 110, 6
HOLES = [(-90, 25, 14), (90, 80, 14)]   # x, z, radius
box("steel_dark", (6, 10, Hh + 20), (-W / 2 - 3, 0, (Hh + 20) / 2), bevel=1)
box("steel_dark", (6, 10, Hh + 20), (W / 2 + 3, 0, (Hh + 20) / 2), bevel=1)
me = bpy.data.meshes.new("board")
bm = bmesh.new()
# board as a grid with the hole cells left out (fine grid so the holes are round enough)
cell = 5
nx, nz = int(W / cell), int(Hh / cell)
grid = {}
def vert(i, j, y):
    k = (i, j, y)
    if k not in grid:
        grid[k] = bm.verts.new((-W / 2 + i * cell, y, j * cell + 10))
    return grid[k]
def inhole(x, z):
    return any(math.hypot(x - hx, z - hz) < r for hx, hz, r in HOLES)
for i in range(nx):
    for j in range(nz):
        cx, cz = -W / 2 + (i + 0.5) * cell, (j + 0.5) * cell + 10
        if inhole(cx, cz):
            continue
        for y in (-T / 2, T / 2):
            f = [vert(i, j, y), vert(i + 1, j, y), vert(i + 1, j + 1, y), vert(i, j + 1, y)]
            bm.faces.new(list(reversed(f)) if y > 0 else f)   # front (-y) faces -y
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.01)
bm.to_mesh(me); bm.free()
ob = bpy.data.objects.new("board", me); bpy.context.collection.objects.link(ob); ob.data.materials.append(mat("board_white"))
for hx, hz, r in HOLES:
    tube("team_paint", [(hx + r * math.cos(math.radians(a)), -T / 2 - 0.4, hz + 10 + r * math.sin(math.radians(a))) for a in range(0, 360, 12)], 0.8, closed=True, resolution=2, bevel_res=1)
text("team_paint", "TORWAND", 16, (0, -T / 2 - 0.6, 10 + Hh - 14), depth=0.4)
# collision: strips that leave the two holes free (board z 10..120; holes r 14 at (-90, 35) and (90, 90))
def cbox(x0, x1, z0, z1):
    collision((x1 - x0, T, z1 - z0), ((x0 + x1) / 2, 0, (z0 + z1) / 2))
cbox(-150, -104, 10, 120); cbox(-76, 76, 10, 120); cbox(104, 150, 10, 120)
cbox(-104, -76, 10, 21); cbox(-104, -76, 49, 120)
cbox(76, 104, 10, 76); cbox(76, 104, 104, 120)
export()

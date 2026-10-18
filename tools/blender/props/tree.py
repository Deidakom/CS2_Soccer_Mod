# Plaza tree in a round planter: bark trunk with branches, clustered leaf crowns.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
import random
random.seed(11)
cyl("concrete_planter", 34, 22, (0, 0, 11), verts=32, bevel=2)
cyl("soil", 31, 1, (0, 0, 22.2), verts=32)
tube("bark", [(0, 0, 20), (2, 1, 80), (-2, 3, 140), (1, 0, 190)], 6, resolution=6)
crowns = []
for k in range(5):
    a = k * 72 + random.uniform(-20, 20)
    ex, ey = math.cos(math.radians(a)) * 46, math.sin(math.radians(a)) * 46
    tube("bark", [(0, 0, 120 + k * 8), (ex * 0.5, ey * 0.5, 150 + k * 6), (ex, ey, 170 + k * 6)], 2.6, resolution=4)
    crowns.append((ex, ey, 180 + k * 6))
crowns.append((0, 0, 215))
for cx, cy, cz in crowns:
    for j in range(6):
        sphere("leaves", random.uniform(14, 22), (cx + random.uniform(-18, 18), cy + random.uniform(-18, 18), cz + random.uniform(-10, 14)), segments=10, rings=6)
collision((70, 70, 24), (0, 0, 12))
collision((12, 12, 190), (0, 0, 115))
PREVIEW_COLOURS.update(concrete_planter=(0.6, 0.6, 0.58), soil=(0.2, 0.13, 0.08), bark=(0.3, 0.2, 0.12), leaves=(0.15, 0.4, 0.12))
export()

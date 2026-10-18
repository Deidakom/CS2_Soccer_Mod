# Tree for the lawns and the plaza edge (no planter): trunk, branches, clustered leaf crowns.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
import random
random.seed(23)
tube("bark", [(0, 0, -4), (3, 1, 70), (-2, 3, 140), (1, 0, 200)], 7, resolution=6)
crowns = []
for k in range(6):
    a = k * 60 + random.uniform(-20, 20)
    ex, ey = math.cos(math.radians(a)) * 54, math.sin(math.radians(a)) * 54
    tube("bark", [(0, 0, 110 + k * 8), (ex * 0.5, ey * 0.5, 145 + k * 6), (ex, ey, 168 + k * 6)], 3, resolution=4)
    crowns.append((ex, ey, 180 + k * 6))
crowns.append((0, 0, 225))
for cx, cy, cz in crowns:
    for j in range(7):
        sphere("leaves", random.uniform(15, 24), (cx + random.uniform(-20, 20), cy + random.uniform(-20, 20), cz + random.uniform(-10, 16)), segments=10, rings=6)
collision((14, 14, 200), (0, 0, 100))
PREVIEW_COLOURS.update(bark=(0.3, 0.2, 0.12), leaves=(0.15, 0.4, 0.12))
export()

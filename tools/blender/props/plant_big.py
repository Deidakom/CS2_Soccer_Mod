# Big indoor plant (monstera) in a white pot.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
import random
random.seed(5)
cyl("pot_white", 13, 28, (0, 0, 14), verts=28, radius2=11, bevel=1)
cyl("soil", 10, 1, (0, 0, 28.5), verts=28)   # inside the rim, above the pot top (was level with it and wider)
for k in range(14):
    a = k * 137.5
    r = random.uniform(3, 8)
    h = random.uniform(40, 86)
    x, y = math.cos(math.radians(a)) * r, math.sin(math.radians(a)) * r
    tube("plant_stem", [(0, 0, 26), (x * 0.5, y * 0.5, 26 + h * 0.5), (x, y, 26 + h)], 0.6, resolution=4)
    leaf = sphere("plant_leaf", 1, (x, y, 26 + h), scale=(10, 6.5, 1.2), segments=16, rings=6)
    leaf.rotation_euler = (math.radians(random.uniform(-35, 35)), math.radians(random.uniform(-25, 25)), math.radians(a))
collision((26, 26, 30), (0, 0, 15))
export()

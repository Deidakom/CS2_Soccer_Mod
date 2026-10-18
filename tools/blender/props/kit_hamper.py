# Laundry hamper with folded and crumpled shirts, team colour (variant away).
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("wicker", (50, 34, 32), (0, 0, 16), bevel=2)
box("wicker", (52, 36, 2), (0, 0, 32), bevel=0.8)
for k in range(5):
    sphere("jersey_home", 1, (-15 + k * 8, (k % 2) * 6 - 3, 33 + (k % 3) * 2), scale=(9, 8, 4), segments=12, rings=6)
box("towel", (16, 12, 2), (14, 4, 37), rot=(0, 10, 20), bevel=0.6)
collision((52, 36, 34), (0, 0, 17))
export(variants={"away": {"jersey_home": "jersey_away"}})

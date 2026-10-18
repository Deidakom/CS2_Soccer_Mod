# Recovery ice bath tub with steps.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("steel_brushed", (80, 50, 34), (0, 0, 17), bevel=4, segments=4)
box("water_blue", (72, 42, 1), (0, 0, 30))
for k in range(5):
    box("ice", (6, 6, 3), (-20 + k * 10, (k % 2) * 8 - 4, 31.5), rot=(10 * k, 20, 15 * k), bevel=0.8)
for k, z in enumerate((8, 16, 24)):
    box("rubber_black", (30, 12, 2), (-56, 0, z), bevel=0.6)
collision((80, 50, 34), (0, 0, 17))
export()

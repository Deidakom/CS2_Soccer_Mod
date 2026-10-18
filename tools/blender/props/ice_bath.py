# Recovery ice bath tub with steps.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("steel_brushed", (80, 50, 6), (0, 0, 3), bevel=1)                      # floor
for s in (-1, 1):
    box("steel_brushed", (6, 50, 34), (s * 37, 0, 17), bevel=1.2)         # sides
    box("steel_brushed", (68, 6, 34), (0, s * 22, 17), bevel=1.2)         # ends
box("water_blue", (68, 38, 1), (0, 0, 27.5))
for k in range(5):
    box("ice", (6, 6, 3), (-20 + k * 10, (k % 2) * 8 - 4, 30), rot=(6 * k, 10, 12 * k), bevel=0.8)
for k, z in enumerate((8, 16, 24)):
    box("rubber_black", (30, 12, 2), (-56, 0, z), bevel=0.6)
collision((80, 50, 34), (0, 0, 17))
export()

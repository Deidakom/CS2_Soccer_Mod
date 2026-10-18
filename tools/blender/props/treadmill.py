# Treadmill, front towards -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("steel_dark", (34, 80, 8), (0, 0, 6), bevel=1.5)
box("rubber_black", (28, 70, 1), (0, 0, 10.5))
for x in (-15, 15):
    tube("steel_brushed", [(x, -36, 8), (x, -40, 40), (x, -38, 52)], 1.3)
box("camera_body", (34, 10, 12), (0, -40, 56), rot=(-20, 0, 0), bevel=1.5)
box("ticket_glow", (16, 0.4, 6), (0, -45.4, 57), rot=(-20, 0, 0))
collision((36, 84, 58), (0, 0, 29))
export()

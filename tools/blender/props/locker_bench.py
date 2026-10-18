# Padded bench for the middle of the locker room.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("leather_dark", (160, 30, 6), (0, 0, 18), bevel=2.5, segments=3)
for x in (-64, 64):
    box("steel_brushed", (4, 26, 15), (x, 0, 7.5), bevel=0.6)
box("towel", (22, 14, 2), (40, 2, 22), bevel=0.6)
sphere("ball_white", 8.5, (-50, 0, 29.5), segments=16, rings=8)
collision((160, 30, 21), (0, 0, 10.5))
export()

# Training ball bag (net sack) full of footballs.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
import random
random.seed(3)
for x, y in ((-9, -9), (9, -9), (-9, 9), (9, 9)):
    sphere("ball_white", 8.5, (x, y, 8.5), segments=16, rings=8)
sphere("ball_white", 8.5, (0, 0, 19.8), segments=16, rings=8)
for k in range(10):
    c, s_ = math.cos(math.radians(k * 36)), math.sin(math.radians(k * 36))
    tube("net_cord", [(c * 23, s_ * 23, 2), (c * 25, s_ * 25, 14), (c * 6, s_ * 6, 46)], 0.4, resolution=4)
collision((50, 50, 40), (0, 0, 20))
export()

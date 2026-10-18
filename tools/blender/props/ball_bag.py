# Training ball bag (net sack) full of footballs.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
import random
random.seed(3)
for k in range(9):
    sphere("ball_white", 8.5, (random.uniform(-7, 7), random.uniform(-7, 7), 8 + k * 3.2), segments=16, rings=8)
for k in range(10):
    a = k * 36
    tube("net_cord", [(math.cos(math.radians(a)) * 14, math.sin(math.radians(a)) * 14, 2), (math.cos(math.radians(a)) * 15, math.sin(math.radians(a)) * 15, 24), (math.cos(math.radians(a)) * 6, math.sin(math.radians(a)) * 6, 42)], 0.4, resolution=4)
collision((30, 30, 40), (0, 0, 20))
export()

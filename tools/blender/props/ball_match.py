# 1v1 cage ball, the size of the match ball - a playable physics ball (prop_physics, sphere collision of the same radius).
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
R = 11.5
sphere("ball_white", R, (0, 0, 0), segments=24, rings=12)
for k in range(4):
    a = k * 180 / 4
    tube("rubber_black", [(R * 1.002 * math.cos(math.radians(t)) * math.cos(math.radians(a)), R * 1.002 * math.cos(math.radians(t)) * math.sin(math.radians(a)), R * 1.002 * math.sin(math.radians(t))) for t in range(0, 360, 15)], R * 0.04, closed=True, resolution=2, bevel_res=1)
export()

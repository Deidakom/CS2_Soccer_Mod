# Fire extinguisher on a wall bracket with its sign, facing -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
cyl("extinguisher_red", 4.2, 22, (0, -5, 14), verts=20, bevel=1)
sphere("extinguisher_red", 4.2, (0, -5, 25), scale=(1, 1, 0.5), segments=16, rings=6)
box("steel_dark", (4, 3, 4), (0, -5, 29), bevel=0.5)
tube("steel_dark", [(1.5, -5, 30), (5, -7, 26), (6, -8, 12)], 0.6)
box("steel_dark", (10, 1, 3), (0, -0.5, 14))
box("exit_green", (14, 0.4, 14), (0, -0.3, 44))
box("paint_white", (8, 0.3, 8), (0, -0.6, 44))
collision((10, 10, 32), (0, -5, 16))
export()

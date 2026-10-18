# Lit emergency exit sign, facing -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("paint_white", (44, 4, 16), (0, -2, 0), bevel=0.6)
box("exit_green", (40, 0.3, 12), (0, -4.2, 0))
text("paint_white", "EXIT", 7, (4, -4.5, -0.5), depth=0.2)
box("paint_white", (6, 0.3, 8), (-13, -4.5, 0))
export()

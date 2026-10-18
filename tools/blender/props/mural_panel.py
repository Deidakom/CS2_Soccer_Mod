# Light box with the crowd mural for the players' tunnel walls, facing -Y, back on the wall.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("frame_alu", (130, 3, 46), (0, -1.5, 0), bevel=0.6)
box("art_mural", (126, 0.4, 42), (0, -3.2, 0))
export()

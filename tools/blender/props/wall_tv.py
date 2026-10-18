# Flat screen on the wall showing the stadium picture, facing -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("camera_body", (100, 3, 58), (0, -2, 0), bevel=0.8)
box("art_stadium", (96, 0.4, 54), (0, -3.7, 0))
box("steel_dark", (20, 2, 12), (0, -0.5, 0))
export()

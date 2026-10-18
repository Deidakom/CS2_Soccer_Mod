# Stadium map pylon near a gate, map on the -Y side.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("steel_dark", (60, 16, 180), (0, 0, 90), bevel=2)
box("stadium_map", (52, 1, 110), (0, -8.2, 100))
box("tv_blue", (60, 17, 30), (0, 0, 170), bevel=2)
text("sign_letters", "INFO", 12, (0, -9.2, 170), depth=0.6)
collision((60, 16, 186), (0, 0, 93))
PREVIEW_COLOURS.update(steel_dark=(0.1, 0.1, 0.1), stadium_map=(0.8, 0.8, 0.75), tv_blue=(0.05, 0.15, 0.5), sign_letters=(1, 0.85, 0.1))
export()

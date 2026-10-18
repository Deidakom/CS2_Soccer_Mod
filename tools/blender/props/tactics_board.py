# Tactics board on an easel, board facing -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("steel_brushed", (84, 3, 56), (0, 0, 70), bevel=0.8)
box("tactics_board", (80, 1, 52), (0, -1.6, 70))
for x in (-36, 36):
    box("steel_dark", (3, 3, 100), (x, 4, 48), rot=(-8, 0, 0))
box("steel_dark", (3, 3, 104), (0, 22, 46), rot=(18, 0, 0))
box("steel_brushed", (84, 5, 2), (0, -2, 43))
collision((86, 30, 100), (0, 8, 50))
PREVIEW_COLOURS.update(steel_brushed=(0.55, 0.56, 0.58), steel_dark=(0.1, 0.1, 0.1), tactics_board=(0.2, 0.55, 0.25))
export()

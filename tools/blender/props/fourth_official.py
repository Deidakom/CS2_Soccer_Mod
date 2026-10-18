# Fourth official's table with the electronic substitution board, between the dugouts, front -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("steel_dark", (80, 40, 3), (0, 0, 30), bevel=0.8)
for x in (-36, 36):
    for y in (-16, 16):
        cyl("steel_dark", 1, 30, (x, y, 15), verts=6)
box("sub_board", (30, 4, 22), (-20, 0, 45), bevel=1)
box("screen_led", (26, 0.6, 16), (-20, -2.2, 45))
box("leather_dark", (24, 18, 2), (24, 0, 32), bevel=0.6)
for k in range(2):
    box("camera_body", (6, 10, 3), (10 + k * 10, -4, 33), bevel=0.6)
collision((80, 40, 50), (0, 0, 25))
PREVIEW_COLOURS.update(steel_dark=(0.1, 0.1, 0.1), sub_board=(0.15, 0.15, 0.16), screen_led=(0.1, 0.9, 0.2), leather_dark=(0.08, 0.1, 0.18), camera_body=(0.15, 0.15, 0.16))
export()

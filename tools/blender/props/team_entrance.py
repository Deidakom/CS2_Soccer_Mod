# Closed team entrance in the west gate: steel double doors in a lit frame, canopy, sign. Front -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
for s in (-1, 1):
    box("door_steel", (168, 5, 148), (s * 86, 0, 76), bevel=1)
    for k in range(5):
        box("steel_dark", (160, 1.4, 2), (s * 86, -3, 18 + k * 28))
    box("steel_brushed", (3, 3, 40), (s * 8, -4, 80), bevel=1)
box("steel_dark", (360, 10, 8), (0, 0, 154), bevel=1)
for s in (-1, 1):
    box("steel_dark", (10, 10, 158), (s * 178, 0, 79), bevel=1)
box("paint_white", (420, 120, 10), (0, -50, 210), bevel=3)
box("ceiling_light", (380, 90, 1), (0, -50, 204.5))
for s in (-1, 1):
    box("steel_dark", (12, 12, 210), (s * 196, -100, 105), bevel=2)
box("sign_back", (300, 10, 44), (0, -112, 240), bevel=3)
text("sign_letters", "TEAM ENTRANCE", 26, (0, -117.5, 240), depth=2)
box("door_steel", (60, 2, 14), (0, -4, 120))
text("sign_letters", "PLAYERS ONLY", 7, (0, -5.4, 120), depth=0.4)
collision((360, 12, 160), (0, 0, 80))
for s in (-1, 1):
    collision((12, 12, 210), (s * 196, -100, 105))
PREVIEW_COLOURS.update(door_steel=(0.3, 0.32, 0.36), steel_dark=(0.1, 0.1, 0.1), steel_brushed=(0.55, 0.56, 0.58), paint_white=(0.85, 0.85, 0.85),
                       ceiling_light=(1, 1, 0.9), sign_back=(0.05, 0.05, 0.08), sign_letters=(1, 0.85, 0.1))
export()

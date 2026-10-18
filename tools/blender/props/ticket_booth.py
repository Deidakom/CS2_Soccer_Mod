# Ticket booth beside a gate: glass windows on the -Y side, TICKETS sign.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("paint_blue", (120, 90, 110), (0, 0, 55), bevel=2)
box("paint_white", (130, 100, 8), (0, 0, 114), bevel=2)
for x in (-30, 30):
    box("shop_glass", (44, 2, 46), (x, -45, 70))
    box("steel_brushed", (50, 10, 2), (x, -50, 46), bevel=0.5)
    box("steel_dark", (4, 1, 8), (x, -46.5, 54))
box("sign_back", (110, 8, 24), (0, -20, 130), bevel=2)
text("sign_letters", "TICKETS", 16, (0, -24.5, 130), depth=1.5)
collision((122, 92, 118), (0, 0, 59))
PREVIEW_COLOURS.update(paint_blue=(0.05, 0.15, 0.5), paint_white=(0.85, 0.85, 0.85), shop_glass=(0.5, 0.65, 0.75), sign_back=(0.05, 0.05, 0.08), sign_letters=(1, 0.85, 0.1))
export()

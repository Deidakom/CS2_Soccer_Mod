# Over the players' tunnel exit: THIS IS SOCCERMOD ARENA, hung from the ceiling, front towards -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("sign_back", (150, 4, 26), (0, 0, 0), bevel=1.5)
text("sign_letters", "THIS IS", 7, (0, -2.6, 6), depth=0.8)
text("sign_letters", "SOCCERMOD ARENA", 9, (0, -2.6, -5), depth=0.8)
for x in (-60, 60):
    cyl("steel_dark", 0.6, 14, (x, 0, 20), verts=6)
PREVIEW_COLOURS.update(sign_back=(0.05, 0.05, 0.08), sign_letters=(1, 0.85, 0.1), steel_dark=(0.1, 0.1, 0.1))
export()

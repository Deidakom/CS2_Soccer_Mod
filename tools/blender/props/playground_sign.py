# Playground signpost (the word is set per variant by a material: one sign texture each is too much,
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
# so the board stays generic: PLAYGROUND with a ball and arrows).
cyl("steel_dark", 2.4, 110, (0, 0, 55), verts=12)
box("sign_back", (90, 4, 34), (0, 0, 118), bevel=2)
text("sign_letters", "PLAYGROUND", 11, (0, -2.6, 121), depth=0.6)
sphere("ball_white", 5, (-34, -3, 110), segments=12, rings=6)
text("paint_white", "> > >", 6, (14, -2.6, 109), depth=0.4)
collision((8, 8, 136), (0, 0, 68))
export()

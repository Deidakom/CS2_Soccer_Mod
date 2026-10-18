# Interview backdrop (step-and-repeat) on feet with a little podium and two microphones, front -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("frame_alu", (324, 6, 174), (0, 0, 98), bevel=1)
box("art_mediawall", (318, 0.4, 168), (0, -3.2, 98))
for x in (-150, 150):
    box("steel_dark", (6, 60, 4), (x, 0, 2), bevel=1)
box("steel_dark", (60, 30, 4), (0, -60, 2), bevel=1)
for x in (-14, 14):
    cyl("steel_dark", 0.7, 52, (x, -60, 28), verts=8)
    cyl("steel_dark", 2.2, 7, (x, -63, 56), rot=(70, 0, 0), verts=12)
    sphere("leather_dark", 2.6, (x, -66, 58), segments=10, rings=6)
collision((324, 8, 186), (0, 0, 93))
export()

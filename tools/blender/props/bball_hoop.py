# Basketball hoop: padded pole, glass backboard, rim (top at 3.05 m = 119 units) and a net; rim faces -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
cyl("paint_blue", 5, 150, (0, 40, 75), verts=20)
box("paint_blue", (8, 46, 8), (0, 18, 146), bevel=1)
box("shop_glass", (72, 2, 42), (0, -6, 140))
box("paint_white", (74, 2.4, 2), (0, -6, 161), bevel=0.3)
box("paint_white", (74, 2.4, 2), (0, -6, 119), bevel=0.3)
for x in (-36, 36):
    box("paint_white", (2, 2.4, 42), (x, -6, 140), bevel=0.3)
box("paint_white", (24, 2.6, 1.2), (0, -6.4, 133))
box("paint_white", (24, 2.6, 1.2), (0, -6.4, 124))
for x in (-12, 12):
    box("paint_white", (1.2, 2.6, 10), (x, -6.4, 128.5))
cyl("pad_red", 7, 60, (0, 40, 30), verts=20)
pts = [(9.5 * math.cos(math.radians(a)), -18 + 9.5 * math.sin(math.radians(a)), 119) for a in range(0, 360, 20)]
tube("cone_orange", pts, 0.7, closed=True, resolution=2, bevel_res=1)
box("cone_orange", (4, 8, 1.2), (0, -9, 119))
for k in range(12):
    a = math.radians(k * 30)
    tube("net_cord", [(9.5 * math.cos(a), -18 + 9.5 * math.sin(a), 119), (6 * math.cos(a + 0.3), -18 + 6 * math.sin(a + 0.3), 106)], 0.35, resolution=2, bevel_res=1)
collision((76, 4, 46), (0, -6, 140))
collision((12, 12, 150), (0, 40, 75))
export()

# Food truck with a serving hatch and awning on the -Y side, front towards +X. Variants blue, green.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
L, W = 300, 92
profile("truck_paint", [(-L / 2, 18), (L / 2 - 60, 18), (L / 2, 22), (L / 2 + 2, 70), (L / 2 - 40, 120), (L / 2 - 60, 150), (-L / 2, 150)], W, bevel=6, segments=3)
box("car_glass", (4, W - 16, 38), (L / 2 - 26, 0, 100), rot=(0, -38, 0))
box("steel_dark", (130, 2, 60), (-30, -W / 2 - 0.5, 96))
box("wood_counter", (140, 16, 3), (-30, -W / 2 - 8, 66), bevel=0.6)
for k in range(8):
    box("awning_red" if k % 2 == 0 else "awning_white", (18.5, 40, 1.2), (-95 + k * 18.5, -W / 2 - 18, 132), rot=(-20, 0, 0))
box("sign_back", (150, 6, 22), (-30, -W / 2 - 1, 140))
text("sign_letters", "STREET FOOD", 12, (-30, -W / 2 - 4.5, 140), depth=1)
for x in (L / 2 - 50, -L / 2 + 60):
    for s in (-1, 1):
        cyl("car_tyre", 17, 14, (x, s * (W / 2 - 8), 17), rot=(90, 0, 0), verts=28, bevel=2)
        cyl("car_rim", 10, 14.6, (x, s * (W / 2 - 8), 17), rot=(90, 0, 0), verts=20)
box("car_headlight", (2, 14, 7), (L / 2 + 1.5, 28, 40), bevel=1)
box("car_headlight", (2, 14, 7), (L / 2 + 1.5, -28, 40), bevel=1)
cyl("steel_brushed", 6, 30, (-120, 20, 165), verts=12)
collision((L + 4, W + 4, 152), (0, 0, 76))
PREVIEW_COLOURS.update(truck_paint=(0.8, 0.5, 0.05), car_glass=(0.04, 0.06, 0.09), steel_dark=(0.1, 0.1, 0.1), wood_counter=(0.45, 0.26, 0.12),
                       awning_red=(0.7, 0.03, 0.03), awning_white=(0.9, 0.9, 0.9), sign_back=(0.05, 0.05, 0.08), sign_letters=(1, 0.85, 0.1),
                       car_tyre=(0.03, 0.03, 0.03), car_rim=(0.7, 0.7, 0.72), car_headlight=(1, 1, 0.9), steel_brushed=(0.55, 0.56, 0.58))
export(variants={"blue": {"truck_paint": "paint_blue"}, "green": {"truck_paint": "paint_green"}})

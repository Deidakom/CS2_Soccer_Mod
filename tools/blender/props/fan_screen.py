# Fan zone stage with a big screen on a truss frame, screen facing -Y. 860 wide.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("stage_deck", (900, 260, 40), (0, 60, 20), bevel=2)
for k in range(5):
    box("stage_step", (160, 24, 8 + k * 8), (0, -80 - k * 0 - 12 * (4 - k), (8 + k * 8) / 2))
box("screen_led", (720, 4, 400), (0, 40, 300))
box("steel_dark", (740, 10, 420), (0, 46, 300), bevel=2)
for s in (-1, 1):
    for y in (20, 120):
        cyl("truss_steel", 4, 560, (s * 410, y, 320), verts=10)
    for k in range(10):
        z = 60 + k * 54
        cyl("truss_steel", 1.6, 100, (s * 410, 70, z), rot=(90, 0, 0), verts=6)
        cyl("truss_steel", 1.6, 112, (s * 410, 70, z + 27), rot=(90 - 62, 0, 0), verts=6)
for y in (20, 120):
    cyl("truss_steel", 4, 830, (0, y, 600), rot=(0, 90, 0), verts=10)
for s in (-1, 1):
    for k in range(4):
        box("steel_dark", (30, 34, 70), (s * (270 + k * 30), -10, 75), bevel=2)
for k in range(6):
    cyl("steel_dark", 6, 14, (-300 + k * 120, 10, 590), rot=(70, 0, 0), verts=16)
    cyl("ceiling_light", 5.4, 1, (-300 + k * 120, 4, 588), rot=(70, 0, 0), verts=16)
box("sign_back", (500, 10, 50), (0, 30, 545), bevel=3)
text("sign_letters", "SOCCERMOD FAN ZONE", 30, (0, 24.5, 545), depth=2)
collision((900, 260, 40), (0, 60, 20))
collision((760, 30, 600), (0, 60, 330))
PREVIEW_COLOURS.update(stage_deck=(0.12, 0.12, 0.13), stage_step=(0.2, 0.2, 0.22), screen_led=(0.2, 0.3, 0.6), steel_dark=(0.1, 0.1, 0.1),
                       truss_steel=(0.75, 0.76, 0.78), ceiling_light=(1, 1, 0.9), sign_back=(0.05, 0.05, 0.08), sign_letters=(1, 0.85, 0.1))
export()

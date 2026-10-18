# Team coach (home livery; variant away), 470 long, front towards +X.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
L, W, Hh = 470, 98, 150
profile("team_paint", [(-L / 2, 14), (L / 2 - 6, 14), (L / 2, 24), (L / 2 + 2, 110), (L / 2 - 6, Hh), (-L / 2 + 4, Hh), (-L / 2, Hh - 6)], W, bevel=6, segments=3)
box("car_glass", (L - 70, W + 1, 48), (-20, 0, 100))
box("car_glass", (4, W - 10, 70), (L / 2 + 1, 0, 92), rot=(0, -8, 0))
box("bus_white", (L - 30, W + 1.4, 16), (-10, 0, 60))
for s in (-1, 1):
    t = text("bus_white", "SOCCERMOD", 22, (-40, s * (W / 2 + 1), 40), rot=(90, 0, 0 if s < 0 else 180), depth=0.6)
for x in (L / 2 - 70, -L / 2 + 70, -L / 2 + 140):
    for s in (-1, 1):
        cyl("car_tyre", 20, 16, (x, s * (W / 2 - 8), 20), rot=(90, 0, 0), verts=28, bevel=2)
        cyl("car_rim", 12, 16.6, (x, s * (W / 2 - 8), 20), rot=(90, 0, 0), verts=20)
for s in (-1, 1):
    box("car_headlight", (2, 18, 7), (L / 2 + 2.5, s * 34, 36), bevel=1)
    box("car_taillight", (2, 10, 28), (-L / 2 - 1, s * 40, 60), bevel=1)
    box("team_paint", (10, 6, 10), (L / 2 - 10, s * (W / 2 + 4), 110), bevel=2)
box("car_trim", (4, W - 10, 14), (L / 2 + 2, 0, 20), bevel=2)
box("car_plate", (1, 26, 6), (L / 2 + 4.5, 0, 26))
collision((L + 6, W + 4, Hh), (0, 0, Hh / 2))
PREVIEW_COLOURS.update(team_paint=(0.7, 0.05, 0.06), car_glass=(0.04, 0.06, 0.09), bus_white=(0.95, 0.95, 0.95), car_tyre=(0.03, 0.03, 0.03),
                       car_rim=(0.7, 0.7, 0.72), car_headlight=(1, 1, 0.9), car_taillight=(0.8, 0.05, 0.05), car_trim=(0.06, 0.06, 0.07), car_plate=(0.9, 0.9, 0.9))
export(variants={"away": {"team_paint": "team_paint_away"}})

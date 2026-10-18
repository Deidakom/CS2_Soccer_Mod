# Bike rack: three steel hoops and one parked bike.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
for k in range(3):
    tube("steel_brushed", [(-12, (k - 1) * 30, 0), (-12, (k - 1) * 30, 26), (-6, (k - 1) * 30, 32), (6, (k - 1) * 30, 32), (12, (k - 1) * 30, 26), (12, (k - 1) * 30, 0)], 1.4)
for x in (-24, 24):
    tube("car_tyre", [(x + 13 * math.cos(a / 12 * 2 * math.pi), 32, 14 + 13 * math.sin(a / 12 * 2 * math.pi)) for a in range(12)], 1.2, closed=True)
tube("team_paint", [(-24, 32, 14), (0, 32, 30), (24, 32, 14), (0, 32, 14), (-24, 32, 14)], 1.1)
tube("team_paint", [(0, 32, 30), (-6, 32, 40)], 1)
box("leather_dark", (10, 4, 2), (-7, 32, 41))
collision((30, 90, 34), (0, 0, 17))
PREVIEW_COLOURS.update(steel_brushed=(0.55, 0.56, 0.58), car_tyre=(0.03, 0.03, 0.03), team_paint=(0.7, 0.05, 0.06), leather_dark=(0.08, 0.1, 0.18))
export()

# Beer garden set: table, two benches, a parasol in team colours. Long side along X.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
box("locker_wood", (200, 50, 3), (0, 0, 30), bevel=0.8)
for s in (-1, 1):
    box("locker_wood", (200, 22, 3), (0, s * 46, 18), bevel=0.8)
    for x in (-80, 80):
        box("steel_dark", (3, 40, 3), (x, 0, 15), rot=(0, 0, 0))
        box("steel_dark", (3, 3, 18), (x, s * 46, 8))
for x in (-80, 80):
    box("steel_dark", (3, 3, 28), (x, 0, 15))
cyl("steel_brushed", 2, 110, (0, 0, 60), verts=10)
cyl("parasol_home", 110, 30, (0, 0, 120), verts=8, radius2=4)
for k in range(8):
    a = k * 45
    box("parasol_white", (4, 110, 0.6), (0, 0, 118), rot=(-15, 0, a + 22.5))
for i, m in enumerate(("beer", "beer", "foam")):
    cyl("beer", 2.6, 8, (-40 + i * 30, 6, 35.5), verts=10)
collision((200, 120, 32), (0, 0, 16))
PREVIEW_COLOURS.update(locker_wood=(0.55, 0.35, 0.18), steel_dark=(0.1, 0.1, 0.1), steel_brushed=(0.55, 0.56, 0.58), parasol_home=(0.7, 0.05, 0.06),
                       parasol_white=(0.95, 0.95, 0.95), beer=(0.9, 0.6, 0.1), foam=(0.95, 0.95, 0.9))
export()

# Plaza bench, seat facing -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
for k in range(4):
    box("locker_wood", (100, 4.5, 2), (0, -6 + k * 5, 18), bevel=0.5)
for k in range(3):
    box("locker_wood", (100, 2, 4.5), (0, 10, 24 + k * 6), rot=(-12, 0, 0), bevel=0.5)
for x in (-40, 40):
    box("steel_dark", (3, 22, 3), (x, 0, 16), bevel=0.6)
    box("steel_dark", (3, 3, 18), (x, -8, 8))
    box("steel_dark", (3, 3, 36), (x, 10, 17), rot=(-12, 0, 0))
collision((102, 24, 20), (0, 2, 10))
PREVIEW_COLOURS.update(locker_wood=(0.55, 0.35, 0.18), steel_dark=(0.1, 0.1, 0.1))
export()

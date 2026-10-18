# Beer kiosk: taps, cups, a giant foaming beer mug on the roof.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from kiosk import *
reset()

def inside(hc, front):
    for i in range(4):
        cyl("steel_brushed", 1.2, 16, (-40 + i * 14, front + 6, hc + 11), verts=10)
        box("paint_blue", (3, 3, 5), (-40 + i * 14, front + 6, hc + 21), bevel=0.5)
    for i in range(5):
        cyl("beer", 3, 10, (30 + i * 9, front + 2, hc + 5), verts=14, radius2=3.6)
        cyl("foam", 3.7, 1.6, (30 + i * 9, front + 2, hc + 10.8), verts=14)
    cyl("steel_brushed", 12, 30, (-70, front + 30, hc + 15), verts=24)

def topper(rz):
    cyl("beer", 22, 44, (0, 10, rz + 58), verts=32)
    sphere("foam", 1, (0, 10, rz + 86), scale=(27, 27, 9), segments=24, rings=8)
    for k in range(5):
        sphere("foam", 1, (-14 + k * 7, 10 - 18, rz + 80 - k % 2 * 6), scale=(7, 6, 9), segments=12, rings=6)
    tube("glass_clear", [(20, 10, rz + 76), (36, 10, rz + 74), (38, 10, rz + 58), (34, 10, rz + 46), (20, 10, rz + 44)], 3.2)
    for x in (-18, 18):
        cyl("paint_white", 3, 14, (x, 10, rz + 30), verts=12)

kiosk("COLD BEER", body="paint_blue", awning=("awning_blue", "awning_white"), topper=topper, inside=inside)
export()

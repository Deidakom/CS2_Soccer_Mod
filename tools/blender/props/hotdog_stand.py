# Hot dog kiosk: grill with sausages, condiments, giant hot dog on the roof.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from kiosk import *
reset()

def inside(hc, front):
    box("steel_dark", (90, 30, 6), (-30, front + 26, hc + 3), bevel=0.5)
    for i in range(6):
        cyl("sausage", 1.6, 11, (-66 + i * 9, front + 22, hc + 7.2), rot=(90, 0, 0), verts=10)
    for x in (34, 52):
        cyl("steel_brushed", 7, 14, (x, front + 26, hc + 7), verts=20)
    for i, m in enumerate(("bottle_yellow", "bottle_red", "bottle_yellow")):
        cyl(m, 2, 9, (60 + i * 6, front + 2, hc + 7.5), verts=12, radius2=1.5)
    box("steel_brushed", (8, 6, 9), (-80, front + 2, hc + 7.5), bevel=0.6)

def topper(rz):
    sphere("bun", 1, (0, 10, rz + 46), scale=(64, 13, 11), segments=32, rings=12)
    sphere("sausage", 1, (0, 10, rz + 54), scale=(76, 7.5, 7.5), segments=32, rings=12)
    for i in range(9):
        box("mustard", (16, 2.2, 1.6), (-56 + i * 14, 10 + (3 if i % 2 else -3), rz + 61.4), rot=(0, 0, 35 if i % 2 else -35), bevel=0.6)
    for x in (-30, 30):
        cyl("paint_white", 3, 14, (x, 10, rz + 30), verts=12)

kiosk("HOT DOGS", topper=topper, inside=inside)
export()

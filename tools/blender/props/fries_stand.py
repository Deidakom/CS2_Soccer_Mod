# Fries kiosk: fryers, paper cones, a giant box of fries on the roof.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from kiosk import *
reset()

def inside(hc, front):
    for x in (-50, -20):
        box("steel_brushed", (26, 24, 12), (x, front + 26, hc + 6), bevel=0.6)
        box("fries", (20, 18, 1), (x, front + 26, hc + 12.2))
    for i in range(6):
        cyl("fries_box", 2.8, 9, (24 + i * 8, front + 3, hc + 4.5), verts=10, radius2=4)
        for k in range(3):
            box("fries", (1, 1, 6), (23 + i * 8 + k * 1.2, front + 3, hc + 10))

def topper(rz):
    cyl("fries_box", 26, 50, (0, 10, rz + 52), verts=4, radius2=36)
    import random
    random.seed(4)
    for k in range(40):
        box("fries", (4, 4, 34), (random.uniform(-26, 26), 10 + random.uniform(-14, 14), rz + 82 + random.uniform(-6, 8)), rot=(random.uniform(-14, 14), random.uniform(-14, 14), random.uniform(0, 90)))

kiosk("FRIES", body="paint_yellow", awning=("awning_yellow", "awning_red"), letters="sign_letters", topper=topper, inside=inside)
export()

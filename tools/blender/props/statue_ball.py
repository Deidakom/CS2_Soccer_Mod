# Monument: a giant bronze football on a stepped granite plinth with the stadium name, front -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
from mathutils import Vector, Euler
box("plaza_granite_dark", (260, 260, 20), (0, 0, 10), bevel=2)
box("plaza_granite_dark", (200, 200, 30), (0, 0, 35), bevel=2)
box("plaza_granite_dark", (130, 130, 110), (0, 0, 105), bevel=3)
box("bronze", (110, 1, 22), (0, -65.6, 120))
text("sign_letters", "SOCCERMOD", 13, (0, -66.4, 125), depth=0.6)
text("sign_letters", "ARENA", 9, (0, -66.4, 111), depth=0.6)
ball = sphere("bronze", 85, (0, 0, 245), segments=48, rings=24)
for k in range(12):
    a, b = k * 30, (k % 3) * 40 - 40
    sp = sphere("bronze_dark", 18, (0, 0, 0), scale=(1, 1, 0.35), segments=5, rings=3)
    v = Vector((0, 0, 84)); v.rotate(Euler((math.radians(b + 30), 0, math.radians(a))))
    sp.location = Vector((0, 0, 245)) + v
    sp.rotation_euler = v.to_track_quat("Z", "Y").to_euler()
collision((260, 260, 40), (0, 0, 20))
collision((130, 130, 160), (0, 0, 80))
collision((150, 150, 170), (0, 0, 245))
PREVIEW_COLOURS.update(plaza_granite_dark=(0.25, 0.25, 0.27), bronze=(0.55, 0.35, 0.15), bronze_dark=(0.25, 0.15, 0.07), sign_letters=(1, 0.85, 0.1))
export()

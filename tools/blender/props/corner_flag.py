# Corner flag (home red; variant away blue), no collision so the ball is never stopped.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
cyl("jersey_trim", 0.9, 60, (0, 0, 30), verts=10)
for k in range(6):
    cyl("flag_home" if k % 2 == 0 else "jersey_trim", 0.95, 10, (0, 0, 5 + k * 10), verts=10)
me = bpy.data.meshes.new("cf")
bm = bmesh.new()
cols, rows = 8, 5
vs = [[bm.verts.new((i / cols * 20, 1.2 * math.sin(i / cols * 4) * i / cols, 59 - j / rows * 14)) for i in range(cols + 1)] for j in range(rows + 1)]
for j in range(rows):
    for i in range(cols):
        bm.faces.new((vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]))
bm.to_mesh(me); bm.free()
ob = bpy.data.objects.new("cf", me); bpy.context.collection.objects.link(ob); ob.data.materials.append(mat("flag_home"))
ob.modifiers.new("t", "SOLIDIFY").thickness = 0.3
PREVIEW_COLOURS.update(jersey_trim=(0.95, 0.95, 0.95), flag_home=(0.7, 0.05, 0.06))
export(variants={"away": {"flag_home": "flag_away"}})

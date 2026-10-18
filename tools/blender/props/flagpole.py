# Flagpole with a waving team flag (home; variant away), flag along +X.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
cyl("steel_brushed", 2.2, 420, (0, 0, 210), verts=12, radius2=1.4)
sphere("steel_brushed", 3.5, (0, 0, 422), segments=10, rings=6)
cyl("concrete_planter", 10, 6, (0, 0, 3), verts=16)
me = bpy.data.meshes.new("flag")
bm = bmesh.new()
cols, rows = 16, 8
vs = [[bm.verts.new((i / cols * 120, 4 * math.sin(i / cols * 6.0) * (i / cols), 400 - j / rows * 72)) for i in range(cols + 1)] for j in range(rows + 1)]
for j in range(rows):
    for i in range(cols):
        bm.faces.new((vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]))
bm.to_mesh(me); bm.free()
ob = bpy.data.objects.new("flag", me); bpy.context.collection.objects.link(ob); ob.data.materials.append(mat("flag_home"))
ob.modifiers.new("thick", "SOLIDIFY").thickness = 0.6
collision((8, 8, 420), (0, 0, 210))
PREVIEW_COLOURS.update(steel_brushed=(0.55, 0.56, 0.58), concrete_planter=(0.6, 0.6, 0.58), flag_home=(0.7, 0.05, 0.06))
export(variants={"away": {"flag_home": "flag_away"}})

# Away team's: same as tunnel_canopy with AWAY and the away cover. Extending players' tunnel on the pitch side: arched ribs with a team-coloured cover, 280 long
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
# along Y (front -Y towards the pitch), 190 wide, 132 high. Only the feet collide.
import bmesh as _b
L, W, H = 280, 190, 132
for k in range(8):
    y = -L / 2 + k * L / 7
    pts = [(math.cos(math.radians(a)) * W / 2, y, 8 + math.sin(math.radians(a)) * (H - 8)) for a in range(0, 181, 15)]
    tube("steel_brushed", pts, 2, resolution=2, bevel_res=1)
    for s in (-1, 1):
        box("steel_dark", (8, 8, 8), (s * W / 2, y, 4), bevel=1)
me = bpy.data.meshes.new("cover")
bm = _b.new()
rows = 8
vs = [[bm.verts.new((math.cos(math.radians(a)) * (W / 2 + 1.5), -L / 2 + r * L / rows, 8 + math.sin(math.radians(a)) * (H - 6.5))) for a in range(0, 181, 10)] for r in range(rows + 1)]
for r in range(rows):
    for c in range(len(vs[0]) - 1):
        bm.faces.new((vs[r][c], vs[r][c + 1], vs[r + 1][c + 1], vs[r + 1][c]))
bm.to_mesh(me); bm.free()
ob = bpy.data.objects.new("cover", me); bpy.context.collection.objects.link(ob); ob.data.materials.append(mat("canopy_away"))
ob.modifiers.new("t", "SOLIDIFY").thickness = 1
# lettering on a small plate hung at the apex, inside the arch and clear of the front rib (it was 112 wide at
# head height: the ends sat in the cover and a rib hid the second C)
box("sign_back", (76, 3, 16), (0, -L / 2 - 3, 104), bevel=0.6)
text("paint_white", "AWAY", 14, (0, -L / 2 - 5.2, 104), depth=1.5)
for s in (-1, 1):
    collision((10, L, 10), (s * W / 2, 0, 5))
export()

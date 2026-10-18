# Team crest for the walls (home; variant away): shield with a ball and two stars, front -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
import bmesh as _bm
pts = [(-40, 40), (40, 40), (40, -5), (0, -48), (-40, -5)]
s = profile("crest_rim", [(x * 1.12, z * 1.12) for x, z in pts], 4, bevel=1.5)
s.rotation_euler = (0, 0, 0)
profile("crest_field", pts, 6, bevel=1, loc=(0, -0.5, 0))
sphere("jersey_trim", 16, (0, -4, 0), segments=24, rings=12)
for a in range(5):
    sphere("steel_dark", 4, (16 * math.cos(math.radians(90 + a * 72)) * 0.55, -16.5, 16 * math.sin(math.radians(90 + a * 72)) * 0.55), scale=(1, 0.3, 1), segments=8, rings=4)
for x in (-22, 22):
    text("crest_star", "*", 18, (x, -3.5, 28), depth=1)
PREVIEW_COLOURS.update(crest_rim=(0.9, 0.75, 0.2), crest_field=(0.7, 0.05, 0.06), jersey_trim=(0.95, 0.95, 0.95), steel_dark=(0.1, 0.1, 0.1), crest_star=(0.9, 0.75, 0.2))
export(variants={"away": {"crest_field": "crest_field_away"}})

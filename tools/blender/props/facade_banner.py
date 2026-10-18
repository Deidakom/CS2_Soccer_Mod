# Long banner hanging on the facade (home; variant away): pole, cloth with crest and text, front -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
cyl("steel_dark", 1.6, 100, (0, -6, 330), rot=(0, 90, 0), verts=8)
box("flag_home", (90, 1.2, 320), (0, -4, 168))
box("jersey_trim", (90, 1.4, 10), (0, -4.2, 318))
box("jersey_trim", (90, 1.4, 10), (0, -4.2, 18))
sphere("jersey_trim", 22, (0, -5.6, 250), scale=(1, 0.1, 1), segments=20, rings=8)
for k, ch in enumerate("SOCCERMOD"):
    text("jersey_trim", ch, 18, (0, -5.4, 200 - k * 20), depth=0.4)
for x in (-40, 40):
    cyl("steel_dark", 0.5, 16, (x, -6, 338), verts=6)
PREVIEW_COLOURS.update(steel_dark=(0.1, 0.1, 0.1), flag_home=(0.7, 0.05, 0.06), jersey_trim=(0.95, 0.95, 0.95))
export(variants={"away": {"flag_home": "flag_away"}})

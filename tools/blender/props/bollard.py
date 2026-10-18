# Steel bollard with a reflective ring.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
cyl("steel_dark", 6, 40, (0, 0, 20), verts=16, bevel=1)
sphere("steel_dark", 6, (0, 0, 40), scale=(1, 1, 0.5), segments=16, rings=6)
cyl("jersey_trim", 6.2, 3, (0, 0, 32), verts=16)
collision((12, 12, 44), (0, 0, 22))
PREVIEW_COLOURS.update(steel_dark=(0.1, 0.1, 0.1), jersey_trim=(0.95, 0.95, 0.95))
export()

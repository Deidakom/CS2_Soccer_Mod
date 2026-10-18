# art_action in a frame, wall mounted (back at y = 0), facing -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
def framed(art, w, h, frame="frame_black", depth=3, border=3, glass=True):
    box(frame, (w + 2 * border, depth, h + 2 * border), (0, -depth / 2, 0), bevel=0.8)
    box(art, (w, 0.4, h), (0, -depth - 0.2, 0))
    box("paint_white", (w - 1, 0.2, 1), (0, -depth - 0.4, -h / 2 - 0.4))
framed("art_action", 96, 64, "frame_black")
PREVIEW_COLOURS.update(frame_black=(0.1, 0.1, 0.1))
export()

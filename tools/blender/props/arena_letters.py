# The stadium name over the main gate: lit letters, 110 tall, front towards -Y.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
text("arena_letters", "SOCCERMOD ARENA", 110, (0, 0, 0), depth=12)
box("steel_dark", (1300, 6, 8), (0, 8, -66))
PREVIEW_COLOURS.update(arena_letters=(1, 1, 1), steel_dark=(0.1, 0.1, 0.1))
export()

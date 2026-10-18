# Small potted shrub for corners and desks.
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
import random
random.seed(9)
box("pot_dark", (18, 18, 16), (0, 0, 8), bevel=1.5)
for k in range(9):
    sphere("leaves", random.uniform(6, 9), (random.uniform(-5, 5), random.uniform(-5, 5), 20 + random.uniform(0, 12)), segments=10, rings=6)
collision((18, 18, 18), (0, 0, 9))
export()

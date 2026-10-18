# Gate canopy (props/gate_*.py): two pillars, a lit roof slab and the gate letter, front towards -Y.
from lib import *


def gate(letter):
    for x in (-200, 200):
        box("steel_dark", (16, 16, 220), (x, -60, 110), bevel=2)
    box("paint_white", (440, 140, 12), (0, -20, 226), bevel=3)
    box("ceiling_light", (400, 100, 1), (0, -20, 219.5))
    box("sign_back", (200, 10, 50), (0, -92, 258), bevel=3)
    text("sign_letters", f"GATE {letter}", 34, (0, -98, 258), depth=2)
    for x in (-200, 200):
        collision((16, 16, 220), (x, -60, 110))
    PREVIEW_COLOURS.update(steel_dark=(0.1, 0.1, 0.1), paint_white=(0.85, 0.85, 0.85), ceiling_light=(1, 1, 0.9), sign_back=(0.05, 0.05, 0.08), sign_letters=(1, 0.85, 0.1))
    export()

# Food and drink kiosks for the plaza and the concourse (props/*_stand.py). 192 wide, 120 deep,
# serving side towards -Y, origin on the floor at the centre. topper(roof_z) adds the roof eye-catcher.
from lib import *

W, D = 192, 120
H_COUNTER, H_ROOF = 42, 118


def kiosk(sign, body="paint_red", awning=("awning_red", "awning_white"), letters="sign_letters", topper=None, inside=None):
    box(body, (W, D, H_COUNTER - 4), (0, 0, (H_COUNTER - 4) / 2 + 4), bevel=1.5)
    box("steel_brushed", (W + 2, D + 2, 4), (0, 0, 2), bevel=0.5)
    box("steel_brushed", (W - 24, 1, 20), (0, -D / 2 - 0.5, 22), bevel=0.4)
    for x in (-W / 2 + 6, W / 2 - 6):
        for y in (-D / 2 + 4, D / 2 - 4):
            box("paint_white", (8, 8, H_ROOF - H_COUNTER), (x, y, (H_ROOF + H_COUNTER) / 2), bevel=1)
    box("wood_counter", (W + 10, 26, 3), (0, -D / 2 + 6, H_COUNTER + 1.5), bevel=0.8)
    box(body, (W, D - 30, H_ROOF - H_COUNTER), (0, 15, (H_ROOF + H_COUNTER) / 2), bevel=1)
    box("menu_board", (120, 1, 30), (0, -D / 2 + 30.6, H_ROOF - 26))
    if inside:
        inside(H_COUNTER, -D / 2)
    box("paint_white", (W + 8, D + 8, 6), (0, 0, H_ROOF + 3), bevel=1.2)
    n = 12
    for i in range(n):
        w = (W + 16) / n
        m, x = awning[i % 2], -(W + 16) / 2 + w * (i + 0.5)
        box(m, (w, 36, 1.2), (x, -D / 2 - 15, H_ROOF - 6), rot=(18, 0, 0))
        box(m, (w, 1.2, 10), (x, -D / 2 - 32, H_ROOF - 16.5))
    box("sign_back", (150, 8, 30), (0, -10, H_ROOF + 21), bevel=2)
    text(letters, sign, 20 if len(sign) <= 9 else 16, (0, -14.6, H_ROOF + 21), depth=2)
    if topper:
        topper(H_ROOF)
    collision((W + 4, D + 4, H_ROOF + 6), (0, 0, (H_ROOF + 6) / 2))


PREVIEW = dict(paint_red=(0.6, 0.04, 0.04), paint_white=(0.85, 0.85, 0.85), steel_brushed=(0.55, 0.56, 0.58), steel_dark=(0.1, 0.1, 0.1),
               wood_counter=(0.45, 0.26, 0.12), sausage=(0.55, 0.18, 0.08), bun=(0.85, 0.6, 0.3), mustard=(0.95, 0.75, 0.05),
               awning_red=(0.7, 0.03, 0.03), awning_white=(0.9, 0.9, 0.9), sign_back=(0.05, 0.05, 0.08), sign_letters=(1, 0.85, 0.1),
               menu_board=(0.1, 0.1, 0.1), bottle_red=(0.8, 0.05, 0.05), bottle_yellow=(0.95, 0.8, 0.1), paint_blue=(0.05, 0.15, 0.5),
               paint_green=(0.05, 0.35, 0.15), paint_yellow=(0.9, 0.7, 0.05), awning_blue=(0.05, 0.2, 0.6), awning_green=(0.05, 0.4, 0.15),
               awning_yellow=(0.95, 0.75, 0.1), beer=(0.9, 0.6, 0.1), foam=(0.95, 0.95, 0.9), pretzel=(0.55, 0.3, 0.1), fries=(0.95, 0.8, 0.3),
               fries_box=(0.8, 0.05, 0.05), glass_clear=(0.7, 0.8, 0.85))
PREVIEW_COLOURS.update(PREVIEW)

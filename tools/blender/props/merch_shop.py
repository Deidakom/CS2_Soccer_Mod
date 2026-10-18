# Fan shop: a walk-in pavilion, glass front towards -Y with an open door in the middle, home (red)
# and away (blue) jerseys on racks and walls, scarves, a counter at the back. Origin on the floor
# at the centre. The plugin's "Fan shop" counter spot is COUNTER (local), facing +Y from the
# customer side (tools/arena/world.mjs turns it into world coordinates for ArenaLayout.cs).
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *

reset()
W, D, H = 360, 240, 150
COUNTER = (0, 70)          # customer stands at y ~ 40, counter top at y 70..96

SHIRT = [(-0.5, 0), (0.5, 0), (0.5, 0.62), (0.84, 0.54), (0.98, 0.76), (0.56, 0.98), (0.2, 1.0), (0, 0.9),
         (-0.2, 1.0), (-0.56, 0.98), (-0.98, 0.76), (-0.84, 0.54), (-0.5, 0.62)]


def shirt(team, x, y, z, s=26, yaw=0, number=None):
    ob = profile(f"jersey_{team}", [(px * s, pz * s) for px, pz in SHIRT], 1.6, bevel=0.4, segments=1)
    ob.location = (x, y, z)
    ob.rotation_euler = (0, 0, math.radians(yaw))
    box("jersey_trim", (s * 0.4, 1.8, s * 0.06), (x, y, z + s * 0.93))
    if number:
        t = text("jersey_number", number, s * 0.42, (x, y - 1.0, z + s * 0.48), depth=0.3)
        t.rotation_euler = (math.radians(90), 0, math.radians(yaw))


# floor, back and side walls, roof, glass front with a door gap
box("shop_floor", (W, D, 2), (0, 0, 1))
box("shop_wall", (W, 8, H), (0, D / 2 - 4, H / 2))
for s in (-1, 1):
    box("shop_wall", (8, D, H), (s * (W / 2 - 4), 0, H / 2))
box("paint_white", (W + 16, D + 16, 8), (0, 0, H + 4), bevel=1.5)
box("shop_ceiling", (W - 16, D - 16, 1), (0, 0, H - 0.5))
DOOR = 90
for s in (-1, 1):
    x0, x1 = s * DOOR / 2, s * (W / 2 - 8)
    box("shop_glass", (abs(x1 - x0), 2, H - 12), ((x0 + x1) / 2, -D / 2 + 2, (H - 12) / 2 + 2))
    box("steel_dark", (4, 4, H), (x0, -D / 2 + 2, H / 2))
box("steel_dark", (DOOR, 4, 12), (0, -D / 2 + 2, H - 6))
box("steel_dark", (W, 4, 4), (0, -D / 2 + 2, 2))
# big lit sign over the front
box("sign_back", (300, 10, 36), (0, -D / 2 - 4, H + 26), bevel=2)
text("sign_letters", "FAN SHOP", 26, (0, -D / 2 - 9.5, H + 26), depth=2)
# ceiling lights
for x in (-110, 0, 110):
    for y in (-50, 40):
        box("ceiling_light", (40, 12, 1), (x, y, H - 1.2))

# jerseys on the side walls, two rows, home left, away right, with numbers
for side, team in ((-1, "home"), (1, "away")):
    for row, z in enumerate((40, 92)):
        for k in range(5):
            y = -70 + k * 34
            shirt(team, side * (W / 2 - 10), y, z, 26, yaw=90 * side, number=str(7 + k + row * 3) if (k + row) % 2 == 0 else None)
    box("steel_brushed", (2, D - 50, 2), (side * (W / 2 - 12), -10, 70))
    box("steel_brushed", (2, D - 50, 2), (side * (W / 2 - 12), -10, 122))

# free-standing round racks in the middle with shirts all round
for rx, team in ((-90, "home"), (90, "away")):
    cyl("steel_brushed", 2, 66, (rx, -20, 33), verts=10)
    cyl("steel_dark", 18, 2, (rx, -20, 1), verts=20)
    tube("steel_brushed", [(rx + 26 * math.cos(a / 8 * 2 * math.pi), -20 + 26 * math.sin(a / 8 * 2 * math.pi), 64) for a in range(8)], 0.8, closed=True)
    for k in range(8):
        a = k / 8 * 2 * math.pi
        shirt(team, rx + 27 * math.cos(a), -20 + 27 * math.sin(a), 34, 24, yaw=math.degrees(a) + 90)

# scarves hanging from a rail over the counter, striped in team colours
for k in range(10):
    team = "home" if k % 2 == 0 else "away"
    x = -90 + k * 20
    for j in range(6):
        box(f"jersey_{team}" if j % 2 == 0 else "jersey_trim", (9, 1, 7), (x, D / 2 - 30, H - 22 - j * 7))
box("steel_brushed", (220, 2, 2), (0, D / 2 - 30, H - 16))

# counter with till, folded shirts and caps
box("wood_counter", (150, 26, 4), (COUNTER[0], COUNTER[1] + 13, 42), bevel=0.8)
box("shop_wall", (146, 22, 40), (COUNTER[0], COUNTER[1] + 13, 20), bevel=1)
box("steel_dark", (14, 10, 8), (COUNTER[0] + 50, COUNTER[1] + 14, 48), bevel=1)
for k in range(4):
    box(f"jersey_{'home' if k % 2 == 0 else 'away'}", (18, 14, 3), (COUNTER[0] - 50 + k * 22, COUNTER[1] + 12, 45.5 + (k % 2) * 3), bevel=0.6)
for k in range(3):
    sphere(f"jersey_{'away' if k % 2 == 0 else 'home'}", 1, (COUNTER[0] + 20 + k * 9, COUNTER[1] + 6, 46), scale=(4.5, 4.5, 3), segments=12, rings=6)

# collision: walls, counter, racks (the inside is walkable)
collision((W, 10, H), (0, D / 2 - 4, H / 2))
for s in (-1, 1):
    collision((10, D, H), (s * (W / 2 - 4), 0, H / 2))
    collision(((W - DOOR) / 2 - 8, 6, H), (s * (DOOR / 2 + ((W - DOOR) / 2 - 8) / 2), -D / 2 + 2, H / 2))
collision((150, 28, 46), (COUNTER[0], COUNTER[1] + 13, 23))
for rx in (-90, 90):
    collision((56, 56, 66), (rx, -20, 33))

PREVIEW_COLOURS.update(shop_floor=(0.6, 0.6, 0.62), shop_wall=(0.92, 0.92, 0.9), paint_white=(0.85, 0.85, 0.85), shop_ceiling=(0.9, 0.9, 0.9),
                       shop_glass=(0.5, 0.65, 0.75), steel_dark=(0.1, 0.1, 0.1), sign_back=(0.05, 0.05, 0.08), sign_letters=(1, 0.85, 0.1),
                       ceiling_light=(1, 1, 0.95), jersey_home=(0.7, 0.05, 0.06), jersey_away=(0.08, 0.2, 0.65), jersey_trim=(0.95, 0.95, 0.95),
                       jersey_number=(1, 1, 1), steel_brushed=(0.55, 0.56, 0.58), wood_counter=(0.45, 0.26, 0.12))
export()

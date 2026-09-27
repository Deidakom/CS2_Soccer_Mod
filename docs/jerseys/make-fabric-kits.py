"""Fabric shading + shorts/socks paint for the test kits (owner 2026-09-27).

Inputs are the live kit source PNGs (content addon soccermod_jerseys_dynamic)
and the leg masks in docs/jerseys/kit-masks. Outputs go to <out>:

  body_fx_normal.png / body_fx_rough.png / body_fx_ao.png      (2048)
  legs_fx_normal.png / legs_fx_rough.png / legs_fx_ao.png      (1024)
  legs_fx_home_color.png / legs_fx_away_color.png              (1024)

Fabric: the stock tm_leet maps carry flannel/denim detail (weave, buttons,
pocket seams). A Gaussian blur keeps the large folds and removes the fine
detail; a fine knit relief and an evened-out roughness give a sports-shirt
surface. Legs: team shorts from the waist to mid-thigh with a trim hem, bare
skin over the knee, the existing socks (y >= SOCK_TOP) unchanged.

  python3 make-fabric-kits.py <src dir> <out dir>
"""
import sys
import numpy as np
from PIL import Image, ImageFilter

src, out = sys.argv[1], sys.argv[2]
SHORTS_END = 600   # leg texture row where the shorts hem ends (just above the knee; was 540)
HEM = 12           # trim band height above SHORTS_END
SOCK_TOP = 690     # socks start here, just below the knee (painted sock zone begins at 700)
SKIN = np.array([186, 134, 102], dtype=np.float32)  # matched to the forearms (was lighter)


def load(name, mode=None):
    im = Image.open(f"{src}/{name}")
    return im.convert(mode) if mode else im


def blur_l(arr, radius):
    return np.asarray(Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "L").filter(ImageFilter.GaussianBlur(radius)), dtype=np.float32)


def fabric_normal(name, radius, period, strength):
    rgb = np.asarray(load(name, "RGB"), dtype=np.float32)
    low = np.stack([blur_l(rgb[..., c], radius) for c in range(3)], -1) / 127.5 - 1.0
    h, w = low.shape[:2]
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    k = 2 * np.pi / period
    # knit relief: offset rows of small bumps (derivatives of sin*sin)
    dx = np.cos(k * x + (np.floor(y / period) % 2) * np.pi) * np.sin(k * y)
    dy = np.sin(k * x + (np.floor(y / period) % 2) * np.pi) * np.cos(k * y)
    n = np.stack([low[..., 0] + strength * dx, low[..., 1] + strength * dy, low[..., 2]], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True) + 1e-6
    return Image.fromarray(np.clip((n + 1.0) * 127.5, 0, 255).astype(np.uint8), "RGB"), dx


def fabric_rough(name, radius, knit):
    r = blur_l(np.asarray(load(name, "L"), dtype=np.float32), radius)
    even = 150 + (r - r.mean()) * 0.35 + knit * 6  # polyester: mid roughness, slight sheen variation
    return Image.fromarray(np.clip(even, 0, 255).astype(np.uint8), "L")


def soft_ao(name, radius):
    return Image.fromarray(np.clip(blur_l(np.asarray(load(name, "L"), dtype=np.float32), radius), 0, 255).astype(np.uint8), "L")


body_n, body_knit = fabric_normal("tm_leet_v2_body_variantb_normal.png", 3, 8, 0.10)  # radius 5 -> 3: medium folds back
body_n.save(f"{out}/body_fx_normal.png")
fabric_rough("tm_leet_v2_body_variantb_b613ee64_rough.png", 5, body_knit).save(f"{out}/body_fx_rough.png")
soft_ao("tm_leet_v2_body_variantb_ao.png", 3).save(f"{out}/body_fx_ao.png")

legs_n, legs_knit = fabric_normal("tm_leet_v2_lower_body_variantb_normal.png", 2, 5, 0.08)
legs_n.save(f"{out}/legs_fx_normal.png")
fabric_rough("tm_leet_v2_lower_body_variantb_f840e24d_rough.png", 3, legs_knit).save(f"{out}/legs_fx_rough.png")
legs_ao = soft_ao("tm_leet_v2_lower_body_varianta_ao.png", 2)
legs_ao.save(f"{out}/legs_fx_ao.png")
ao = np.asarray(legs_ao, dtype=np.float32) / 255.0


def paint_legs(color_name, mask_name, trim_row, out_name):
    col = np.asarray(load(color_name, "RGB"), dtype=np.float32).copy()
    mask = np.asarray(load(mask_name, "L")) > 127
    trim = col[trim_row, :, :][mask[trim_row]].mean(axis=0)  # the sock colour, reused as the shorts hem
    rows = np.arange(col.shape[0])[:, None]
    hem = mask & (rows >= SHORTS_END - HEM) & (rows < SHORTS_END)
    skin = mask & (rows >= SHORTS_END) & (rows < SOCK_TOP)
    shade = (0.72 + 0.28 * ao)[..., None]
    col[hem] = (trim * shade)[hem]
    # bare skin: only a hint of the trouser folds, or it reads as dirt
    col[skin] = (SKIN * (0.9 + 0.1 * ao)[..., None])[skin]
    # the belt (brown straps, top-left of this atlas) -> shorts colour
    shorts = np.median(col[400:460, 60:180].reshape(-1, 3), axis=0)
    col[140:268, 0:445] = (shorts * shade[140:268, 0:445])
    Image.fromarray(np.clip(col, 0, 255).astype(np.uint8), "RGB").save(f"{out}/{out_name}")


paint_legs("tm_leet_v2_lower_body_varianta_color.png", "home_legs_editable_mask.png", 850, "legs_fx_home_color.png")
paint_legs("tm_leet_v2_lower_body_variantb_color.png", "away_legs.png", 850, "legs_fx_away_color.png")
# 2026-09-27 owner: keepers get shorts and socks too
paint_legs("tm_leet_v2_lower_body_variantb_gkhome_color.png", "gkhome_legs.png", 850, "legs_fx_gkhome_color.png")
paint_legs("tm_leet_v2_lower_body_variantb_gkaway_color.png", "gkaway_legs.png", 850, "legs_fx_gkaway_color.png")

# previews: legs side by side; body normal before/after crop (torso front)
prev = Image.new("RGB", (2048 + 16, 1024), (30, 30, 30))
prev.paste(Image.open(f"{out}/legs_fx_home_color.png"), (0, 0))
prev.paste(Image.open(f"{out}/legs_fx_away_color.png"), (1024 + 16, 0))
prev.resize((1032, 512)).save(f"{out}/preview_legs.png")
before = load("tm_leet_v2_body_variantb_normal.png", "RGB").crop((300, 300, 812, 812))
after = body_n.crop((300, 300, 812, 812))
cmp = Image.new("RGB", (1040, 512), (30, 30, 30)); cmp.paste(before, (0, 0)); cmp.paste(after, (528, 0))
cmp.save(f"{out}/preview_normal.png")
print("done")


# ---- 2026-09-27 round 2 (owner "mach 1-5" + gloves) --------------------------
# Body colour maps (2048; shapes measured at 1024 and doubled):
#  - belt: the long waist strap (bottom strips) and the vertical strap/gear
#    column on the right -> shorts colour, so the belt disappears;
#  - rolled sleeve ends (sleeve panels, rows 650-716) -> trim band;
#  - the two collar tabs at the top of the front -> trim colour.
# Trim: Home dark red (owner: "Home is red, not black"), otherwise the kit's
# side-panel colour. Only shirt-coloured pixels are repainted, so numbers,
# logo and stripes stay.
def shorts_colour(legs_name):
    a = np.asarray(load(legs_name, "RGB"), dtype=np.float32)
    return np.median(a[400:460, 60:180].reshape(-1, 3), axis=0)


BODY_AO = np.asarray(load("tm_leet_v2_body_variantb_ao.png", "L"), dtype=np.float32) / 255.0


def paint_body(color_name, legs_name, trim, out_name):
    col = np.asarray(load(color_name, "RGB"), dtype=np.float32).copy()
    shirt = np.median(col[600:700, 900:1000].reshape(-1, 3), axis=0)
    shade = (0.72 + 0.28 * BODY_AO)[..., None]
    near_shirt = np.abs(col - shirt).sum(-1) < 150
    h, w = col.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    s = 2  # 1024 -> 2048
    belt = ((yy >= 925 * s) & (yy < 1015 * s) & (xx >= 15 * s) & (xx < 1005 * s)) \
        | ((xx >= 842 * s) & (yy >= 185 * s) & (yy < 840 * s) & ~near_shirt)
    cuff = (yy >= 650 * s) & (yy < 716 * s) & (((xx < 380 * s)) | ((xx >= 440 * s) & (xx < 812 * s))) & near_shirt
    collar = np.zeros_like(belt)
    for x0, y0, x1, y1 in ((58, 22, 168, 58), (298, 16, 388, 54)):
        collar |= (xx >= x0 * s) & (xx < x1 * s) & (yy >= y0 * s) & (yy < y1 * s)
    collar &= near_shirt
    col[belt] = (shorts_colour(legs_name) * shade)[belt]
    col[cuff] = (np.asarray(trim, np.float32) * shade)[cuff]
    # collar tabs NOT painted: they are shoulder straps in game (owner saw black bands on the back)
    # neckline: the dark inner-collar ovals read as a black neck in game -> inner
    # jersey fabric (shirt colour, a bit darker)
    for x0, y0, x1, y1 in ((780, 0, 980, 420), (0, 60, 170, 380), (0, 870, 170, 1090)):
        box = col[y0:y1, x0:x1]
        dark = (box.sum(-1) < 420) & (np.abs(box - shirt).sum(-1) > 60)  # ovals + their stock pattern, not the pale ring
        box[dark] = (shirt * 0.8 * shade[y0:y1, x0:x1])[dark]
    Image.fromarray(np.clip(col, 0, 255).astype(np.uint8), "RGB").save(f"{out}/{out_name}")


paint_body("tm_leet_v2_body_varianta_color.png", "tm_leet_v2_lower_body_varianta_color.png", (112, 14, 22), "body_fx_home_color.png")
paint_body("tm_leet_v2_body_variantb_color.png", "tm_leet_v2_lower_body_variantb_color.png", (250, 250, 252), "body_fx_away_color.png")
paint_body("tm_leet_v2_body_variantb_gkhome_color.png", "tm_leet_v2_lower_body_variantb_gkhome_color.png", (21, 24, 29), "body_fx_gkhome_color.png")
paint_body("tm_leet_v2_body_variantb_gkaway_color.png", "tm_leet_v2_lower_body_variantb_gkaway_color.png", (27, 89, 197), "body_fx_gkaway_color.png")

# Gloves (stock fingerless glove UV, 2048): outfield players get bare hands
# (glove shell painted skin, knuckle creases kept as light shading); keepers get
# goalkeeper gloves - white, one panel in the kit colour, a grey latex panel and
# a dark wrist strap. The fingertips belong to the hand mesh and stay skin.
g = np.asarray(load("glove_stock.png", "RGB"), dtype=np.float32)
gl = g.mean(-1)
detail = np.clip(1.0 + (gl - blur_l(gl, 6)) / 160.0, 0.85, 1.12)[..., None]
Image.fromarray(np.clip(SKIN * detail * 1.05, 0, 255).astype(np.uint8), "RGB").save(f"{out}/hands_bare_color.png")


def keeper_gloves(team, out_name):
    h, w = gl.shape
    yy, xx = np.mgrid[0:h, 0:w]
    base = np.empty((h, w, 3), np.float32); base[:] = (238, 240, 242)
    back = (yy >= 800) & (xx < 1080)          # left main panel (back of hand)
    palm = (yy >= 800) & (xx >= 1080)         # right main panel with thumb hole
    strap = yy < 800                          # wrist strap and thumb piece
    base[back] = team
    base[palm] = (170, 176, 184)
    base[strap] = (30, 33, 38)
    Image.fromarray(np.clip(base * detail, 0, 255).astype(np.uint8), "RGB").save(f"{out}/{out_name}")


keeper_gloves((103, 37, 134), "gloves_fx_gkhome_color.png")
keeper_gloves((27, 89, 197), "gloves_fx_gkaway_color.png")

prev = Image.new("RGB", (1024 * 2 + 16, 1024), (30, 30, 30))
prev.paste(Image.open(f"{out}/body_fx_home_color.png").resize((1024, 1024)), (0, 0))
prev.paste(Image.open(f"{out}/body_fx_away_color.png").resize((1024, 1024)), (1040, 0))
prev.resize((1032, 512)).save(f"{out}/preview_body.png")
gp = Image.new("RGB", (512 * 3 + 32, 512), (30, 30, 30))
for i, n in enumerate(("hands_bare_color", "gloves_fx_gkhome_color", "gloves_fx_gkaway_color")):
    gp.paste(Image.open(f"{out}/{n}.png").resize((512, 512)), (i * 528, 0))
gp.save(f"{out}/preview_gloves.png")
print("round 2 done")


# ---- 2026-09-27 round 3: keeper gloves cover the whole hand ------------------
# The fingertips and hand belong to the shared bare-arm material (bare_arm_133,
# 1024x2048: fingers at the top, back of hand left / palm right, wrist, then
# forearm). Keeper kits remap it to this texture: glove up to the wrist (back in
# the kit colour, palm grey latex), dark wrist band, forearm skin unchanged.
arm = np.asarray(load("arm_stock.png", "RGB"), dtype=np.float32)
al = arm.mean(-1)
arm_detail = np.clip(1.0 + (al - blur_l(al, 8)) / 140.0, 0.85, 1.12)[..., None]


def keeper_arm(team, out_name):
    col = arm.copy()
    h, w = al.shape
    yy, xx = np.mgrid[0:h, 0:w]
    glove = yy < 560
    band = (yy >= 560) & (yy < 660)
    back = glove & (xx < 620)
    palm = glove & (xx >= 620)
    col[back] = (np.asarray(team, np.float32) * arm_detail)[back]
    col[palm] = (np.array([170, 176, 184], np.float32) * arm_detail)[palm]
    col[band] = (np.array([30, 33, 38], np.float32) * arm_detail)[band]
    Image.fromarray(np.clip(col, 0, 255).astype(np.uint8), "RGB").save(f"{out}/{out_name}")


keeper_arm((103, 37, 134), "arm_fx_gkhome_color.png")
keeper_arm((27, 89, 197), "arm_fx_gkaway_color.png")
gp = Image.new("RGB", (512 * 4 + 48, 1024), (30, 30, 30))
for i, n in enumerate(("legs_fx_gkhome_color", "legs_fx_home_color")):
    gp.paste(Image.open(f"{out}/{n}.png").resize((512, 512)), (i * 528, 0))
for i, n in enumerate(("arm_fx_gkhome_color", "arm_fx_gkaway_color")):
    gp.paste(Image.open(f"{out}/{n}.png").resize((512, 1024)), ((i + 2) * 528, 0))
gp.save(f"{out}/preview_round3.png")
print("round 3 done")

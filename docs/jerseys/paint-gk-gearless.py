"""Paint the two goalkeeper kits for the validated gearless variant-b model.

The field-player gearless addon already uses variant b for both squads.  The
old keeper materials were painted for variants c/d, so they cannot safely be
assigned to that common mesh.  This script creates new variant-b-compatible
body and lower-body colour maps, preserving every pixel outside the authored
cloth/leg masks.  It also bakes the requested fixed number ``1`` onto the
front and back torso islands.

Run from the repository root with the Workshop Tools source material folder:

    python docs/jerseys/paint-gk-gearless.py --stock-dir \
      "E:/.../content/csgo_addons/soccermod_jerseys_gearless/materials/soccermod/kits"

The generated PNGs are review artefacts.  ``prepare-gk-gearless.ps1`` copies
them into the isolated gearless addon after checking their dimensions and
hashes.  No live addon is edited by this script.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from fixed_number_overlay import render_adidas_front, render_fixed_number


HERE = Path(__file__).resolve().parent
OUT = HERE / "refs" / "gk-gearless"
MASKS = HERE / "kit-masks" / "gk-gearless"
GEOMETRY = HERE / "kit-painter-geometry.json"


def polygon(points: list[list[int]], size: tuple[int, int]) -> Image.Image:
    image = Image.new("L", size)
    ImageDraw.Draw(image).polygon(points, fill=255)
    return image


def body_mask() -> Image.Image:
    # Variant-b cloth silhouette at half resolution.  These holes are skin or
    # the exposed lower strip, not jersey pixels, and must remain stock.
    image = polygon(
        [
            (0, 0),
            (814, 0),
            (814, 185),
            (837, 197),
            (834, 353),
            (840, 370),
            (838, 455),
            (790, 503),
            (809, 546),
            (818, 571),
            (817, 642),
            (808, 748),
            (737, 726),
            (672, 716),
            (405, 722),
            (345, 737),
            (220, 723),
            (80, 734),
            (0, 734),
        ],
        (1024, 1024),
    )
    draw = ImageDraw.Draw(image)
    for box in [(7, 50, 70, 154), (414, 6, 461, 79), (402, 84, 477, 189), (17, 451, 61, 522)]:
        draw.ellipse(box, fill=0)
    return image.resize((2048, 2048), Image.Resampling.NEAREST)


def legs_mask(geometry: dict) -> Image.Image:
    image = Image.new("L", (1024, 1024))
    draw = ImageDraw.Draw(image)
    for points in geometry["legs"]["b"]:
        draw.polygon(points, fill=255)
    for points in [
        [(0, 270), (220, 270), (224, 360), (0, 360)],
        [(230, 270), (450, 270), (454, 360), (230, 360)],
        [(460, 270), (695, 270), (700, 360), (460, 360)],
        [(700, 270), (945, 270), (950, 360), (700, 360)],
    ]:
        draw.polygon(points, fill=255)
    return image


def boots_mask(geometry: dict) -> Image.Image:
    image = Image.new("L", (1024, 1024))
    draw = ImageDraw.Draw(image)
    for points in geometry["boots"]["b"]:
        draw.polygon(points, fill=255)
    return image


def shade(image: Image.Image) -> np.ndarray:
    lum = np.asarray(image.convert("L")).astype(float)
    smooth = np.asarray(image.convert("L").filter(ImageFilter.GaussianBlur(12))).astype(float)
    return np.clip(0.96 + (smooth - 100) / 800 + (lum - smooth) / 1200, 0.80, 1.07)


def save_png(name: str, base: Image.Image, editable: Image.Image, result: np.ndarray, source: Path) -> dict:
    original = np.asarray(base.convert("RGB"))
    result = np.clip(result, 0, 255).astype("uint8")
    path = OUT / f"{name}.png"
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(result, "RGB").save(path, icc_profile=Image.open(HERE / "refs" / "home_body_color.png").info.get("icc_profile"))
    check = Image.open(path)
    check.load()
    decoded = np.asarray(check.convert("RGB"))
    editable.save(MASKS / f"{name}.png")
    outside = np.asarray(editable) == 0
    assert check.mode == "RGB"
    assert decoded.shape == original.shape
    assert np.array_equal(decoded[outside], original[outside])
    return {
        "file": str(path.relative_to(HERE)).replace("\\", "/"),
        "base": str(source).replace("\\", "/"),
        "base_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
        "dimensions": list(check.size),
        "mode": check.mode,
        "changed_pixels": int(np.any(decoded != original, axis=2).sum()),
        "outside_mask_changed_pixels": int(np.any(decoded[outside] != original[outside], axis=1).sum()),
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
    }


def paint_body(stock: Path, kit: str, primary: tuple[int, int, int], trim: tuple[int, int, int], number: tuple[int, int, int], stroke: tuple[int, int, int]) -> dict:
    source = stock / "tm_leet_v2_body_variantb_color.png"
    base = Image.open(source).convert("RGB")
    editable = body_mask()
    shade_values = shade(base)
    target = np.array(primary, dtype=float) * shade_values[:, :, None]

    geometry = json.loads(GEOMETRY.read_text(encoding="utf-8"))
    design = Image.new("L", (1024, 1024))
    draw = ImageDraw.Draw(design)
    for points in geometry["side_panels"]:
        draw.polygon(points, fill=255)
    for x, y, length, width in geometry["bars"]:
        draw.polygon([(x, y), (x + length, y - 17), (x + length + 3, y - 17 + width), (x + 3, y + width)], fill=255)
    trim_mask = np.asarray(design.resize(base.size, Image.Resampling.NEAREST)) > 0
    target[trim_mask] = np.array(trim, dtype=float) * shade_values[trim_mask, None]

    label = "Home" if kit == "gkhome_gearless" else "Away"
    number_mask, rendered = render_fixed_number(
        base.size, "1", number, stroke, label=label,
        label_colour=number, label_stroke=stroke,
    )
    logo_mask, logo_layer = render_adidas_front(base.size, (255,255,255), (20,23,27))
    editable_array = np.maximum.reduce((np.asarray(editable), np.asarray(number_mask), np.asarray(logo_mask)))
    result = np.asarray(base).copy()
    cloth = np.asarray(editable) > 0
    result[cloth] = np.clip(target[cloth], 0, 255)
    result = Image.alpha_composite(Image.fromarray(result, "RGB").convert("RGBA"), rendered).convert("RGB")
    result = Image.alpha_composite(result.convert("RGBA"), logo_layer).convert("RGB")
    return save_png(f"{kit}_body_color", base, Image.fromarray(editable_array), np.asarray(result), source)


def paint_legs(stock: Path, kit: str, upper: tuple[int, int, int], sock: tuple[int, int, int], trim: tuple[int, int, int], boot_colour: tuple[int, int, int], geometry: dict) -> dict:
    source = stock / "tm_leet_v2_lower_body_variantb_color.png"
    base = Image.open(source).convert("RGB")
    editable = legs_mask(geometry)
    boot = boots_mask(geometry)
    original = np.asarray(base).astype(float)
    lum = np.asarray(base.convert("L")).astype(float)
    light = np.clip(0.98 + (lum - 120) / 340, 0.62, 1.28)
    target = np.array(upper, dtype=float) * light[:, :, None]
    target[700:] = np.array(sock, dtype=float) * light[700:, :, None]
    target[717:731] = np.array(trim, dtype=float) * light[717:731, :, None]
    target[746:754] = np.array(trim, dtype=float) * light[746:754, :, None]
    boot_array = np.asarray(boot) > 0
    valid = boot_array & (lum > 35)
    weight = np.clip((lum[valid] - 35) / 45, 0, 1)[:, None]
    target[valid] = np.array(boot_colour, dtype=float) * np.clip(0.90 + (lum[valid] - 90) / 210, 0.58, 1.22)[:, None] * weight + original[valid] * (1 - weight)
    editable_array = np.maximum(np.asarray(editable), np.asarray(boot))
    result = np.asarray(base).copy()
    mask_array = editable_array > 0
    result[mask_array] = np.clip(target[mask_array], 0, 255)
    return save_png(f"{kit}_legs_color", base, Image.fromarray(editable_array), result, source)


def make_glove_texture(name: str, colour: tuple[int, int, int]) -> dict:
    """Create a small self-contained glove albedo for Workshop Tools.

    The stock fingerless glove albedo lives in a Valve VPK and cannot be used
    as a source dependency by the addon resource compiler.  These two simple
    albedos keep the glove material self-contained while making the requested
    colour unmistakable on both first- and third-person models.
    """
    size = (512, 512)
    image = Image.new("RGB", size, colour)
    draw = ImageDraw.Draw(image)
    dark = tuple(max(0, channel - 28) for channel in colour)
    light = tuple(min(255, channel + 24) for channel in colour)
    for offset in range(-512, 1024, 48):
        draw.line((offset, 0, offset + 512, 512), fill=dark, width=6)
        draw.line((offset + 18, 0, offset + 530, 512), fill=light, width=2)
    path = OUT / f"{name}.png"
    path.parent.mkdir(parents=True, exist_ok=True)
    reference = OUT / "gkhome_gearless_body_color.png"
    icc = Image.open(reference).info.get("icc_profile") if reference.exists() else None
    image.save(path, icc_profile=icc)
    check = Image.open(path)
    check.load()
    return {
        "file": str(path.relative_to(HERE)).replace("\\", "/"),
        "dimensions": list(check.size),
        "mode": check.mode,
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--stock-dir", type=Path, required=True)
    args = parser.parse_args()
    geometry = json.loads(GEOMETRY.read_text(encoding="utf-8"))
    MASKS.mkdir(parents=True, exist_ok=True)
    reports = []
    reports.append(paint_body(args.stock_dir, "gkhome_gearless", (111, 40, 145), (20, 23, 27), (255, 255, 255), (20, 23, 27)))
    reports.append(paint_legs(args.stock_dir, "gkhome_gearless", (20, 23, 27), (111, 40, 145), (20, 23, 27), (20, 23, 27), geometry))
    reports.append(paint_body(args.stock_dir, "gkaway_gearless", (238, 241, 245), (25, 83, 185), (20, 23, 27), (245, 245, 245)))
    reports.append(paint_legs(args.stock_dir, "gkaway_gearless", (232, 236, 243), (232, 236, 243), (25, 83, 185), (52, 194, 80), geometry))
    reports.append(make_glove_texture("gkhome_gearless_gloves_color", (245, 245, 245)))
    reports.append(make_glove_texture("gkaway_gearless_gloves_color", (25, 93, 220)))
    (OUT / "validation.json").write_text(json.dumps(reports, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(reports, indent=2))


if __name__ == "__main__":
    main()

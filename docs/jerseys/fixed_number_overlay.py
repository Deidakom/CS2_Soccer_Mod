"""Shared fixed jersey-number and jersey-mark rendering helpers."""

from pathlib import Path
from typing import Optional

from PIL import Image, ImageDraw, ImageFont


TORSO_CENTRES = ((490, 500), (1275, 500))


def _font(size: int) -> ImageFont.FreeTypeFont:
    for path in (Path("C:/Windows/Fonts/arialbd.ttf"), Path("C:/Windows/Fonts/seguisb.ttf")):
        if path.is_file():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def render_fixed_number(
    size: tuple[int, int],
    number: str,
    colour: tuple[int, int, int],
    stroke: tuple[int, int, int],
    label: Optional[str] = None,
    label_colour: Optional[tuple[int, int, int]] = None,
    label_stroke: Optional[tuple[int, int, int]] = None,
) -> tuple[Image.Image, Image.Image]:
    """Return the editable mask and RGBA layer for front/back fixed markings.

    These are the same UV islands and typography used by the validated
    goalkeeper-number painter: X+ front at u=.24 and X- back at u=.62.
    Labels, when supplied, are rendered above both numbers.
    """
    mask = Image.new("L", size)
    rendered = Image.new("RGBA", size, (0, 0, 0, 0))
    mask_draw = ImageDraw.Draw(mask)
    draw = ImageDraw.Draw(rendered)
    font = _font(300)
    for centre in TORSO_CENTRES:
        mask_draw.text(centre, number, font=font, anchor="mm", fill=255, stroke_width=12, stroke_fill=255)
        draw.text(
            centre,
            number,
            font=font,
            anchor="mm",
            fill=colour + (255,),
            stroke_width=12,
            stroke_fill=stroke + (255,),
        )
        if label:
            text_colour = label_colour or colour
            text_stroke = label_stroke or stroke
            label_centre = (centre[0], 260)
            label_font = _font(120)
            mask_draw.text(
                label_centre,
                label,
                font=label_font,
                anchor="mm",
                fill=255,
                stroke_width=10,
                stroke_fill=255,
            )
            draw.text(
                label_centre,
                label,
                font=label_font,
                anchor="mm",
                fill=text_colour + (255,),
                stroke_width=10,
                stroke_fill=text_stroke + (255,),
            )
    return mask, rendered


def render_adidas_front(
    size: tuple[int, int],
    colour: tuple[int, int, int],
    stroke: tuple[int, int, int],
) -> tuple[Image.Image, Image.Image]:
    """Return a small outlined three-stripe Adidas mark for the front island."""
    mask = Image.new("L", size)
    rendered = Image.new("RGBA", size, (0, 0, 0, 0))
    mask_draw = ImageDraw.Draw(mask)
    draw = ImageDraw.Draw(rendered)
    cx, cy = 250, 175
    bars = (
        ((cx - 100, cy + 50), (cx - 70, cy - 20), (cx - 40, cy - 20), (cx - 70, cy + 50)),
        ((cx - 50, cy + 50), (cx - 10, cy - 55), (cx + 20, cy - 55), (cx - 15, cy + 50)),
        ((cx, cy + 50), (cx + 50, cy - 90), (cx + 80, cy - 90), (cx + 40, cy + 50)),
    )
    for points in bars:
        closed = points + (points[0],)
        mask_draw.polygon(points, fill=255)
        mask_draw.line(closed, fill=255, width=10, joint="curve")
        draw.polygon(points, fill=colour + (255,))
        draw.line(closed, fill=stroke + (255,), width=10, joint="curve")
    return mask, rendered

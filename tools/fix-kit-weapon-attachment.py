"""Generate isolated player models whose held-weapon socket follows the hand.

Reuse the model's own weapon_hand_r grip transform instead of an independent
wpn bone, which can remain at the origin when not driven by the animation graph.
No geometry, material, skeleton or animation data is changed.
"""
import argparse
from pathlib import Path
import re
import shutil


def fix_attachment(text):
    blocks = list(re.finditer(r'\{\s*_class = "Attachment"\s+name = "([^"]+)"[^{}]*\}', text))
    weapon = [m for m in blocks if m[1] == "weapon"]
    hand = [m for m in blocks if m[1] == "weapon_hand_r"]
    if len(weapon) != 1 or len(hand) != 1:
        raise ValueError("Expected exactly one weapon and right-hand attachment")
    if 'parent_bone = "hand_R"' not in hand[0][0]:
        raise ValueError("Unexpected right-hand grip parent")
    if 'parent_bone = "wpn"' not in weapon[0][0]:
        raise ValueError("Unexpected weapon parent; refusing to overwrite another fix")
    replacement = hand[0][0].replace('name = "weapon_hand_r"', 'name = "weapon"', 1)
    return text[:weapon[0].start()] + replacement + text[weapon[0].end():]


def prepare(csroot):
    source = csroot / "content/csgo_addons/soccermod_jerseys_gearless"
    destination = csroot / "content/csgo_addons/soccermod_jerseys_attachment"
    if destination.exists():
        raise ValueError("Refusing to overwrite an existing candidate")
    replacements = {}
    for kit in ("home", "away", "gkhome", "gkaway"):
        relative = Path(f"models/soccermod/kits/kit_{kit}.vmdl")
        replacements[relative] = fix_attachment((source / relative).read_text())
    shutil.copytree(source, destination)
    for relative, content in replacements.items():
        (destination / relative).write_text(content, encoding="utf-8")
        print(f"{relative}: weapon -> hand_R using existing weapon_hand_r transform")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("csroot", type=Path)
    prepare(parser.parse_args().csroot)

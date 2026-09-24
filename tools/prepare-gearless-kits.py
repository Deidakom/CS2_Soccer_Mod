"""Generate an isolated home/away model candidate; never replace the live addon.

Generated model/material edits are mechanical template transforms. No image
pixels, vertex streams, skeletons or animation graphs are edited.
"""
import argparse
import hashlib
from pathlib import Path
import shutil
import subprocess


def prepare(csroot, mesh):
    original = csroot / "content/csgo_addons/soccermod_jerseys"
    candidate = csroot / "content/csgo_addons/soccermod_jerseys_gearless"
    game_candidate = csroot / "game/csgo_addons/soccermod_jerseys_gearless"
    if candidate.exists() or game_candidate.exists():
        raise ValueError("Candidate already exists; refusing to overwrite it")
    if hashlib.sha256(mesh.read_bytes()).hexdigest() != "8464000a056bb294439ab44cf6ddacf727db4c604d0f159c19fdc58c20e02f16":
        raise ValueError("Unexpected candidate mesh hash")
    for relative in ("materials/soccermod/kits", "models/soccermod/kits"):
        shutil.copytree(original / relative, candidate / relative)
    # Keep original GK models/materials in this candidate; only the two field
    # kits are changed by this operation.
    model_dir = candidate / "models/soccermod/kits"
    # resourcecompiler crashes on a valid keyvalues2_flat DMX on this build;
    # Valve's binary conversion compiles successfully. Explicit output is vital.
    subprocess.run([str(csroot / "game/bin/win64/dmxconvert.exe"), "-i", str(mesh.resolve()),
                    "-o", str(model_dir / "src/tm_leet_variantb_thirdperson_body.dmx"), "-oe", "binary"], check=True)
    away = (original / "models/soccermod/kits/kit_away.vmdl").read_text()
    (model_dir / "kit_home.vmdl").write_text(away.replace("kit_b_body.vmat", "kit_a_body.vmat")
        .replace("kit_b_lower_body.vmat", "kit_a_lower_body.vmat"), encoding="utf-8")
    # Retain replacement base b's normal/AO/roughness maps. Only its color-map
    # reference uses the existing red kit; retained clothing UVs were checked.
    material_dir = candidate / "materials/soccermod/kits"
    for part in ("body", "lower_body"):
        template = (original / f"materials/soccermod/kits/kit_b_{part}.vmat").read_text()
        color = f"tm_leet_v2_{part}_variantb_color.png"
        if template.count(color) != 1:
            raise ValueError("Unexpected color-map template")
        (material_dir / f"kit_a_{part}.vmat").write_text(template.replace(color, color.replace("variantb", "varianta")), encoding="utf-8")
    game_candidate.mkdir(parents=True)
    shutil.copyfile(csroot / "game/csgo_addons/soccermod_jerseys/addoninfo.txt", game_candidate / "addoninfo.txt")
    print(candidate)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("csroot", type=Path)
    parser.add_argument("mesh", type=Path)
    args = parser.parse_args()
    prepare(args.csroot, args.mesh)

"""Check candidate resource refs against its files and previously verified stock refs."""
import hashlib
import argparse
import json
from pathlib import Path
import re
import subprocess

root = Path(__file__).resolve().parents[1]
cli = root.parent / "cs2-soccermod/.local/tools/valve-resource-format-20.0/cli/Source2Viewer-CLI.exe"
parser = argparse.ArgumentParser()
parser.add_argument("--addon", type=Path, default=Path("E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/game/csgo_addons/soccermod_jerseys_gearless"))
parser.add_argument("--package", type=Path, default=root / "artifacts/kits-gearless-upload/3797479770_dir.vpk")
args = parser.parse_args()
addon = args.addon
old = json.loads((root / "docs/jerseys/2026-09-08-route2-package-audit.json").read_text())
stock = {ref["resource_reference"] for resource in old["resources"] for ref in resource["rerl_references"] if ref["resolution"] == "stock"}
# The self-contained goalkeeper glove material intentionally uses the engine's
# standard fallback AO/metal/normal textures.  They are shipped by CS2's base
# pak01_dir.vpk, not by the jersey Workshop package.
stock.update({
    "materials/default/default_ao_tga_559f1ac6.vtex",
    "materials/default/default_metal_tga_dbfb4d6e.vtex",
    "materials/default/default_normal_tga_7be61377.vtex",
})
files = sorted(p for folder in (addon / "models/soccermod/kits", addon / "materials/soccermod/kits") for p in folder.rglob("*_c"))
resources, missing = [], []
for path in files:
    output = subprocess.run([str(cli), "-i", str(path), "-b", "RERL"], capture_output=True, text=True, check=True).stdout
    refs = sorted(set(re.findall(r'm_pResourceName = "([^"]+)"', output)))
    resolved = []
    for ref in refs:
        local = addon / (ref + "_c")
        if ref.startswith(("materials/soccermod/kits/", "models/soccermod/kits/")) and local.is_file():
            provider = "candidate"
        elif ref in stock:
            provider = "previously_verified_stock"
        else:
            provider = "unresolved"
            missing.append(ref)
        resolved.append({"reference": ref, "provider": provider})
    resources.append({"path": path.relative_to(addon).as_posix(), "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "references": resolved})
package = args.package
report = {"candidate_only": True, "in_game_verified": False,
          "package_sha256": hashlib.sha256(package.read_bytes()).hexdigest(),
          "package_bytes": package.stat().st_size, "resources": resources,
          "unresolved": sorted(set(missing))}
(package.parent / "dependency-audit.json").write_text(json.dumps(report, indent=2))
print(json.dumps({"resources": len(resources), "unresolved": report["unresolved"],
                  "package_sha256": report["package_sha256"], "bytes": report["package_bytes"]}, indent=2))
if missing:
    raise SystemExit(1)

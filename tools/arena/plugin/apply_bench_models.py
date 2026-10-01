#!/usr/bin/env python3
"""Owner 2026-10-01: "the coach and bench are not the same model as the fans, fix that".

On the stadium the plugin spawns the bench and coach models built from the fan atlas
(tools/arena/generate-arena-bench.mjs) - but only when the Workshop item mounted on the server
carries them, so this build can go live before the item update is released (it then keeps the
old 3D figures). Exact-match edits in AtmoDugouts.cs, idempotent, with a backup.

  python3 apply_bench_models.py /root/football-build/src/server-plugin/SoccerModMvp
"""
import shutil, sys, time
from pathlib import Path

path = Path(sys.argv[1]) / "SoccerModMvpPlugin.AtmoDugouts.cs"
raw = path.read_bytes().decode("utf-8")
eol = "\r\n" if "\r\n" in raw else "\n"

EDITS = [
    (
        '        ("models/soccermod/atmo/coach_blue.vmdl", new Vector(-1330f, -340f, StadiumPitchPlaneZ)),\n'
        "    };\n",
        '        ("models/soccermod/atmo/coach_blue.vmdl", new Vector(-1330f, -340f, StadiumPitchPlaneZ)),\n'
        "    };\n"
        "    // 2026-10-01 owner: on the stadium the bench and the coach look like the fans - cut-out cards from\n"
        "    // the fan atlas (tools/arena/generate-arena-bench.mjs), same places and clips. Used when the\n"
        "    // Workshop item mounted on the server has them; otherwise the 3D figures above stay.\n"
        "    private static readonly string[] ArenaDugoutModels =\n"
        "    {\n"
        '        "models/soccermod/atmo/crowd_arena/bench_red.vmdl", "models/soccermod/atmo/crowd_arena/bench_blue.vmdl",\n'
        '        "models/soccermod/atmo/crowd_arena/coach_red.vmdl", "models/soccermod/atmo/crowd_arena/coach_blue.vmdl",\n'
        "    };\n"
        "    private bool _arenaDugoutModels;\n"
        "    private string AtmoDugoutModel(int i) => _arenaDugoutModels ? ArenaDugoutModels[i] : AtmoDugoutProps[i].Model;\n",
    ),
    (
        "            foreach (var (model, _) in AtmoDugoutProps) manifest.AddResource(model);\n",
        '            _arenaDugoutModels = ArenaLoading && MountedAddonFiles().Contains(ArenaDugoutModels[0] + "_c");\n'
        "            for (var i = 0; i < AtmoDugoutProps.Length; i++) manifest.AddResource(AtmoDugoutModel(i));\n",
    ),
    (
        '            kv.SetString("model", AtmoDugoutProps[i].Model);\n',
        '            kv.SetString("model", AtmoDugoutModel(i));\n',
    ),
]

if "ArenaDugoutModels" in raw:
    print("already applied")
    sys.exit(0)
new = raw
for old, repl in EDITS:
    old, repl = old.replace("\n", eol), repl.replace("\n", eol)
    if new.count(old) != 1:
        sys.exit(f"expected exactly one match for:\n{old}")
    new = new.replace(old, repl)
backup = Path("/root/arena-plugin") / ("backup-" + time.strftime("%Y%m%d-%H%M%S"))
backup.mkdir(parents=True, exist_ok=True)
shutil.copy2(path, backup / path.name)
path.write_bytes(new.encode("utf-8"))
print("edited", path.name, "- backup in", backup)

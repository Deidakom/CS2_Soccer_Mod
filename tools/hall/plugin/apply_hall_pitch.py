#!/usr/bin/env python3
"""SoccerMod indoor hall: pitch designs and 3D grass on ka_soccermod_indoor (owner 2026-10-01).

Run apply_hall_atmo.py first (it writes HallLayout.cs with the model names). Here: the small
switches in Grass.cs and PitchDesign.cs. The hall has its own grass tiles (12 x 16) and its own
design floors; a player with a pitch design gets the design floor without the 3D grass on top
(the hall has no design-coloured grass). Exact-match edits, idempotent, with backups.

  python3 apply_hall_pitch.py /root/football-build/src/server-plugin/SoccerModMvp
"""
import shutil, sys, time
from pathlib import Path

src = Path(sys.argv[1])
backup = Path("/root/arena-plugin") / ("backup-" + time.strftime("%Y%m%d-%H%M%S"))

EDITS = {
    "SoccerModMvpPlugin.Grass.cs": [
        (
            "        _grassBakeWanted = !File.Exists(ConfigPath(GrassFineFlagFile)) && !ArenaLoading;\n        RegisterListener",
            "        _grassBakeWanted = !File.Exists(ConfigPath(GrassFineFlagFile)) && !ArenaLoading && !HallLoading;\n        RegisterListener",
        ),
        (
            "            _grassBakeWanted = !File.Exists(ConfigPath(GrassFineFlagFile)) && !ArenaLoading;   // arena: no roof shadow on the pitch (ArenaLayout.cs)\n            _grassBakePrecached = false;\n",
            "            _grassBakeWanted = !File.Exists(ConfigPath(GrassFineFlagFile)) && !ArenaLoading && !HallLoading;   // arena / hall: no roof shadow on the pitch\n            _grassBakePrecached = false;\n"
            "            HallGrassPrecache(manifest);   // the hall's own tiles (HallLayout.cs)\n",
        ),
        (
            "            _grassFloorZ = DetectCsslPitchFloor(out var detail);\n",
            "            string detail;\n"
            "            // the hall: its own court and tiles, nothing to measure (HallLayout.cs)\n"
            "            if (OnHall) { _grassFloorZ = _hallGrassPrecached ? HallFloorZ : null; detail = \"hall tiles \" + (_hallGrassPrecached ? \"mounted\" : \"not in the mounted Workshop item\"); }\n"
            "            else _grassFloorZ = DetectCsslPitchFloor(out detail);\n",
        ),
        (
            "        var tileW = 2 * GrassHalfX / GrassTilesX;\n        var tileH = 2 * GrassHalfY / GrassTilesY;\n        for (var ty = 0; ty < GrassTilesY; ty++)\n        for (var tx = 0; tx < GrassTilesX; tx++)\n        {\n            var tile = Utilities.CreateEntityByName<CDynamicProp>(\"prop_dynamic\");\n",
            "        var (tilesX, tilesY, halfX, halfY) = OnHall ? (HallGrassTilesX, HallGrassTilesY, HallHalfX, HallHalfY) : (GrassTilesX, GrassTilesY, GrassHalfX, GrassHalfY);\n"
            "        var tileW = 2 * halfX / tilesX;\n        var tileH = 2 * halfY / tilesY;\n        for (var ty = 0; ty < tilesY; ty++)\n        for (var tx = 0; tx < tilesX; tx++)\n        {\n            var tile = Utilities.CreateEntityByName<CDynamicProp>(\"prop_dynamic\");\n",
        ),
        (
            '            keyValues.SetString("model", GrassActiveTileModel(tx, ty));\n',
            '            keyValues.SetString("model", OnHall ? HallGrassTileModel(tx, ty) : GrassActiveTileModel(tx, ty));\n',
        ),
        (
            '            keyValues.SetVector("origin", new Vector(-GrassHalfX + (tx + 0.5f) * tileW, -GrassHalfY + (ty + 0.5f) * tileH, floorZ));\n',
            '            keyValues.SetVector("origin", new Vector(-halfX + (tx + 0.5f) * tileW, -halfY + (ty + 0.5f) * tileH, floorZ));\n',
        ),
        (
            "            if (_menuParity.GrassSkin != 0 && !GrassBakeActive) tile.AcceptInput(",
            "            if (_menuParity.GrassSkin != 0 && !GrassBakeActive && !OnHall) tile.AcceptInput(",
        ),
        (
            "            if (!on || design >= 0)\n",
            "            // the hall has no design-coloured grass: with a pitch design the design floor shows without 3D grass\n"
            "            if (!on || design >= 0 || OnHall && PitchDesignOf(receiver) > 0)\n",
        ),
    ],
    "SoccerModMvpPlugin.PitchDesign.cs": [
        (
            "    private bool PitchDesignAvailable => _pitchDesignPrecached && FlagFileOn(PitchDesignFlagFile) && IsFoundationMap(_currentMapName);",
            "    private bool PitchDesignAvailable => _pitchDesignPrecached && FlagFileOn(PitchDesignFlagFile) && (IsFoundationMap(_currentMapName) || OnHall);",
        ),
        (
            "            if (ArenaLoading)\n            {\n",
            "            if (HallLoading)\n            {\n"
            "                // the indoor hall: its own design floors in the map's Workshop item (HallLayout.cs)\n"
            '                if (!MountedAddonFiles().Contains(HallPitchDesignModels[0] + "_c"))\n'
            "                {\n"
            '                    Logger.LogInformation("[SM2DIAG] pitch_design_unavailable reason=model_not_in_mounted_workshop_items model={Model}", HallPitchDesignModels[0]);\n'
            "                    return;\n"
            "                }\n"
            "                foreach (var model in HallPitchDesignModels) manifest.AddResource(model);\n"
            "                _pitchDesignPrecached = true;\n"
            "                return;\n"
            "            }\n"
            "            if (ArenaLoading)\n            {\n",
        ),
        (
            "    private int PitchDesignSkin(int i) => OnArena ? 0 : i + 4 * GrassDesignMode;",
            "    private int PitchDesignSkin(int i) => OnArena || OnHall ? 0 : i + 4 * GrassDesignMode;",
        ),
        (
            "        if (!PitchDesignAvailable || !IsFoundationMap(_currentMapName)) return;\n",
            "        if (!PitchDesignAvailable) return;\n",
        ),
        (
            '            keyValues.SetString("model", OnArena ? ArenaPitchDesignModels[skin] : PitchDesignModel);\n',
            '            keyValues.SetString("model", OnHall ? HallPitchDesignModels[skin] : OnArena ? ArenaPitchDesignModels[skin] : PitchDesignModel);\n',
        ),
    ],
}

plan = {}
for name, edits in EDITS.items():
    path = src / name
    raw = path.read_bytes().decode("utf-8")
    eol = "\r\n" if "\r\n" in raw else "\n"
    new = raw
    for old, repl in edits:
        old, repl = old.replace("\n", eol), repl.replace("\n", eol)
        if repl in new:
            continue
        if new.count(old) != 1:
            sys.exit(f"{name}: expected exactly one match for:\n{old}")
        new = new.replace(old, repl)
    if new != raw:
        plan[path] = new
for path, new in plan.items():
    backup.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, backup / path.name)
    path.write_bytes(new.encode("utf-8"))
    print("edited", path.name)
print(f"{len(plan)} file(s) changed" + (f", backups in {backup}" if plan else " (already applied)"))

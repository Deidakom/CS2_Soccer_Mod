#!/usr/bin/env python3
"""SoccerMod indoor hall: the stadium effects on ka_soccermod_indoor (owner 2026-10-01: "can you
fix all that" - the fans there did not cheer, no goal show).

On the hall the stadium director runs too: the map's own fans (prop_dynamic sm_hall_crowd_*) are
driven by the crowd module (goal clips, cheer, wave), the goal show plays in hall positions
(flares on the terraces and the stands' front rows, confetti cannons on the goal housings,
confetti from under the ceiling net, camera flashes in the stands; no fireworks under a roof),
with crowd sounds and announcer. What belongs to the big stadiums stays off there: LED boards,
light ring, waving banners, plugin bench/coach, spidercam.

Writes SoccerModMvpPlugin.HallLayout.cs and applies exact-match edits, idempotent, with backups.
  python3 apply_hall_atmo.py /root/football-build/src/server-plugin/SoccerModMvp
"""
import shutil, sys, time
from pathlib import Path

src = Path(sys.argv[1])
backup = Path("/root/arena-plugin") / ("backup-" + time.strftime("%Y%m%d-%H%M%S"))

HALL_LAYOUT = '''using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;

// soccer_indoor_hall, first called ka_soccermod_indoor (2026-10-01, owner: an own indoor hall for 3 to 4 players per team, tools/hall).
// The pitch numbers are the map profile "Hall" (MapProfiles.cs). This file is what the stadium
// effects need there: the map's own fans and where the goal show plays. Numbers from
// tools/hall/layout.mjs (floor -32; terraces behind the goals from |y| 1394, first step 30 high;
// stands' front rows at x -948 / +948, 34 high; goal housings 204 half wide, 146 high, to |y| 1366;
// ceiling net at z 460).
public sealed partial class SoccerModMvpPlugin
{
    // 2026-10-02 owner: the map is called soccer_indoor_hall. Its first Workshop revisions were
    // ka_soccermod_indoor; both names count, so a server still on an old revision keeps working.
    private const string HallMapName = "soccer_indoor_hall";
    private const string HallFirstMapName = "ka_soccermod_indoor";
    // soccer_2v2_arena (2026-10-02, owner: a small 2v2 hall with a wooden floor, tools/gym; profile "Gym").
    // The hall rules apply there too: the map's own fans cheer (sm_hall_crowd_east / _west), no stadium
    // sounds, no celebrations. It has a parquet floor: no 3D grass and no pitch designs.
    private const string GymMapName = "soccer_2v2_arena";
    private static bool IsGymMap(string? map) => string.Equals(map, GymMapName, StringComparison.OrdinalIgnoreCase);
    private static bool IsHallMap(string? map) => string.Equals(map, HallMapName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(map, HallFirstMapName, StringComparison.OrdinalIgnoreCase) || IsGymMap(map);
    private bool OnHall => IsHallMap(_currentMapName);
    private bool OnGym => IsGymMap(_currentMapName);

    // The fans are part of the map (prop_dynamic, clips idle / cheer / wave / goal_red / goal_blue).
    // Index = the crowd module's section slots: east, red end, west, blue end (the "lower" slots).
    private static readonly string?[] HallCrowdNames =
    {
        "sm_hall_crowd_east", null, "sm_hall_crowd_end_red", null, "sm_hall_crowd_west", null, "sm_hall_crowd_end_blue", null,
    };

    private static CDynamicProp? HallCrowd(string name)
    {
        foreach (var designer in new[] { "prop_dynamic", "prop_dynamic_override" })
            foreach (var prop in Utilities.FindAllEntitiesByDesignerName<CDynamicProp>(designer))
                if (prop.IsValid && prop.Entity?.Name == name) return prop;
        return null;
    }

    private static readonly (float X, float Y)[] HallStandCentres = { (1030f, 0f), (-1080f, 0f), (0f, 1500f), (0f, -1500f) };
    private const float HallFlashZ = 70f, HallRainZ = 400f;

    // flares: among the fans on the first terrace step behind a goal / in a stand's front row
    private static Vector HallEndRowSpot(float x, int fanSign) => new(Math.Clamp(x * 0.7f, -700f, 700f), fanSign * 1418f, 68f);
    private static Vector HallSideRowSpot(float sideX, float y) => new(MathF.Sign(sideX) * 966f, Math.Clamp(y, -900f, 900f), 72f);
    // confetti cannons: on the goal housing of the fans' end
    private static Vector HallCannonSpot(float sideSign, int fanSign) => new(sideSign * 170f, fanSign * 1290f, 118f);

    // ---- pitch designs and 3D grass on the hall (tools/hall/generate-hall.mjs, generate-hall-grass.mjs) ----
    private static bool HallLoading => IsHallMap(Server.MapName);
    private const float HallFloorZ = -32f, HallHalfX = 840f, HallHalfY = 1150f;
    private const int HallGrassTilesX = 12, HallGrassTilesY = 16;
    private static string HallGrassTileModel(int tx, int ty) => $"models/soccermod_hall/grass_{tx}_{ty}.vmdl";
    // one floor per design (index = design - 1), lines included
    private static readonly string[] HallPitchDesignModels =
    {
        "models/soccermod_hall/hall_design_stripes.vmdl", "models/soccermod_hall/hall_design_lengthwise.vmdl",
        "models/soccermod_hall/hall_design_diamond.vmdl", "models/soccermod_hall/hall_design_circles.vmdl",
    };
    private bool _hallGrassPrecached;

    // from the grass precache (Grass.cs): the hall tiles, when the Workshop item of the map carries them
    private void HallGrassPrecache(ResourceManifest manifest)
    {
        _hallGrassPrecached = false;
        if (!HallLoading || IsGymMap(Server.MapName) || !MountedAddonFiles().Contains(HallGrassTileModel(0, 0) + "_c")) return;
        for (var ty = 0; ty < HallGrassTilesY; ty++)
        for (var tx = 0; tx < HallGrassTilesX; tx++) manifest.AddResource(HallGrassTileModel(tx, ty));
        _hallGrassPrecached = true;
    }

    // 2026-10-02 owner: "also disable the stadium celebration stuff from the map" / "fans can celebrate
    // but no other effect". In the hall no flares, confetti, camera flashes or fireworks play (AtmoFx.cs
    // asks here for every effect); only the ball's own effects stay (trail, post sparks). The fans still
    // jump at a goal - they are the map's own entities and that is an animation.
    private static bool HallFx(ref string effect, ref Vector at) =>
        effect.Contains("ball_trail", StringComparison.Ordinal) || effect.Contains("post_sparks", StringComparison.Ordinal);
}
'''

EDITS = {
    "SoccerModMvpPlugin.Atmosphere.cs": [
        (
            "StringComparison.OrdinalIgnoreCase) || OnArena) && AtmoSet.Director;",
            "StringComparison.OrdinalIgnoreCase) || OnArena || OnHall) && AtmoSet.Director;",
        ),
    ],
    "SoccerModMvpPlugin.AtmoFx.cs": [
        (
            "    private (float X, float Y)[] AtmoStandCentres => OnArena ? ArenaStandCentres : AtmoStandCentresV8;   // ArenaLayout.cs\n",
            "    private (float X, float Y)[] AtmoStandCentres => OnHall ? HallStandCentres : OnArena ? ArenaStandCentres : AtmoStandCentresV8;   // HallLayout.cs, ArenaLayout.cs\n",
        ),
        (
            '        if (!effect.StartsWith("particles/", StringComparison.Ordinal)) effect = AtmoParticleDir + effect + ".vpcf";\n',
            '        if (!effect.StartsWith("particles/", StringComparison.Ordinal)) effect = AtmoParticleDir + effect + ".vpcf";\n'
            "        if (OnHall && !HallFx(ref effect, ref at)) return null;   // HallLayout.cs\n",
        ),
    ],
    "SoccerModMvpPlugin.ArenaLayout.cs": [
        (
            "    private Vector AtmoEndRowSpot(float x, int fanSign) => OnArena ? ",
            "    private Vector AtmoEndRowSpot(float x, int fanSign) => OnHall ? HallEndRowSpot(x, fanSign) : OnArena ? ",
        ),
        (
            "    private Vector AtmoSideRowSpot(float sideX, float y) => OnArena ? ",
            "    private Vector AtmoSideRowSpot(float sideX, float y) => OnHall ? HallSideRowSpot(sideX, y) : OnArena ? ",
        ),
        (
            "    private Vector AtmoCannonSpot(float sideSign, int fanSign) => OnArena ? ",
            "    private Vector AtmoCannonSpot(float sideSign, int fanSign) => OnHall ? HallCannonSpot(sideSign, fanSign) : OnArena ? ",
        ),
    ],
    "SoccerModMvpPlugin.AtmoCrowd.cs": [
        (
            "        for (var i = 0; i < AtmoCrowdSections.Length; i++)\n        {\n            if (!AtmoCrowdSectionWanted(i))\n",
            "        for (var i = 0; i < AtmoCrowdSections.Length; i++)\n        {\n"
            "            // the indoor hall: the fans are the map's own entities - take them over, never spawn or remove (HallLayout.cs)\n"
            "            if (OnHall) { _atmoCrowds[i] = AtmoOn && AtmoSet.Crowd && HallCrowdNames[i] is { } hallName ? HallCrowd(hallName) : null; continue; }\n"
            "            if (!AtmoCrowdSectionWanted(i))\n",
        ),
    ],
    "SoccerModMvpPlugin.AtmoBanners.cs": [
        ("    private bool AtmoBannersWanted => AtmoOn && AtmoSet.WavingBanners;", "    private bool AtmoBannersWanted => AtmoOn && !OnHall && AtmoSet.WavingBanners;"),
    ],
    "SoccerModMvpPlugin.AtmoBoards.cs": [
        ("    private bool AtmoBoardsWanted => AtmoOn && AtmoSet.LedBoards;", "    private bool AtmoBoardsWanted => AtmoOn && !OnHall && AtmoSet.LedBoards;"),
    ],
    "SoccerModMvpPlugin.AtmoDugouts.cs": [
        ("    private bool AtmoDugoutsWanted => AtmoOn && AtmoSet.Dugouts;", "    private bool AtmoDugoutsWanted => AtmoOn && !OnHall && AtmoSet.Dugouts;"),
    ],
    "SoccerModMvpPlugin.AtmoRing.cs": [
        ("    private bool AtmoRingWanted => AtmoOn && AtmoSet.LightRing;", "    private bool AtmoRingWanted => AtmoOn && !OnHall && AtmoSet.LightRing;"),
    ],
    "SoccerModMvpPlugin.AtmoSpider.cs": [
        ("        var want = AtmoOn && AtmoSet.Spidercam;", "        var want = AtmoOn && !OnHall && AtmoSet.Spidercam;"),
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
layout = src / "SoccerModMvpPlugin.HallLayout.cs"
if not layout.exists() or layout.read_text(encoding="utf-8") != HALL_LAYOUT:
    plan[layout] = HALL_LAYOUT
for path, new in plan.items():
    backup.mkdir(parents=True, exist_ok=True)
    if path.exists():
        shutil.copy2(path, backup / path.name)
    path.write_bytes(new.encode("utf-8"))
    print("wrote", path.name)
print(f"{len(plan)} file(s) changed" + (f", backups in {backup}" if plan else " (already applied)"))

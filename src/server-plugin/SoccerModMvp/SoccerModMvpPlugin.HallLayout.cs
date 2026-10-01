using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;

// ka_soccermod_indoor (2026-10-01, owner: an own indoor hall for 3 to 4 players per team, tools/hall).
// The pitch numbers are the map profile "Hall" (MapProfiles.cs). This file is what the stadium
// effects need there: the map's own fans and where the goal show plays. Numbers from
// tools/hall/layout.mjs (floor -32; terraces behind the goals from |y| 1394, first step 30 high;
// stands' front rows at x -948 / +948, 34 high; goal housings 204 half wide, 146 high, to |y| 1366;
// ceiling net at z 460).
public sealed partial class SoccerModMvpPlugin
{
    private const string HallMapName = "ka_soccermod_indoor";
    private static bool IsHallMap(string? map) => string.Equals(map, HallMapName, StringComparison.OrdinalIgnoreCase);
    private bool OnHall => IsHallMap(_currentMapName);

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
        if (!HallLoading || !MountedAddonFiles().Contains(HallGrassTileModel(0, 0) + "_c")) return;
        for (var ty = 0; ty < HallGrassTilesY; ty++)
        for (var tx = 0; tx < HallGrassTilesX; tx++) manifest.AddResource(HallGrassTileModel(tx, ty));
        _hallGrassPrecached = true;
    }

    // The goal show's effects were placed for the big stadiums; in the hall: no fireworks under the
    // roof, confetti falls from under the ceiling net over the court, camera flashes at seat height.
    private bool HallFx(ref string effect, ref Vector at)
    {
        if (effect.Contains("firework", StringComparison.Ordinal)) return false;
        if (effect.Contains("confetti_rain", StringComparison.Ordinal)) at = new Vector(at.X * 0.5f, at.Y * 0.5f, HallRainZ);
        else if (effect.Contains("camera_flashes", StringComparison.Ordinal)) at = new Vector(at.X, at.Y, HallFlashZ);
        return true;
    }
}

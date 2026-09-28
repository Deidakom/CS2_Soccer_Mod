namespace SoccerModMvp;

// Map profiles (2026-09-27, test server): maps other than the CSSL v8
// stadium. A profile names the map's ball and describes each pitch as a
// frame; the plugin works in pitch-local coordinates (centre at 0,0, floor at
// the v8 floor height) so the goal, kickoff and keeper-box code written for
// v8 runs unchanged. Numbers for soccer_multi_indoor were measured from the
// decompiled map (goal models, goal triggers, pitch lines, grass brushes).
internal sealed record PitchFrame(
    string Mode,
    string Label,
    float CenterX,
    float CenterY,
    float FloorZ,
    // Distance from the centre spot to each goal line along Y.
    float GoalLineY,
    float GoalHalfWidth,
    // Goal mouth height above the floor.
    float GoalHeight,
    bool HasGoals,
    // The pitch's area in world coordinates (used to find the active pitch).
    float MinX, float MaxX, float MinY, float MaxY,
    // func_button the map uses to switch to this pitch.
    string Button,
    // Goals built like soccer_cssl_stadium_v8's (posts, crossbar, net shape):
    // the plugin adds the v8 goal frame, moving net and net pocket there.
    bool V8Goals = false)
{
    internal bool Contains(float x, float y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
}

internal sealed record MapProfile(
    string MapName,
    string[] BallNames,
    string BallDesignerName,
    float BallRadius,
    string DefaultMode,
    PitchFrame[] Frames,
    // Removed at every round start: the control room's way in and the
    // freeplay ball spawner (owner: pitch size only via !menu, training balls).
    string[] RemoveEntityNames)
{
    internal PitchFrame? Frame(string mode) => Frames.FirstOrDefault(f => f.Mode == mode);
    internal PitchFrame? FrameAt(float x, float y) => Frames.FirstOrDefault(f => f.Contains(x, y));
}

internal static class MapProfiles
{
    // v8 floor; pitch-local coordinates put every frame's floor here.
    internal const float LocalFloorZ = -31.997691f;

    internal static readonly MapProfile MultiIndoor = new(
        "soccer_multi_indoor",
        // v3 (2026-09-28): the v8 ball entity (prop_physics_multiplayer
        // "filter_ball", Jabulani) - BindBall takes it over as on v8.
        new[] { "filter_ball" },
        "prop_physics_multiplayer",
        18.805f, // = DefaultBallCollisionRadius: the Jabulani, scale 1
        "2v2",
        // 2026-09-28 rework (tools/port/multiindoor-rework.mjs): freeplay removed;
        // the bounds are the inner faces of the new walls (run-off beside the
        // touchlines and behind the goals, v8 style). v3: 3v3 and 4v4 have the
        // v8 goal (mouth +-124 x 97 between posts and crossbar).
        new PitchFrame[]
        {
            new("2v2", "2v2 (small)", 3152.0f, 3492.5f, -4.0f, 717.5f, 78.0f, 62.0f, true, 2446, 3858, 2582.5f, 4402.5f, "2v2button"),
            new("3v3", "3v3", -3152.0f, 3492.0f, -4.0f, 1146.0f, 124.0f, 97.0f, true, -4274, -2030, 2090, 4894, "3v3button", V8Goals: true),
            new("4v4", "4v4 (big)", -3152.0f, -3492.0f, -4.0f, 1276.0f, 124.0f, 97.0f, true, -4404, -1900, -5024, -1960, "4v4button", V8Goals: true),
        },
        new[]
        {
            "controlport", "controlportfree", "controlbutton", "controlbuttonfree", "controlbutton_timer",
            "keypadmaker2v2", "keypadmaker3v3", "keypadmaker4v4", "keypadmakerfree", "keypadtemplate", "keypadtemplatefree",
            "spawnerbutton", "removebutton", "spawnremovesign", "buttonmaker", "buttontemplate",
            "spawner1", "spawner2", "ball_spawner", "ballcounter",
        });

    internal static readonly MapProfile[] All = { MultiIndoor };

    internal static MapProfile? For(string? mapName) =>
        mapName is null ? null : All.FirstOrDefault(p => string.Equals(p.MapName, mapName, StringComparison.OrdinalIgnoreCase));

    // World -> pitch-local and back (x/y about the pitch centre, z shifted so
    // the pitch floor sits at the v8 floor height).
    internal static (float X, float Y, float Z) ToLocal(PitchFrame f, float x, float y, float z) =>
        (x - f.CenterX, y - f.CenterY, z - (f.FloorZ - LocalFloorZ));

    internal static (float X, float Y, float Z) ToWorld(PitchFrame f, float x, float y, float z) =>
        (x + f.CenterX, y + f.CenterY, z + (f.FloorZ - LocalFloorZ));
}

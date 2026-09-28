using SoccerModMvp;

internal static class MapProfileChecks
{
    internal static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var p = MapProfiles.For("SOCCER_MULTI_INDOOR");
        Check(p is not null && MapProfiles.For("soccer_cssl_stadium_v8") is null, "Profiles are looked up by map name, case-insensitive; v8 has none.");
        Check(p!.Frames.Select(f => f.Mode).SequenceEqual(new[] { "2v2", "3v3", "4v4" }) && p.DefaultMode == "2v2", "Three pitches (freeplay removed), 2v2 by default.");
        foreach (var f in p.Frames)
        {
            var (lx, ly, lz) = MapProfiles.ToLocal(f, f.CenterX + 10, f.CenterY - 20, f.FloorZ + 5);
            Check(MathF.Abs(lx - 10) < 1e-3 && MathF.Abs(ly + 20) < 1e-3 && MathF.Abs(lz - (MapProfiles.LocalFloorZ + 5)) < 1e-3, $"{f.Mode}: local coordinates are centred on the pitch with the v8 floor.");
            var (wx, wy, wz) = MapProfiles.ToWorld(f, lx, ly, lz);
            Check(MathF.Abs(wx - (f.CenterX + 10)) < 1e-3 && MathF.Abs(wy - (f.CenterY - 20)) < 1e-3 && MathF.Abs(wz - (f.FloorZ + 5)) < 1e-3, $"{f.Mode}: world/local round trip.");
            Check(p.FrameAt(f.CenterX, f.CenterY)?.Mode == f.Mode, $"{f.Mode}: its centre lies on its own pitch.");
            Check(p.FrameAt(f.CenterX, f.CenterY + f.GoalLineY - 1)?.Mode == f.Mode, $"{f.Mode}: its goal lines lie on its own pitch.");
            // v8-style run-off: the goal (up to 90 deep) stands free of the end walls.
            Check(f.MaxY - (f.CenterY + f.GoalLineY) >= 128 && (f.CenterY - f.GoalLineY) - f.MinY >= 128, $"{f.Mode}: room behind both goals.");
        }
        Check(p.Frames.All(f => f.HasGoals), "Every pitch has goals.");
        // v3: the v8 ball (Jabulani, no rescale) and v8 goals on 3v3/4v4.
        Check(p.BallNames.SequenceEqual(new[] { "filter_ball" }) && p.BallDesignerName == "prop_physics_multiplayer" && MathF.Abs(p.BallRadius - 18.805f) < 1e-3,
            "The profile ball is the v8 Jabulani entity.");
        Check(!p.Frame("2v2")!.V8Goals && p.Frame("3v3")!.V8Goals && p.Frame("4v4")!.V8Goals, "v8 goals on 3v3 and 4v4, small goals on 2v2.");
        Check(p.Frames.Where(f => f.V8Goals).All(f => f.GoalHalfWidth == 124f && f.GoalHeight == 97f), "v8 goal mouth: +-124 x 97.");
        Check(p.FrameAt(0, 0) is null, "Between the pitches is no pitch.");
        Check(p.Frame("2v2")!.GoalHalfWidth < p.Frame("3v3")!.GoalHalfWidth, "2v2 uses the small goals.");
        Console.WriteLine("Map profile checks passed (6 frame scenarios x 3 pitches + 5).");
    }
}

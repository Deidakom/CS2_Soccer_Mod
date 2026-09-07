using System.Numerics;
using SoccerModMvp;

internal static class WallLiftChecks
{
    internal static void Run()
    {
        foreach (var (vertical, recentFall, nearFloor, requested, expected) in new[] {
            (0f, -250f, true, 180f, 0f),
            (200f, -400f, true, 180f, 0f),
            (250f, 0f, false, 180f, 0f),
            (50f, 0f, false, 180f, 130f),
            (0f, 0f, true, 180f, 180f),
            (-100f, -100f, false, 180f, 180f),
            (0f, 0f, true, 0f, 0f) })
        {
            var extra = BallContactMath.WallLift(vertical, recentFall, nearFloor, requested);
            if (MathF.Abs(extra - expected) > 0.001f)
                throw new Exception("Wall lift must distinguish ordinary wall hops from floor landings and existing upward rebounds.");
        }
        foreach (var currentZ in new[] { 0f, 50f, 180f, 400f })
        {
            var added = BallContactMath.WallLift(currentZ, 0, true, 180);
            var output = BallContactMath.WallReboundVertical(0, currentZ + added, 1000, true, 180);
            if (MathF.Abs(output - 180) > .001f)
                throw new Exception("Configured wall pop must survive the flat-hop limiter, without stacking existing lift.");
        }
        var landingAdded = BallContactMath.WallLift(60, -300, true, 180);
        if (landingAdded != 0 || BallContactMath.WallReboundVertical(0, 60 + landingAdded, 1000, true, 0) != 60)
            throw new Exception("A floor landing beside a wall must not gain the configured wall pop.");
        // The wall follow-up must retain both a falling velocity and the new
        // upward velocity after ground contact instead of restoring old lift.
        foreach (var normal in new[] { Vector3.UnitX, -Vector3.UnitY, Vector3.Normalize(new Vector3(1, 1, 0)) })
        foreach (var vertical in new[] { -600f, -50f, 0f, 80f, 300f })
        {
            var tangent = new Vector3(-normal.Y, normal.X, 0);
            var current = normal * 10 + tangent * 73 + Vector3.UnitZ * vertical;
            var result = BallContactMath.Separate(current, normal, 200);
            if (MathF.Abs(result.Z - vertical) > 0.001f
                || MathF.Abs(Vector3.Dot(result, tangent) - 73) > 0.001f
                || MathF.Abs(Vector3.Dot(result, normal) - 200) > 0.001f)
                throw new Exception("Wall separation must affect only the wall-normal component.");
        }
        Console.WriteLine("Wall landing/lift checks passed (27 scenarios).");
    }
}

using SoccerModMvp;

internal static class LowBallKickChecks
{
    internal static void Run()
    {
        static float DotAt(float degrees) => MathF.Cos(degrees * MathF.PI / 180);
        // Looking forward, ball centre ten units ahead and 45 below the eyes:
        // the old centre-only test rejects it, although the ball edge is in cone.
        var distance = MathF.Sqrt(10 * 10 + 45 * 45);
        var dot = 10 / distance;
        if (dot >= DotAt(70) || !BallContactMath.KickSphereInCone(dot, distance, 18.8f, 70))
            throw new Exception("A close low ball must be hittable at its reachable edge.");
        foreach (var cone in new[] { 10f, 40f, 70f })
        foreach (var range in new[] { 40f, 70f, 100f })
        {
            var angularRadius = MathF.Asin(18.8f / range) * 180 / MathF.PI;
            var insideAngle = MathF.Min(89, cone + angularRadius - 1);
            var outsideAngle = cone + angularRadius + 1;
            if (!BallContactMath.KickSphereInCone(DotAt(insideAngle), range, 18.8f, cone)
                || BallContactMath.KickSphereInCone(DotAt(outsideAngle), range, 18.8f, cone))
                throw new Exception("Cone must intersect the sphere edge, with no extra angular margin.");
        }
        foreach (var invalid in new[] { float.NaN, float.NegativeInfinity, -1f, 0f })
        {
            if (BallContactMath.KickSphereInCone(invalid, 40, 18.8f, 70)
                || BallContactMath.KickSphereInCone(1, invalid, 18.8f, 70))
                throw new Exception("Invalid samples and balls behind the aim must not become kicks.");
        }
        if (BallContactMath.KickSphereInCone(DotAt(82), 100, 18.8f, 70)
            || !BallContactMath.KickSphereInCone(DotAt(82), 40, 18.8f, 70))
            throw new Exception("Near-ball edge tolerance must shrink with distance.");
        Console.WriteLine("Low-ball kick geometry checks passed (15 scenarios).");
    }
}

using System.Numerics;
using SoccerModMvp;

internal static class AirborneKickChecks
{
    internal static void Run()
    {
        static Vector3 Aim(float elevation, float yaw)
        {
            var e = elevation * MathF.PI / 180;
            var y = yaw * MathF.PI / 180;
            return new(MathF.Cos(y) * MathF.Cos(e), MathF.Sin(y) * MathF.Cos(e), MathF.Sin(e));
        }
        static float Angle(Vector3 a, Vector3 b) => MathF.Acos(Math.Clamp(
            Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1, 1)) * 180 / MathF.PI;
        static void Near(Vector3 actual, Vector3 expected)
        {
            if (Vector3.Distance(actual, expected) > 0.01f)
                throw new Exception($"Unexpected volley velocity: {actual}, expected {expected}");
        }
        // Real server volleys: excessive lateral drift and falling momentum
        // must no longer dominate either the aim yaw or the elevation.
        foreach (var (incoming, elevation, yaw) in new[] {
            (new Vector3(-218.89f, 103.91f, -637.39f), 37.75f, -124.28f),
            (new Vector3(-68.02f, 192.83f, -421.45f), 28.40f, 18.49f) })
        {
            var direction = Aim(elevation, yaw);
            var result = BallContactMath.AirborneKickVelocity(incoming, direction, 1578.5f);
            var deviation = Angle(result, direction);
            if (deviation > 3.01f || deviation < 0.1f)
                throw new Exception("Logged volley must retain a little drift within the aim limit.");
            Console.WriteLine($"Logged volley aim deviation after correction: {deviation:F2} degrees.");
        }
        Near(BallContactMath.AirborneKickVelocity(Vector3.Zero, Vector3.UnitX, 1000), new(1000, 0, 0));
        Near(BallContactMath.AirborneKickVelocity(new(500, 0, 0), Vector3.UnitX, 1000), new(1500, 0, 0));
        Near(BallContactMath.AirborneKickVelocity(new(-500, 0, 0), Vector3.UnitX, 1000), new(1000, 0, 0));
        Near(BallContactMath.AirborneKickVelocity(new(0, 100, 0), Vector3.UnitX, 1000), new(1000, 25, 0));
        // Include weak passes, downward spikes and near-vertical overhead kicks.
        foreach (var elevation in new[] { -60f, 0f, 30f, 89.9f, 90f })
        foreach (var yaw in new[] { -179f, 0f, 90f })
        foreach (var power in new[] { 50f, 500f, 1602f })
        foreach (var incoming in new[] { new Vector3(3000, -3000, -3000), new Vector3(-3000, 0, 2500) })
        {
            var aim = Aim(elevation, yaw);
            var result = BallContactMath.AirborneKickVelocity(incoming, aim, power);
            if (!float.IsFinite(result.Length()) || Angle(result, aim) > 3.01f
                || Vector3.Dot(result, aim) < power - 0.01f)
                throw new Exception("Volley direction and forward kick strength must survive extreme incoming motion.");
            var capped = Vector3.Normalize(result) * MathF.Min(3500, result.Length());
            if (Angle(capped, aim) > 3.01f)
                throw new Exception("The final speed cap must preserve the direction limit.");
        }
        Console.WriteLine("Airborne kick checks passed (96 scenarios).");
    }
}

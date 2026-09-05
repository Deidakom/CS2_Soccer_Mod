using System.Numerics;
namespace SoccerModMvp;
internal static class TrainingTargetMath
{
    internal static bool ThroughHoop(Vector3 from, Vector3 to, Vector3 centre, float yaw, float radius, float ballRadius)
    {
        if (!float.IsFinite(yaw) || !float.IsFinite(radius) || !float.IsFinite(ballRadius) || radius <= ballRadius) return false;
        var normal = new Vector3(-MathF.Sin(yaw), MathF.Cos(yaw), 0);
        var a = Vector3.Dot(from - centre, normal); var b = Vector3.Dot(to - centre, normal);
        if (!float.IsFinite(a) || !float.IsFinite(b) || a * b > 0 || Math.Abs(a - b) < 0.001f) return false;
        var hit = Vector3.Lerp(from, to, a / (a - b));
        return Vector3.DistanceSquared(hit, centre) <= (radius - ballRadius) * (radius - ballRadius);
    }
}

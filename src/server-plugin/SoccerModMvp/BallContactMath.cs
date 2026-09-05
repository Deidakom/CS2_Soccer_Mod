using System.Numerics;

namespace SoccerModMvp;

// Shared by the shipped plugin and its executable regression suite. Units are
// Source units, seconds and degrees/second; this does not replace Rubikon.
internal static class BallContactMath
{
    internal readonly record struct Contact(float Fraction, Vector3 Normal);

    // Sweep a sphere centre in relative coordinates against a vertical capsule.
    // Expanding the capsule radius by the ball radius is an exact Minkowski sum.
    internal static Contact? SweepCapsule(Vector3 start, Vector3 end, Vector3 bottom, Vector3 top, float radius)
    {
        var delta = end - start;
        Vector3 Nearest(Vector3 p) => new(bottom.X, bottom.Y, Math.Clamp(p.Z, bottom.Z, top.Z));
        var initial = start - Nearest(start);
        if (initial.LengthSquared() <= radius * radius)
        {
            var normal = initial.LengthSquared() > 1e-8f ? Vector3.Normalize(initial)
                : delta.LengthSquared() > 1e-8f ? -Vector3.Normalize(delta) : Vector3.UnitZ;
            return new Contact(0, normal);
        }
        float best = float.PositiveInfinity;
        void Root(float a, float b, float c, bool cylinder)
        {
            if (a < 1e-8f) return;
            var discriminant = b * b - 4 * a * c;
            if (discriminant < 0) return;
            var t = (-b - MathF.Sqrt(discriminant)) / (2 * a);
            if (t < 0 || t > 1 || t >= best) return;
            var z = start.Z + delta.Z * t;
            if (cylinder && (z < bottom.Z || z > top.Z)) return;
            best = t;
        }
        var offset = start - bottom;
        Root(delta.X * delta.X + delta.Y * delta.Y,
            2 * (offset.X * delta.X + offset.Y * delta.Y),
            offset.X * offset.X + offset.Y * offset.Y - radius * radius, true);
        foreach (var centre in new[] { bottom, top })
        {
            offset = start - centre;
            Root(delta.LengthSquared(), 2 * Vector3.Dot(offset, delta), offset.LengthSquared() - radius * radius, false);
        }
        if (!float.IsFinite(best)) return null;
        var point = start + delta * best;
        return new Contact(best, Vector3.Normalize(point - Nearest(point)));
    }

    internal static Vector3 CombinePushes(Vector3 inherited, IEnumerable<(int Slot, Vector3 Delta)> pushes)
    {
        // Stable reduction and averaging prevent order-dependent last-writer
        // wins and stop a cluster of players multiplying the dribble impulse.
        var sum = Vector3.Zero;
        var count = 0;
        foreach (var push in pushes.OrderBy(p => p.Slot)) { sum += push.Delta; count++; }
        return count == 0 ? inherited : inherited + sum / count;
    }

    internal static Vector3 Separate(Vector3 current, Vector3 normal, float minimum)
        => current + normal * Math.Max(0, minimum - Vector3.Dot(current, normal));

    internal static float ContactSide(Vector3 offset, float yaw, float radius)
        => Math.Clamp(Vector3.Dot(offset, new Vector3(MathF.Sin(yaw), -MathF.Cos(yaw), 0)) / radius, -1, 1);

    internal static Vector3 CurveStep(Vector3 velocity, float spin, float dt)
    {
        // Explicit optional arcade aerodynamics. Rotation preserves speed and
        // Z; bounded curvature decays independently of the engine's spin.
        var angle = Math.Clamp(spin, -1, 1) * 0.30f * Math.Clamp(dt, 0, 0.05f);
        var c = MathF.Cos(angle); var s = MathF.Sin(angle);
        return new(velocity.X * c - velocity.Y * s, velocity.X * s + velocity.Y * c, velocity.Z);
    }
}

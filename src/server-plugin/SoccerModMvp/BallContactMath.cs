using System.Numerics;

namespace SoccerModMvp;

// Shared by the shipped plugin and its executable regression suite. Units are
// Source units, seconds and degrees/second; this does not replace Rubikon.
internal static class BallContactMath
{
    internal static float BodyContactApproach(float measured, float intentAlong, float minimum) =>
        intentAlong > .5f ? MathF.Max(measured, minimum) : measured;
    internal readonly record struct Contact(float Fraction, Vector3 Normal);

    // Restored pre-September-7 wall response: add lift, rather than treating
    // it as a target that replaces or caps the existing vertical velocity.
    internal static float AdditiveWallLift(float speedLost, float ratio, float maximum) =>
        Math.Min(speedLost * ratio, maximum);

    internal static float ReachPower(float surfaceDistance, float reach, bool approaching = true)
    {
        if (!float.IsFinite(surfaceDistance) || !float.IsFinite(reach) || reach <= 0) return 0;
        if (!approaching) return 1;
        // Broad firm-contact range; only the very tip cushions the ball.
        return 1 - 0.25f * EarlyContactFraction(surfaceDistance, reach);
    }

    internal static float EarlyContactFraction(float surfaceDistance, float reach)
    {
        if (!float.IsFinite(surfaceDistance + reach) || reach <= 0) return 0;
        var t = Math.Clamp((surfaceDistance / reach - 0.90f) / 0.10f, 0, 1);
        return t * t * (3 - 2 * t);
    }

    internal static bool IsIncomingContact(Vector3 ballVelocity, Vector3 playerVelocity, Vector3 toPlayer, bool grounded = false)
    {
        if (grounded) return false;
        var distance = toPlayer.Length();
        if (!float.IsFinite(distance) || distance <= .001f) return false;
        var direction = toPlayer / distance;
        // Require the ball itself to approach, not merely the player chasing
        // it. Also exclude a player retreating faster than the incoming ball.
        return Vector3.Dot(ballVelocity, direction) > 5
            && Vector3.Dot(ballVelocity - playerVelocity, direction) > 5;
    }

    internal static Vector3 CushionEarlyKick(Vector3 incoming, Vector3 fullKick, Vector3 direction,
        float deltaSpeed, float surfaceDistance, float reach, bool approaching)
    {
        if (!approaching) return fullKick;
        var early = EarlyContactFraction(surfaceDistance, reach);
        if (early <= 0) return fullKick;
        direction = Vector3.Normalize(direction);
        var along = Vector3.Dot(incoming, direction);
        // An incoming tip touch brakes first; a hard shot may stop.
        // Stationary/outgoing balls bypass this entirely. Close hits stay firm.
        var remaining = Math.Max(0, deltaSpeed - Math.Max(0, -along));
        var softSpeed = Math.Min(100, remaining * 0.10f + Math.Max(0, along) * 0.075f);
        return Vector3.Lerp(fullKick, direction * softSpeed, early);
    }

    internal static float ImpactTargetAlong(float playerAlong, float push)
        => Math.Max(0, playerAlong) + Math.Max(0, push);

    internal static float ImpactPulseTarget(float initial, int frame, int frames)
        => initial * (1 - 0.25f * Math.Clamp((float)frame / Math.Max(1, frames), 0, 1));

    internal static float LandingVertical(float incomingZ, float outgoingZ)
        => incomingZ < -80 && outgoingZ > 0 ? Math.Min(outgoingZ, -incomingZ * 0.55f) : outgoingZ;

    internal static float WallReboundVertical(float incomingZ, float outgoingZ, float normalSpeed, bool nearFloor,
        float intentionalLift = 0)
    {
        if (!nearFloor || MathF.Abs(incomingZ) > 80 || outgoingZ <= 0) return outgoingZ;
        // Flat rebounds may hop, but do not convert a fast ground pass into
        // a lob. Never add lift or damp an already airborne shot here.
        // The native-hop guard must not cancel the administrator's explicitly
        // configured wall lift. This is a total allowance, not another impulse.
        var nativeLimit = MathF.Max(MathF.Max(0, incomingZ), Math.Clamp(normalSpeed * .10f, 0, 90));
        var allowedLift = float.IsFinite(intentionalLift) ? MathF.Max(0, intentionalLift) : 0;
        return MathF.Min(outgoingZ, MathF.Max(nativeLimit, allowedLift));
    }

    internal static bool HorizontalKickAim(Vector3 toBall, float yaw, float radius, float cone)
    {
        var distance = new Vector2(toBall.X, toBall.Y).Length();
        if (distance <= radius) return true; // directly above/below: vertical cone still applies
        var dot = (toBall.X * MathF.Cos(yaw) + toBall.Y * MathF.Sin(yaw)) / distance;
        return KickSphereInCone(dot, distance, radius, MathF.Min(cone, 45));
    }

    // A finite low-speed rollout, never a speed floor or perpetual motion.
    // Large losses/direction changes belong to collisions and are not restored.
    internal static float RollingSpeed(float previous, float current, float directionDot, float dt)
    {
        if (!float.IsFinite(previous + current + directionDot + dt) || dt <= 0 || dt > 0.05f
            || previous <= 4 || previous > 220 || current <= 4 || current > previous
            || current < previous * 0.7f || directionDot < 0.995f) return current;
        return MathF.Max(current, MathF.Min(previous - 6 * dt, current + 100 * dt));
    }

    internal static float RollAllowance(float initial, float elapsed)
        => elapsed < 0 || elapsed >= 20 ? 0 : MathF.Max(0, initial - 6 * elapsed);

    // Native low-speed hull friction can sleep a moving ball in one tick.
    // Only the caller's clear-floor/no-player/no-wall checks permit bridging
    // that loss, and only for an already recorded, finite rolling episode.
    internal static float RollingTail(float previous, float current, float dot, float dt, float allowance)
    {
        if (!float.IsFinite(previous + current + dot + dt + allowance) || dt <= 0 || dt > .05f
            || previous <= 4 || previous > 70 || current < 0 || current > previous || dot < .995f) return current;
        return MathF.Max(current, MathF.Min(previous - 6 * dt, allowance));
    }

    internal static Vector3 RollingLocalSpin(Vector3 velocity, float radius, float strength, Quaternion rotation)
        => Vector3.Transform(new Vector3(-velocity.Y, velocity.X, 0) * (strength * 180 / MathF.PI / radius),
            Quaternion.Inverse(rotation));

    internal static bool KickSphereInCone(float centreDot, float distance, float radius, float coneDegrees)
    {
        if (!float.IsFinite(centreDot) || !float.IsFinite(distance) || distance <= 0 || centreDot <= 0)
            return false;
        // Test the nearest visible edge, not only the centre. A close low ball
        // occupies a large angle below the eyes even while its top is reachable.
        var edgeAngle = MathF.Asin(Math.Clamp(radius / distance, 0, 1));
        var centreAngle = MathF.Acos(Math.Clamp(centreDot, -1, 1));
        return centreAngle <= coneDegrees * MathF.PI / 180 + edgeAngle;
    }

    internal static float WallLift(float currentZ, float recentMinimumZ, bool nearFloor, float requestedLift)
    {
        // Landing near a wall must not gain an artificial second bounce.
        if (nearFloor && recentMinimumZ < -80) return 0;
        // A native upward rebound already supplies some or all of the lift.
        return MathF.Max(0, requestedLift - MathF.Max(0, currentZ));
    }

    // The kick owns a volley's direction. Keep a quarter of cross-aim momentum
    // for natural drift, but bound its 3D deflection to three degrees. Momentum
    // already along the shot still contributes fully; opposing motion is stopped.
    internal static Vector3 AirborneKickVelocity(Vector3 inherited, Vector3 direction, float deltaSpeed)
    {
        direction = Vector3.Normalize(direction);
        var along = Vector3.Dot(inherited, direction);
        var forwardSpeed = MathF.Max(0, along) + deltaSpeed;
        var drift = (inherited - direction * along) * 0.25f;
        var driftSpeed = drift.Length();
        var maxDrift = forwardSpeed * MathF.Tan(3.0f * MathF.PI / 180.0f);
        if (driftSpeed > maxDrift && driftSpeed > 0)
            drift *= maxDrift / driftSpeed;
        return direction * forwardSpeed + drift;
    }

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

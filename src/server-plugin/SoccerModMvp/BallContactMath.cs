using System.Numerics;

namespace SoccerModMvp;

// Shared by the shipped plugin and its executable regression suite. Units are
// Source units, seconds and degrees/second; this does not replace Rubikon.
internal static class BallContactMath
{
    // Two equal balls touching along `normal` (from A to B): if they close on
    // each other, exchange the normal velocity component with restitution
    // (equal masses), and push each apart by half the overlap.
    internal static (Vector3 A, Vector3 B, float Push) BallBallResponse(
        Vector3 velocityA, Vector3 velocityB, Vector3 normal, float distance, float minDistance, float restitution)
    {
        var push = MathF.Max(0, minDistance - distance) / 2 + 0.25f;
        var closing = Vector3.Dot(velocityA - velocityB, normal);
        if (closing <= 0) return (velocityA, velocityB, push);
        var impulse = (1 + restitution) / 2 * closing;
        return (velocityA - normal * impulse, velocityB + normal * impulse, push);
    }

    // A player heading at the ball (planar direction dirX/dirY from player to
    // ball) faster than `minimum`; used to unfreeze the kickoff ball before contact.
    internal static bool ClosingOnBall(Vector3 playerVelocity, float dirX, float dirY, float minimum) =>
        float.IsFinite(playerVelocity.X + playerVelocity.Y + dirX + dirY)
        && playerVelocity.X * dirX + playerVelocity.Y * dirY >= minimum;

    internal static float BodyContactApproach(float measured, float intentAlong, float minimum) =>
        intentAlong > .5f ? MathF.Max(measured, minimum) : measured;
    internal readonly record struct Contact(float Fraction, Vector3 Normal);

    // Restored pre-September-7 wall response: add lift, rather than treating
    // it as a target that replaces or caps the existing vertical velocity.
    internal static float AdditiveWallLift(float speedLost, float ratio, float maximum) =>
        Math.Min(speedLost * ratio, maximum);

    // Ground bounce: the fastest downward speed of the last few ticks is the
    // impact. Once the pitch has taken at least half of it (the contact tick),
    // the rebound is raised to the restitution for that impact speed if the
    // engine gave less. Null = not a bounce, or the engine's own rebound is
    // already enough.
    internal static float? GroundBounceVertical(float impactVz, float currentVz, float restitution, float minimumImpact)
    {
        if (restitution <= 0 || impactVz > -minimumImpact || currentVz < impactVz * 0.5f) return null;
        var target = -impactVz * GroundBounceRestitutionAt(restitution, -impactVz);
        return currentVz < target ? target : null;
    }

    // A real ball keeps a little less of a hard landing than of a soft one
    // (the grass and the ball deform more). The setting is the value at a
    // mid-speed landing: x1.1 for a gentle drop, x0.9 from 1500 u/s up.
    // 2026-09-24 owner: knifing a fast ball that is coming at you takes its
    // speed out, so it rolls away slowly (CS:S: the knife hit ADDED an
    // impulse to the ball's velocity). The kick keeps its aimed direction;
    // its speed loses `absorb` x the ball's horizontal speed towards the
    // kicker along the shot line, never below `minimumSpeed`. Only the
    // horizontal approach counts, so volleys on falling balls are unchanged.
    internal static Vector3 AbsorbIncoming(Vector3 requested, Vector3 inherited, Vector3 launchDirection, float absorb, float minimumSpeed)
    {
        var planar = new Vector2(launchDirection.X, launchDirection.Y);
        var speed = requested.Length();
        if (absorb <= 0 || planar.LengthSquared() < 1e-6f || speed < 1e-3f) return requested;
        planar = Vector2.Normalize(planar);
        var approach = MathF.Max(0, -(inherited.X * planar.X + inherited.Y * planar.Y));
        if (approach <= 0) return requested;
        var target = MathF.Max(MathF.Min(minimumSpeed, speed), speed - absorb * approach);
        return target >= speed ? requested : requested * (target / speed);
    }

    internal static float GroundBounceRestitutionAt(float restitution, float impactSpeed) =>
        restitution * (1.1f - 0.2f * Math.Clamp(impactSpeed / 1500f, 0f, 1f));

    // Grass friction during the contact: forward speed lost is grip x the
    // vertical speed change (impact + rebound), so a steep drop loses more
    // than a skim. The first landing of a flight costs the most (the ball
    // goes from sliding to rolling, at most 25%); after that it is already
    // rolling and later bounces lose at most 5% each, so a long ball still
    // rolls on. (A flat 40% per bounce killed long balls after 3-4 bounces.)
    internal const float GroundBounceFirstLossLimit = 0.25f;
    internal const float GroundBounceLaterLossLimit = 0.05f;

    internal static float GroundBouncePlanarScale(float planarSpeed, float impactSpeed, float rebound, float grip, bool firstBounce)
    {
        if (planarSpeed < 1f || grip <= 0f) return 1f;
        var limit = firstBounce ? GroundBounceFirstLossLimit : GroundBounceLaterLossLimit;
        var loss = MathF.Min(grip * (impactSpeed + rebound), limit * planarSpeed);
        return 1f - loss / planarSpeed;
    }

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

    // 2026-09-25 owner (CS:S parity): the ball pushes a player along the
    // contact normal, like the VPhysics impulse in CS:S. A hit on the left
    // side sends you back-right, a glancing hit pushes less. Returns the
    // planar push direction (away from the ball) and the ball's own speed
    // along it. A near-vertical contact (a ball dropping on the head) keeps
    // the travel direction.
    internal static (Vector2 Direction, float Speed) ImpactPushAlongNormal(Vector3 incoming, Vector3 normal)
    {
        var travel = new Vector2(incoming.X, incoming.Y);
        var away = new Vector2(-normal.X, -normal.Y);
        if (!float.IsFinite(away.X + away.Y + travel.X + travel.Y)) return (Vector2.Zero, 0);
        if (away.Length() < 0.3f)
            return travel.LengthSquared() > 1 ? (Vector2.Normalize(travel), travel.Length()) : (Vector2.Zero, 0);
        away = Vector2.Normalize(away);
        return (away, MathF.Max(0, Vector2.Dot(travel, away)));
    }

    // 2026-09-25 owner: post and crossbar hit sounds. A hit is a sudden
    // velocity change (the caller gates speed and change) while the ball sits
    // against the frame at either goal mouth (|y| near the line). The crossbar
    // band is across the mouth at bar height; a post is the column at
    // |x| = halfWidth below the bar. A change mostly vertical at a post is a
    // ground bounce next to it, not a post hit.
    internal enum GoalFrameHit { None, Crossbar, Post }

    internal static GoalFrameHit ClassifyGoalFrameHit(Vector3 origin, Vector3 before, Vector3 after, float radius,
        float halfWidth, float lineY, float crossbarZ, float margin = 14f)
    {
        if (!float.IsFinite(origin.X + origin.Y + origin.Z + before.X + before.Y + before.Z + after.X + after.Y + after.Z))
            return GoalFrameHit.None;
        var ax = MathF.Abs(origin.X);
        var ay = MathF.Abs(origin.Y);
        if (ay < lineY - radius - margin - 10 || ay > lineY + radius + margin + 10) return GoalFrameHit.None;
        var change = after - before;
        var horizontal = new Vector2(change.X, change.Y).Length();
        var vertical = MathF.Abs(change.Z);
        if (ax <= halfWidth + radius && MathF.Abs(origin.Z - crossbarZ) <= radius + margin) return GoalFrameHit.Crossbar;
        if (MathF.Abs(ax - halfWidth) <= radius + margin && origin.Z <= crossbarZ + radius && horizontal >= vertical)
            return GoalFrameHit.Post;
        return GoalFrameHit.None;
    }

    internal static float ImpactTargetAlong(float playerAlong, float push)
        => Math.Max(0, playerAlong) + Math.Max(0, push);

    internal static float ImpactPulseTarget(float initial, int frame, int frames)
        => initial * (1 - 0.25f * Math.Clamp((float)frame / Math.Max(1, frames), 0, 1));

    // The ratio follows the ground-bounce setting (0.55 with it off).
    internal static float LandingVertical(float incomingZ, float outgoingZ, float ratio = 0.55f)
        => incomingZ < -80 && outgoingZ > 0 ? Math.Min(outgoingZ, -incomingZ * ratio) : outgoingZ;

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
    // `decel` is the loss per second the rollout keeps (6 = the original
    // glide; the rolling resistance dial when that is set).
    internal const float RollAssistDecel = 6f;
    internal static float RollingSpeed(float previous, float current, float directionDot, float dt, float decel = RollAssistDecel)
    {
        if (!float.IsFinite(previous + current + directionDot + dt) || dt <= 0 || dt > 0.05f
            || previous <= 4 || previous > 220 || current <= 4 || current > previous
            || current < previous * 0.7f || directionDot < 0.995f) return current;
        return MathF.Max(current, MathF.Min(previous - decel * dt, current + 100 * dt));
    }

    internal static float RollAllowance(float initial, float elapsed, float decel = RollAssistDecel)
        => elapsed < 0 || elapsed >= 20 ? 0 : MathF.Max(0, initial - decel * elapsed);

    // Native low-speed hull friction can sleep a moving ball in one tick.
    // Only the caller's clear-floor/no-player/no-wall checks permit bridging
    // that loss, and only for an already recorded, finite rolling episode.
    internal static float RollingTail(float previous, float current, float dot, float dt, float allowance, float decel = RollAssistDecel)
    {
        if (!float.IsFinite(previous + current + dot + dt + allowance) || dt <= 0 || dt > .05f
            || previous <= 4 || previous > 70 || current < 0 || current > previous || dot < .995f) return current;
        return MathF.Max(current, MathF.Min(previous - decel * dt, allowance));
    }

    // 2026-09-24 ball analysis: below 220 u/s the engine takes only about
    // 1 u/s per second from a rolling ball, so it glided for up to 20 s. Real
    // grass takes 0.06-0.15 g (FIFA roll test), 50-120 u/s per second here.
    // With a resistance set, a clean roll loses exactly that much per second:
    // anything the engine keeps above it is braked off, and the last few u/s
    // stop the ball. Speeding up (a push the caller missed) is not braked.
    internal const float RollStopSpeed = 4f;
    internal static float BrakedRollSpeed(float previous, float current, float resistance, float dt)
    {
        if (resistance <= 0 || !float.IsFinite(previous + current + resistance + dt) || dt <= 0 || dt > .05f
            || current > previous * 1.05f + 1) return current;
        var braked = MathF.Min(current, previous - resistance * dt);
        return braked < RollStopSpeed ? 0 : braked;
    }

    // Curve in flight from side spin (Magnus), see SoccerModMvpPlugin.Aero.cs.
    // The pull is R x spin x speed / MagnusLength, sideways; divided by the
    // speed it turns the horizontal velocity at R x spinZ / MagnusLength
    // radians per second, so speed and vertical velocity stay the same. The
    // lift coefficient levels off above a spin parameter of 0.3 like a real
    // ball's. MagnusLength: measured drag length ~4000 u x drag coefficient 0.25.
    internal const float MagnusLength = 1000f;
    internal static Vector3 MagnusCurveStep(Vector3 velocity, float spinZ, float radius, float strength, float dt)
    {
        var planar = MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
        if (strength <= 0 || planar < 1 || radius <= 0
            || !float.IsFinite(spinZ + radius + strength + dt + velocity.X + velocity.Y + velocity.Z)) return velocity;
        var spinParameter = radius * MathF.Abs(spinZ) / planar;
        var levelled = spinParameter > .3f ? .3f / spinParameter : 1f;
        var angle = strength * levelled * radius * spinZ / MagnusLength * Math.Clamp(dt, 0, .05f);
        var c = MathF.Cos(angle); var s = MathF.Sin(angle);
        return new(velocity.X * c - velocity.Y * s, velocity.X * s + velocity.Y * c, velocity.Z);
    }

    internal static Vector3 RollingLocalSpin(Vector3 velocity, float radius, float strength, Quaternion rotation)
        => Vector3.Transform(new Vector3(-velocity.Y, velocity.X, 0) * (strength * 180 / MathF.PI / radius),
            Quaternion.Inverse(rotation));

    // 2026-09-24 owner: a ball rolling towards the kicker kept its roll after
    // being knifed back, which is backspin for the new direction ("always a
    // backspin"). The spin kept from before a kick is everything except the
    // part rolling AGAINST the new direction; side-spin (curve) and roll that
    // already matches the shot survive. Spins are in the ball's local frame.
    internal static Vector3 KeepSpinForKick(Vector3 measuredLocal, Vector3 launchDirection, Quaternion rotation)
    {
        var roll = RollingLocalSpin(new Vector3(launchDirection.X, launchDirection.Y, 0), 1f, 1f, rotation);
        if (roll.LengthSquared() < 1e-9f) return measuredLocal;
        var axis = Vector3.Normalize(roll);
        var along = Vector3.Dot(measuredLocal, axis);
        return along < 0 ? measuredLocal - axis * along : measuredLocal;
    }

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

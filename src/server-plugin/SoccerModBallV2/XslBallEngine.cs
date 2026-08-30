namespace SoccerModBallV2;

/// <summary>
/// Engine-independent ball state. Source 2 never integrates this state: the
/// adapter only uses the engine for swept-sphere world queries and rendering.
/// </summary>
internal sealed class XslBallEngine
{
    private const float MinimumSpeed = 3.0f;

    public XslBallEngine(XslBallProfile profile, BallVec3 resetPosition)
    {
        Profile = profile;
        Reset(resetPosition);
    }

    public XslBallProfile Profile { get; }
    public BallVec3 Position { get; private set; }
    public BallVec3 Velocity { get; private set; }
    public bool IsGrounded { get; private set; }

    public void Reset(BallVec3 position)
    {
        Position = position;
        Velocity = BallVec3.Zero;
        IsGrounded = false;
    }

    public void SetPosition(BallVec3 position) => Position = position;

    public void SetGrounded(bool grounded)
    {
        IsGrounded = grounded;
        if (grounded && Velocity.Z < 0.0f)
        {
            Velocity = Velocity.WithZ(0.0f);
        }
    }

    public void BeginStep(float deltaTime)
    {
        if (!IsGrounded || Velocity.Z > 0.0f)
        {
            Velocity += new BallVec3(0.0f, 0.0f, -Profile.Gravity * deltaTime);
        }

        var airRetention = MathF.Exp(-Profile.AirDamping * deltaTime);
        Velocity *= airRetention;
        ClampSpeed();
    }

    public BallVec3 RequestedDisplacement(float deltaTime) => Velocity * deltaTime;

    public void ResolveWorldCollision(BallVec3 normal, bool nearGround)
    {
        normal = normal.Normalized();
        if (normal.LengthSquared < 0.5f)
        {
            Velocity = BallVec3.Zero;
            return;
        }

        var normalSpeed = BallVec3.Dot(Velocity, normal);
        if (normalSpeed >= 0.0f)
        {
            return;
        }

        var normalVelocity = normal * normalSpeed;
        var tangentVelocity = Velocity - normalVelocity;

        if (normal.Z >= Profile.FloorNormalThreshold)
        {
            tangentVelocity *= Profile.FloorTangentRetention;
            var incomingSpeed = -normalSpeed;
            var reboundSpeed = incomingSpeed >= Profile.MinimumFloorBounceSpeed
                ? incomingSpeed * Profile.FloorRestitution
                : 0.0f;
            Velocity = tangentVelocity + normal * reboundSpeed;
            if (reboundSpeed < Profile.MinimumFloorBounceSpeed)
            {
                Velocity = Velocity.WithZ(0.0f);
                IsGrounded = true;
            }
            return;
        }

        if (normal.Z <= -Profile.FloorNormalThreshold)
        {
            Velocity = tangentVelocity * Profile.CeilingTangentRetention
                - normalVelocity * Profile.CeilingRestitution;
            return;
        }

        var wallImpactSpeed = -normalSpeed;
        Velocity = tangentVelocity * Profile.WallTangentRetention
            - normalVelocity * Profile.WallRestitution;

        // The old faceted XSL hull converted a low wall impact into useful
        // height. Reproduce the gameplay result explicitly, without copying
        // the unstable geometry bug.
        if (nearGround && wallImpactSpeed >= Profile.WallPopMinimumImpactSpeed)
        {
            var wallPop = MathF.Min(
                Profile.WallPopMaximumVerticalSpeed,
                wallImpactSpeed * Profile.WallPopVerticalConversion);
            if (wallPop > Velocity.Z)
            {
                Velocity = Velocity.WithZ(wallPop);
                IsGrounded = false;
            }
        }

        ClampSpeed();
    }

    public void EndStep(float deltaTime, bool grounded)
    {
        IsGrounded = grounded;
        if (grounded)
        {
            var planar = new BallVec3(Velocity.X, Velocity.Y, 0.0f);
            // The real XSL inline hull sheds a lot of speed in the first part
            // of a roll, then keeps a recognisable low-speed coast.  A single
            // constant drag cannot reproduce both parts: it either feels like
            // a stone at kick speed or stops the ball dead at dribble speed.
            var planarSpeed = planar.Length;
            var dampingBlend = Math.Clamp(
                (planarSpeed - Profile.RollingDampingStartSpeed)
                    / (Profile.RollingDampingFullSpeed - Profile.RollingDampingStartSpeed),
                0.0f,
                1.0f);
            var rollingDamping = Profile.RollingRestDamping
                + (Profile.RollingHighSpeedDamping - Profile.RollingRestDamping)
                    * dampingBlend;
            var planarRetention = MathF.Exp(-rollingDamping * deltaTime);
            planar *= planarRetention;
            if (planar.Length < MinimumSpeed)
            {
                planar = BallVec3.Zero;
            }
            Velocity = new BallVec3(planar.X, planar.Y, 0.0f);
        }

        if (Velocity.Length < MinimumSpeed)
        {
            Velocity = BallVec3.Zero;
        }
    }

    public void ApplyKnifeKick(BallVec3 viewForward, BallVec3 playerVelocity)
    {
        viewForward = viewForward.Normalized();
        // Keep a normally downward-aimed ground kick on the grass.  The
        // previous preset clamped every such kick to a 150 u/s vertical launch,
        // which is why the ball visibly hopped instead of rolling.  Height is
        // now earned by the player's upward aim, using the same left-click.
        var vertical = Profile.KickBaseLift
            + viewForward.Z * Profile.KickAimVerticalInfluence;
        vertical = Math.Clamp(vertical, Profile.KickMinimumLift, Profile.KickMaximumLift);

        var planarForward = new BallVec3(viewForward.X, viewForward.Y, 0.0f).Normalized();

        var impulse = new BallVec3(
            planarForward.X * Profile.KickImpulse,
            planarForward.Y * Profile.KickImpulse,
            vertical);
        var inheritedPlayer = new BallVec3(playerVelocity.X, playerVelocity.Y, 0.0f)
            * Profile.KickPlayerVelocityInheritance;

        Velocity = Velocity * Profile.KickExistingVelocityRetention
            + impulse
            + inheritedPlayer;
        IsGrounded = false;
        ClampSpeed();
    }

    public void ApplyBodyContact(BallVec3 contactNormal, BallVec3 playerVelocity)
    {
        contactNormal = new BallVec3(contactNormal.X, contactNormal.Y, 0.0f).Normalized();
        if (contactNormal.LengthSquared < 0.5f)
        {
            return;
        }

        var playerNormalSpeed = BallVec3.Dot(playerVelocity, contactNormal);
        var ballNormalSpeed = BallVec3.Dot(Velocity, contactNormal);
        if (playerNormalSpeed <= 1.0f || playerNormalSpeed <= ballNormalSpeed)
        {
            return;
        }

        var requestedNormalSpeed = MathF.Min(
            Profile.BodyMaximumPushSpeed,
            playerNormalSpeed * Profile.BodyVelocityMultiplier + Profile.BodyContactBoost);
        Velocity += contactNormal * (requestedNormalSpeed - ballNormalSpeed);
        ClampSpeed();
    }

    public void ApplyDebugImpulse(BallVec3 velocity)
    {
        Velocity = velocity;
        IsGrounded = false;
        ClampSpeed();
    }

    private void ClampSpeed()
    {
        var speed = Velocity.Length;
        if (!float.IsFinite(speed))
        {
            Velocity = BallVec3.Zero;
            return;
        }

        if (speed > Profile.MaximumSpeed)
        {
            Velocity *= Profile.MaximumSpeed / speed;
        }
    }
}

internal sealed class XslBallProfile
{
    // Initial reference preset derived from the exact XSL B1 telemetry plus
    // the 2026-08-28 4K/60 recording. These are CS2 solver values, not a blind
    // copy of Source 1 material numbers.
    public float Radius { get; init; } = 15.0f;
    public float Gravity { get; init; } = 800.0f;
    public float AirDamping { get; init; } = 0.025f;
    public float RollingRestDamping { get; init; } = 0.08f;
    public float RollingHighSpeedDamping { get; init; } = 0.93f;
    public float RollingDampingStartSpeed { get; init; } = 30.0f;
    public float RollingDampingFullSpeed { get; init; } = 400.0f;
    public float FloorNormalThreshold { get; init; } = 0.55f;
    public float FloorRestitution { get; init; } = 0.06f;
    public float FloorTangentRetention { get; init; } = 0.985f;
    public float MinimumFloorBounceSpeed { get; init; } = 90.0f;
    public float WallRestitution { get; init; } = 0.22f;
    public float WallTangentRetention { get; init; } = 0.92f;
    public float CeilingRestitution { get; init; } = 0.35f;
    public float CeilingTangentRetention { get; init; } = 0.94f;
    public float WallPopMinimumImpactSpeed { get; init; } = 280.0f;
    public float WallPopVerticalConversion { get; init; } = 0.15f;
    public float WallPopMaximumVerticalSpeed { get; init; } = 380.0f;
    public float KickImpulse { get; init; } = 1335.0f;
    public float KickBaseLift { get; init; } = 300.0f;
    public float KickAimVerticalInfluence { get; init; } = 750.0f;
    public float KickMinimumLift { get; init; } = 20.0f;
    public float KickMaximumLift { get; init; } = 1300.0f;
    public float KickExistingVelocityRetention { get; init; } = 1.0f;
    public float KickPlayerVelocityInheritance { get; init; } = 0.70f;
    public float BodyVelocityMultiplier { get; init; } = 1.12f;
    public float BodyContactBoost { get; init; } = 28.0f;
    public float BodyMaximumPushSpeed { get; init; } = 520.0f;
    public float MaximumSpeed { get; init; } = 2600.0f;
}

internal readonly struct BallVec3
{
    public BallVec3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static BallVec3 Zero => new(0.0f, 0.0f, 0.0f);
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public float LengthSquared => X * X + Y * Y + Z * Z;
    public float Length => MathF.Sqrt(LengthSquared);

    public BallVec3 WithZ(float z) => new(X, Y, z);

    public BallVec3 Normalized()
    {
        var length = Length;
        return length > 0.00001f && float.IsFinite(length)
            ? this / length
            : Zero;
    }

    public static float Dot(BallVec3 left, BallVec3 right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    public static BallVec3 operator +(BallVec3 left, BallVec3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static BallVec3 operator -(BallVec3 left, BallVec3 right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static BallVec3 operator *(BallVec3 value, float scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale);

    public static BallVec3 operator /(BallVec3 value, float scale) =>
        new(value.X / scale, value.Y / scale, value.Z / scale);
}

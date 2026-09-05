using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using V3 = System.Numerics.Vector3;
using Q = System.Numerics.Quaternion;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private static Q Rotation(QAngle angles)
    {
        const float radians = MathF.PI / 180;
        return Q.CreateFromAxisAngle(V3.UnitZ, angles.Y * radians)
            * Q.CreateFromAxisAngle(V3.UnitY, angles.X * radians)
            * Q.CreateFromAxisAngle(V3.UnitX, angles.Z * radians);
    }
    private void SampleBallRotation(CPhysicsPropMultiplayer ball, ContactState state)
    {
        var rotation = Rotation(ball.AbsRotation!);
        var now = Server.TickedTime;
        var dt = now - state.RotationTime;
        state.SpinMeasured = false;
        if (state.PreviousRotation is { } previous && dt > 0 && dt <= 0.05)
        {
            var delta = Q.Normalize(Q.Inverse(previous) * rotation);
            if (delta.W < 0) delta = new Q(-delta.X, -delta.Y, -delta.Z, -delta.W);
            var axis = new V3(delta.X, delta.Y, delta.Z);
            var length = axis.Length();
            state.MeasuredSpin = length > 1e-6f
                ? axis / length * (2 * MathF.Atan2(length, delta.W) * 180 / MathF.PI / (float)dt) : V3.Zero;
            state.SpinMeasured = float.IsFinite(state.MeasuredSpin.Length());
        }
        state.PreviousRotation = rotation; state.RotationTime = now;
    }
    private void ApplyContactSpin(CPhysicsPropMultiplayer ball, Vector eye, Vector forward, float alongRay,
        Vector origin, Vector launchDirection, float deltaSpeed)
    {
        var state = State(ball);
        // Orientation differences are observable even when AngVelocity is zero
        // for a Rubikon prop. If unavailable, skip rather than accumulate blind
        // spin. This is sampled readback, not an exact native inertia query.
        if (!state.SpinMeasured || Server.TickedTime - state.RotationTime > 0.05) return;
        var offset = N(eye) + N(forward) * alongRay - N(origin);
        if (offset.Length() > BallCollisionRadius) offset = V3.Normalize(offset) * BallCollisionRadius;
        var worldDelta = V3.Cross(offset, N(launchDirection) * deltaSpeed)
            * (_ballSpinFactor * 180 / MathF.PI / (BallCollisionRadius * BallCollisionRadius));
        var localDelta = V3.Transform(worldDelta, Q.Inverse(Rotation(ball.AbsRotation!)));
        var desired = state.MeasuredSpin + localDelta;
        const float maximumSpin = 6000; // conservative experimental bound, not a measured CS:S constant
        if (desired.Length() > maximumSpin) desired = V3.Normalize(desired) * maximumSpin;
        var impulse = desired - state.MeasuredSpin;
        SendAngularImpulse(ball, impulse);
        state.SpinMeasured = false; // wait for the next physical sample before another correction
    }
    private static void SendAngularImpulse(CPhysicsPropMultiplayer ball, V3 impulse)
    {
        var ptr = ball.Handle.ToInt64().ToString("X", CultureInfo.InvariantCulture);
        Server.ExecuteCommand(FormattableString.Invariant($"sm2_native_angular_impulse {ptr} {impulse.X:F2} {impulse.Y:F2} {impulse.Z:F2}"));
    }
}

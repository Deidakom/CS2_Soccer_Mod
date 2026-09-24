using CounterStrikeSharp.API;
using V3 = System.Numerics.Vector3;

namespace SoccerModMvp;

// 2026-09-24 owner approved (ball analysis): curve in flight. A spinning
// football bends towards the side its front turns to (Magnus effect). The
// engine has no such force, so side spin only mattered at contacts.
// Only spin about the vertical axis (side spin from an off-centre kick) bends
// the ball. Top/back spin lift is left out: a ball kicked out of a roll keeps
// its ground roll as topspin here, and every dribbled shot would dip.
// Strength 1 = a real ball (BallContactMath.MagnusCurveStep). How much a
// kick spins the ball is the separate "Native spin factor": at 0.1 an
// edge-on kick bends ~40 u over a 0.6 s flight, 0.3-0.5 gives real curlers.
public sealed partial class SoccerModMvpPlugin
{
    private float _magnusStrength; // 0 = off, the behaviour until 2026-09-24
    private const float MagnusMinimumSpeed = 150f;
    private const float MagnusMinimumSpin = 0.5f; // rad/s, below that it is sampling noise

    // After UpdateSharedBallHandling, which measured this tick's spin.
    private void UpdateBallAerodynamics()
    {
        if (_magnusStrength <= 0 || _pausedBallHandle != 0 || _ballMotionFrozen) return;
        foreach (var target in PlayableBalls())
        {
            var ball = target.Ball;
            var state = State(ball);
            if (!state.SpinMeasured || Server.TickCount - state.LastContactTick < 4 || KnifeKickOwnsTick(ball)
                || Server.TickedTime - state.LastWall <= WallAssistCooldownSeconds
                || ball.AbsRotation is not { } angles) continue;
            var velocity = N(target.Inherited);
            var spinZ = V3.Transform(state.MeasuredSpin, Rotation(angles)).Z * (MathF.PI / 180);
            if (MathF.Abs(spinZ) < MagnusMinimumSpin || new V3(velocity.X, velocity.Y, 0).Length() < MagnusMinimumSpeed
                || IsBallGrounded(ball, target.Origin)) continue;
            var turned = BallContactMath.MagnusCurveStep(velocity, spinZ, BallCollisionRadius, _magnusStrength, Server.TickInterval);
            ball.Teleport(velocity: C(turned));
        }
    }
}

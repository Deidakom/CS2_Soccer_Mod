using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using V3 = System.Numerics.Vector3;

namespace SoccerModMvp;

public sealed partial class SoccerModMvpPlugin
{
    private readonly Dictionary<uint, (V3 Origin, V3 Velocity, int Tick)> _landingSamples = new();

    private void UpdateLandingLimits()
    {
        if (_pausedBallHandle != 0 || _ballMotionFrozen) { _landingSamples.Clear(); return; }
        var balls = PlayableBalls().ToArray();
        var live = balls.Select(b => b.Ball.EntityHandle.Raw).ToHashSet();
        foreach (var key in _landingSamples.Keys.Where(k => !live.Contains(k)).ToArray()) _landingSamples.Remove(key);
        foreach (var target in balls)
        {
            var ball = target.Ball;
            var key = ball.EntityHandle.Raw;
            var velocity = N(target.Inherited);
            var origin = N(target.Origin);
            if (_landingSamples.TryGetValue(key, out var previous)
                && Server.TickCount - previous.Tick == 1 && Server.TickCount - State(ball).LastKickTick > 2
                // Do not let today's floor limiter undo the restored wall pop
                // during its four separation frames. Ordinary landings retain it.
                && Server.TickedTime - State(ball).LastWall > (WallAssistSeparationFrames + 1) * Server.TickInterval
                && V3.Distance(origin, previous.Origin) < _kickMaximumBallSpeed * Server.TickInterval * 2
                && previous.Velocity.Z < -80 && velocity.Z > 0
                && !Utilities.GetPlayers().Any(p => IsEligiblePlayer(p) && p.PlayerPawn.Value?.AbsOrigin is { } pos
                    && V3.Distance(N(pos), origin) < BallPushContactDistance + 12))
            {
                // Confirm a nearby flat static floor. This must not clamp
                // volleys, player bounces, ramps, or high wall rebounds.
                var trace = Trace.TraceEndShape(target.Origin, C(origin - V3.UnitZ * (BallCollisionRadius + 30)),
                    ball, new TraceOptions { InteractsWith = Masks.Solid });
                if (trace.DidHit() && trace.Normal.Z >= .95f && IsStaticWallSurface(trace))
                {
                    var limited = BallContactMath.LandingVertical(previous.Velocity.Z, velocity.Z);
                    if (limited < velocity.Z - 1)
                    {
                        Logger.LogInformation("[SM2DIAG] landing_limit ball={Ball} incomingZ={Incoming:F1} outgoingZ={Outgoing:F1} limitedZ={Limited:F1}",
                            ball.Index, previous.Velocity.Z, velocity.Z, limited);
                        velocity.Z = limited;
                        ball.Teleport(velocity: C(velocity));
                        if (target.IsMatchBall) _derivedBallVelocity = C(velocity);
                        else if (_trainingBalls.TryGetValue(ball.Index, out var training)) training.DerivedVelocity = C(velocity);
                        NewBallContact(ball);
                    }
                }
            }
            _landingSamples[key] = (origin, velocity, Server.TickCount);
        }
    }
}

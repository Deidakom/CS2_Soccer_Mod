using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using V3 = System.Numerics.Vector3;

namespace SoccerModMvp;

public sealed partial class SoccerModMvpPlugin
{
    private bool _rollingAssistEnabled = true;
    private readonly Dictionary<uint, (V3 Velocity, double Time)> _rollingSamples = new();

    private void UpdateRollingAssist()
    {
        if (!_rollingAssistEnabled || _pausedBallHandle != 0 || _ballMotionFrozen)
        { _rollingSamples.Clear(); return; }
        var balls = PlayableBalls().ToArray();
        var live = balls.Select(b => b.Ball.EntityHandle.Raw).ToHashSet();
        foreach (var key in _rollingSamples.Keys.Where(k => !live.Contains(k)).ToArray()) _rollingSamples.Remove(key);
        foreach (var target in balls)
        {
            var ball = target.Ball;
            var key = ball.EntityHandle.Raw;
            var state = State(ball);
            var velocity = N(target.Inherited);
            var planar = new V3(velocity.X, velocity.Y, 0);
            var speed = planar.Length();
            var hasPrevious = _rollingSamples.TryGetValue(key, out var previous);
            // Only support already rolling balls, never landings, kicks, held
            // balls, interceptions, or a ball that has naturally stopped.
            if (Server.TickCount - state.LastContactTick < 4
                || (speed <= 4 && !hasPrevious) || speed > 220 || MathF.Abs(velocity.Z) > 12
                || !IsBallGrounded(ball, target.Origin)
                || Utilities.GetPlayers().Any(p => IsEligiblePlayer(p) && p.PlayerPawn.Value?.AbsOrigin is { } pos
                    && V3.Distance(N(pos), N(target.Origin)) < BallPushContactDistance + 12))
            { _rollingSamples.Remove(key); continue; }
            var direction = speed > .01f ? planar / speed : V3.Normalize(previous.Velocity);
            // Check ahead AND behind: a recent wall rebound must separate
            // before the grass rollout can take over. Do not push into walls.
            bool blocked = false;
            foreach (var sign in new[] { -1f, 1f })
            {
                var end = C(N(target.Origin) + direction * sign * (BallCollisionRadius + 24));
                var trace = Trace.TraceEndShape(target.Origin, end, ball, new TraceOptions { InteractsWith = Masks.Solid });
                if (trace.DidHit() && trace.Fraction < 0.999f) { blocked = true; break; }
            }
            if (blocked) { _rollingSamples.Remove(key); continue; }
            var now = Server.TickedTime;
            if (state.RollStart < 0) { state.RollStart = now; state.RollInitialSpeed = speed; }
            var allowance = BallContactMath.RollAllowance(state.RollInitialSpeed, (float)(now - state.RollStart));
            if (hasPrevious)
            {
                var previousSpeed = previous.Velocity.Length();
                var dot = V3.Dot(previous.Velocity / previousSpeed, direction);
                var desired = BallContactMath.RollingSpeed(previousSpeed, speed, dot, (float)(now - previous.Time));
                desired = MathF.Max(desired, BallContactMath.RollingTail(previousSpeed, speed, dot, (float)(now - previous.Time), allowance));
                desired = MathF.Min(desired, allowance);
                if (desired > speed + 0.01f)
                {
                    ball.AcceptInput("Wake");
                    ball.Teleport(velocity: C(direction * desired + V3.UnitZ * velocity.Z));
                    planar = direction * desired;
                }
            }
            if (planar.Length() <= 4 || allowance <= 4) _rollingSamples.Remove(key);
            else _rollingSamples[key] = (planar, now);
        }
    }
}

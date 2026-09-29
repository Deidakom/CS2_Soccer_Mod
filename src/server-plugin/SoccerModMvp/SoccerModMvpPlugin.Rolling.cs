using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using V3 = System.Numerics.Vector3;

namespace SoccerModMvp;

public sealed partial class SoccerModMvpPlugin
{
    private bool _rollingAssistEnabled = true;
    // Speed a clean rolling ball loses per second below 220 u/s; 0 = the
    // original glide (see BallContactMath.BrakedRollSpeed). Realistic: 50-100.
    private float _rollResistance;
    // 2026-09-29 owner (ball feel): CS:S roll-out. Above 65 u/s a clean roll
    // loses this share of its speed per second (CS:S roll capture:
    // 186 -> 112 -> 59 u/s at 1 s steps, about 0.5-0.64), then the slow tail
    // (rolling resistance, or the original glide). 0 = the old linear path.
    private const float DefaultRollDecayPerSecond = 0.6f;
    private float _rollDecayPerSecond = DefaultRollDecayPerSecond;
    private readonly Dictionary<uint, (V3 Velocity, double Time)> _rollingSamples = new();

    private void UpdateRollingAssist()
    {
        if (!_rollingAssistEnabled || _pausedBallHandle != 0)
        { _rollingSamples.Clear(); return; }
        var balls = PlayableBalls().ToArray();
        var live = balls.Select(b => b.Ball.EntityHandle.Raw).ToHashSet();
        foreach (var key in _rollingSamples.Keys.Where(k => !live.Contains(k)).ToArray()) _rollingSamples.Remove(key);
        foreach (var target in balls)
        {
            var ball = target.Ball;
            var key = ball.EntityHandle.Raw;
            // Audit 2026-09-29: only the frozen match ball is skipped; training/cannon balls keep this
            // assist while the kickoff ball sits frozen (the whole assist used to switch off).
            if (target.IsMatchBall && _ballMotionFrozen) { _rollingSamples.Remove(key); continue; }
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
            var decel = _rollResistance > 0 ? _rollResistance : BallContactMath.RollAssistDecel;
            var cssRoll = _rollDecayPerSecond > 0;
            if (state.RollStart < 0) { state.RollStart = now; state.RollInitialSpeed = speed; }
            var allowance = cssRoll
                ? BallContactMath.CssRollAllowance(state.RollInitialSpeed, (float)(now - state.RollStart), _rollDecayPerSecond, decel)
                : BallContactMath.RollAllowance(state.RollInitialSpeed, (float)(now - state.RollStart), decel);
            if (hasPrevious)
            {
                var previousSpeed = previous.Velocity.Length();
                // CS:S roll-out: the loss per second follows the speed (tail
                // rate below the knee); the old path keeps its constant rate.
                var brake = _rollResistance;
                if (cssRoll)
                {
                    decel = BallContactMath.CssRollDecel(previousSpeed, _rollDecayPerSecond, decel);
                    brake = decel;
                }
                var dot = V3.Dot(previous.Velocity / previousSpeed, direction);
                var dt = (float)(now - previous.Time);
                var desired = BallContactMath.RollingSpeed(previousSpeed, speed, dot, dt, decel);
                desired = MathF.Max(desired, BallContactMath.RollingTail(previousSpeed, speed, dot, dt, allowance, decel));
                desired = MathF.Min(desired, allowance);
                if (desired > speed + 0.01f)
                {
                    ball.AcceptInput("Wake");
                    ball.Teleport(velocity: C(direction * desired + V3.UnitZ * velocity.Z));
                    planar = direction * desired;
                }
                else if (dot >= .99f && BallContactMath.BrakedRollSpeed(previousSpeed, speed, brake, dt) is var braked
                    && braked < speed - 0.01f)
                {
                    ball.Teleport(velocity: C(direction * braked + V3.UnitZ * velocity.Z));
                    planar = direction * braked;
                }
            }
            if (planar.Length() <= 4 || allowance <= 4) _rollingSamples.Remove(key);
            else _rollingSamples[key] = (planar, now);
        }
    }
}

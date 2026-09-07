using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using V3 = System.Numerics.Vector3;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private static BallContactMath.Contact? SweepPlayerContact(CCSPlayerPawn pawn, Vector start, Vector end)
    {
        if (pawn.AbsOrigin is not { } origin) return null;
        var mins = pawn.Collision.Mins; var maxs = pawn.Collision.Maxs;
        var height = Math.Max(2, maxs.Z - mins.Z);
        var radius = Math.Clamp(Math.Max(maxs.X - mins.X, maxs.Y - mins.Y) * 0.5f, 1, height * 0.5f);
        var centre = N(origin) + new V3((mins.X + maxs.X) * .5f, (mins.Y + maxs.Y) * .5f, 0);
        return BallContactMath.SweepCapsule(N(start) + N(pawn.AbsVelocity) * Server.TickInterval, N(end),
            centre + V3.UnitZ * (mins.Z + radius), centre + V3.UnitZ * (maxs.Z - radius), radius + BallCollisionRadius);
    }
    private void ApplySweptBallImpact(CPhysicsPropMultiplayer ball, Vector start, Vector end, Vector velocity)
    {
        var state = State(ball);
        if (state.LastKickTick == Server.TickCount) return;
        var now = Server.TickedTime;
        var incoming = N(velocity);
        // Ignore discontinuities from resets/cannons; one engine tick cannot
        // legitimately traverse more than this at the configured speed limit.
        if (V3.Distance(N(start), N(end)) > _kickMaximumBallSpeed * Server.TickInterval * 2) return;
        CCSPlayerController? first = null;
        BallContactMath.Contact? earliest = null;
        foreach (var player in Utilities.GetPlayers().OrderBy(p => p.Slot))
        {
            if (!IsEligiblePlayer(player) || player.PlayerPawn.Value is not { IsValid: true } pawn
                || pawn.AbsOrigin is not { } origin) continue;
            if (state.Impacts.TryGetValue(pawn.EntityHandle.Raw, out var last) && now - last < BallImpactCooldownSeconds) continue;
            var mins = pawn.Collision.Mins; var maxs = pawn.Collision.Maxs;
            var height = Math.Max(2, maxs.Z - mins.Z);
            var radius = Math.Clamp(Math.Max(maxs.X - mins.X, maxs.Y - mins.Y) * 0.5f, 1, height * 0.5f);
            var centre = N(origin) + new V3((mins.X + maxs.X) * 0.5f, (mins.Y + maxs.Y) * 0.5f, 0);
            var bottom = centre + V3.UnitZ * (mins.Z + radius);
            var top = centre + V3.UnitZ * (maxs.Z - radius);
            var playerMotion = N(pawn.AbsVelocity) * Server.TickInterval;
            var hit = BallContactMath.SweepCapsule(N(start) + playerMotion, N(end), bottom, top, radius + BallCollisionRadius);
            if (hit is not { } contact) continue;
            // Evaluate closing at entry, not at the end of a fast crossing.
            // Both gates exclude an overtaking player chasing an outgoing ball.
            if (V3.Dot(incoming, contact.Normal) >= -1
                || V3.Dot(incoming - N(pawn.AbsVelocity), contact.Normal) >= -1) continue;
            if (earliest is null || contact.Fraction < earliest.Value.Fraction - 1e-5f)
            { earliest = contact; first = player; }
        }
        if (first?.PlayerPawn.Value is not { IsValid: true } firstPawn || earliest is not { } impact) return;
        NewBallContact(ball);
        var pawnKey = firstPawn.EntityHandle.Raw;
        state.Impacts[pawnKey] = now;
        var sequence = ++_nextPawnImpact;
        _pawnImpacts[pawnKey] = sequence;
        if (CreativeHandling && _trapUntil.TryGetValue(pawnKey, out var expires) && now <= expires)
        {
            // Cushion once, preserving 20% relative momentum. No attachment,
            // ownership lock, secondary-click or sprint input override.
            _trapUntil[pawnKey] = now;
            ball.Teleport(velocity: C(N(firstPawn.AbsVelocity) + (incoming - N(firstPawn.AbsVelocity)) * _trapRetention));
            RecordBallTouchIfMatch(first, ball, end);
            return;
        }
        var planar = new V3(incoming.X, incoming.Y, 0);
        var push = Math.Min(planar.Length() * _ballImpactPlayerPushRatio, _ballImpactPlayerPushMax);
        if (planar.LengthSquared() > 1)
        {
            var direction = V3.Normalize(planar);
            var target = BallContactMath.ImpactTargetAlong(V3.Dot(N(firstPawn.AbsVelocity), direction), push);
            ApplyBallImpactKnockback(firstPawn, direction.X, direction.Y, target);
            ScheduleContactKnockback(firstPawn, pawnKey, sequence, direction, target, BallImpactKnockbackReapplyFrames);
        }
        ApplyBallImpactFeedback(first, firstPawn, Math.Max(push, Math.Abs(incoming.Z) * _ballImpactPlayerPushRatio));
        V3 rebound;
        if (impact.Normal.Z > 0.45f && incoming.Z < -_ballImpactFallSpeedThreshold)
        {
            rebound = new(incoming.X * _ballImpactBounceHorizontalRetention, incoming.Y * _ballImpactBounceHorizontalRetention,
                Math.Min(-incoming.Z * _ballImpactBounceRestitution, _ballImpactBounceMaxVertical));
        }
        else
        {
            // Reflect the closing normal only; retain the tangent and avoid
            // inventing energy on oblique contacts or changing vertical falls
            // into a horizontal push from an arbitrary direction.
            var relative = incoming - N(firstPawn.AbsVelocity);
            var normalSpeed = V3.Dot(relative, impact.Normal);
            rebound = incoming - impact.Normal * normalSpeed * (1 + _ballImpactBounceRestitution);
            rebound.X *= _ballImpactBounceHorizontalRetention;
            rebound.Y *= _ballImpactBounceHorizontalRetention;
            var limit = Math.Min(_kickMaximumBallSpeed, Math.Max(incoming.Length(), N(firstPawn.AbsVelocity).Length()));
            if (rebound.Length() > limit) rebound = V3.Normalize(rebound) * limit;
        }
        ball.AcceptInput("Wake");
        ball.Teleport(velocity: C(rebound));
        RecordBallTouchIfMatch(first, ball, end);
        Logger.LogInformation("[SM2DIAG] swept_ball_impact ball={Ball} slot={Slot} t={Fraction:F3} normal={Normal} incoming={Incoming} rebound={Rebound}",
            ball.Index, first.Slot, impact.Fraction, impact.Normal, incoming, rebound);
    }
    private void RecordBallTouchIfMatch(CCSPlayerController player, CPhysicsPropMultiplayer ball, Vector origin)
    {
        if (ball.Index == _ball?.Index) RecordBallTouch(player, origin);
    }
    private void ScheduleContactKnockback(CCSPlayerPawn pawn, uint key, int sequence, V3 direction, float target, int frames,
        V3? startPosition = null, byte? initialTeam = null)
    {
        if (frames <= 0) return;
        startPosition ??= pawn.AbsOrigin is { } origin ? N(origin) : V3.Zero;
        initialTeam ??= pawn.TeamNum;
        Server.NextFrame(() =>
        {
            if (!pawn.IsValid || pawn.EntityHandle.Raw != key || !IsAlive(pawn) || pawn.TeamNum != initialTeam
                || _pausedBallHandle != 0 || _matchPhase == MatchPhase.Paused
                || !_pawnImpacts.TryGetValue(key, out var active) || active != sequence) return;
            var pulseTarget = BallContactMath.ImpactPulseTarget(target, BallImpactKnockbackReapplyFrames - frames + 1, BallImpactKnockbackReapplyFrames);
            ApplyBallImpactKnockback(pawn, direction.X, direction.Y, pulseTarget);
            if (frames == 1)
            {
                _pawnImpacts.Remove(key);
                if (pawn.AbsOrigin is { } end)
                    Logger.LogInformation("[SM2DIAG] impact_motion pawn={Pawn} target={Target:F1} displacementAlong={Displacement:F1} finalAlong={Final:F1}",
                        pawn.Index, target, V3.Dot(N(end) - startPosition.Value, direction), V3.Dot(N(pawn.AbsVelocity), direction));
            }
            else ScheduleContactKnockback(pawn, key, sequence, direction, target, frames - 1, startPosition, initialTeam);
        });
    }
}

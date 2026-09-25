using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using V3 = System.Numerics.Vector3;

namespace SoccerModMvp;

// 2026-09-25 owner: "Goal Hit Post TOP" when the ball hits the crossbar with
// force, "Goal Hit Post Sides" for the left/right posts. Detected from the
// ball's own motion: at least GoalFrameHitMinimumSpeed before, a velocity
// change of at least GoalFrameHitMinimumChange in one tick, the ball against
// the frame (BallContactMath.ClassifyGoalFrameHit), no kick and no player
// right at the ball. Short cooldown so one hit plays once. Effects group.
public sealed partial class SoccerModMvpPlugin
{
    internal const string PostTopSoundEvent = "SoccerMod.Goal.PostTop";
    internal const string PostSideSoundEvent = "SoccerMod.Goal.PostSide";
    private const float GoalFrameHitMinimumSpeed = 300f;
    private const float GoalFrameHitMinimumChange = 250f;
    private const double GoalFrameHitCooldownSeconds = 0.4;
    private V3? _goalFramePreviousVelocity;
    private double _lastGoalFrameSound = -10;

    private void GoalFrameSoundOnTick()
    {
        if (_ball is not { IsValid: true } ball || ball.AbsOrigin is not { } origin || _pausedBallHandle != 0)
        {
            _goalFramePreviousVelocity = null;
            return;
        }

        var velocity = N(_derivedBallVelocity);
        var before = _goalFramePreviousVelocity;
        _goalFramePreviousVelocity = velocity;
        if (before is not { } previous || KnifeKickOwnsTick(ball)) return;
        var now = (double)Server.TickedTime;
        if (now - _lastGoalFrameSound < GoalFrameHitCooldownSeconds
            || previous.Length() < GoalFrameHitMinimumSpeed
            || (velocity - previous).Length() < GoalFrameHitMinimumChange) return;
        if (Utilities.GetPlayers().Any(p => IsEligiblePlayer(p) && p.PlayerPawn.Value?.AbsOrigin is { } pos
                && V3.Distance(N(pos), N(origin)) < BallPushContactDistance + 24)) return;

        var hit = BallContactMath.ClassifyGoalFrameHit(N(origin), previous, velocity, BallCollisionRadius,
            _goalHalfWidthX, _goalLineY, StadiumPitchPlaneZ + _goalApertureMaxZ);
        if (hit == BallContactMath.GoalFrameHit.None) return;
        _lastGoalFrameSound = now;
        ball.EmitSound(hit == BallContactMath.GoalFrameHit.Crossbar ? PostTopSoundEvent : PostSideSoundEvent,
            SoundRecipients(SoccerSoundGroup.Effects));
        Logger.LogInformation("[SM2DIAG] goal_frame_hit kind={Kind} speed={Speed:F0} change={Change:F0} origin={Origin}",
            hit, previous.Length(), (velocity - previous).Length(), FormatVector(origin));
    }
}

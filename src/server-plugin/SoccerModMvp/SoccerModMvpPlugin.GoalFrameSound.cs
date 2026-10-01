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
        // 2026-09-26 owner: fast post hits stopped playing their sound. Any
        // sharp stop near a goal that is NOT classified as a frame hit is
        // logged with the reason, so the next miss shows why.
        var nearGoal = MathF.Abs(ToPitchLocal(origin).Y) > GoalLineNow - 200;
        if (Utilities.GetPlayers().Any(p => IsEligiblePlayer(p) && p.PlayerPawn.Value?.AbsOrigin is { } pos
                && V3.Distance(N(pos), N(origin)) < BallPushContactDistance + 24))
        {
            if (nearGoal) LogGoalFrameMiss("player_near", origin, previous, velocity);
            return;
        }

        // Audit 2026-09-29: classify at the frame itself (v8 posts at y 1384, GoalFrame.cs), not at the
        // goal-detection line 1400 - a ball bouncing off the post front sat outside the band.
        var hit = BallContactMath.ClassifyGoalFrameHit(N(ToPitchLocal(origin)), previous, velocity, BallCollisionRadius,
            GoalHalfWidthNow, ActiveFrame is not null ? GoalLineNow : GoalFrameLineY + GoalShiftY, StadiumPitchPlaneZ + GoalApertureMaxNow);
        if (hit == BallContactMath.GoalFrameHit.None)
        {
            if (nearGoal) LogGoalFrameMiss("not_frame", origin, previous, velocity);
            return;
        }
        _lastGoalFrameSound = now;
        ball.EmitSound(hit == BallContactMath.GoalFrameHit.Crossbar ? PostTopSoundEvent : PostSideSoundEvent,
            SoundRecipients(SoccerSound.Posts));
        // 2026-09-25 owner: hitting the frame is a near miss too; the crowd
        // boos - unless the ball goes in off the post, so wait a moment.
        var hitAt = now;
        AtmoPost(hit == BallContactMath.GoalFrameHit.Crossbar, origin);
        AddTimer(0.8f, () => { if (_lastStadiumGoal < hitAt) StadiumBallWide(); });
        Logger.LogInformation("[SM2DIAG] goal_frame_hit kind={Kind} speed={Speed:F0} change={Change:F0} origin={Origin}",
            hit, previous.Length(), (velocity - previous).Length(), FormatVector(origin));
    }

    private double _lastGoalFrameMissLog = -10;

    private void LogGoalFrameMiss(string reason, CounterStrikeSharp.API.Modules.Utils.Vector origin, V3 before, V3 after)
    {
        var now = (double)Server.TickedTime;
        if (now - _lastGoalFrameMissLog < 0.4) return;
        _lastGoalFrameMissLog = now;
        Logger.LogInformation(
            "[SM2DIAG] goal_frame_miss reason={Reason} speed={Speed:F0} change={Change:F0} origin={Origin} halfWidth={HalfWidth:F0} lineY={LineY:F0} crossbarZ={CrossbarZ:F0} radius={Radius:F1}",
            reason, before.Length(), (after - before).Length(), FormatVector(origin), _goalHalfWidthX, _goalLineY,
            StadiumPitchPlaneZ + _goalApertureMaxZ, BallCollisionRadius);
    }

    // Audit 2026-09-29: the ball is at a goal frame (posts, crossbar, the net around them) - used
    // to keep the wall assist's upward hop off posts and nets.
    private bool IsAtGoalFrame(CounterStrikeSharp.API.Modules.Utils.Vector origin)
    {
        var local = ToPitchLocal(origin);
        return MathF.Abs(MathF.Abs(local.Y) - (ActiveFrame is not null ? GoalLineNow : GoalFrameLineY + GoalShiftY)) <= 120f
            && MathF.Abs(local.X) <= GoalHalfWidthNow + BallCollisionRadius + 40f
            && local.Z <= StadiumPitchPlaneZ + GoalApertureMaxNow + BallCollisionRadius + 40f;
    }
}

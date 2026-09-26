using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-25 owner: a "ball hits the net" sound (owner's file) when the ball
// goes into the goal, once, never repeating. It plays at the goal detection
// (the whole ball is in the goal); the ball is reset right after, so it
// cannot rattle around in the net. A goal locks detection until the next
// kickoff, and the flag below makes a second trigger impossible until the
// ball is reset or a round starts. Heard by everyone at full volume (event in
// soundevents/soccermod_calls.vsndevts, Workshop item 3797479770).
public sealed partial class SoccerModMvpPlugin
{
    internal const string GoalNetSoundEvent = "SoccerMod.Goal.Net";
    // 2026-09-26 owner: the net sound was missing. It was once per ROUND, but a
    // match goal does not start a new round (the ball just goes back to the
    // centre), so only the first goal had it. Now a 3 s lockout per goal.
    private const float GoalNetSoundLockoutSeconds = 3.0f;
    private float _goalNetSoundLastTime = -100f;

    private void GoalNetSoundOnLoad()
    {
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            _goalNetSoundLastTime = -100f;
            return HookResult.Continue;
        });
    }

    private void PlayGoalNetSound()
    {
        if (Server.CurrentTime - _goalNetSoundLastTime < GoalNetSoundLockoutSeconds || _ball is not { IsValid: true } ball) return;
        _goalNetSoundLastTime = Server.CurrentTime;
        ball.EmitSound(GoalNetSoundEvent, SoundRecipients(SoccerSound.GoalNet));
        Logger.LogInformation("[SM2DIAG] goal_net_sound");
    }
}

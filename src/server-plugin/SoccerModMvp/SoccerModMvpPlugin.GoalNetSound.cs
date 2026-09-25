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
    private bool _goalNetSoundPlayed;

    private void GoalNetSoundOnLoad()
    {
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            _goalNetSoundPlayed = false;
            return HookResult.Continue;
        });
    }

    private void PlayGoalNetSound()
    {
        if (_goalNetSoundPlayed || _ball is not { IsValid: true } ball) return;
        _goalNetSoundPlayed = true;
        ball.EmitSound(GoalNetSoundEvent, SoundRecipients(SoccerSound.GoalNet));
        Logger.LogInformation("[SM2DIAG] goal_net_sound");
    }
}

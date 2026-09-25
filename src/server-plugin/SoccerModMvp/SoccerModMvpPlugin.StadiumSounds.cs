using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-25 owner: the XSL stadium's atmosphere in CS2. The CS:S map
// (ka_soccer_xsl_stadium_b1) has no constant background loop; it plays five
// ka_soccer_2006 sounds from triggers: kickoff whistle + crowd air horn when
// the kickoff is taken, goal whistle + cheering crowd on a goal, and booing
// when the ball leaves the pitch. Here: kickoff = the first player touch of a
// frozen kickoff ball, goal = OnGoalScored, boo = a ball crossing a goal line
// outside the goal. Heard by everyone who has "Stadium" sounds on.
public sealed partial class SoccerModMvpPlugin
{
    internal const string StadiumWhistleKickoff = "SoccerMod.Stadium.WhistleKickoff";
    internal const string StadiumAirhorn = "SoccerMod.Stadium.Airhorn";
    internal const string StadiumWhistleGoal = "SoccerMod.Stadium.WhistleGoal";
    internal const string StadiumCrowdGoal = "SoccerMod.Stadium.CrowdGoal";
    internal const string StadiumBoo = "SoccerMod.Stadium.Boo";
    private const double StadiumBooCooldownSeconds = 5.0;
    private double _lastStadiumBoo = -100;

    // Player touches that take a kickoff (UnfreezeBallForPlay reasons).
    private static readonly HashSet<string> KickoffTouchReasons = new(StringComparer.Ordinal)
    {
        "primary_kick", "wall_pop_kick", "body_contact", "body_approach", "body_push",
    };

    private void StadiumSoundsOnLoad()
    {
        MigrateSoundGroups();
    }

    private void PlayStadiumSounds(params string[] events)
    {
        if (_ball is not { IsValid: true } ball) return;
        var recipients = SoundRecipients(SoccerSound.Stadium);
        foreach (var soundEvent in events) ball.EmitSound(soundEvent, recipients);
    }

    private void StadiumKickoffTaken(string reason)
    {
        if (!KickoffTouchReasons.Contains(reason)) return;
        PlayStadiumSounds(StadiumWhistleKickoff, StadiumAirhorn);
        Logger.LogInformation("[SM2DIAG] stadium_sound kind=kickoff reason={Reason}", reason);
    }

    private void StadiumGoal() => PlayStadiumSounds(StadiumWhistleGoal, StadiumCrowdGoal);

    private void StadiumBallWide()
    {
        var now = (double)Server.TickedTime;
        if (now - _lastStadiumBoo < StadiumBooCooldownSeconds) return;
        _lastStadiumBoo = now;
        PlayStadiumSounds(StadiumBoo);
    }
}

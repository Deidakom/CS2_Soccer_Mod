using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-25 owner: the XSL stadium's atmosphere in CS2. The CS:S map
// (ka_soccer_xsl_stadium_b1) has no constant background loop; it plays five
// ka_soccer_2006 sounds from triggers: kickoff whistle + crowd air horn when
// the kickoff is taken, goal whistle + cheering crowd on a goal, and booing
// when the ball leaves the pitch. Here: kickoff = right after every round
// reset (owner, 2026-09-25: no need to wait for the first touch), goal =
// OnGoalScored, boo = a fast shot just missing the frame. Heard by everyone
// who has "Stadium" sounds on.
public sealed partial class SoccerModMvpPlugin
{
    internal const string StadiumWhistleKickoff = "SoccerMod.Stadium.WhistleKickoff";
    internal const string StadiumAirhorn = "SoccerMod.Stadium.Airhorn";
    internal const string StadiumWhistleGoal = "SoccerMod.Stadium.WhistleGoal";
    internal const string StadiumCrowdGoal = "SoccerMod.Stadium.CrowdGoal";
    internal const string StadiumBoo = "SoccerMod.Stadium.Boo";
    private const double StadiumBooCooldownSeconds = 5.0;
    private double _lastStadiumBoo = -100;
    private double _lastStadiumGoal = -100;

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

    // A short delay so the round restart has rebuilt the ball the sound
    // plays from.
    private void StadiumRoundReset()
    {
        AddTimer(0.3f, () =>
        {
            PlayStadiumSounds(StadiumWhistleKickoff, StadiumAirhorn);
            Logger.LogInformation("[SM2DIAG] stadium_sound kind=kickoff reason=round_reset");
        });
    }

    private void StadiumGoal()
    {
        _lastStadiumGoal = Server.TickedTime;
        PlayStadiumSounds(StadiumWhistleGoal, StadiumCrowdGoal);
    }

    private void StadiumBallWide()
    {
        // 2026-09-25 owner: no booing while a training cannon is firing.
        if (CannonGoalsSuppressed) return;
        var now = (double)Server.TickedTime;
        if (now - _lastStadiumBoo < StadiumBooCooldownSeconds) return;
        _lastStadiumBoo = now;
        PlayStadiumSounds(StadiumBoo);
    }
}

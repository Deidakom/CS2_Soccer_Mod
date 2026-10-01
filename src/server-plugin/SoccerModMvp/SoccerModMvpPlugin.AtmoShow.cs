using System.Drawing;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-29 "Arena Vision" matchday show (v8 only, admin toggle "Matchday
// show"). Owner: only in matches an admin started, short, no tunnel walk-out
// for now. Match start: the ring splits into the two team colours, both ends
// light flares, gold fireworks, the crowds wave, the boards show the SoccerMod
// banner. Half time: fair-play page and a warm ring sweep. Full time: the
// winner's colour everywhere - confetti over both goals, fireworks, the
// winner's end cheers (a draw gets gold). Everything is timed so it is over
// before the kickoff whistle and never blocks play.
public sealed partial class SoccerModMvpPlugin
{

    private void AtmoShowPhase(AtmoMoment moment)
    {
        if (!AtmoOn || !AtmoSet.MatchdayShow) return;
        switch (moment)
        {
            case AtmoMoment.MatchStart: AtmoShowMatchStart(); break;
            case AtmoMoment.HalfTime: AtmoShowHalfTime(); break;
            case AtmoMoment.FullTime: AtmoShowFullTime(); break;
        }
    }

    private void AtmoShowMatchStart()
    {
        if (AtmoSet.LightRing) AtmoRingPlay(AtmoRingMode.Split, AtmoRingNeutral, 8.0);
        if (AtmoSet.LedBoards) AtmoBoardsHold(AtmoBoardBanner, 8.0);
        AtmoCrowdPlay("wave", 6.0);
        if (AtmoSet.Pyro)
            foreach (var (colour, fan) in new[] { ("red", 1), ("blue", -1) })
                for (var x = -900f; x <= 900f; x += 450f)
                    AtmoParticle($"flare_{colour}", AtmoEndRowSpot(x, fan), 9.0);
        if (AtmoSet.Fireworks)
            for (var i = 0; i < 6; i++)
            {
                var side = i % 2 == 0 ? 1 : -1;
                var x = -800f + (i / 2) * 800f;
                AtmoLater(0.8 + i * 0.4, () => AtmoParticle("firework_gold", new Vector(x, side * 1500f, 1450f), 5.0));
            }
        if (AtmoSet.CameraFlashes)
            foreach (var (x, y) in AtmoStandCentres) AtmoParticle("camera_flashes", new Vector(x, y, 420f), 5.0);
        Logger.LogInformation("[SM2DIAG] atmo_show phase=match_start");
    }

    private void AtmoShowHalfTime()
    {
        if (AtmoSet.LightRing) AtmoRingPlay(AtmoRingMode.Sweep, AtmoRingNeutral, 3.0);
        if (AtmoSet.LedBoards) AtmoBoardsHold(AtmoBoardFairPlay, 12.0);
        Logger.LogInformation("[SM2DIAG] atmo_show phase=half_time");
    }

    private void AtmoShowFullTime()
    {
        var winner = _scoreT > _scoreCt ? 0 : _scoreCt > _scoreT ? 1 : -1;   // 0 red, 1 blue, -1 draw
        var colour = winner switch { 0 => "red", 1 => "blue", _ => "gold" };
        var ring = winner switch { 0 => AtmoRingRed, 1 => AtmoRingBlue, _ => AtmoRingNeutral };
        if (AtmoSet.LightRing) AtmoRingPlay(AtmoRingMode.Chase, ring, 12.0);
        if (AtmoSet.LedBoards)
        {
            if (winner >= 0) AtmoBoardsHold(winner == 0 ? AtmoBoardGoalRed : AtmoBoardGoalBlue, 3.0, then: AtmoBoardBanner, thenSeconds: 12.0);
            else AtmoBoardsHold(AtmoBoardBanner, 15.0);
        }
        if (winner >= 0) { AtmoCrowdGoal(winner, 12.0); AtmoDugoutsGoal(winner == 0, 12.0); }
        else AtmoCrowdPlay("cheer", 10.0);
        if (AtmoSet.Pyro)
            foreach (var y in new[] { 1250f, -1250f })
                AtmoLater(0.5, () => AtmoParticle($"confetti_rain_{colour}", new Vector(0f, y, 1100f), 14.0));
        if (AtmoSet.Fireworks)
            for (var i = 0; i < 10; i++)
            {
                var x = -1350f + (i % 5) * 675f;
                var y = i < 5 ? 1500f : -1500f;
                var effect = i % 3 == 2 ? "firework_gold" : $"firework_{colour}";
                var z = 1400f + (i % 2) * 250f;   // copied: the delayed action must not read the loop variable
                AtmoLater(1.0 + i * 0.45, () => AtmoParticle(effect, new Vector(x, y, z), 5.0));
            }
        Logger.LogInformation("[SM2DIAG] atmo_show phase=full_time winner={Winner}", colour);
    }
}

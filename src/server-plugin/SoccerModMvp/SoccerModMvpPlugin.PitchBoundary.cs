using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;

// 2026-09-24 user report: the sideline railing has an opening at the halfway
// line on both sides (tunnel doors), and players walked off the pitch there.
// The railing stands on the side-wall plane (FoundationWallPlaneX), so a
// player centre is held one player radius inside it along the whole touchline.
// Where the railing exists the clamp never engages; it only closes the gaps.
// Nothing is enforced away from the halfway-line openings (see below).
// 2026-09-25 owner: outside a match everyone may leave the pitch through the
// tunnels again; the guard only holds while a match is running.
public sealed partial class SoccerModMvpPlugin
{
    private const float PitchBoundaryPlayerRadius = 16.0f;
    // 2026-09-25 owner report: kicks from the touchline behaved oddly during
    // a match. A player pressed against the railing sits a hair past the
    // exact limit (float rounding), so the clamp teleported them every tick
    // and disturbed body contact with the ball. The clamp now only acts in
    // the tunnel openings at the halfway line (doors at |y| < ~470) and with
    // a small tolerance; the railing itself does the rest.
    private const float PitchBoundaryGapHalfLength = 520.0f;
    private const float PitchBoundaryTolerance = 2.0f;

    private bool PitchBoundaryActive => _matchPhase is MatchPhase.Countdown or MatchPhase.Live
        or MatchPhase.GoalPause or MatchPhase.PeriodBreak or MatchPhase.Paused;

    private void PitchBoundaryOnTick()
    {
        if (!PitchBoundaryActive) return;
        var limit = FoundationWallPlaneX - PitchBoundaryPlayerRadius;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)
                || player.PlayerPawn.Value is not { IsValid: true } pawn || !IsAlive(pawn)
                || pawn.AbsOrigin is not { } origin)
            {
                continue;
            }

            if (Math.Abs(origin.Y) > PitchBoundaryGapHalfLength || Math.Abs(origin.X) <= limit + PitchBoundaryTolerance) continue;
            var velocity = pawn.AbsVelocity;
            var outward = Math.Sign(origin.X);
            var newVelocity = new Vector(Math.Sign(velocity.X) == outward ? 0.0f : velocity.X, velocity.Y, velocity.Z);
            pawn.Teleport(new Vector(outward * limit, origin.Y, origin.Z), null, newVelocity);
        }
    }
}

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;

// 2026-09-24 user report: the sideline railing has an opening at the halfway
// line on both sides (tunnel doors), and players walked off the pitch there.
// The railing stands on the side-wall plane (FoundationWallPlaneX), so a
// player centre is held one player radius inside it along the whole touchline.
// Where the railing exists the clamp never engages; it only closes the gaps.
// Behind the goal lines (|y| > PitchBoundaryHalfLength) nothing is enforced.
public sealed partial class SoccerModMvpPlugin
{
    private const float PitchBoundaryPlayerRadius = 16.0f;
    private const float PitchBoundaryHalfLength = 1400.0f;

    private void PitchBoundaryOnTick()
    {
        var limit = FoundationWallPlaneX - PitchBoundaryPlayerRadius;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)
                || player.PlayerPawn.Value is not { IsValid: true } pawn || !IsAlive(pawn)
                || pawn.AbsOrigin is not { } origin)
            {
                continue;
            }

            if (Math.Abs(origin.Y) > PitchBoundaryHalfLength || Math.Abs(origin.X) <= limit) continue;
            var velocity = pawn.AbsVelocity;
            var outward = Math.Sign(origin.X);
            var newVelocity = new Vector(Math.Sign(velocity.X) == outward ? 0.0f : velocity.X, velocity.Y, velocity.Z);
            pawn.Teleport(new Vector(outward * limit, origin.Y, origin.Z), null, newVelocity);
        }
    }
}

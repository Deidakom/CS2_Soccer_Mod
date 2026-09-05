using System.Numerics;

namespace SoccerModMvp;
internal static class KickoffBoundary
{
    // Semicircle extends into the receiving team's half, as in SoMoE's
    // wallcircle. Coordinates are relative to the configured ball reset.
    internal static float Edge(float x, float radius)
        => Math.Abs(x) < radius ? -MathF.Sqrt(radius * radius - x * x) : 0;

    internal static (Vector3 Position, Vector3 Velocity, bool Changed) Constrain(
        Vector3 position, Vector3 velocity, Vector3 centre, int kickingHomeSign, bool kickingPlayer, float radius = 252.5f)
    {
        var x = position.X - centre.X;
        var y = (position.Y - centre.Y) * kickingHomeSign;
        var edge = Edge(x, radius);
        var limit = edge + (kickingPlayer ? 16 : -16);
        if (kickingPlayer ? y >= limit : y <= limit) return (position, velocity, false);
        position.Y = centre.Y + limit * kickingHomeSign;
        // Remove only the forbidden normal component; keep running parallel
        // to the boundary and retain jump/fall velocity.
        if (kickingPlayer ? velocity.Y * kickingHomeSign < 0 : velocity.Y * kickingHomeSign > 0) velocity.Y = 0;
        return (position, velocity, true);
    }
}

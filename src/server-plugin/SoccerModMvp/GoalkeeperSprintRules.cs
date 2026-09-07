using System.Numerics;

namespace SoccerModMvp;

internal static class GoalkeeperSprintRules
{
    // 70% of the sprint BONUS, not 70% of total running speed (which would
    // make a 1.25x sprint slower than ordinary running).
    internal static float SpeedMultiplier(float normalSprint) => 1 + (normalSprint - 1) * 0.70f;

    internal static bool InBox(Vector3 feet,
        (float minX, float maxX, float minY, float maxY, float minZ, float maxZ) box) =>
        float.IsFinite(feet.X) && float.IsFinite(feet.Y) && float.IsFinite(feet.Z)
        && feet.X >= box.minX && feet.X <= box.maxX
        && feet.Y >= box.minY && feet.Y <= box.maxY
        // A small floor tolerance accommodates the pawn's standing origin.
        && feet.Z >= box.minZ - 2 && feet.Z <= box.maxZ;
}

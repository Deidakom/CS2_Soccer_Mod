namespace SoccerModMvp;

// Values, not native entity wrappers: the sample must survive movement and respawn.
internal readonly record struct PlayerActivitySample(
    float X, float Y, float Z, float Pitch, float Yaw, ulong Buttons)
{
    public int UnchangedComponents(PlayerActivitySample current)
    {
        var unchanged = 0;
        if (MathF.Abs(X - current.X) < 1.0f && MathF.Abs(Y - current.Y) < 1.0f
            && MathF.Abs(Z - current.Z) < 1.0f)
            unchanged++;
        if (MathF.Abs(Pitch - current.Pitch) < 1.0f && MathF.Abs(Yaw - current.Yaw) < 1.0f)
            unchanged++;
        if (Buttons == current.Buttons)
            unchanged++;
        return unchanged;
    }
}

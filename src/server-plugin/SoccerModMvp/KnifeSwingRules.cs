namespace SoccerModMvp;

internal static class KnifeSwingRules
{
    internal const double Window = 0.08;
    internal static bool WithinWindow(double now, double started) => now >= started && now - started <= Window;
    internal static bool AimUnchanged(float pitch, float yaw, float initialPitch, float initialYaw) =>
        Math.Abs(pitch - initialPitch) <= 8 && Math.Abs(Math.IEEERemainder(yaw - initialYaw, 360)) <= 8;
}

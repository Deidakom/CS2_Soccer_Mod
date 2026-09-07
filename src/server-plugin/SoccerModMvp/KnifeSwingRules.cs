namespace SoccerModMvp;

internal static class KnifeSwingRules
{
    internal const double Window = 0.08;
    // Repeated held input is rate-limited even when a swing misses the ball.
    // A miss must not disarm the hold or turn it into a per-tick kick attempt.
    internal static double NextHeldSwing(double now, double cooldown) => now + Math.Max(.48, cooldown);
    internal static bool HeldSwingDue(double now, double next, bool held) => held && now >= next;
    internal static bool WithinWindow(double now, double started) => now >= started && now - started <= Window;
    internal static bool AimUnchanged(float pitch, float yaw, float initialPitch, float initialYaw) =>
        Math.Abs(pitch - initialPitch) <= 8 && Math.Abs(Math.IEEERemainder(yaw - initialYaw, 360)) <= 8;
}

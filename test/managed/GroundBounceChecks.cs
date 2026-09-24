using SoccerModMvp;

internal static class GroundBounceChecks
{
    internal static void Run()
    {
        static void Check(bool condition, string why) { if (!condition) throw new Exception(why); }
        static bool Near(float? a, float b) => a is { } v && Math.Abs(v - b) < 1e-4f;

        // A 400 u/s landing on grass comes back at 0.55 x 400 = 220 u/s.
        Check(Near(BallContactMath.GroundBounceVertical(-400, -20, .55f, 120), 220), "engine rebound raised to 55%");
        Check(Near(BallContactMath.GroundBounceVertical(-400, 60, .55f, 120), 220), "weak engine rebound raised");
        // The engine already bounced more: leave it alone.
        Check(BallContactMath.GroundBounceVertical(-400, 250, .55f, 120) is null, "stronger engine rebound kept");
        // Still falling fast: not the contact tick yet.
        Check(BallContactMath.GroundBounceVertical(-400, -300, .55f, 120) is null, "no bounce before contact");
        // Soft landings and rolling are left alone, so the ball settles.
        Check(BallContactMath.GroundBounceVertical(-100, 0, .55f, 120) is null, "below minimum impact");
        Check(BallContactMath.GroundBounceVertical(0, 0, .55f, 120) is null, "rolling ball");
        // Off.
        Check(BallContactMath.GroundBounceVertical(-400, -20, 0, 120) is null, "0 disables the assist");
        // Successive bounces shrink geometrically: 400 -> 220 -> 121 (below 120 stops next time).
        var second = BallContactMath.GroundBounceVertical(-220, 0, .55f, 120);
        Check(Near(second, 121), "second bounce smaller");
        Check(BallContactMath.GroundBounceVertical(-(second ?? 0) + 5, 0, .55f, 120) is null, "bounces die out");

        Console.WriteLine("Ground bounce checks passed (target rebound, engine kept when stronger, settles).");
    }
}

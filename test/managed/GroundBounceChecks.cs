using SoccerModMvp;

internal static class GroundBounceChecks
{
    internal static void Run()
    {
        static void Check(bool condition, string why) { if (!condition) throw new Exception(why); }
        static bool Near(float? a, float b) => a is { } v && Math.Abs(v - b) < 1e-3f;
        static float E(float e, float speed) => BallContactMath.GroundBounceRestitutionAt(e, speed);

        // Restitution depends on impact speed: x1.1 soft, x1.0 at 750 u/s, x0.9 from 1500 u/s.
        Check(Math.Abs(E(.55f, 0) - .605f) < 1e-4f, "soft landing is a little bouncier");
        Check(Math.Abs(E(.55f, 750) - .55f) < 1e-4f, "setting is the mid-speed value");
        Check(Math.Abs(E(.55f, 1500) - .495f) < 1e-4f && Math.Abs(E(.55f, 4000) - .495f) < 1e-4f, "hard landings clamp at x0.9");

        // A 400 u/s landing: the engine's weak rebound is raised to the target.
        Check(Near(BallContactMath.GroundBounceVertical(-400, -20, .55f, 80), 400 * E(.55f, 400)), "engine rebound raised");
        Check(Near(BallContactMath.GroundBounceVertical(-400, 60, .55f, 80), 400 * E(.55f, 400)), "weak engine rebound raised");
        Check(BallContactMath.GroundBounceVertical(-400, 300, .55f, 80) is null, "stronger engine rebound kept");
        Check(BallContactMath.GroundBounceVertical(-400, -300, .55f, 80) is null, "no bounce before contact");
        Check(BallContactMath.GroundBounceVertical(-70, 0, .55f, 80) is null, "below minimum impact");
        Check(BallContactMath.GroundBounceVertical(0, 0, .55f, 80) is null, "rolling ball");
        Check(BallContactMath.GroundBounceVertical(-400, -20, 0, 80) is null, "0 disables the assist");

        // Bounces die out: each rebound is below the previous impact, then below the minimum.
        float impact = 600; var bounces = 0;
        while (BallContactMath.GroundBounceVertical(-impact, 0, .55f, 80) is { } next) { Check(next < impact, "each bounce smaller"); impact = next; bounces++; }
        Check(bounces is >= 2 and <= 6, $"a 600 u/s drop bounces a few times ({bounces})");

        // Grass grip: loss = grip x (impact + rebound); first landing at most 25%, later bounces at most 5%.
        Check(Math.Abs(BallContactMath.GroundBouncePlanarScale(1000, 100, 60, .25f, true) - (1 - .25f * 160 / 1000)) < 1e-5f, "skim loses a little");
        Check(Math.Abs(BallContactMath.GroundBouncePlanarScale(300, 800, 440, .25f, true) - .75f) < 1e-5f, "steep first landing capped at 25%");
        Check(Math.Abs(BallContactMath.GroundBouncePlanarScale(300, 800, 440, .25f, false) - .95f) < 1e-5f, "later bounce capped at 5%");
        // A long ball keeps rolling: 1000 u/s forward, four steep bounces still leave more than 60%.
        var forward = 1000f; var first = true;
        foreach (var drop in new[] { 900f, 500f, 280f, 150f }) { forward *= BallContactMath.GroundBouncePlanarScale(forward, drop, drop * .55f, .25f, first); first = false; }
        Check(forward > 600f, $"long ball rolls on after its bounces ({forward:F0})");
        Check(BallContactMath.GroundBouncePlanarScale(0.5f, 400, 220, .25f, true) == 1f, "no forward speed, nothing to lose");
        Check(BallContactMath.GroundBouncePlanarScale(500, 400, 220, 0, true) == 1f, "grip 0 keeps forward speed");

        Console.WriteLine("Ground bounce checks passed (speed-dependent restitution, grass grip, bounces die out).");
    }
}

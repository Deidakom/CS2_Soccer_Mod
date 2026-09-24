using System.Numerics;
using SoccerModMvp;

// 2026-09-24 ball analysis follow-up: landing ratio, rolling resistance, curve in flight.
internal static class BallRealismChecks
{
    internal static void Run()
    {
        static void Check(bool condition, string why) { if (!condition) throw new Exception(why); }
        const float tick = 1f / 64, radius = 18.805f;

        // The landing limiter follows the ground-bounce ratio; 0.55 stays the default.
        Check(BallContactMath.LandingVertical(-200, 600) == 110 && MathF.Abs(BallContactMath.LandingVertical(-200, 600, .6f) - 120) < 1e-3f,
            "landing limiter ratio");

        // Rolling resistance: 0 keeps the original glide.
        Check(BallContactMath.BrakedRollSpeed(100, 99.9f, 0, tick) == 99.9f, "0 = no brake");
        // 60 per second stops a 200 u/s roll after ~3.3 s, and the last few u/s stop it dead.
        float roll = 200; var ticks = 0;
        while (roll > 0 && ticks < 64 * 10) { roll = BallContactMath.BrakedRollSpeed(roll, roll - .02f, 60, tick); ticks++; }
        Check(roll == 0 && ticks is > 64 * 3 and < 64 * 4, $"200 u/s at 60 per second stops after ~3.3 s ({ticks / 64f:F2} s)");
        Check(BallContactMath.BrakedRollSpeed(100, 90, 60, tick) == 90, "an engine loss above the brake is left to the rollout bridge");
        Check(BallContactMath.BrakedRollSpeed(100, 130, 60, tick) == 130, "speeding up is not braked");
        Check(BallContactMath.BrakedRollSpeed(100, 99, 60, .5f) == 99, "stale sample is not braked");
        Check(BallContactMath.RollAllowance(200, 1, 60) == 140 && BallContactMath.RollAllowance(200, 1) == 194, "allowance follows the brake");
        Check(BallContactMath.RollingSpeed(100, 0.5f, 1, tick, 60) == 0.5f, "a stopped ball is not restarted");
        Console.WriteLine("Rolling resistance checks passed (7 scenarios).");

        // Curve in flight: only side spin (about the vertical axis) bends the ball.
        var v = new Vector3(1000, 0, 200);
        Check(BallContactMath.MagnusCurveStep(v, 0, radius, 1, tick) == v, "no side spin, no curve");
        Check(BallContactMath.MagnusCurveStep(v, 8, radius, 0, tick) == v, "strength 0 = off");
        var left = BallContactMath.MagnusCurveStep(v, 8, radius, 1, tick);
        Check(left.Y > 0 && MathF.Abs(new Vector2(left.X, left.Y).Length() - 1000) < .01f && left.Z == 200,
            "counter-clockwise spin bends left and keeps speed and height");
        Check(BallContactMath.MagnusCurveStep(v, -8, radius, 1, tick).Y < 0, "clockwise spin bends right");
        // Spin factor 0.1, edge-on kick: ~8 rad/s. A 0.6 s flight at 1450 u/s bends ~40 u (analysis estimate).
        var position = Vector3.Zero; var velocity = new Vector3(1450, 0, 0);
        for (var i = 0; i < 38; i++) { velocity = BallContactMath.MagnusCurveStep(velocity, 8, radius, 1, tick); position += velocity * tick; }
        Check(position.Y is > 30 and < 50, $"0.6 s flight bends ~40 u ({position.Y:F1})");
        // Above a spin parameter of 0.3 the pull levels off like a real ball's.
        var levelled = BallContactMath.MagnusCurveStep(v, 100, radius, 1, tick);
        var atLimit = BallContactMath.MagnusCurveStep(v, .3f * 1000 / radius, radius, 1, tick);
        Check(Vector3.Distance(levelled, atLimit) < .01f, "spin parameter levels off at 0.3");
        Check(BallContactMath.MagnusCurveStep(new Vector3(0, 0, -300), 8, radius, 1, tick) == new Vector3(0, 0, -300), "falling straight down: nothing to bend");
        Console.WriteLine("Curve in flight checks passed (7 scenarios).");
    }
}

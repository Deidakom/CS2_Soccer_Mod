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

        // Ball-hits-player push follows the contact normal (CS:S parity).
        var ballWest = new Vector3(-1000, 0, 0); // the player faces +X, the ball comes at him
        var (headDir, headSpeed) = BallContactMath.ImpactPushAlongNormal(ballWest, new Vector3(1, 0, 0));
        Check(Vector2.Distance(headDir, new Vector2(-1, 0)) < 1e-4f && MathF.Abs(headSpeed - 1000) < .01f, "head-on: straight back, full speed");
        var leftSide = Vector3.Normalize(new Vector3(1, 1, 0)); // contact on the player's left front
        var (leftDir, leftSpeed) = BallContactMath.ImpactPushAlongNormal(ballWest, leftSide);
        Check(leftDir.X < 0 && leftDir.Y < 0 && MathF.Abs(leftSpeed - 707.1f) < .5f, "hit on the left: pushed back-right, less");
        var (glanceDir, glanceSpeed) = BallContactMath.ImpactPushAlongNormal(ballWest, new Vector3(0, 1, 0));
        Check(glanceSpeed < .01f && glanceDir.Y < 0, "pure side graze: no push along the travel line");
        var (dropDir, dropSpeed) = BallContactMath.ImpactPushAlongNormal(new Vector3(300, 0, -600), new Vector3(0, 0, 1));
        Check(Vector2.Distance(dropDir, new Vector2(1, 0)) < 1e-4f && MathF.Abs(dropSpeed - 300) < .01f, "ball dropping on the head keeps its travel direction");
        Console.WriteLine("Contact-normal push checks passed (4 scenarios).");

        // The frozen kickoff ball is released just before a player running at it arrives.
        Check(BallContactMath.ClosingOnBall(new Vector3(250, 0, 0), 1, 0, 20), "running straight at the ball");
        Check(!BallContactMath.ClosingOnBall(new Vector3(0, 250, 0), 1, 0, 20), "running past it does not release it");
        Check(!BallContactMath.ClosingOnBall(new Vector3(-250, 0, 0), 1, 0, 20), "walking away does not release it");
        Check(!BallContactMath.ClosingOnBall(new Vector3(10, 0, 0), 1, 0, 20), "standing next to it does not release it");
        Console.WriteLine("Kickoff release checks passed (4 scenarios).");

        // Panorama sprint bar: 21 fill steps of 5.
        Check(SprintBarView.FillStep(100) == 100 && SprintBarView.FillStep(0) == 0 && SprintBarView.FillStep(62.4f) == 60
            && SprintBarView.FillStep(63) == 65 && SprintBarView.FillStep(float.NaN) == 0 && SprintBarView.FillStep(140) == 100,
            "sprint bar fill steps");
        Console.WriteLine("Sprint bar fill-step checks passed.");

        // Goal frame hits (line y=1400, posts |x|=127, bar underside z=68, ball r=16.4).
        const float r = 16.4f, hw = 127f, lineY = 1400f, bar = 68f;
        var postHit = BallContactMath.ClassifyGoalFrameHit(new Vector3(-140, 1392, -10), new Vector3(0, 900, 0), new Vector3(300, -200, 0), r, hw, lineY, bar);
        Check(postHit == BallContactMath.GoalFrameHit.Post, "ball bouncing off the left post");
        var barHit = BallContactMath.ClassifyGoalFrameHit(new Vector3(20, 1395, 55), new Vector3(0, 900, 300), new Vector3(0, 600, -250), r, hw, lineY, bar);
        Check(barHit == BallContactMath.GoalFrameHit.Crossbar, "ball clipping the crossbar");
        var groundBounce = BallContactMath.ClassifyGoalFrameHit(new Vector3(-130, 1395, -15), new Vector3(0, 400, -500), new Vector3(0, 380, 280), r, hw, lineY, bar);
        Check(groundBounce == BallContactMath.GoalFrameHit.None, "a ground bounce next to the post is not a post hit");
        var midfield = BallContactMath.ClassifyGoalFrameHit(new Vector3(-127, 200, -10), new Vector3(0, 900, 0), new Vector3(0, -600, 0), r, hw, lineY, bar);
        Check(midfield == BallContactMath.GoalFrameHit.None, "far from the goal mouth: nothing");
        var inside = BallContactMath.ClassifyGoalFrameHit(new Vector3(0, 1395, -12), new Vector3(0, 900, 0), new Vector3(0, -300, 0), r, hw, lineY, bar);
        Check(inside == BallContactMath.GoalFrameHit.None, "centre of the mouth at ground height: neither post nor bar");
        Console.WriteLine("Goal frame hit checks passed (5 scenarios).");
    }
}

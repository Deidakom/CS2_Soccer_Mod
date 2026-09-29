using System.Numerics;
using SoccerModMvp;

// 2026-09-29 ball feel dials: CS:S launch angles, CS:S roll-out, body rebound
// with friction, recent approach speed and the legacy fill for old presets.
internal static class BallFeelDialChecks
{
    internal static void Run()
    {
        static void Check(bool condition, string why) { if (!condition) throw new Exception(why); }
        static bool Near(float a, float b, float tolerance = .01f) => MathF.Abs(a - b) <= tolerance;

        // CS:S view pitch -> launch angle (positive pitch = looking up).
        Check(Near(BallContactMath.CssLaunchDegrees(-60), 2) && Near(BallContactMath.CssLaunchDegrees(-30), 2),
            "Looking down 30+ degrees keeps the ball flat (2).");
        Check(Near(BallContactMath.CssLaunchDegrees(-15), 6.3f), "Half way to level is linear (6.3).");
        Check(Near(BallContactMath.CssLaunchDegrees(0), 10.6f), "A level view gives the measured 10.6.");
        Check(Near(BallContactMath.CssLaunchDegrees(10), 22.8f), "Half way up is linear (22.8).");
        Check(Near(BallContactMath.CssLaunchDegrees(20), 35) && Near(BallContactMath.CssLaunchDegrees(60), 35),
            "Looking up 20+ degrees lofts to 35.");
        Check(Near(BallContactMath.CssLaunchDegrees(float.NaN), 2), "Bad pitch input falls back to flat.");
        var lastAngle = 0f;
        for (var pitch = -40; pitch <= 30; pitch++)
        {
            var angle = BallContactMath.CssLaunchDegrees(pitch);
            Check(angle >= lastAngle - 1e-4f, "Launch angle never drops as the view rises.");
            lastAngle = angle;
        }

        // CS:S roll-out: exponential above the 65 u/s knee, slow tail below.
        Check(Near(BallContactMath.CssRollDecel(186, .6f, 6), 111.6f), "Above the knee the loss follows the speed.");
        Check(Near(BallContactMath.CssRollDecel(60, .6f, 6), 6), "Below the knee only the tail rate applies.");
        Check(Near(BallContactMath.CssRollDecel(66, .05f, 6), 6), "The loss never drops under the tail rate.");
        var oneSecond = BallContactMath.CssRollAllowance(186, 1, .6f, 6);
        Check(oneSecond is > 95 and < 115, $"186 u/s rolls to about 100-110 after 1 s (got {oneSecond:F1}).");
        var knee = MathF.Log(186f / 65f) / .6f;
        Check(Near(BallContactMath.CssRollAllowance(186, knee - 1e-4f, .6f, 6), 65, .1f)
            && Near(BallContactMath.CssRollAllowance(186, knee + 1e-4f, .6f, 6), 65, .1f), "The knee is continuous.");
        var lastAllowance = float.PositiveInfinity;
        for (var t = 0f; t < 31; t += .05f)
        {
            var allowance = BallContactMath.CssRollAllowance(186, t, .6f, 6);
            Check(allowance <= lastAllowance + 1e-3f && allowance >= 0, "Roll-out allowance is monotone and never negative.");
            lastAllowance = allowance;
        }
        Check(BallContactMath.CssRollAllowance(186, 30, .6f, 0) == 0, "Nothing rolls on after 30 s.");
        Check(BallContactMath.CssRollAllowance(50, 2, .6f, 6) == 38, "A slow roll only loses the tail rate.");
        Check(BallContactMath.CssRollAllowance(float.NaN, 1, .6f, 6) == 0 && BallContactMath.CssRollAllowance(186, -1, .6f, 6) == 0,
            "Bad input gives no allowance.");

        // Body rebound in the player's frame.
        var headOn = BallContactMath.BodyRebound(new(-400, 0, 0), Vector3.Zero, Vector3.UnitX, .3f, .5f);
        Check(Vector3.Distance(headOn, new(120, 0, 0)) < .01f, "Head-on returns 0.3 of the closing speed.");
        var runner = BallContactMath.BodyRebound(Vector3.Zero, new(200, 0, 0), Vector3.UnitX, .3f, .5f);
        Check(Vector3.Distance(runner, new(260, 0, 0)) < .01f, "A runner hitting a resting ball sends it ahead of him.");
        var graze = BallContactMath.BodyRebound(new(0, 300, 0), Vector3.Zero, Vector3.UnitX, .3f, .5f);
        Check(Near(graze.Length(), 300), "A pure graze keeps its speed even with friction.");
        var nearGraze = BallContactMath.BodyRebound(new(-10, 300, 0), Vector3.Zero, Vector3.UnitX, .3f, .5f);
        Check(Vector3.Distance(nearGraze, new(3, 293.5f, 0)) < .01f, "A near graze loses friction x (1 + e) x normal speed of tangent.");
        var frictionless = BallContactMath.BodyRebound(new(-100, 200, 0), Vector3.Zero, Vector3.UnitX, .3f, 0);
        Check(Vector3.Distance(frictionless, new(30, 200, 0)) < .01f, "Friction 0 keeps the whole tangent.");
        var stuck = BallContactMath.BodyRebound(new(-100, 20, 0), Vector3.Zero, Vector3.UnitX, .3f, 1);
        Check(Vector3.Distance(stuck, new(30, 0, 0)) < .01f, "Friction never reverses the tangent.");
        Check(BallContactMath.BodyRebound(new(100, 0, 0), Vector3.Zero, Vector3.UnitX, .3f, .5f) == new Vector3(100, 0, 0),
            "A separating ball is unchanged.");

        // Recent approach: the clipped contact tick must not hide the run-in.
        var history = new[] { Vector3.Zero, new Vector3(250, 0, 0), new Vector3(240, 0, 0), new Vector3(400, 0, 0) };
        Check(BallContactMath.RecentApproach(history, 3, 1, 0) == 250, "The fastest of the last 3 ticks counts.");
        Check(BallContactMath.RecentApproach(history, 1, 1, 0) == 0, "One tick is the current sample only.");
        Check(BallContactMath.RecentApproach(history, 3, 0, 1) == 0, "Sideways running is no approach.");

        // Old presets: the new dials get their old-behaviour values.
        var old = new Dictionary<string, float> { ["ballImpactBounceRestitution"] = .6f, ["ballPushMaxSpeed"] = 396 };
        var filled = BallDialLegacy.FillMissing(old);
        Check(old.Count == 2, "Filling must not mutate the stored preset.");
        Check(filled["ballPushMaxSpeed"] == 396 && filled["kickCssLaunchAngles"] == 0 && filled["rollDecayPerSecond"] == 0
            && filled["ballImpactFriction"] == 0 && filled["ballImpactSoftMinSpeed"] == 0 && filled["headerCentreNormalZ"] == .45f
            && filled["headerJumpTransfer"] == 0 && filled["ballPushMinSpeed"] == 135 && filled["ballPushApproachTicks"] == 0,
            "Missing new dials take their old-behaviour values.");
        Check(filled["headerRestitution"] == .6f, "The old header bounce is the preset's own body restitution.");
        var tuned = BallDialLegacy.FillMissing(new() { ["ballImpactBounceRestitution"] = .9f, ["ballPushMinSpeed"] = 60 });
        Check(tuned["headerRestitution"] == .9f && tuned["ballPushMinSpeed"] == 60, "Present values are never overwritten.");
        Check(BallDialLegacy.FillMissing(new())["headerRestitution"] == .6f, "Without a body value the old default 0.6 applies.");
        Console.WriteLine("Ball feel dial checks passed (6 groups).");
    }
}

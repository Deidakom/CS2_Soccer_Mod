using System.Numerics;
using SoccerModMvp;

internal static class BalancedBallChecks
{
    internal static void Run()
    {
        static void Check(bool condition, string why) { if (!condition) throw new Exception(why); }
        float last = 1;
        for (int i = 0; i <= 100; i++)
        {
            float power = BallContactMath.ReachPower(81.5f * i / 100, 81.5f);
            Check(power <= last + 0.0001f && power >= 0.7499f, "Reach power must decrease smoothly without ghost hits.");
            if (i <= 90) Check(MathF.Abs(power - 1) < 0.0001f, "Normal-range kicks must retain full power.");
            last = power;
        }
        Check(MathF.Abs(last - 0.75f) < 0.001f && BallContactMath.ReachPower(20, 81.5f) == 1, "Near/full and far/soft power endpoints.");
        Check(MathF.Abs(BallContactMath.ReachPower(81.5f * .95f, 81.5f) - .875f) < .001f, "Midway through outer reach retains 87.5% power.");
        Check(BallContactMath.HorizontalKickAim(new(10, 0, -45), 0, 18.8f, 70), "Low forward balls remain reachable.");
        Check(!BallContactMath.HorizontalKickAim(new(20, 80, 0), 0, 18.8f, 70), "Sideways reach must require aiming closer.");
        foreach (float yaw in new[] { 0f, 0.8f, 2f, 3.1f })
        {
            var rotation = Quaternion.CreateFromYawPitchRoll(yaw, 0.7f, 1.2f);
            var local = BallContactMath.RollingLocalSpin(new(100, 50, 0), 18.8f, 0.5f, rotation);
            var world = Vector3.Transform(local, rotation);
            Check(Vector3.Distance(world, new Vector3(-50, 100, 0) * (0.5f * 180 / MathF.PI / 18.8f)) < 0.01f,
                "Rolling spin must remain in the correct world direction for every ball orientation.");
        }
        float speed = 150;
        int ticks = 0;
        while (speed > 8 && ticks++ < 2000)
        {
            var actual = speed * 0.99f;
            var result = BallContactMath.RollingSpeed(speed, actual, 1, 1f / 64);
            Check(result <= speed && result >= actual, "Rollout cannot accelerate above its previous speed.");
            speed = result;
        }
        Check(ticks > 900 && ticks < 1800, "Rollout should coast for seconds but must eventually stop.");
        for (var second = 0; second <= 20; second++)
        {
            var allowance = BallContactMath.RollAllowance(90, second);
            Check(allowance <= MathF.Max(0, 90 - second * 6), "Native speed fluctuations cannot renew rollout energy.");
            if (second >= 15) Check(allowance == 0, "Assistance expires without another contact.");
        }
        foreach (var (previous, current, dot, dt) in new[] {
            (100f, 0f, 1f, .015625f), (100f, 40f, 1f, .015625f),
            (100f, 95f, -1f, .015625f), (100f, 95f, 1f, 1f),
            (300f, 295f, 1f, .015625f), (100f, 120f, 1f, .015625f) })
            Check(BallContactMath.RollingSpeed(previous, current, dot, dt) == current,
                "Stopped balls, collisions, reversals, stale samples and fast motion are native-only.");
        Console.WriteLine("Balanced ball checks passed: reach curve, aim, spin coordinates, finite rollout and collision guards.");
    }
}

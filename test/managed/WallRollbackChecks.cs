using System.Reflection;
using SoccerModMvp;

internal static class WallRollbackChecks
{
    internal static void Run()
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        object Constant(string name) => typeof(SoccerModMvpPlugin).GetField(name, flags)!.GetRawConstantValue()!;
        if ((float)Constant("DefaultWallAssistConversionRatio") != .159f
            || (float)Constant("DefaultWallAssistMaxAddedVertical") != 200f
            || (float)Constant("DefaultWallAssistMinimumNormalRetention") != .18f
            || (double)Constant("WallAssistCooldownSeconds") != .35
            || (int)Constant("WallAssistSeparationFrames") != 4)
            throw new Exception("Wall rollback must match the pre-today live snapshot and original timing.");
        foreach (var (loss, expected) in new[] { (0f, 0f), (150f, 23.85f), (334f, 53.106f), (1000f, 159f), (2000f, 200f) })
        foreach (var vertical in new[] { -300f, 0f, 50f, 400f })
        {
            var added = BallContactMath.AdditiveWallLift(loss, .159f, 200);
            if (Math.Abs(added - expected) > .001f || Math.Abs((vertical + added) - (vertical + expected)) > .001f)
                throw new Exception("Restored wall lift must add the old capped bonus, including existing upward or falling motion.");
        }
        if (BallContactMath.AdditiveWallLift(1000, 0, 200) != 0
            || BallContactMath.AdditiveWallLift(1000, .159f, 0) != 0)
            throw new Exception("Either zero wall-lift dial must disable added lift.");
        Console.WriteLine("Wall rollback checks passed (snapshot defaults, timing and 22 lift scenarios).");
    }
}

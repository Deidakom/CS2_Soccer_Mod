using SoccerModMvp;

internal static class GoalGeometryChecks
{
    internal static void Run()
    {
        // The pitch is below world zero; aperture height is relative to it.
        // Use exact binary fractions so the inclusive boundary is unambiguous.
        if (!MatchRuleMath.BallFitsBelowCrossbar(49.25f, 18.75f, -32, 100, 68)
            || MatchRuleMath.BallFitsBelowCrossbar(49.5f, 18.75f, -32, 100, 68)
            || MatchRuleMath.BallFitsBelowCrossbar(65, 18.75f, -32, 100, 68))
            throw new Exception("The whole ball must fit beneath the crossbar in world coordinates.");
        if (MatchRuleMath.BallFitsBelowCrossbar(45, 18.75f, -32, 100, 60)
            || !MatchRuleMath.BallFitsBelowCrossbar(41.25f, 18.75f, -32, 100, 60)
            || MatchRuleMath.BallFitsBelowCrossbar(50, 18.75f, -32, 100, 80))
            throw new Exception("A measured underside may narrow, but never enlarge, the configured aperture.");
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            if (MatchRuleMath.BallFitsBelowCrossbar(invalid, 18.75f, -32, 100, 68)
                || MatchRuleMath.BallFitsBelowCrossbar(0, invalid, -32, 100, 68)
                || MatchRuleMath.BallFitsBelowCrossbar(0, 18.75f, invalid, 100, 68)
                || MatchRuleMath.BallFitsBelowCrossbar(0, 18.75f, -32, invalid, 68)
                || MatchRuleMath.BallFitsBelowCrossbar(0, 18.75f, -32, 100, invalid))
                throw new Exception("Invalid crossbar geometry must reject the goal.");
        }
        if (MatchRuleMath.BallFitsBelowCrossbar(0, -1, -32, 100, 68)
            || MatchRuleMath.BallFitsBelowCrossbar(-40, 18.75f, -32, 0, 68)
            || MatchRuleMath.BallFitsBelowCrossbar(-60, 18.75f, -32, 100, -32))
            throw new Exception("Negative radii and empty goal openings must reject the goal.");
        Console.WriteLine("Goal geometry checks passed: whole-ball clearance, pitch offset, measured bar and invalid geometry.");
    }
}

using SoccerModMvp;

internal static class EloChecks
{
    internal static void Run()
    {
        static void Check(bool condition, string why) { if (!condition) throw new Exception(why); }
        static bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;

        // Equal teams: 50% expected, the winner gains K/2, the loser drops K/2.
        Check(Near(EloMath.ExpectedScore(1500, 1500), 0.5), "equal teams expect 0.5");
        var equalWin = EloMath.TeamDeltas(1500, 1500, true, new double[] { 10, 10 });
        Check(equalWin.All(d => Near(d, 16)), "equal win is +16 for every player with equal points");
        var equalLoss = EloMath.TeamDeltas(1500, 1500, false, new double[] { 10, 10 });
        Check(equalLoss.All(d => Near(d, -16)), "equal loss is -16");

        // A 400-point favourite expects 10/11 and gains little for winning.
        Check(Near(EloMath.ExpectedScore(1900, 1500), 10.0 / 11.0), "400 gap expects 10/11");
        var favouriteWin = EloMath.TeamDeltas(1900, 1500, true, new double[] { 0 });
        Check(Near(favouriteWin[0], 32.0 / 11.0), "favourite win gains K/11");

        // MVP nudge: relative to the own team's average, capped at +/- K/2.
        var nudged = EloMath.TeamDeltas(1500, 1500, true, new double[] { 30, 10, 20 });
        Check(Near(nudged[0], 16 + 0.5 * 16), "above-average player gains half the cap on top");
        Check(Near(nudged[1], 16 - 0.5 * 16), "below-average player loses half the cap");
        Check(Near(nudged[2], 16), "average player gets the plain outcome");
        var capped = EloMath.TeamDeltas(1500, 1500, false, new double[] { 100, 0, 0, 0 });
        Check(Near(capped[0], -16 + 16), "nudge ratio is capped at +1");
        // Average points below 1 use a denominator of 1.
        var low = EloMath.TeamDeltas(1500, 1500, true, new double[] { 1, 0 });
        Check(Near(low[0], 16 + 0.5 * 16) && Near(low[1], 16 - 0.5 * 16), "low-point teams divide by 1");

        Check(EloMath.IsRatedRoster(5, 5) && EloMath.IsRatedRoster(6, 6), "5v5 and 6v6 are rated");
        Check(!EloMath.IsRatedRoster(5, 6) && !EloMath.IsRatedRoster(4, 4) && !EloMath.IsRatedRoster(7, 7), "other rosters are not rated");

        Check(Near(EloMath.GapPercent(1500, 1500), 0), "no gap");
        Check(Math.Abs(EloMath.GapPercent(1600, 1500) - 100.0 / 1550.0 * 100.0) < 1e-9, "gap relative to the mean");

        // Swaps: pick the pair that evens the team sums; respect `allowed`.
        var a = new double[] { 1800, 1500 };
        var b = new double[] { 1400, 1300 };
        var best = EloMath.BestSwaps(a, b, (_, _) => true);
        Check(best.Count == 3, "top 3 of 4 pairs");
        // 3300 vs 2700: swapping 1800<->1400 or 1500<->1300 both leave a 200 gap;
        // the tie keeps the earlier pair first. The cross pairs leave 400.
        Check(best[0].A == 0 && best[0].B == 0 && Near(best[0].Gap, 200), "1800<->1400 leaves 200");
        Check(best[1].A == 1 && best[1].B == 1 && Near(best[1].Gap, 200), "1500<->1300 also leaves 200");
        Check(Near(best[2].Gap, 400), "cross pairs leave 400");
        var restricted = EloMath.BestSwaps(a, b, (i, _) => i != 0);
        Check(restricted.All(s => s.A == 1), "disallowed players are never offered");
        Check(EloMath.BestSwaps(a, b, (_, _) => false).Count == 0, "no allowed pair, no vote");

        // Vote: a clear winner swaps; ties and "keep" keep the teams.
        Check(EloMath.TallySwapVote(new[] { 3, 1, 0 }, 2) == 0, "clear winner");
        Check(EloMath.TallySwapVote(new[] { 2, 2, 0 }, 1) == -1, "tie between swaps keeps");
        Check(EloMath.TallySwapVote(new[] { 2, 0, 0 }, 2) == -1, "tie with keep keeps");
        Check(EloMath.TallySwapVote(new[] { 0, 0, 0 }, 0) == -1, "no votes keeps");
        Check(EloMath.TallySwapVote(new[] { 1, 0, 0 }, 3) == -1, "keep wins");

        Check(EloMath.PercentileAttribute(0, 10) == 40 && EloMath.PercentileAttribute(10, 10) == 99, "attributes span 40..99");
        Check(EloMath.PercentileAttribute(5, 10) == 70, "half below is 70 (40 + 29.5 rounded away)");
        Check(EloMath.Tier(1) == "LEGEND" && EloMath.Tier(3) == "LEGEND" && EloMath.Tier(4) == "WORLD CLASS"
            && EloMath.Tier(10) == "WORLD CLASS" && EloMath.Tier(20) == "GOLD" && EloMath.Tier(40) == "SILVER"
            && EloMath.Tier(41) == "BRONZE" && EloMath.Tier(0) == "BRONZE", "rank tiers");

        Console.WriteLine("ELO checks passed (rating, MVP nudge, roster, first pick, swaps, vote, card).");
    }
}

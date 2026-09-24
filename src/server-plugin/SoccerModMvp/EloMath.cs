#nullable enable
namespace SoccerModMvp;

// Rating rules of the SoMoE ELO ranking (github.com/Subear-17/soccer-mod-elo-ranking),
// reimplemented from its documented behaviour. Pure and static so the managed test
// suite covers them without a server.
internal static class EloMath
{
    internal const double DefaultRating = 1500.0;
    internal const double KFactor = 32.0;
    // A player's match points against their own team's average can move their
    // result by up to half the K-factor, on top of the team outcome.
    internal const double MvpModifierMax = 0.50;

    internal static double ExpectedScore(double teamAverage, double opponentAverage) =>
        1.0 / (1.0 + Math.Pow(10.0, (opponentAverage - teamAverage) / 400.0));

    // One team's rating changes. Every player gets the same outcome delta, then
    // a nudge from their own points relative to their team's average points.
    internal static double[] TeamDeltas(double teamAverage, double opponentAverage, bool won, IReadOnlyList<double> points)
    {
        var outcome = KFactor * ((won ? 1.0 : 0.0) - ExpectedScore(teamAverage, opponentAverage));
        var averagePoints = points.Count > 0 ? points.Average() : 0.0;
        var denominator = Math.Max(1.0, averagePoints);
        var deltas = new double[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var ratio = Math.Clamp((points[i] - averagePoints) / denominator, -1.0, 1.0);
            deltas[i] = outcome + ratio * KFactor * MvpModifierMax;
        }
        return deltas;
    }

    // Ranked covers 5v5 and 6v6 only, same size on both sides.
    internal static bool IsRatedRoster(int teamA, int teamB) => teamA == teamB && teamA is 5 or 6;

    // Percent gap between two captains relative to their mean. Above the
    // threshold the lower-rated captain picks first without a cap fight.
    internal static double GapPercent(double a, double b)
    {
        var mean = (a + b) / 2.0;
        return mean > 0.0 ? Math.Abs(a - b) / mean * 100.0 : 0.0;
    }

    // Halftime rebalance: for every allowed pair (x on side A, y on side B)
    // the team-sum gap after swapping them. Returns the best `count` pairs,
    // smallest gap first; ties keep the earlier pair.
    internal static List<(int A, int B, double Gap)> BestSwaps(
        IReadOnlyList<double> sideA,
        IReadOnlyList<double> sideB,
        Func<int, int, bool> allowed,
        int count = 3)
    {
        var sumA = sideA.Sum();
        var sumB = sideB.Sum();
        var candidates = new List<(int A, int B, double Gap)>();
        for (var i = 0; i < sideA.Count; i++)
        {
            for (var j = 0; j < sideB.Count; j++)
            {
                if (!allowed(i, j)) continue;
                var gap = Math.Abs((sumA - sideA[i] + sideB[j]) - (sumB - sideB[j] + sideA[i]));
                candidates.Add((i, j, gap));
            }
        }
        return candidates
            .Select((c, order) => (c, order))
            .OrderBy(x => x.c.Gap).ThenBy(x => x.order)
            .Take(count)
            .Select(x => x.c)
            .ToList();
    }

    // Vote result: index of the winning swap option, or -1 for "keep". A tie
    // at the top, including a tie with "keep", or no votes at all keeps teams.
    internal static int TallySwapVote(IReadOnlyList<int> swapVotes, int keepVotes)
    {
        var best = -1;
        var bestCount = keepVotes;
        var tie = false;
        for (var i = 0; i < swapVotes.Count; i++)
        {
            if (swapVotes[i] > bestCount)
            {
                best = i;
                bestCount = swapVotes[i];
                tie = false;
            }
            else if (swapVotes[i] == bestCount && bestCount > 0)
            {
                tie = true;
            }
        }
        return tie || bestCount == 0 ? -1 : best;
    }

    // Card attribute: 40..99 by the share of other rated players below you.
    internal static int PercentileAttribute(int below, int total) =>
        total < 1 ? 40 : 40 + (int)Math.Round((double)below / total * 59.0, MidpointRounding.AwayFromZero);

    internal static string Tier(int rank) => rank switch
    {
        <= 0 => "BRONZE",
        <= 3 => "LEGEND",
        <= 10 => "WORLD CLASS",
        <= 20 => "GOLD",
        <= 40 => "SILVER",
        _ => "BRONZE",
    };
}

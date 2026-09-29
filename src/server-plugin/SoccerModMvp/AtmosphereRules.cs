namespace SoccerModMvp;

// 2026-09-29 owner ("Arena Vision" plan, test server first): the stadium
// director. This is its pure part - the hype model and how each match moment
// moves it - kept free of CounterStrikeSharp so test/managed can check it.
// SoccerModMvpPlugin.Atmosphere.cs feeds it the moments and plays the
// instruments (crowd, lights, boards, pyro) from its answers.
internal enum AtmoMoment
{
    Shot,
    NearMiss,
    Post,
    Save,
    Goal,
    Equaliser,
    Lead,
    LastMinute,
    HatTrick,
    OwnGoal,
    Kickoff,
    MatchStart,
    HalfTime,
    FullTime,
}

internal static class AtmosphereRules
{
    public const float HypeMin = 0.0f;
    public const float HypeMax = 100.0f;
    public const float HypeStart = 18.0f;
    // Without new moments the hype falls halfway back to the base level in this time.
    public const float HypeHalfLifeSeconds = 7.0f;
    // A shot counts as a shot for the crowd from this kick speed (u/s).
    public const float ShotMinSpeed = 1400.0f;
    // The ball starts to make the crowd nervous this far from the halfway line (u).
    public const float PressureStartY = 500.0f;
    public const float PressureFullY = 1300.0f;

    public static bool IsGoal(AtmoMoment moment) => moment is AtmoMoment.Goal or AtmoMoment.Equaliser
        or AtmoMoment.Lead or AtmoMoment.LastMinute or AtmoMoment.HatTrick;

    public static float Impulse(AtmoMoment moment) => moment switch
    {
        AtmoMoment.Shot => 9.0f,
        AtmoMoment.NearMiss => 30.0f,
        AtmoMoment.Post => 34.0f,
        AtmoMoment.Save => 22.0f,
        AtmoMoment.OwnGoal => 60.0f,
        AtmoMoment.Kickoff => 6.0f,
        AtmoMoment.MatchStart => 30.0f,
        AtmoMoment.HalfTime => 20.0f,
        AtmoMoment.FullTime => 60.0f,
        _ when IsGoal(moment) => 100.0f,
        _ => 0.0f,
    };

    // 0 while the ball is around the halfway line, 1 in front of either goal.
    public static float Pressure(float ballDistanceFromHalfway) =>
        Math.Clamp((MathF.Abs(ballDistanceFromHalfway) - PressureStartY) / (PressureFullY - PressureStartY), 0.0f, 1.0f);

    // The level the hype drifts back to: higher while the ball is near a goal
    // and in a close game near the end.
    public static float BaseLevel(float pressure, bool closeGameLate) =>
        14.0f + 22.0f * Math.Clamp(pressure, 0.0f, 1.0f) + (closeGameLate ? 8.0f : 0.0f);

    public static float Step(float hype, float baseLevel, float dtSeconds)
    {
        if (dtSeconds <= 0.0f) return hype;
        var k = 1.0f - MathF.Exp(-dtSeconds * MathF.Log(2.0f) / HypeHalfLifeSeconds);
        return Math.Clamp(hype + (baseLevel - hype) * k, HypeMin, HypeMax);
    }

    public static float Add(float hype, AtmoMoment moment) => Math.Clamp(hype + Impulse(moment), HypeMin, HypeMax);

    public static string Mood(float hype) => hype < 30.0f ? "calm" : hype < 55.0f ? "restless" : hype < 80.0f ? "tension" : "ecstasy";

    // Which kind of goal it was, for the choreography. secondsLeft < 0 means
    // no match clock is running (warmup, public play).
    public static AtmoMoment ClassifyGoal(int scoringTeamGoalsAfter, int otherTeamGoals, bool ownGoal, int scorerMatchGoalsAfter, double secondsLeft)
    {
        if (ownGoal) return AtmoMoment.OwnGoal;
        if (scorerMatchGoalsAfter == 3) return AtmoMoment.HatTrick;
        if (secondsLeft is >= 0.0 and < 60.0) return AtmoMoment.LastMinute;
        if (scoringTeamGoalsAfter == otherTeamGoals) return AtmoMoment.Equaliser;
        if (scoringTeamGoalsAfter == otherTeamGoals + 1) return AtmoMoment.Lead;
        return AtmoMoment.Goal;
    }
}

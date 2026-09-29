using SoccerModMvp;

// 2026-09-29 stadium director (AtmosphereRules.cs): hype model and goal kinds.
internal static class AtmosphereRulesChecks
{
    internal static void Run()
    {
        var checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        void Near(float actual, float expected, float tol, string message) => Check(Math.Abs(actual - expected) < tol, message);

        // Decay: halfway back to the base level after one half-life, never overshooting.
        Near(AtmosphereRules.Step(100f, 20f, AtmosphereRules.HypeHalfLifeSeconds), 60f, .01f, "One half-life halves the distance to the base level.");
        var h = 100f;
        for (var i = 0; i < 400; i++) h = AtmosphereRules.Step(h, 20f, .25f);
        Near(h, 20f, .01f, "Hype settles on the base level.");
        Check(AtmosphereRules.Step(10f, 20f, .25f) > 10f && AtmosphereRules.Step(10f, 20f, .25f) < 20f, "Hype rises towards the base without overshooting.");
        Check(AtmosphereRules.Step(42f, 20f, 0f) == 42f, "No time, no change.");

        // Impulses and clamping.
        Check(AtmosphereRules.Add(95f, AtmoMoment.Goal) == 100f, "Hype is clamped at 100.");
        Check(AtmosphereRules.Impulse(AtmoMoment.Post) > AtmosphereRules.Impulse(AtmoMoment.NearMiss), "The post beats a near miss.");
        Check(AtmosphereRules.Impulse(AtmoMoment.NearMiss) > AtmosphereRules.Impulse(AtmoMoment.Save), "A near miss beats a save.");
        Check(AtmosphereRules.Impulse(AtmoMoment.Save) > AtmosphereRules.Impulse(AtmoMoment.Shot), "A save beats a plain shot.");

        // Pressure and base level.
        Check(AtmosphereRules.Pressure(0f) == 0f && AtmosphereRules.Pressure(-400f) == 0f, "No pressure around the halfway line.");
        Check(AtmosphereRules.Pressure(1300f) == 1f && AtmosphereRules.Pressure(-1500f) == 1f, "Full pressure in front of either goal.");
        Check(AtmosphereRules.BaseLevel(1f, true) > AtmosphereRules.BaseLevel(1f, false), "A close late game raises the base level.");

        // Moods.
        Check(AtmosphereRules.Mood(10f) == "calm" && AtmosphereRules.Mood(60f) == "tension" && AtmosphereRules.Mood(95f) == "ecstasy", "Mood bands.");

        // Goal kinds, in priority order.
        Check(AtmosphereRules.ClassifyGoal(1, 0, true, 0, 30) == AtmoMoment.OwnGoal, "An own goal stays an own goal.");
        Check(AtmosphereRules.ClassifyGoal(3, 0, false, 3, -1) == AtmoMoment.HatTrick, "Third goal of a player is a hat-trick.");
        Check(AtmosphereRules.ClassifyGoal(2, 2, false, 1, 30) == AtmoMoment.LastMinute, "Last minute beats equaliser.");
        Check(AtmosphereRules.ClassifyGoal(2, 2, false, 1, -1) == AtmoMoment.Equaliser, "Level score is an equaliser.");
        Check(AtmosphereRules.ClassifyGoal(3, 2, false, 1, 400) == AtmoMoment.Lead, "One ahead is a lead goal.");
        Check(AtmosphereRules.ClassifyGoal(4, 1, false, 2, 400) == AtmoMoment.Goal, "Anything else is a goal.");
        Check(AtmosphereRules.IsGoal(AtmoMoment.LastMinute) && !AtmosphereRules.IsGoal(AtmoMoment.OwnGoal), "Own goals are not celebrated as goals.");

        Console.WriteLine($"Atmosphere rules checks passed ({checks} checks).");
    }
}

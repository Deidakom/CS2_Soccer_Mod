using System.Numerics;
using SoccerModMvp;

internal static class GoalkeeperSprintChecks
{
    internal static void Run()
    {
        var checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        void Near(float actual, float expected, string message) => Check(Math.Abs(actual - expected) < .01f, message);
        Near(GoalkeeperSprintRules.SpeedMultiplier(1.25f), 1.175f, "Keeper gets 70% of the sprint bonus.");
        Check(GoalkeeperSprintRules.SpeedMultiplier(1.25f) > 1, "Keeper sprint must not be slower than ordinary running.");
        foreach (var sign in new[] { -1, 1 })
        {
            var box = (-250f, 250f, Math.Min(sign * 1400f, sign * 1180f), Math.Max(sign * 1400f, sign * 1180f), -32f, 118f);
            bool Inside(float x, float y, float z = -32) => GoalkeeperSprintRules.InBox(new(x, y, z), box);
            Check(Inside(0, sign * 1300), "Both defending ends must work.");
            Check(Inside(250, sign * 1180), "Box edges are inclusive.");
            Check(!Inside(251, sign * 1300), "Lateral exit must lose unlimited sprint.");
            Check(!Inside(0, sign * 1179), "Pitch-side exit must lose unlimited sprint.");
            Check(!Inside(0, sign * 1401), "Behind goal line is not the small box.");
            Check(!Inside(0, -sign * 1300), "Opposing box is excluded.");
            Check(Inside(0, sign * 1300, -34), "Standing floor tolerance.");
            Check(!Inside(0, sign * 1300, -35) && !Inside(0, sign * 1300, 119), "Vertical bounds are enforced.");
            Check(!Inside(float.NaN, sign * 1300), "Invalid coordinates are rejected.");
        }

        var free = new SprintStamina { Stamina = 45 };
        free.Update(0, true); Check(free.TryStart(0), "Keeper sprint starts with partial stamina.");
        for (var tick = 1; tick <= 64 * 60; tick++) free.Update(tick / 64d, true);
        Check(free.Active && !free.Exhausted, "Keeper sprint lasts beyond the normal three-second limit.");
        Near(free.Stamina, 45, "One minute of keeper sprint neither consumes nor refills normal stamina.");
        free.Update(60.01, false);
        Check(free.Active && !free.Unlimited, "Leaving continues a normal sprint when stamina permits.");
        free.Update(60.11, false);
        Check(free.Stamina < 45, "Normal stamina drains immediately outside.");
        for (var tick = 1; tick <= 120; tick++) free.Update(60.11 + tick / 64d);
        Check(!free.Active && free.Exhausted, "Leaving cannot carry unlimited duration into the field.");

        var exhausted = new SprintStamina { Stamina = 0, Exhausted = true, RequireRelease = true, RegenAt = 100 };
        exhausted.Update(0, true);
        Check(exhausted.TryStart(0), "Exhausted keepers may still sprint in the box.");
        exhausted.Update(.2, true);
        Check(exhausted.Exhausted && exhausted.Stamina == 0, "Keeper sprint preserves normal exhaustion.");
        exhausted.Update(.21, false);
        Check(!exhausted.Active && exhausted.Exhausted && exhausted.RequireRelease, "Outside, exhausted sprint stops immediately.");
        Check(!exhausted.TryStart(.22), "Leaving cannot bypass normal recovery.");

        var cooldown = new SprintStamina { Stamina = 40, RegenAt = 10 };
        cooldown.Update(0, true); Check(cooldown.TryStart(0), "Box sprint bypasses normal recovery delay locally.");
        cooldown.Update(.1, false);
        Check(!cooldown.Active && cooldown.Stamina == 40, "Pending normal recovery still applies outside.");

        var hold = new SprintStamina(); hold.Update(0, true); hold.Input(0, true, true);
        Check(hold.Active, "Hold starts keeper sprint.");
        hold.Update(.1, true); hold.Input(.1, false, true);
        Check(!hold.Active, "Releasing hold stops keeper sprint.");
        var toggle = new SprintStamina(); toggle.Update(0, true); toggle.Input(0, true, false);
        toggle.Input(.1, false, false); Check(toggle.Active, "Toggle remains active after release.");
        toggle.Input(.2, true, false); Check(!toggle.Active, "Second press toggles off.");
        Check(toggle.TryStart(.21), "Keeper sprint has no restart cooldown inside the box.");
        toggle.Stop(.22); Check(!toggle.Active, "Pause/death cleanup can stop free sprint.");

        var crossings = new SprintStamina { Stamina = 30 }; crossings.Update(0, true); crossings.TryStart(0);
        for (var tick = 1; tick <= 100; tick++) crossings.Update(tick / 100d, tick % 2 == 0);
        Check(crossings.Stamina < 30 && crossings.Stamina > 0, "Repeated boundary crossings cannot refill stamina.");
        Console.WriteLine($"Goalkeeper sprint checks passed ({checks} scenarios).");
    }
}

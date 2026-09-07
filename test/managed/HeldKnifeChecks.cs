using SoccerModMvp;

internal static class HeldKnifeChecks
{
    internal static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        double next = KnifeSwingRules.NextHeldSwing(0, .48);
        Check(!KnifeSwingRules.HeldSwingDue(.079, next, true), "Contact-window retry must not create a second swing.");
        Check(!KnifeSwingRules.HeldSwingDue(.479, next, true), "Holding must respect kick cadence.");
        Check(KnifeSwingRules.HeldSwingDue(.48, next, true), "Holding must re-arm after a wall miss or an accepted kick.");
        Check(!KnifeSwingRules.HeldSwingDue(2, next, false), "Release must stop repeat swings.");
        int repeats = 0;
        for (int tick = 1; tick <= 640; tick++)
        {
            var now = tick / 64.0;
            if (!KnifeSwingRules.HeldSwingDue(now, next, true)) continue;
            repeats++;
            // No accepted-contact state is necessary: a whole string of misses
            // at the wall must never wedge held input.
            next = KnifeSwingRules.NextHeldSwing(now, .48);
        }
        Check(repeats == 20, "A ten-second hold must keep re-arming, not stop after its first window.");
        Check(KnifeSwingRules.NextHeldSwing(2, .9) == 2.9, "Longer configured cooldown must be honoured.");
        Check(KnifeSwingRules.NextHeldSwing(2, .05) == 2.48, "Short configured cooldown must not cause per-tick automatic kicks.");
        Check(!KnifeSwingRules.HeldSwingDue(2.48, KnifeSwingRules.NextHeldSwing(2.2, .48), true), "No catch-up burst after delayed ticks.");
        Console.WriteLine("Held knife regression checks passed (8 scenarios).");
    }
}

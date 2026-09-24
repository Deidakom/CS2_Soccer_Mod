using System.Numerics;
using SoccerModMvp;

internal static class KickRewindChecks
{
    internal static void Run()
    {
        static void Check(bool condition, string why) { if (!condition) throw new Exception(why); }
        const float tick = 1f / 64;

        // Window: round trip plus two ticks, capped by the dial, 0 = off.
        Check(KickRewind.WindowSeconds(40, tick, 0) == 0 && KickRewind.WindowSeconds(40, tick, float.NaN) == 0
            && KickRewind.WindowSeconds(40, tick, -10) == 0, "Zero, negative or invalid maximum disables lag compensation.");
        Check(Math.Abs(KickRewind.WindowSeconds(40, tick, 100) - 0.07125) < 1e-6, "Ping plus two ticks of interpolation.");
        Check(Math.Abs(KickRewind.WindowSeconds(200, tick, 100) - 0.1) < 1e-6, "High ping is capped by the configured maximum.");
        Check(Math.Abs(KickRewind.WindowSeconds(float.NaN, tick, 100) - 0.03125) < 1e-6
            && Math.Abs(KickRewind.WindowSeconds(-50, tick, 100) - 0.03125) < 1e-6, "Unknown ping falls back to interpolation only.");
        Check(Math.Abs(KickRewind.WindowSeconds(900, tick, 5000) - KickRewind.MaximumMilliseconds / 1000.0) < 1e-6,
            "A hand-edited maximum cannot exceed the supported rewind.");

        // A ball passing a standing player at 800 u/s, 40 units to the side.
        var eye = new Vector3(0, 0, 64);
        const float reach = 81.5f + 18.805f;
        bool Reachable(Vector3 ball) => Vector3.Distance(eye, ball) <= reach;
        BallTrail Passing(int ticks, float startX, float perTick)
        {
            var trail = new BallTrail();
            for (var t = 0; t < ticks; t++) trail.Record(t, t * tick, new(startX + t * perTick, 40, 18.8f));
            return trail;
        }
        var passing = Passing(27, -205, 12.5f); // newest sample at x = 120, already beyond reach
        var now = 26 * tick;
        Check(!Reachable(new(120, 40, 18.8f)), "Scenario: the live ball has already left the knife's reach.");
        (Vector3 Origin, double Age)? FirstReachable(double window, int contactTick) =>
            passing.Rewound(now, window, contactTick, 3500).Select(s => ((Vector3, double)?)s).FirstOrDefault(s => Reachable(s!.Value.Item1));
        var seen = FirstReachable(KickRewind.WindowSeconds(50, tick, 100), -1000);
        Check(seen is { } hit && Math.Abs(hit.Age - 4 * tick) < 1e-6 && Math.Abs(hit.Origin.X - 70) < 1e-3,
            "A 50 ms player is judged against the newest position they saw inside reach.");
        Check(FirstReachable(KickRewind.WindowSeconds(0, tick, 100), -1000) is null,
            "A LAN player gains no extra reach from interpolation alone.");
        Check(FirstReachable(KickRewind.WindowSeconds(50, tick, 0), -1000) is null, "Disabled lag compensation never rewinds.");
        Check(FirstReachable(KickRewind.WindowSeconds(50, tick, 100), 23) is null,
            "Another contact three ticks ago blocks rewinding to the ball before it.");
        Check(FirstReachable(KickRewind.WindowSeconds(50, tick, 100), 21) is { } afterContact && Math.Abs(afterContact.Age - 4 * tick) < 1e-6,
            "Positions recorded after the last contact remain claimable.");

        // Newest first, bounded by the window, never older than the contact tick.
        var ages = passing.Rewound(now, 0.05, -1000, 3500).Select(s => s.Age).ToArray();
        Check(ages.Length == 4 && ages.Zip(ages.Skip(1)).All(p => p.First < p.Second), "Rewind yields newest first inside the window.");
        Check(passing.Rewound(now, 1, 24, 3500).Count() == 2, "Samples on or before the contact tick are excluded.");

        // Teleports (reset, cannon, placement) end the trail.
        var teleported = new BallTrail();
        for (var t = 0; t < 10; t++) teleported.Record(t, t * tick, new(t < 6 ? t * 10 : 900 + t * 10, 0, 18.8f));
        Check(teleported.Rewound(9 * tick, 1, -1000, 3500).Count() == 4, "A jump no ball could make stops the rewind.");

        // Bookkeeping: same tick overwrites, clock reversal restarts, capacity bounds.
        var trail = new BallTrail();
        trail.Record(5, 5 * tick, new(1, 0, 0));
        trail.Record(5, 5 * tick, new(2, 0, 0));
        Check(trail.Count == 1 && trail.Rewound(5 * tick, 1, -1000, 3500).Single().Origin.X == 2, "A tick keeps one freshest sample.");
        trail.Record(3, 3 * tick, new(3, 0, 0));
        Check(trail.Count == 1 && trail.Rewound(3 * tick, 1, -1000, 3500).Single().Origin.X == 3, "A replaced entity/clock starts a new trail.");
        trail.Record(4, 4 * tick, new(float.NaN, 0, 0));
        Check(trail.Count == 1, "Invalid positions are never recorded.");
        for (var t = 10; t < 200; t++) trail.Record(t, t * tick, new(t, 0, 0));
        Check(trail.Count == BallTrail.Capacity && trail.Rewound(199 * tick, 10, -1000, 3500).Count() == BallTrail.Capacity,
            "The trail is bounded to its fixed capacity.");
        Check(!trail.Rewound(199 * tick, 0, -1000, 3500).Any() && !new BallTrail().Rewound(0, 1, -1000, 3500).Any(),
            "Zero window and empty trail yield nothing.");
        trail.Clear();
        Check(trail.Count == 0 && !trail.Rewound(199 * tick, 1, -1000, 3500).Any(), "Clearing forgets every sample.");

        // Simultaneous body pushes: the outcome must not depend on slot numbers.
        var inherited = new Vector3(20, -10, 5);
        var a = new Vector3(200, 0, 0);
        var b = new Vector3(-150, 60, 0);
        Check(BallContactMath.CombinePushes(inherited, new[] { (1, a), (9, b) })
            == BallContactMath.CombinePushes(inherited, new[] { (9, a), (1, b) }),
            "The higher slot must not win a two-player body duel.");
        var single = BallContactMath.CombinePushes(inherited, new[] { (4, a) });
        Check(Vector3.Distance(single, inherited + a) < 1e-4f, "A lone pusher keeps exactly its own push.");
        Console.WriteLine("Kick lag compensation and fair body-push checks passed (22 scenarios).");
    }
}

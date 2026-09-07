using System.Numerics;
using SoccerModMvp;

internal static class BallFeelChecks
{
    internal static void Run()
    {
        static void Check(bool condition, string why) { if (!condition) throw new Exception(why); }
        const float reach = 81.5f;
        foreach (var direction in new[] { Vector3.UnitX, Vector3.Normalize(new Vector3(1, 0, 1)) })
        {
            var incoming = -direction * 1400;
            var firm = direction * 1600;
            Check(BallContactMath.CushionEarlyKick(incoming, firm, direction, 1200, reach * .9f, reach, true) == firm,
                "Broad normal reach stays full power on ground and airborne.");
            Check(BallContactMath.CushionEarlyKick(incoming, firm, direction, 1200, reach, reach, true).Length() < .01f,
                "Tip contact spends its impulse stopping a strong incoming shot.");
            var last = firm.Length();
            for (int percent = 90; percent <= 100; percent++)
            {
                var output = BallContactMath.CushionEarlyKick(incoming, firm, direction, 1200, reach * percent / 100, reach, true);
                Check(output.Length() <= last + .01f, "Tip cushioning must change smoothly without an outer power spike.");
                last = output.Length();
            }
            foreach (var velocity in new[] { Vector3.Zero, direction * 2000 })
            {
                var approaching = BallContactMath.IsIncomingContact(velocity, Vector3.Zero, -direction * 70);
                Check(!approaching && BallContactMath.ReachPower(reach, reach, approaching) == 1,
                    "Stationary/outgoing balls must not receive a distance power penalty.");
                Check(BallContactMath.CushionEarlyKick(velocity, firm, direction, 1000, reach, reach, approaching) == firm,
                    "Stationary/outgoing balls retain the full normal kick even at maximum reach.");
            }
            Check(BallContactMath.IsIncomingContact(incoming, Vector3.Zero, -direction * 70), "Incoming ball qualifies.");
            var groundEarly = BallContactMath.IsIncomingContact(incoming, Vector3.Zero, -direction * 70, grounded: true);
            Check(!groundEarly && BallContactMath.ReachPower(reach, reach, groundEarly) == 1
                && BallContactMath.CushionEarlyKick(incoming, firm, direction, 1200, reach, reach, groundEarly) == firm,
                "Even incoming ground balls bypass both early-touch penalties at maximum reach.");
        }
        Check(!BallContactMath.IsIncomingContact(new(100, 0, 0), new(200, 0, 0), new(-70, 0, 0)), "Chasing an outgoing ball is not an early touch.");
        Check(!BallContactMath.IsIncomingContact(new(0, 100, 0), Vector3.Zero, new(-70, 0, 0)), "Sideways travel does not qualify.");
        Check(!BallContactMath.IsIncomingContact(new(-100, 0, 0), new(-200, 0, 0), new(-70, 0, 0)), "A faster retreating player is not closing.");
        Check(!BallContactMath.IsIncomingContact(new(-1, 0, 0), Vector3.Zero, new(-70, 0, 0)), "Resting-ball jitter is not incoming.");
        Check(BallContactMath.IsIncomingContact(new(0, 0, -300), Vector3.Zero, new(0, 0, -50)), "A falling overhead ball can approach the body.");
        Check(KnifeSwingRules.WithinWindow(1, 1) && KnifeSwingRules.WithinWindow(1.079, 1)
            && !KnifeSwingRules.WithinWindow(1.081, 1) && !KnifeSwingRules.WithinWindow(.99, 1), "Only a short forward contact window.");
        Check(KnifeSwingRules.AimUnchanged(0, -179, 0, 179) && !KnifeSwingRules.AimUnchanged(9, 0, 0, 0)
            && !KnifeSwingRules.AimUnchanged(0, 20, 0, 0), "Aim locks account for yaw wrapping and reject retargeting.");
        Check(BallContactMath.ImpactTargetAlong(-300, 650) == 650 && BallContactMath.ImpactTargetAlong(100, 650) == 750,
            "Running into a ball cannot erase its impact; outgoing player momentum survives.");
        var pulse = 650f;
        for (var frame = 1; frame <= 12; frame++)
        {
            var next = BallContactMath.ImpactPulseTarget(650, frame, 12);
            Check(next <= pulse && next >= 487.5f, "Short impact target decays, never repeatedly adds impulses.");
            pulse = next;
        }
        Check(BallContactMath.LandingVertical(-200, 600) == 110
            && BallContactMath.LandingVertical(-200, 80) == 80
            && BallContactMath.LandingVertical(300, 600) == 600, "Landing limiter only removes excess vertical bounce.");
        Check(BallContactMath.WallReboundVertical(0, 400, 1500, true) == 90
            && BallContactMath.WallReboundVertical(0, 20, 300, true) == 20
            && BallContactMath.WallReboundVertical(-300, 400, 1500, true) == 400
            && BallContactMath.WallReboundVertical(0, 400, 1500, false) == 400,
            "Flat wall hops are limited without raising small hops or altering lofted shots/landings.");
        float speed = 45;
        var elapsed = 0f;
        var ticks = 0;
        while (speed > 4 && ticks++ < 1000)
        {
            elapsed += 1f / 64;
            var next = BallContactMath.RollingTail(speed, 0, 1, 1f / 64, BallContactMath.RollAllowance(45, elapsed));
            Check(next < speed, "Bridging a native low-speed stop still loses energy every tick.");
            speed = next;
        }
        Check(ticks > 350 && ticks < 500, "45 u/s tail coasts about seven seconds, then terminates.");
        Check(BallContactMath.RollingTail(0, 0, 1, .016f, 40) == 0
            && BallContactMath.RollingTail(100, 0, 1, .016f, 40) == 0
            && BallContactMath.RollingTail(40, 20, -1, .016f, 40) == 20
            && BallContactMath.RollingTail(40, 0, 1, .5f, 40) == 0, "Tail cannot restart resting balls, restore hard hits, reverse direction or use stale history.");
        Console.WriteLine("Ball feel checks passed: firm/tip contact, swing timing, decaying push, landing cap and finite hull-snag rollout.");
    }
}

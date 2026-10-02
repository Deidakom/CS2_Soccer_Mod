namespace SoccerModMvp;

// Football mode (2026-09-26, map-test server only): hold a mouse button to
// charge, release to kick. Pure numbers so ActivityTests can check them.
internal static class FootballKickRules
{
    // Seconds of holding for a full charge; holding on up to MaxCharge overhits.
    internal const double FullChargeSeconds = 0.8;
    internal const double MaxChargeSeconds = 1.3;
    // After the release the kick waits this long for the ball to come into
    // reach (round 1 had 0.25 s: 9 of 48 shots missed that way).
    internal const double ContactWindow = 0.35;
    // Between two football kicks (the knife's 0.48 s blocked 12 of 48 shots
    // of players re-charging right after a touch).
    internal const float Cooldown = 0.25f;

    internal const float LobElevationDegrees = 40.0f;
    internal const float ChipElevationDegrees = 35.0f;
    internal const float OverhitLiftDegrees = 12.0f;
    internal const float OverhitSprayDegrees = 4.0f;
    internal const float HeaderMinPower = 0.6f;
    internal const float FinessePower = 0.85f;
    internal const float FinesseCurl = 1.6f;

    // Curl: the horizontal velocity turns by Curve rad/s, decaying; a full
    // curl bends a shot by roughly 30 degrees over its flight.
    internal const float CurlRate = 0.55f;
    internal const float CurlSeconds = 1.6f;
    internal const float CurlDecay = 1.2f;

    internal static float ChargeFraction(double held) =>
        (float)Math.Clamp(held / FullChargeSeconds, 0.0, 1.0);

    internal static float Overhit(double held) =>
        (float)Math.Clamp((held - FullChargeSeconds) / (MaxChargeSeconds - FullChargeSeconds), 0.0, 1.0);

    // Left = shot, Ctrl + left = chip, right = ground pass, Ctrl + right = lofted pass.
    internal static string Style(bool secondary, bool crouching) =>
        secondary ? (crouching ? "lob" : "pass") : (crouching ? "chip" : "shot");

    internal static bool IsStyle(string mode) => mode is "shot" or "chip" or "pass" or "lob";

    internal static bool CanOverhit(string style) => style is "shot" or "chip";

    internal static bool CanCurl(string style) => style is "shot" or "chip" or "lob";

    // Power relative to the plugin's base kick (1602 u/s delta). A tap still
    // moves the ball; a full shot is a little stronger than the old fixed kick.
    internal static float PowerScale(string style, float charge, bool finesse = false) => (style switch
    {
        "pass" => Lerp(0.15f, 0.75f, charge),
        "lob" => Lerp(0.30f, 0.85f, charge),
        "chip" => Lerp(0.30f, 0.80f, charge),
        _ => Lerp(0.35f, 1.20f, charge),
    }) * (finesse && CanOverhit(style) ? FinessePower : 1.0f);

    // +1 = bends left (A held), -1 = bends right (D held), 0 = straight.
    internal static int CurlSide(bool left, bool right) => left == right ? 0 : left ? 1 : -1;

    internal static float Curl(string style, int side, float charge, bool finesse) =>
        !CanCurl(style) || side == 0 ? 0.0f : side * (0.5f + 0.5f * Math.Clamp(charge, 0.0f, 1.0f)) * (finesse ? FinesseCurl : 1.0f);

    // Turns a horizontal velocity by angle radians (positive = to the left).
    internal static (float X, float Y) Turn(float x, float y, float angle)
    {
        var c = MathF.Cos(angle);
        var s = MathF.Sin(angle);
        return (x * c - y * s, x * s + y * c);
    }

    internal static string Label(string style, bool header = false) => header ? "HEADER" : style switch
    {
        "pass" => "PASS",
        "lob" => "LOFTED PASS",
        "chip" => "CHIP",
        _ => "SHOT",
    };

    // 0..100 in steps of 5, like the sprint bar.
    internal static int FillStep(float charge) => (int)MathF.Round(Math.Clamp(charge, 0.0f, 1.0f) * 20.0f) * 5;

    // Centre-text fallback when the Panorama HUD is not mounted.
    internal static string BarHtml(string label, float charge, float overhit = 0.0f, int curlSide = 0)
    {
        const int segments = 20;
        var filled = (int)MathF.Round(Math.Clamp(charge, 0.0f, 1.0f) * segments);
        var colour = overhit > 0.0f ? "#FF2020" : charge < 0.5f ? "#7CFC00" : charge < 0.85f ? "#FFD700" : "#FF8C00";
        var curl = curlSide > 0 ? "◄ " : curlSide < 0 ? " ►" : "";
        var title = curlSide > 0 ? curl + label : curlSide < 0 ? label + curl : label;
        return $"<font class='fontSize-l' color='#DDDDDD'>{(overhit > 0.0f ? "TOO MUCH" : title)}</font><br>"
            + $"<font color='{colour}'>{new string('█', filled)}</font><font color='#444444'>{new string('█', segments - filled)}</font>";
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0.0f, 1.0f);
}

// Goalkeeper numbers (FootballKeeper.cs). Based on the 2026-09-07 trial
// (docs/goalkeeper-trial.md), a little more forgiving for first person.
internal static class FootballKeeperRules
{
    internal const float CatchReach = 80.0f;
    internal const float DiveReachBonus = 25.0f;
    internal const float CatchConeDegrees = 70.0f;
    internal const double CatchWindow = 0.35;
    internal const double CatchCooldown = 1.0;
    internal const float CatchMaxSpeed = 900.0f;
    internal const float ParryRetain = 0.55f;
    internal const float ParryMaxSpeed = 1000.0f;
    internal const double HoldSeconds = 6.0;
    internal const float HandForward = 38.0f;
    internal const float HandDown = 22.0f;
    internal const float DiveSpeed = 450.0f;
    internal const float DiveLift = 200.0f;
    internal const double DiveReachSeconds = 0.7;
    internal const double DiveCooldown = 2.5;

    internal static bool Catches(float speed) => speed <= CatchMaxSpeed;

    internal static float ParrySpeed(float speed) => MathF.Min(speed * ParryRetain, ParryMaxSpeed);

    // Throw: flat and accurate; punt: high and long. Relative to the base kick.
    internal static float ThrowPower(float charge) => 0.25f + 0.55f * Math.Clamp(charge, 0.0f, 1.0f);
    internal static float PuntPower(float charge) => 0.60f + 0.75f * Math.Clamp(charge, 0.0f, 1.0f);

    // Elevation in degrees from the view pitch (negative = looking up).
    internal static float ThrowElevation(float viewPitch) => Math.Clamp(-viewPitch, -5.0f, 25.0f);
    internal static float PuntElevation(float viewPitch) => Math.Clamp(-viewPitch + 25.0f, 15.0f, 55.0f);
}

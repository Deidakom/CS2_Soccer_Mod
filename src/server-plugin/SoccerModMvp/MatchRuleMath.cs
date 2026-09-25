namespace SoccerModMvp;
internal static class MatchRuleMath
{
    internal static bool BallFitsBelowCrossbar(float centreZ, float radius, float pitchZ,
        float apertureHeight, float undersideZ) =>
        float.IsFinite(centreZ) && float.IsFinite(radius) && radius >= 0
        && float.IsFinite(pitchZ) && float.IsFinite(apertureHeight) && apertureHeight > 0
        && float.IsFinite(undersideZ) && undersideZ > pitchZ
        && (double)centreZ + radius <= Math.Min((double)pitchZ + apertureHeight, undersideZ);

    // 2026-09-25 owner: the crowd only boos a real chance, i.e. a shot that
    // misses the frame by a little. The end line runs the whole pitch width,
    // so a ball rolling out far from the goal must stay quiet.
    internal const float NearMissMargin = 48f;
    internal const float NearMissMinSpeed = 400f;

    internal static bool IsNearMiss(float crossX, float centerX, float halfWidth, float crossZ, float crossbarZ, float speed) =>
        float.IsFinite(crossX) && float.IsFinite(crossZ) && float.IsFinite(speed)
        && speed >= NearMissMinSpeed
        && Math.Abs(crossX - centerX) <= halfWidth + NearMissMargin
        && crossZ <= crossbarZ + NearMissMargin;

    // The server's own name from its config (2026-09-25: the map test server
    // is "KA Soccer Mod - Map Test"); our status suffix is cut off again.
    internal static string HostnameBase(string? hostname)
    {
        var name = (hostname ?? "").Split(" | ")[0].Trim();
        return name.Length == 0 ? "KA Soccer Mod - Public Server" : name;
    }

    internal static bool EveryoneReady<T>(IReadOnlyDictionary<ulong, T> required, IReadOnlyDictionary<ulong, T> current, ISet<ulong> ready)
        => required.Count > 0 && required.All(pair => ready.Contains(pair.Key)
            && current.TryGetValue(pair.Key, out var team) && EqualityComparer<T>.Default.Equals(team, pair.Value))
            && current.Keys.All(ready.Contains);

    internal static bool CrossedHalfway(float previous, float current, float radius) =>
        float.IsFinite(previous) && float.IsFinite(current) && float.IsFinite(radius) && radius >= 0
        && (Math.Abs(current) <= radius || (previous < -radius && current > radius) || (previous > radius && current < -radius));

    // Overnight windows belong to their start day. Equal endpoints mean all day.
    internal static bool InLogWindow(DateTime now, int days, int start, int end)
    {
        var minute = now.Hour * 60 + now.Minute;
        var day = (int)now.DayOfWeek;
        if (start == end) return (days & (1 << day)) != 0;
        if (start < end) return (days & (1 << day)) != 0 && minute >= start && minute < end;
        if (minute < end) day = (day + 6) % 7;
        return (days & (1 << day)) != 0 && (minute >= start || minute < end);
    }
}

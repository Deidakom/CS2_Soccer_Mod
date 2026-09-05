namespace SoccerModMvp;
internal static class MatchRuleMath
{
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

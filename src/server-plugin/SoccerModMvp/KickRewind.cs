using System.Numerics;

namespace SoccerModMvp;

// Lag-compensated knife contact. A client draws the ball roughly one round
// trip plus interpolation behind the server, so a knife press that looked
// like a clean touch on screen is judged against a ball that has already
// moved on. Every tick keeps a short trail of where each ball was; a press
// that misses the ball's current position may be re-judged against the
// position the player actually saw. The kick itself always acts on the live
// ball: rewinding only decides whether the contact counts.
//
// Deliberately conservative: never rewinds past the ball's last contact (a
// kick, push, body impact or wall/landing correction by anyone), never across
// a teleport, and never beyond the configured maximum. A press that already
// qualifies at the current position is handled exactly as before.
internal sealed class BallTrail
{
    internal const int Capacity = 32; // half a second at 64 tick

    private readonly (int Tick, double Time, Vector3 Origin)[] _samples = new (int, double, Vector3)[Capacity];
    private int _count;
    private int _newest = -1;

    internal int Count => _count;

    internal void Record(int tick, double time, Vector3 origin)
    {
        if (!float.IsFinite(origin.X + origin.Y + origin.Z) || !double.IsFinite(time)) return;
        if (_count > 0)
        {
            var newest = _samples[_newest];
            // Same tick: keep the freshest position. Time running backwards
            // means the entity or the clock was replaced; start over.
            if (tick == newest.Tick) { _samples[_newest] = (tick, time, origin); return; }
            if (tick < newest.Tick || time < newest.Time) Clear();
        }
        _newest = (_newest + 1) % Capacity;
        _samples[_newest] = (tick, time, origin);
        if (_count < Capacity) _count++;
    }

    internal void Clear()
    {
        _count = 0;
        _newest = -1;
    }

    // Positions newest first whose age is within `window` seconds and which
    // were recorded strictly after `contactTick` (a sample taken on the
    // contact tick still shows the ball before that contact moved it),
    // stopping at the first jump that no ball travelling at `maxSpeed` could
    // make between two samples (reset, cannon, placement).
    internal IEnumerable<(Vector3 Origin, double Age)> Rewound(double now, double window, int contactTick, float maxSpeed)
    {
        if (_count == 0 || !(window > 0) || !double.IsFinite(now) || !(maxSpeed > 0)) yield break;
        (int Tick, double Time, Vector3 Origin)? later = null;
        for (var i = 0; i < _count; i++)
        {
            var sample = _samples[(_newest - i + Capacity) % Capacity];
            var age = now - sample.Time;
            if (sample.Tick <= contactTick || age > window + 1e-6) yield break;
            if (later is { } next)
            {
                var elapsed = Math.Max(next.Time - sample.Time, 1e-3);
                if (Vector3.Distance(next.Origin, sample.Origin) > maxSpeed * elapsed * 2 + 1) yield break;
            }
            later = sample;
            if (age >= 0) yield return (sample.Origin, age);
        }
    }
}

internal static class KickRewind
{
    internal const float DefaultMaximumMilliseconds = 100f;
    internal const float MaximumMilliseconds = 250f;

    // How far behind the server the player saw the ball: the measured round
    // trip plus two ticks of interpolation and command processing, capped by
    // the administrator's maximum. Zero or an invalid maximum disables it.
    internal static double WindowSeconds(float pingMilliseconds, float tickInterval, float maximumMilliseconds)
    {
        if (!float.IsFinite(maximumMilliseconds) || maximumMilliseconds <= 0) return 0;
        var ping = float.IsFinite(pingMilliseconds) ? Math.Clamp(pingMilliseconds, 0, 1000) : 0;
        var tick = float.IsFinite(tickInterval) && tickInterval > 0 ? tickInterval : 1f / 64;
        return Math.Min(Math.Min(maximumMilliseconds, MaximumMilliseconds), ping + 2000 * tick) / 1000.0;
    }
}

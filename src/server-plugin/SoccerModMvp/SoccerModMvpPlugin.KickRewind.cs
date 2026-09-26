using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;

// Lag-compensated knife contact, see KickRewind.cs. Administrators tune the
// maximum rewind in the Ball workbench (Kick power); 0 disables it and stops
// recording ball trails altogether.
public sealed partial class SoccerModMvpPlugin
{
    private float _kickLagCompensationMs = KickRewind.DefaultMaximumMilliseconds;
    private readonly Dictionary<uint, BallTrail> _ballTrails = new();
    private readonly HashSet<uint> _ballTrailLiveKeys = new();

    // Called once per tick after the ball samples are updated and before any
    // knife contact is evaluated.
    private void RecordBallTrails()
    {
        if (_kickLagCompensationMs <= 0.0f)
        {
            _ballTrails.Clear();
            return;
        }

        _ballTrailLiveKeys.Clear();
        foreach (var target in PlayableBalls())
        {
            var key = target.Ball.EntityHandle.Raw;
            _ballTrailLiveKeys.Add(key);
            if (!_ballTrails.TryGetValue(key, out var trail))
            {
                _ballTrails[key] = trail = new BallTrail();
            }
            trail.Record(Server.TickCount, Server.TickedTime, N(target.Origin));
        }

        if (_ballTrails.Count > _ballTrailLiveKeys.Count)
        {
            foreach (var key in _ballTrails.Keys.Where(key => !_ballTrailLiveKeys.Contains(key)).ToArray())
            {
                _ballTrails.Remove(key);
            }
        }
    }

    // Where this player plausibly saw the ball, newest first. Empty when lag
    // compensation is off, the ball has no trail yet, or its last contact is
    // more recent than anything the player could have been reacting to.
    private IEnumerable<(Vector Origin, double Age)> RewoundKickOrigins(CCSPlayerController player, CPhysicsPropMultiplayer ball)
    {
        if (_kickLagCompensationMs <= 0.0f || !_ballTrails.TryGetValue(ball.EntityHandle.Raw, out var trail))
        {
            yield break;
        }

        // Hard shots get less rewind (HardShots.cs).
        var window = KickRewind.WindowSeconds(player.Ping, Server.TickInterval, HardShotLagCompensationMs(ball));
        foreach (var (origin, age) in trail.Rewound(Server.TickedTime, window, State(ball).LastContactTick, _kickMaximumBallSpeed))
        {
            yield return (C(origin), age);
        }
    }
}

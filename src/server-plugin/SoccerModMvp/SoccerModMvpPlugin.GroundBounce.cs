using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-24 owner request: a more realistic, not over-the-top bounce. The
// pitch rebound comes from the compiled model's surface property only (the
// entity elasticity is inert, measured 2026-08-29), and it is far below a real
// football: CS:S measured ~0.17 of the impact speed. On natural grass a match
// ball keeps about 55-65% (FIFA turf test: 2 m drop -> 60-85 cm rebound).
// So when the ball lands on the pitch hard enough, the upward speed is raised
// to GroundBounceRestitution x impact speed. Horizontal motion, spin and every
// other bounce stay Rubikon's. Each bounce is smaller than the last, and
// landings below the minimum impact speed are left alone, so the ball still
// settles. 0 turns the assist off. Runs in every handling profile.
public sealed partial class SoccerModMvpPlugin
{
    private const float DefaultGroundBounceRestitution = 0.55f;
    private const float GroundBounceMinimumImpact = 120.0f;
    private const float GroundBounceGroundTolerance = 4.0f;
    private const double GroundBounceCooldownSeconds = 0.10;
    private const int GroundBounceHistoryTicks = 4;

    private float _groundBounceRestitution = DefaultGroundBounceRestitution;
    private readonly Queue<float> _recentBallVerticalSpeeds = new();
    private double _lastGroundBounceTime;

    private void TryApplyGroundBounce(Vector origin, Vector current, double now)
    {
        _recentBallVerticalSpeeds.Enqueue(current.Z);
        while (_recentBallVerticalSpeeds.Count > GroundBounceHistoryTicks) _recentBallVerticalSpeeds.Dequeue();

        if (_groundBounceRestitution <= 0.0f
            || _ball is not { IsValid: true } ball
            || now - _lastGroundBounceTime < GroundBounceCooldownSeconds
            || _pausedBallHandle != 0 || _matchPhase == MatchPhase.Paused
            || KnifeKickOwnsTick(ball)
            || origin.Z > StadiumPitchPlaneZ + BallCollisionRadius + GroundBounceGroundTolerance)
        {
            return;
        }

        var impact = _recentBallVerticalSpeeds.Min();
        if (BallContactMath.GroundBounceVertical(impact, current.Z, _groundBounceRestitution, GroundBounceMinimumImpact) is not { } rebound)
        {
            return;
        }

        ball.Teleport(velocity: new Vector(current.X, current.Y, rebound));
        _lastGroundBounceTime = now;
        _recentBallVerticalSpeeds.Clear();
        Logger.LogInformation(
            "[SM2DIAG] ground_bounce impact={Impact:F1} engineRebound={Engine:F1} rebound={Rebound:F1} restitution={Restitution:F2}",
            impact, current.Z, rebound, _groundBounceRestitution);
    }
}

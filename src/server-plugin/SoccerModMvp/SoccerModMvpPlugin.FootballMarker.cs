using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;

// Football mode: where a high ball will come down (first person has no
// overview for headers and volleys). A small yellow cross on the grass,
// predicted from the ball's derived velocity with gravity and air drag
// (docs/ball-realism-analysis-2026-09-24.md: g 800, drag |v|v/4000).
// Beams are seen by everyone (per-player hiding needs CheckTransmit, which
// has crashed this server), so it is a server switch: Admin - Settings.
public sealed partial class SoccerModMvpPlugin
{
    private readonly List<CBeam> _footballMarkerBeams = new();
    private Vector? _footballMarkerPoint;
    private const float MarkerMinDistance = 250.0f;
    private const float MarkerMinHeight = 60.0f;
    private const float MarkerHalfSize = 22.0f;

    private void FootballMarkerOnTick()
    {
        if (Server.TickCount % 8 != 0) return;
        if (!_menuParity.FootballLandingMarker || KeeperHoldsBall || _ballMotionFrozen || _pausedBallHandle != 0
            || _ball is not { IsValid: true } ball || ball.AbsOrigin is not { } origin
            || PredictLanding(origin, _derivedBallVelocity) is not { } landing)
        {
            FootballMarkerClear();
            return;
        }
        if (_footballMarkerPoint is { } last && MathF.Abs(last.X - landing.X) + MathF.Abs(last.Y - landing.Y) < 20.0f) return;
        FootballMarkerClear();
        _footballMarkerPoint = landing;
        var color = System.Drawing.Color.FromArgb(255, 255, 215, 0);
        MarkerBeam(new Vector(landing.X - MarkerHalfSize, landing.Y - MarkerHalfSize, landing.Z), new Vector(landing.X + MarkerHalfSize, landing.Y + MarkerHalfSize, landing.Z), color);
        MarkerBeam(new Vector(landing.X - MarkerHalfSize, landing.Y + MarkerHalfSize, landing.Z), new Vector(landing.X + MarkerHalfSize, landing.Y - MarkerHalfSize, landing.Z), color);
    }

    // Null when the ball is low, rolling, or lands close to where it is now.
    private Vector? PredictLanding(Vector origin, Vector velocity)
    {
        var groundZ = StadiumPitchPlaneZ + BallCollisionRadius;
        if (origin.Z < groundZ + MarkerMinHeight && velocity.Z < 150.0f) return null;
        float x = origin.X, y = origin.Y, z = origin.Z, vx = velocity.X, vy = velocity.Y, vz = velocity.Z;
        const float dt = 1.0f / 64.0f;
        for (var i = 0; i < 64 * 5; i++)
        {
            var speed = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
            vx -= speed * vx / 4000.0f * dt;
            vy -= speed * vy / 4000.0f * dt;
            vz -= (800.0f + speed * vz / 4000.0f) * dt;
            x += vx * dt; y += vy * dt; z += vz * dt;
            if (z > groundZ || vz > 0) continue;
            var dx = x - origin.X;
            var dy = y - origin.Y;
            if (dx * dx + dy * dy < MarkerMinDistance * MarkerMinDistance) return null;
            return new Vector(Math.Clamp(x, -1270.0f, 1270.0f), Math.Clamp(y, -1654.0f, 1654.0f), StadiumPitchPlaneZ + 1.5f);
        }
        return null;
    }

    private void MarkerBeam(Vector start, Vector end, System.Drawing.Color color)
    {
        var beam = Utilities.CreateEntityByName<CBeam>("beam");
        if (beam is null) return;
        beam.Render = color;
        beam.Width = 2.0f;
        beam.EndWidth = 2.0f;
        beam.Teleport(position: start);
        beam.EndPos.X = end.X;
        beam.EndPos.Y = end.Y;
        beam.EndPos.Z = end.Z;
        beam.DispatchSpawn();
        beam.Render = color;
        _footballMarkerBeams.Add(beam);
    }

    private void FootballMarkerClear()
    {
        foreach (var beam in _footballMarkerBeams)
            if (beam.IsValid) beam.Remove();
        _footballMarkerBeams.Clear();
        _footballMarkerPoint = null;
    }
}

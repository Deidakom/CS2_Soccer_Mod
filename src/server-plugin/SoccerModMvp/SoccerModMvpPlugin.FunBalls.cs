using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;

// The arena's playground (tools/arena/world.mjs PLAYGROUND, owner 2026-10-18: "a playground outside with
// loads of options, all working"). The map places its balls as prop_physics_multiplayer sm_fun_<kind>_<n>;
// they ride along as training balls without an owner, so kicks, bounces and body contact are the pitch's.
// Here: each ball goes home when it leaves its field or lies away from it, and every field has its score
// check (positions from world.mjs and the prop scripts in tools/blender/props).
public sealed partial class SoccerModMvpPlugin
{
    private const string FunBallPrefix = "sm_fun_";
    private const int FunBallOwner = -2;   // never a player slot
    private const float PlaygroundFloor = -32.0f;

    private sealed class FunBallState
    {
        public required string Kind;
        public required Vector Home;
        public Vector Last = new(0, 0, 0);
        public double LastMoved, ScoredAt = -1, CooldownUntil;
    }

    private readonly Dictionary<string, FunBallState> _funBalls = new();
    private double _funBallScanAt;

    private static (float X, float Y) FieldCentre(string kind) => kind switch
    {
        "cage" => (3600, 1150), "shootout" => (3600, -1150), "crossbar" => (-3600, 1150), "golf" => (-3600, -1150),
        "volley" => (-1250, 4050), "basket" => (1250, 4050), "torwand" => (-1150, -4050), _ => (1150, -4050),
    };

    private void FunBallsOnTick()
    {
        if (!IsFoundationMap(_currentMapName)) return;
        var now = (double)Server.TickedTime;
        if (now >= _funBallScanAt) { _funBallScanAt = now + 1.0; ScanFunBalls(now); }
        foreach (var training in _trainingBalls.Values.ToArray())
        {
            if (training.OwnerSlot != FunBallOwner || !training.Entity.IsValid || training.Entity.AbsOrigin is not { } at
                || training.Entity.Entity?.Name is not { } name || !_funBalls.TryGetValue(name, out var ball)) continue;
            var p = new Vector(at.X, at.Y, at.Z);
            if ((p - ball.Last).Length() > 2.0f) ball.LastMoved = now;
            var (cx, cy) = FieldCentre(ball.Kind);
            var away = (p - ball.Home).Length();
            var lost = p.Z < PlaygroundFloor - 200 || MathF.Abs(p.X - cx) > 900 || MathF.Abs(p.Y - cy) > 900;
            if (lost || ball.ScoredAt >= 0 && now - ball.ScoredAt > 3.0 || away > 150 && now - ball.LastMoved > 40.0)
            {
                FunBallHome(training, ball);
                continue;
            }
            if (ball.ScoredAt < 0 && now >= ball.CooldownUntil && FunBallScore(ball, ball.Last, p) is { } text)
            {
                ball.CooldownUntil = now + 2.0;
                if (ball.Kind != "crossbar") ball.ScoredAt = now;   // the bar: the ball plays on
                FunBallAnnounce(cx, cy, text);
            }
            ball.Last = p;
        }
    }

    private void ScanFunBalls(double now)
    {
        foreach (var e in Utilities.FindAllEntitiesByDesignerName<CPhysicsPropMultiplayer>("prop_physics_multiplayer"))
        {
            if (!e.IsValid || e.Entity?.Name is not { } name || !name.StartsWith(FunBallPrefix, StringComparison.Ordinal)
                || _trainingBalls.ContainsKey(e.Index) || e.AbsOrigin is not { } at) continue;
            if (!_funBalls.TryGetValue(name, out var state))
            {
                var kind = name[FunBallPrefix.Length..name.LastIndexOf('_')];
                _funBalls[name] = state = new FunBallState { Kind = kind, Home = new Vector(at.X, at.Y, at.Z) };
            }
            state.Last = new Vector(at.X, at.Y, at.Z); state.LastMoved = now; state.ScoredAt = -1;
            _trainingBalls[e.Index] = new TrainingBall { Entity = e, Index = e.Index, OwnerSlot = FunBallOwner };
        }
    }

    private void FunBallHome(TrainingBall training, FunBallState ball)
    {
        training.Entity.Teleport(position: ball.Home, angles: new QAngle(0, 0, 0), velocity: new Vector(0, 0, 0));
        training.DerivedVelocity = new Vector(0, 0, 0);
        training.PreviousOrigin = null;
        ball.Last = new Vector(ball.Home.X, ball.Home.Y, ball.Home.Z);
        ball.LastMoved = Server.TickedTime;
        ball.ScoredAt = -1;
    }

    // a: last tick, b: now
    private static string? FunBallScore(FunBallState ball, Vector a, Vector b)
    {
        const float F = PlaygroundFloor;
        var (cx, cy) = FieldCentre(ball.Kind);
        switch (ball.Kind)
        {
            case "cage":   // 156 wide, 62 high goals in the end boards at y = centre +-440
                if (MathF.Abs(b.X - cx) < 78 && b.Z < F + 65)
                {
                    if (a.Y <= cy + 440 && b.Y > cy + 440) return "Goal in the cage (north)!";
                    if (a.Y >= cy - 440 && b.Y < cy - 440) return "Goal in the cage (south)!";
                }
                return null;
            case "shootout":   // goal line at y = centre - 300, 254 x 96, mouth towards the spot
                return a.Y >= cy - 300 && b.Y < cy - 300 && MathF.Abs(b.X - cx) < 127 && b.Z < F + 99 ? "Penalty scored!" : null;
            case "crossbar":   // lit bar at y = centre + 360, 99 over the floor
                return MathF.Abs(b.Y - (cy + 360)) < 18 && MathF.Abs(b.Z - (F + 99)) < 18 && MathF.Abs(b.X - cx) < 130 ? "Crossbar hit!" : null;
            case "torwand":   // board at y = centre - 180 (turned 180 degrees): through it only through a hole
                if (a.Y >= cy - 180 && b.Y < cy - 180 && MathF.Abs(b.X - cx) < 135 && b.Z < F + 120)
                    return b.X > cx ? "Torwand: bottom hole!" : "Torwand: top hole!";
                return null;
            case "basket":   // rim centre 18 in front of the board at y = centre + 268, top at 121 over the floor
                var dx = b.X - cx; var dy = b.Y - (cy + 250);
                return a.Z > F + 121 && b.Z <= F + 121 && dx * dx + dy * dy < 10 * 10 ? "Basket!" : null;
            case "golf":   // a 20 x 20 cup at the far end of each lane, its floor 3 over the floor
                for (var k = 0; k < 6; k++)
                {
                    var hx = cx + (k - 2.5f) * 90;
                    if (MathF.Abs(b.X - hx) < 10 && MathF.Abs(b.Y - (cy + 130)) < 10 && b.Z < F + 12) return $"Hole in lane {k + 1}!";
                }
                return null;
            case "volley":   // the ball lands in the sand: the point goes to the other side of the net
                return a.Z > F + 30 && b.Z <= F + 30 && MathF.Abs(b.X - cx) < 400 && MathF.Abs(b.Y - cy) < 200
                    ? (b.X < cx ? "Point for the east side!" : "Point for the west side!") : null;
            default: return null;
        }
    }

    private void FunBallAnnounce(float cx, float cy, string text)
    {
        foreach (var player in Utilities.GetPlayers())
            if (player is { IsValid: true, IsBot: false } && player.PlayerPawn.Value?.AbsOrigin is { } at
                && MathF.Abs(at.X - cx) < 1200 && MathF.Abs(at.Y - cy) < 1200)
                player.PrintToChat(FormatSoccerModMessage(text));
    }
}

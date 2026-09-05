using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using V3 = System.Numerics.Vector3;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private sealed class ShotPractice
    {
        public uint Ball;
        public double Start;
        public readonly List<V3> Points = new();
        public V3? Target;
        public bool RequireWall;
        public bool Hit;
        public int Attempts;
        public int Hits;
        public int NextSampleTick;
    }
    private readonly Dictionary<uint, ShotPractice> _practice = new();
    private readonly List<CBeam> _coachBeams = new();
    private void TrainingCoachOnLoad()
    {
        AddCommand("css_ball_target", "Training: target at crosshair; optional wall or off.", OnPracticeTarget);
        AddCommand("css_ball_replay", "Training: show the last personal ball shot path for eight seconds.", (player, command) =>
        {
            if (!CanCoach(player, command)) return;
            if (!_practice.TryGetValue(player!.PlayerPawn.Value!.EntityHandle.Raw, out var shot) || shot.Points.Count < 2)
            { command.ReplyToCommand("Kick a personal training ball first."); return; }
            var stride = Math.Max(1, (shot.Points.Count - 1 + 23) / 24);
            for (var i = 0; i < shot.Points.Count - 1; i += stride)
                CoachLine(shot.Points[i], shot.Points[Math.Min(i + stride, shot.Points.Count - 1)], System.Drawing.Color.Cyan);
            command.ReplyToCommand($"[SM] Last shot path: {shot.Points.Count} samples. Target score: {shot.Hits}/{shot.Attempts}.");
        });
    }
    private bool CanCoach(CCSPlayerController? player, CommandInfo command)
    {
        if (!ImprovedHandling || MatchRunning || !IsEligiblePlayer(player) || !TrainingHasAccess(player!))
        { command.ReplyToCommand("Training coaching requires training access, a living player and no active match."); return false; }
        return true;
    }
    private void OnPracticeTarget(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanCoach(player, command)) return;
        var key = player!.PlayerPawn.Value!.EntityHandle.Raw;
        if (!_practice.TryGetValue(key, out var shot)) _practice[key] = shot = new();
        if (command.ArgCount > 1 && command.GetArg(1) == "off") { shot.Target = null; command.ReplyToCommand("Practice target cleared."); return; }
        if (!TryGetAimHitPoint(player, out var point)) return;
        // Lift clear of the surface so a ball centre can reach floor targets.
        shot.Target = N(point) + V3.UnitZ * BallCollisionRadius;
        shot.RequireWall = command.ArgCount > 1 && command.GetArg(1) == "wall";
        shot.Hits = shot.Attempts = 0;
        DrawPracticeTarget(shot.Target.Value);
        command.ReplyToCommand($"[SM] Target radius 48; {(shot.RequireWall ? "wall pass" : "direct shot")}. Kick your personal training ball. !ball_replay shows its path.");
    }
    private void StartTrainingShot(CCSPlayerController player, PlayableBall target)
    {
        if (!ImprovedHandling || target.IsMatchBall || MatchRunning || !TrainingHasAccess(player)
            || player.PlayerPawn.Value is not { IsValid: true } pawn
            || !_trainingBalls.TryGetValue(target.Ball.Index, out var training) || training.OwnerSlot != player.Slot) return;
        var key = pawn.EntityHandle.Raw;
        if (!_practice.TryGetValue(key, out var shot)) _practice[key] = shot = new();
        shot.Ball = target.Ball.EntityHandle.Raw;
        shot.Start = Server.TickedTime; shot.Points.Clear(); shot.Points.Add(N(target.Origin)); shot.Hit = false;
        shot.NextSampleTick = Server.TickCount + 4;
        if (shot.Target is { } point) { shot.Attempts++; DrawPracticeTarget(point); }
    }
    private void TrainingCoachOnTick(PlayableBall[] balls)
    {
        var living = Utilities.GetPlayers().Where(IsEligiblePlayer).Select(p => p.PlayerPawn.Value!.EntityHandle.Raw).ToHashSet();
        foreach (var key in _practice.Keys.Where(k => !living.Contains(k)).ToArray()) _practice.Remove(key);
        if (MatchRunning) { ClearTrainingCoach(); return; }
        foreach (var (key, shot) in _practice)
        {
            if (shot.Points.Count == 0 || Server.TickedTime - shot.Start > 5 || Server.TickCount < shot.NextSampleTick) continue;
            var target = balls.FirstOrDefault(b => b.Ball.EntityHandle.Raw == shot.Ball);
            if (target.Ball is null) continue;
            var point = N(target.Origin);
            var previous = shot.Points[^1];
            shot.Points.Add(point); shot.NextSampleTick = Server.TickCount + 4;
            if (shot.Target is not { } goal || shot.Hit || shot.RequireWall && State(target.Ball).LastWall < shot.Start) continue;
            if (BallContactMath.SweepCapsule(previous, point, goal, goal, 48) is null) continue;
            shot.Hit = true; shot.Hits++;
            var owner = Utilities.GetPlayers().FirstOrDefault(p => p.PlayerPawn.Value is { IsValid: true } pawn && pawn.EntityHandle.Raw == key);
            owner?.PrintToChat($" \x04[SM]\x01 Target hit! {shot.Hits}/{shot.Attempts}");
        }
    }
    private void DrawPracticeTarget(V3 point)
    {
        CoachLine(point - V3.UnitX * 48, point + V3.UnitX * 48, System.Drawing.Color.Lime);
        CoachLine(point - V3.UnitY * 48, point + V3.UnitY * 48, System.Drawing.Color.Lime);
        CoachLine(point - V3.UnitZ * 48, point + V3.UnitZ * 48, System.Drawing.Color.Lime);
    }
    private void CoachLine(V3 start, V3 end, System.Drawing.Color color)
    {
        _coachBeams.RemoveAll(b => !b.IsValid);
        if (_coachBeams.Count >= 128) return;
        var beam = Utilities.CreateEntityByName<CBeam>("beam");
        if (beam is null || !beam.IsValid) return;
        beam.Render = color; beam.Width = 1; beam.EndWidth = 1;
        beam.Teleport(position: C(start));
        beam.EndPos.X = end.X; beam.EndPos.Y = end.Y; beam.EndPos.Z = end.Z;
        beam.DispatchSpawn();
        _coachBeams.Add(beam);
        AddTimer(8, () => { if (beam.IsValid) beam.Remove(); _coachBeams.Remove(beam); }, TimerFlags.STOP_ON_MAPCHANGE);
    }
    private void ClearTrainingCoach()
    {
        foreach (var beam in _coachBeams) if (beam.IsValid) beam.Remove();
        _coachBeams.Clear(); _practice.Clear();
    }
}

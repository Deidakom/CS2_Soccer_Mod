using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using V3 = System.Numerics.Vector3;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private readonly List<CBeam> _kickoffBeams = new();
    private void ClearKickoffOutline()
    {
        foreach (var beam in _kickoffBeams) if (beam.IsValid) beam.Remove();
        _kickoffBeams.Clear();
    }
    private void DrawKickoffOutline()
    {
        ClearKickoffOutline();
        if (!_kickoffRestrictionActive || !_menuParity.KickoffOutline) return;
        var homeNegative = _kickoffTeam == CsTeam.CounterTerrorist ? _ctDefendsNegativeY : !_ctDefendsNegativeY;
        var sign = homeNegative ? -1 : 1;
        var centre = N(CreateBallResetOrigin());
        var color = _kickoffTeam == CsTeam.CounterTerrorist ? System.Drawing.Color.Cyan : System.Drawing.Color.Red;
        void Line(V3 start, V3 end)
        {
            var beam = Utilities.CreateEntityByName<CBeam>("beam");
            if (beam is null || !beam.IsValid) return;
            beam.Render = color; beam.Width = 2; beam.EndWidth = 2;
            beam.Teleport(position: C(start));
            beam.EndPos.X = end.X; beam.EndPos.Y = end.Y; beam.EndPos.Z = end.Z;
            beam.DispatchSpawn(); _kickoffBeams.Add(beam);
        }
        const float radius = 252.5f;
        foreach (var height in new[] { 8f, 110f })
        {
            var z = centre.Z + height;
            Line(new(-FoundationWallPlaneX, centre.Y, z), new(centre.X - radius, centre.Y, z));
            Line(new(centre.X + radius, centre.Y, z), new(FoundationWallPlaneX, centre.Y, z));
            V3 Point(float angle) => new(centre.X + radius * MathF.Cos(angle), centre.Y - sign * radius * MathF.Sin(angle), z);
            for (var i = 0; i < 16; i++) Line(Point(i * MathF.PI / 16), Point((i + 1) * MathF.PI / 16));
        }
    }
    private void EnforceOutlinedKickoff()
    {
        var homeNegative = _kickoffTeam == CsTeam.CounterTerrorist ? _ctDefendsNegativeY : !_ctDefendsNegativeY;
        var centre = N(CreateBallResetOrigin());
        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsEligiblePlayer(player) || player.PlayerPawn.Value is not { IsValid: true } pawn || pawn.AbsOrigin is not { } origin) continue;
            var result = KickoffBoundary.Constrain(N(origin), N(pawn.AbsVelocity), centre, homeNegative ? -1 : 1, player.Team == _kickoffTeam);
            if (result.Changed) pawn.Teleport(position: C(result.Position), velocity: C(result.Velocity));
        }
    }
    private bool IsKickoffTouchAllowed(CCSPlayerController player)
        => !_kickoffRestrictionActive || !_menuParity.KickoffOutline || player.Team == _kickoffTeam;
}

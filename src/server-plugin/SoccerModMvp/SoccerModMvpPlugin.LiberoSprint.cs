using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;

// 2026-09-26 owner: "Libero sprint" like in Rematch - with the owner's rules:
//  - The keeper (the designated GK slot) is never the libero; he keeps only
//    his own box sprint (GoalkeeperSprint.cs).
//  - A libero exists only while a field player of the team stands BEHIND his
//    own keeper - nearer his own goal line than the keeper, measured on the
//    pitch only: behind the goal line (behind or inside the goal) does not
//    count, and a keeper in his goal counts as standing on the line.
//  - Only in his own half: crossing the halfway line ends it.
//  - No designated keeper in the team: no libero.
// The libero sprints without limit at the normal sprint speed and his bar
// shows LIBERO - UNLIMITED. He takes over once LiberoEnterMargin behind the
// keeper and stays libero while he is behind him at all; of several players
// behind the keeper the deepest one is the libero (LiberoSwitchMargin keeps
// two level players from flipping). Admin - Settings - "Libero sprint".
public sealed partial class SoccerModMvpPlugin
{
    private const float LiberoEnterMargin = 16f;
    private const float LiberoSwitchMargin = 32f;
    private const float PitchHalfWidth = 1024f; // painted touchlines (1018..1024)
    // The painted goal line (touchline rects end at +-1384, the line is 1378..1384),
    // not the goal-detection plane _goalLineY (1400) behind it.
    private const float PitchGoalLineY = 1381f;
    private readonly Dictionary<CsTeam, int> _liberoSlotByTeam = new();

    private bool LiberoSprintAllowed =>
        _menuParity.LiberoSprint && !_sprintSuppressed && !_capFightStarted && !_capFightPending
        && _matchPhase is MatchPhase.Warmup or MatchPhase.Live;

    private void LiberoOnTick()
    {
        if (Server.TickCount % 4 != 0) return;
        if (!LiberoSprintAllowed)
        {
            _liberoSlotByTeam.Clear();
            return;
        }
        foreach (var team in new[] { CsTeam.Terrorist, CsTeam.CounterTerrorist })
        {
            var defendsNegativeY = team == CsTeam.CounterTerrorist ? _ctDefendsNegativeY : !_ctDefendsNegativeY;
            // Distance from the own goal line into the pitch; 0 on (or behind) the line.
            float Depth(Vector o) => MathF.Max(0f, PitchGoalLineY + (defendsNegativeY ? o.Y : -o.Y));
            // On the pitch (not behind the goal line) and in the own half.
            bool OnPitch(Vector o) => MathF.Abs(o.X) <= PitchHalfWidth && MathF.Abs(o.Y) <= PitchGoalLineY
                && (defendsNegativeY ? o.Y <= 0f : o.Y >= 0f);

            var members = Utilities.GetPlayers()
                .Where(p => p.IsValid && p.Team == team && IsEligiblePlayer(p) && p.PlayerPawn.Value?.AbsOrigin is not null)
                .ToList();
            var keeper = members.FirstOrDefault(p => IsGkSlot(p.Slot, team));
            if (keeper?.PlayerPawn.Value?.AbsOrigin is not { } keeperOrigin)
            {
                _liberoSlotByTeam.Remove(team);
                continue;
            }
            var keeperDepth = Depth(keeperOrigin);
            _liberoSlotByTeam.TryGetValue(team, out var currentSlot);
            var behind = members
                .Where(p => p.Slot != keeper.Slot && p.PlayerPawn.Value!.AbsOrigin is { } o && OnPitch(o)
                    && Depth(o) < keeperDepth - (p.Slot == currentSlot ? 0f : LiberoEnterMargin))
                .Select(p => (Player: p, Depth: Depth(p.PlayerPawn.Value!.AbsOrigin!)))
                .ToList();
            if (behind.Count == 0)
            {
                _liberoSlotByTeam.Remove(team);
                continue;
            }
            var deepest = behind.MinBy(c => c.Depth);
            var current = behind.FirstOrDefault(c => c.Player.Slot == currentSlot);
            if (current.Player is not null && current.Depth <= deepest.Depth + LiberoSwitchMargin) deepest = current;
            _liberoSlotByTeam[team] = deepest.Player.Slot;
        }
    }

    private bool IsLibero(CCSPlayerController player) =>
        LiberoSprintAllowed && _liberoSlotByTeam.TryGetValue(player.Team, out var slot) && slot == player.Slot;

    // Unlimited sprint from the libero rule alone (not the keeper's box).
    private bool LiberoOnlySprint(CCSPlayerController player, CCSPlayerPawn pawn) =>
        IsLibero(player) && !InGoalkeeperBox(player, pawn);
}

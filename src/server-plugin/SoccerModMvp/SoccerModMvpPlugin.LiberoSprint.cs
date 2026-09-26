using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;

// 2026-09-26 owner: "Libero sprint" like in Rematch. In each team (2+ players)
// the player nearest his own goal line - usually the keeper, but whoever is
// last when the keeper rushes out - has unlimited sprint for as long as he
// is the last man. It uses the keeper box sprint's unlimited stamina path
// (HasGoalkeeperBoxSprint) but the normal sprint speed; inside his box the
// keeper keeps the box rule. The sprint bar shows it (LIBERO - UNLIMITED).
// A new libero takes over only when clearly deeper (LiberoSwitchMargin), so
// two players level with each other do not flip every tick.
// Admin - Settings - "Libero sprint" (MenuParity.LiberoSprint).
public sealed partial class SoccerModMvpPlugin
{
    private const float LiberoSwitchMargin = 32f;
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
            // Smaller depth = nearer the own goal line.
            var defendsNegativeY = team == CsTeam.CounterTerrorist ? _ctDefendsNegativeY : !_ctDefendsNegativeY;
            float Depth(CCSPlayerController p) =>
                p.PlayerPawn.Value?.AbsOrigin is { } o ? (defendsNegativeY ? o.Y : -o.Y) : float.MaxValue;
            var members = Utilities.GetPlayers()
                .Where(p => p.IsValid && p.Team == team && IsEligiblePlayer(p) && p.PlayerPawn.Value is { IsValid: true })
                .ToList();
            if (members.Count < 2)
            {
                _liberoSlotByTeam.Remove(team);
                continue;
            }
            var deepest = members.MinBy(Depth)!;
            if (_liberoSlotByTeam.TryGetValue(team, out var currentSlot)
                && members.FirstOrDefault(p => p.Slot == currentSlot) is { } current
                && Depth(current) <= Depth(deepest) + LiberoSwitchMargin)
                deepest = current;
            _liberoSlotByTeam[team] = deepest.Slot;
        }
    }

    private bool IsLibero(CCSPlayerController player) =>
        LiberoSprintAllowed && _liberoSlotByTeam.TryGetValue(player.Team, out var slot) && slot == player.Slot;

    // Unlimited sprint from the libero rule alone (not the keeper's box).
    private bool LiberoOnlySprint(CCSPlayerController player, CCSPlayerPawn pawn) =>
        IsLibero(player) && !InGoalkeeperBox(player, pawn);
}

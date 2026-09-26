using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace SoccerModMvp;

// 2026-09-26 owner: the minimap shows the other team as well. CS2 dropped
// mp_radar_showall; the radar draws every player whose spotted state is set,
// so the plugin marks every living player as spotted by everyone, every tick
// (the engine recomputes the state from line of sight each frame).
// Admin - Settings - "Radar: show all players" (MenuParity.RadarShowAll).
public sealed partial class SoccerModMvpPlugin
{
    private void RadarShowAllOnTick()
    {
        if (!_menuParity.RadarShowAll) return;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.PlayerPawn.Value is not { IsValid: true } pawn || !IsAlive(pawn)) continue;
            var state = pawn.EntitySpottedState;
            state.Spotted = true;
            var mask = state.SpottedByMask;
            for (var i = 0; i < mask.Length; i++) mask[i] = uint.MaxValue;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_entitySpottedState");
        }
    }
}

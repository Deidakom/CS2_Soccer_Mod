using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private readonly HashSet<uint> _celebrationPawns = new();
    private double _celebrationUntil;
    private bool _celebrationFriendlyFire;
    private void BeginCelebration()
    {
        if (!_menuParity.CelebrationWeapons || _capFightStarted || _capFightPending) return;
        EndCelebration();
        var players = Utilities.GetPlayers().Where(IsEligiblePlayer).ToArray();
        if (players.Length == 0) return;
        _celebrationFriendlyFire = ConVar.Find("mp_friendlyfire")?.GetPrimitiveValue<bool>() ?? false;
        ConVar.Find("mp_friendlyfire")?.SetValue(true);
        _celebrationUntil = Server.TickedTime + GoalPauseSeconds;
        var weapons = CapWeapons.Where(w => w.Entity is not ("weapon_knife" or "weapon_hegrenade" or "weapon_flashbang")).ToArray();
        var weapon = weapons[Random.Shared.Next(weapons.Length)].Entity;
        try
        {
            foreach (var p in players)
            {
                var pawn = p.PlayerPawn.Value!;
                _celebrationPawns.Add(pawn.EntityHandle.Raw);
                p.GiveNamedItem(weapon);
            }
        }
        catch (Exception ex) { EndCelebration(); Logger.LogWarning(ex, "[SM2DIAG] celebration_failed"); return; }
        var until = _celebrationUntil;
        AddTimer(GoalPauseSeconds, () => { if (_celebrationUntil == until) EndCelebration(); }, TimerFlags.STOP_ON_MAPCHANGE);
    }
    private void EndCelebration()
    {
        if (_celebrationPawns.Count == 0 && _celebrationUntil == 0) return;
        var participants = _celebrationPawns.ToHashSet();
        _celebrationPawns.Clear(); _celebrationUntil = 0;
        ConVar.Find("mp_friendlyfire")?.SetValue(_celebrationFriendlyFire);
        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && p.PlayerPawn.Value is { IsValid: true } pawn && participants.Contains(pawn.EntityHandle.Raw)))
        {
            try
            {
                p.RemoveWeapons();
                if (IsAlive(p.PlayerPawn.Value)) { EnsurePlayerKnife(p, "celebration_end"); ApplyHealthOnSpawn(p.PlayerPawn.Value!); }
            }
            catch (Exception ex) { Logger.LogWarning(ex, "[SM2DIAG] celebration_player_cleanup_failed slot={Slot}", p.Slot); }
        }
    }
}

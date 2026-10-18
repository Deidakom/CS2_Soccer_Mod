using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace SoccerModMvp;

// 2026-10-18 owner: on soccer_soccermod_arena the black pitch boards can be switched off per player,
// "keep the invisible wall, just visually disable". The map draws them as func_brush sm2_pitch_boards
// (non-solid) in front of an invisible wall (tools/arena/apply-arena-vmap.mjs); a player who switches
// them off is just not sent that brush. On by default: they are the map's own look.
public sealed partial class SoccerModMvpPlugin
{
    private const string ArenaPitchBoardsName = "sm2_pitch_boards";
    private const string ArenaPitchBoardsPrefsFile = "soccermod_arena_pitch_boards_off.json";
    private HashSet<ulong> _arenaPitchBoardsOff = new();
    private readonly List<CBaseEntity> _arenaPitchBoards = new();

    private void ArenaPitchBoardsOnLoad()
    {
        _arenaPitchBoardsOff = LoadJsonOrNull<HashSet<ulong>>(ArenaPitchBoardsPrefsFile) ?? new();
        RegisterListener<Listeners.OnMapStart>(_ => _arenaPitchBoards.Clear());
        RegisterEventHandler<EventRoundStart>((_, _) => { _arenaPitchBoards.Clear(); return HookResult.Continue; });   // the round restart respawns map brushes
        RegisterListener<Listeners.CheckTransmit>(ArenaPitchBoardsCheckTransmit);
    }

    private bool ArenaPitchBoardsShownFor(CCSPlayerController p) => !_arenaPitchBoardsOff.Contains(SteamIdOf(p));

    private void ToggleArenaPitchBoards(CCSPlayerController p)
    {
        var id = SteamIdOf(p);
        if (id == 0) return;
        if (!_arenaPitchBoardsOff.Remove(id)) _arenaPitchBoardsOff.Add(id);
        SaveJsonAtomic(ArenaPitchBoardsPrefsFile, _arenaPitchBoardsOff);
    }

    private void ArenaPitchBoardsCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (!OnArena || _arenaPitchBoardsOff.Count == 0) return;
        if (_arenaPitchBoards.Count == 0 || _arenaPitchBoards.Any(b => !b.IsValid))
        {
            _arenaPitchBoards.Clear();
            _arenaPitchBoards.AddRange(Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_brush").Where(b => b.IsValid && b.Entity?.Name == ArenaPitchBoardsName));
            if (_arenaPitchBoards.Count == 0) return;
        }
        foreach ((CCheckTransmitInfo info, CCSPlayerController? receiver) in infoList)
        {
            if (receiver is not { IsValid: true } || ArenaPitchBoardsShownFor(receiver)) continue;
            foreach (var board in _arenaPitchBoards) info.TransmitEntities.Remove(board);
        }
    }
}

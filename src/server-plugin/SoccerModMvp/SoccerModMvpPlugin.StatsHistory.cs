using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private double _statsPossessionAt;
    private void StatsPossessionOnTick()
    {
        var now = Server.TickedTime;
        var elapsed = Math.Clamp(now - _statsPossessionAt, 0, .25);
        _statsPossessionAt = now;
        if (_matchPhase != MatchPhase.Live || _kickoffClockWaitingForBall || _lastKickerSlot < 0) return;
        var owner = Utilities.GetPlayerFromSlot(_lastKickerSlot);
        if (owner is not { IsValid: true } || owner.Team != _lastKickerTeam) return;
        StatsApply(owner, stats => stats.PossessionSeconds += elapsed);
    }
    private readonly Dictionary<int, (ulong Id, double Since)> _statsOnline = new();
    private readonly Dictionary<ulong, double> _rankNext = new();
    private void StatsConnected(int slot, ulong id)
    {
        if (id == 0) return;
        StatsDisconnected(slot);
        var p = Utilities.GetPlayerFromSlot(slot);
        var entry = GetOrCreateStatsEntry(id, p?.PlayerName ?? $"steamid:{id}");
        entry.LastConnectedUtc = DateTime.UtcNow;
        _statsOnline[slot] = (id, Server.TickedTime);
    }
    private void StatsDisconnected(int slot)
    {
        if (!_statsOnline.Remove(slot, out var session)) return;
        if (_statsBySteamId.TryGetValue(session.Id, out var entry)) entry.PlaySeconds += Math.Max(0, Server.TickedTime - session.Since);
        _rankNext.Remove(session.Id); _statsChatNext.Remove(session.Id);
        _readyPlayers.Remove(session.Id);
        _preCapJoin.Remove(session.Id);
        ClearTrainingDevices(session.Id);
    }
    private void StatsFlushPlayTime()
    {
        foreach (var (slot, session) in _statsOnline.ToArray())
        {
            if (_statsBySteamId.TryGetValue(session.Id, out var entry)) entry.PlaySeconds += Math.Max(0, Server.TickedTime - session.Since);
            _statsOnline[slot] = (session.Id, Server.TickedTime);
        }
    }
    private void OpenLastConnectedMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Rankings - Last connected (UTC)", OnBack = OpenRankingMenu };
        foreach (var entry in _statsStore.Entries.Where(e => e.LastConnectedUtc is not null).OrderByDescending(e => e.LastConnectedUtc).Take(100))
            menu.Add($"{entry.Name}: {entry.LastConnectedUtc:MM-dd HH:mm}", p =>
            {
                var detail = new NumberMenu { Title = entry.Name, OnBack = OpenLastConnectedMenu };
                detail.AddInfo($"Last connected: {entry.LastConnectedUtc:u}");
                detail.AddInfo(entry.CreatedUtc is { } created ? $"First recorded: {created:u}" : "First recorded: unknown (legacy history)");
                detail.AddInfo($"Recorded play time: {entry.PlaySeconds / 3600:0.##} hours");
                detail.Add("Public stats", a => OpenStatsDetail(a, entry.Name, entry.Public, OpenLastConnectedMenu));
                detail.Add("Competitive stats", a => OpenStatsDetail(a, entry.Name, entry.Competitive, OpenLastConnectedMenu));
                OpenNumberMenu(p, detail);
            });
        OpenNumberMenu(player, menu);
    }
}

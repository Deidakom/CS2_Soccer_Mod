using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private readonly List<ulong> _preCapJoin = new();
    private readonly HashSet<ulong> _capEligible = new();
    private bool _capRosterCaptured;
    private readonly Dictionary<ulong, CsTeam> _draftAssignments = new();
    private int CapMatchMaxPlayers => _menuParity.CapTeamSize;
    private int DraftJoinNumber(CCSPlayerController p) => _menuParity.CapFirstPlayers == 2
        ? _preCapJoin.IndexOf(p.AuthorizedSteamID?.SteamId64 ?? 0) + 1 : CapJoinNumber(p.Slot);
    private bool CapAllowed(CCSPlayerController p) => _menuParity.CapFirstPlayers == 0
        || (_capRosterCaptured && _capEligible.Contains(p.AuthorizedSteamID?.SteamId64 ?? 0));
    private void CaptureCapRoster()
    {
        _capEligible.Clear(); _draftAssignments.Clear(); _capDraftCompleted = false;
        var online = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && (p.AuthorizedSteamID?.SteamId64 ?? 0) != 0).ToList();
        var ids = _menuParity.CapFirstPlayers == 2
            ? _preCapJoin.Where(id => online.Any(p => p.AuthorizedSteamID!.SteamId64 == id))
            : online.OrderBy(p => CapJoinNumber(p.Slot) == 0 ? int.MaxValue : CapJoinNumber(p.Slot)).Select(p => p.AuthorizedSteamID!.SteamId64);
        foreach (var id in ids.Take(CapMatchMaxPlayers * 2)) _capEligible.Add(id);
        _capRosterCaptured = true;
    }
    private void OpenCapRulesMenu(CCSPlayerController player)
    {
        if (!SettingsAccess(player)) return;
        var menu = new NumberMenu { Title = "CAP - Settings", OnBack = OpenCapMenu };
        menu.Add($"Team size: {CapMatchMaxPlayers}", p => BeginChatNumberInput(p, "Players per team (1-11).", 1, 11,
            (actor, value) => EditParity(actor, s => { s.CapTeamSize = (int)value; _capRosterCaptured = false; }, OpenCapRulesMenu), OpenCapRulesMenu));
        menu.Add($"First {CapMatchMaxPlayers * 2}: {new[] { "OFF", "Connection order", "Pre-CAP signup" }[_menuParity.CapFirstPlayers]}", p => EditParity(p, s => { s.CapFirstPlayers = (s.CapFirstPlayers + 1) % 3; _capRosterCaptured = false; }, OpenCapRulesMenu));
        OpenNumberMenu(player, menu);
    }
    private void OpenCapRosterMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "CAP - Roster", OnBack = OpenCapMenu };
        menu.AddInfo($"Picks remaining: {_capPicksLeft}; team size: {CapMatchMaxPlayers}");
        menu.Add("Refresh", OpenCapRosterMenu);
        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot).OrderBy(p => CapJoinNumber(p.Slot)))
        {
            var id = p.AuthorizedSteamID?.SteamId64 ?? 0;
            var number = _menuParity.CapFirstPlayers == 2 ? _preCapJoin.IndexOf(id) + 1 : CapJoinNumber(p.Slot);
            menu.AddInfo($"[{number}] {p.PlayerName} | {p.Team} | {FormatCapPositions(id)}{(CapAllowed(p) ? "" : " (waiting)")}");
        }
        OpenNumberMenu(player, menu);
    }
    private void TogglePreCapJoin(CCSPlayerController player)
    {
        if (MatchRunning || _capFightPending || _capFightStarted || _capPicksLeft > 0 || IsWebsiteCapActive()) return;
        var id = player.AuthorizedSteamID?.SteamId64 ?? 0; if (id == 0) return;
        if (!_preCapJoin.Remove(id)) _preCapJoin.Add(id);
        CapChat(player, _preCapJoin.Contains(id) ? $"Pre-CAP signup number {_preCapJoin.IndexOf(id) + 1}." : "Left pre-CAP signup.");
        OpenCapRosterMenu(player);
    }
    private void EnforceDraftAssignment(CCSPlayerController player)
    {
        if ((_capPicksLeft <= 0 && !_capDraftCompleted && !_matchWasCap) || _draftAssignments.Count == 0
            || IsWebsiteCapActive() || !player.IsValid) return;
        var id = player.AuthorizedSteamID?.SteamId64 ?? 0;
        var expected = _draftAssignments.GetValueOrDefault(id, CsTeam.Spectator);
        if (player.Team != expected) player.ChangeTeam(expected);
    }
}

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private bool HasPublicControl(CCSPlayerController? player, bool settings = false) => player is null
        || HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0, "match") || _menuParity.PublicAccess >= (settings ? 2 : 1);
    private bool RequirePublicControl(CCSPlayerController? player, bool settings = false)
    {
        if (HasPublicControl(player, settings)) return true;
        player!.PrintToChat(FormatSoccerModMessage("This action requires admin access in the current public-access mode.")); return false;
    }
    private readonly Dictionary<ulong, CsTeam> _readyRoster = new();
    private bool _stoppageActive;
    private float _stoppagePreviousY;
    private bool LogActive => _menuParity.MatchLogEnabled && (!_menuParity.LogScheduled
        || MatchRuleMath.InLogWindow(DateTime.Now, _menuParity.LogDays, _menuParity.LogStartMinute, _menuParity.LogEndMinute));
    private static IEnumerable<CCSPlayerController> ReadyParticipants() => Utilities.GetPlayers()
        .Where(p => p.IsValid && !p.IsBot && p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist
            && (p.AuthorizedSteamID?.SteamId64 ?? 0) != 0);
    private void BeginReadyCheck()
    {
        _readyPlayers.Clear(); _readyRoster.Clear();
        foreach (var p in ReadyParticipants()) _readyRoster[p.AuthorizedSteamID!.SteamId64] = p.Team;
        if (_menuParity.ReadyMode == 0) return;
        foreach (var p in ReadyParticipants()) OpenReadyMenu(p);
    }
    private void OpenReadyMenu(CCSPlayerController player)
    {
        if (_matchPhase != MatchPhase.Paused || _menuParity.ReadyMode == 0) return;
        var menu = new NumberMenu { Title = "Match - Ready Check", OnBack = OpenMatchMenu };
        menu.AddInfo(_menuParity.ReadyMode == 1 ? "AUTO: waiting for the paused roster" : "MANUAL: resume through Match menu");
        menu.Add("Ready", p => SetPlayerReady(p, true));
        menu.Add("Not ready", p => SetPlayerReady(p, false));
        menu.Add("Refresh", OpenReadyMenu);
        var online = ReadyParticipants().ToDictionary(p => p.AuthorizedSteamID!.SteamId64);
        var roster = _menuParity.ReadyMode == 1 ? _readyRoster : online.ToDictionary(p => p.Key, p => p.Value.Team);
        foreach (var (id, team) in roster)
        {
            var present = online.TryGetValue(id, out var member) && member.Team == team;
            menu.AddInfo($"{(present ? member!.PlayerName : id.ToString())}: {(present && _readyPlayers.Contains(id) ? "READY" : present ? "waiting" : "disconnected / changed team")}");
        }
        OpenNumberMenu(player, menu);
    }
    private void SetPlayerReady(CCSPlayerController player, bool ready)
    {
        if (_matchPhase != MatchPhase.Paused || _menuParity.ReadyMode == 0 || player.IsBot
            || player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)) return;
        var id = player.AuthorizedSteamID?.SteamId64 ?? 0;
        if (id == 0) return;
        if (ready) _readyPlayers.Add(id); else _readyPlayers.Remove(id);
        var current = ReadyParticipants().ToDictionary(p => p.AuthorizedSteamID!.SteamId64, p => p.Team);
        _readyPlayers.RemoveWhere(key => !current.ContainsKey(key));
        if (_menuParity.ReadyMode == 1 && MatchRuleMath.EveryoneReady(_readyRoster, current, _readyPlayers))
        { ResumeFromPause("all_ready"); return; }
        OpenReadyMenu(player);
    }
    private bool ShouldEndPeriod()
    {
        if (!_menuParity.HalfwayStoppage || _inGoldenGoal) return true;
        if (_ball is not { IsValid: true } || _ball.AbsOrigin is not { } origin) return true;
        var relativeY = origin.Y - CreateBallResetOrigin().Y;
        if (!_stoppageActive)
        {
            _stoppageActive = true;
            AnnounceAll("[SM] Stoppage time: this period ends when the ball returns to halfway or a goal is scored.");
            AppendMatchLog("STOPPAGE started");
        }
        var crossed = MatchRuleMath.CrossedHalfway(_stoppagePreviousY, relativeY, BallCollisionRadius);
        _stoppagePreviousY = relativeY;
        return crossed;
    }
    private void OpenMatchRulesMenu(CCSPlayerController player)
    {
        if (!SettingsAccess(player)) return;
        var menu = new NumberMenu { Title = "Match - Rules", OnBack = OpenMatchSettingsMenu };
        menu.Add($"Ready check: {new[] { "OFF", "AUTO", "ON USE / MANUAL" }[_menuParity.ReadyMode]}", p => EditParity(p, s => s.ReadyMode = (s.ReadyMode + 1) % 3, OpenMatchRulesMenu));
        menu.Add($"End period: {(_menuParity.HalfwayStoppage ? "Ball returns to halfway" : "Immediately at time")}", p => EditParity(p, s => s.HalfwayStoppage = !s.HalfwayStoppage, OpenMatchRulesMenu));
        menu.Add($"Round MVP: {OnOff(_menuParity.RoundMvp)}", p => EditParity(p, s => s.RoundMvp = !s.RoundMvp, OpenMatchRulesMenu));
        menu.Add($"Hostname status: {OnOff(_menuParity.HostnameInfo)}", p => EditParity(p, s => s.HostnameInfo = !s.HostnameInfo, OpenMatchRulesMenu));
        OpenNumberMenu(player, menu);
    }
    private void OpenLogScheduleMenu(CCSPlayerController player)
    {
        if (!SettingsAccess(player)) return;
        var menu = new NumberMenu { Title = "Match Log - Schedule (server time)", OnBack = OpenLogSettingsMenu };
        menu.AddInfo($"Server time: {DateTime.Now:ddd HH:mm}; active: {OnOff(LogActive)}");
        menu.Add($"Schedule: {OnOff(_menuParity.LogScheduled)}", p => EditParity(p, s => s.LogScheduled = !s.LogScheduled, OpenLogScheduleMenu));
        for (var i = 0; i < 7; i++)
        {
            var bit = 1 << i;
            menu.Add($"{(DayOfWeek)i}: {OnOff((_menuParity.LogDays & bit) != 0)}", p => EditParity(p, s => s.LogDays ^= bit, OpenLogScheduleMenu));
        }
        void TimeInput(CCSPlayerController p, bool start) => BeginChatTextInput(p, "Enter HH:mm (00:00 to 23:59).", (actor, text) =>
        {
            if (!TimeOnly.TryParseExact(text, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var time))
            { actor.PrintToChat(FormatSoccerModMessage("Use HH:mm, for example 19:30.")); OpenLogScheduleMenu(actor); return; }
            EditParity(actor, s => { if (start) s.LogStartMinute = time.Hour * 60 + time.Minute; else s.LogEndMinute = time.Hour * 60 + time.Minute; }, OpenLogScheduleMenu);
        }, OpenLogScheduleMenu);
        menu.Add($"Start: {_menuParity.LogStartMinute / 60:D2}:{_menuParity.LogStartMinute % 60:D2}", p => TimeInput(p, true));
        menu.Add($"Stop: {_menuParity.LogEndMinute / 60:D2}:{_menuParity.LogEndMinute % 60:D2}", p => TimeInput(p, false));
        menu.AddInfo("Equal times = full day; overnight uses start day.");
        OpenNumberMenu(player, menu);
    }
}

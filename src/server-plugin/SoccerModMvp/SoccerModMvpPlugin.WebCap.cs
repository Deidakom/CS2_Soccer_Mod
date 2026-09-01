using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// The public KICKOFF website is the only cap collector/draw UI. This small,
// server-only bridge accepts its validated draw through the private host RCON
// helper, persists it for six hours, and applies team/position assignments as
// the selected Steam accounts connect to CS2.
public sealed partial class SoccerModMvpPlugin
{
    private const string WebsiteCapFileName = "soccermod_webcap.json";
    private const long WebsiteCapTtlSeconds = 6 * 60 * 60;
    private static readonly HashSet<int> WebsiteCapHalfSeconds = new() { 450, 600, 900 };

    private sealed class WebsiteCapAssignment
    {
        public ulong SteamId64 { get; set; }
        public string Team { get; set; } = "home";
        public string Role { get; set; } = "DEF";
    }

    private sealed class WebsiteCapStore
    {
        public int Version { get; set; } = 2;
        public bool Active { get; set; }
        public long CreatedAtUnix { get; set; }
        public int HalfSeconds { get; set; }
        public List<WebsiteCapAssignment> Assignments { get; set; } = new();
    }

    private WebsiteCapStore _websiteCapStore = new();
    private bool _websiteCapImporting;
    private readonly HashSet<int> _websiteCapAppliedSlots = new();
    private readonly HashSet<int> _websiteCapSpectatorNotifiedSlots = new();
    private readonly Dictionary<ulong, string> _websiteCapOriginalClanTags = new();
    private int _nextWebsiteCapEnforceTick;

    private void WebsiteCapOnLoad()
    {
        _websiteCapStore = LoadJsonOrNull<WebsiteCapStore>(WebsiteCapFileName) ?? new WebsiteCapStore();
        ExpireWebsiteCapIfNeeded();
        AddCommand("css_sm2webcap_begin", "Server only: begin a KICKOFF website assignment import.", OnWebsiteCapBeginCommand);
        AddCommand("css_sm2webcap_reference", "Server only: stage the voted KICKOFF half length.", OnWebsiteCapReferenceCommand);
        AddCommand("css_sm2webcap_assign", "Server only: add a KICKOFF SteamID/team/role assignment.", OnWebsiteCapAssignCommand);
        AddCommand("css_sm2webcap_commit", "Server only: activate the imported KICKOFF assignments.", OnWebsiteCapCommitCommand);
        AddCommand("css_sm2webcap_clear", "Server only: release all KICKOFF website assignments.", OnWebsiteCapClearCommand);
        // Kept as a backwards-compatible bridge command for older website
        // deployments. It now spectates non-cap players instead of kicking
        // them from the server.
        AddCommand("css_sm2webcap_evict", "Server only: spectate players outside the active KICKOFF cap.", OnWebsiteCapEvictCommand);
        AddCommand("css_sm2webcap_status", "Server only: show the active KICKOFF assignment count.", OnWebsiteCapStatusCommand);
        RegisterListener<Listeners.OnClientPutInServer>(WebsiteCapOnClientPutInServer);
        RegisterListener<Listeners.OnClientDisconnect>(WebsiteCapOnPlayerDisconnect);
        RegisterEventHandler<EventPlayerTeam>(WebsiteCapOnPlayerTeam);
    }

    private static bool IsWebsiteCapRole(string role) =>
        role is "GK" or "DEF" or "MID" or "WING";

    private static bool IsServerOnlyWebsiteCapCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            return true;
        }

        command.ReplyToCommand("[SM] this KICKOFF bridge command is server-only");
        return false;
    }

    private void ExpireWebsiteCapIfNeeded()
    {
        if (!_websiteCapStore.Active)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (_websiteCapStore.CreatedAtUnix > 0 && now - _websiteCapStore.CreatedAtUnix <= WebsiteCapTtlSeconds)
        {
            return;
        }

        ClearWebsiteCapState("expired");
    }

    private void OnWebsiteCapBeginCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsServerOnlyWebsiteCapCommand(player, command))
        {
            return;
        }

        ClearWebsiteCapState("new_import");
        _websiteCapImporting = true;
        command.ReplyToCommand("[SM] KICKOFF website assignment import started");
    }

    private void OnWebsiteCapClearCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsServerOnlyWebsiteCapCommand(player, command))
        {
            return;
        }

        var hadState = _websiteCapStore.Active
            || _websiteCapImporting
            || _websiteCapStore.Assignments.Count > 0
            || _websiteCapAppliedSlots.Count > 0;
        ClearWebsiteCapState("website_clear");
        command.ReplyToCommand(hadState
            ? "[SM] KICKOFF website assignments cleared; normal team selection restored"
            : "[SM] no KICKOFF website cap was active");
    }

    private void OnWebsiteCapReferenceCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsServerOnlyWebsiteCapCommand(player, command))
        {
            return;
        }
        if (!_websiteCapImporting
            || command.ArgCount != 2
            || !int.TryParse(command.GetArg(1), out var halfSeconds)
            || !WebsiteCapHalfSeconds.Contains(halfSeconds))
        {
            command.ReplyToCommand("[SM] usage: css_sm2webcap_reference <450|600|900>");
            return;
        }

        _websiteCapStore.HalfSeconds = halfSeconds;
        command.ReplyToCommand($"[SM] KICKOFF cap reference staged: {FormatHalfMinutes(halfSeconds)} min/half");
    }

    private void OnWebsiteCapAssignCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsServerOnlyWebsiteCapCommand(player, command))
        {
            return;
        }
        if (!_websiteCapImporting || command.ArgCount != 4 || _websiteCapStore.Assignments.Count >= 12)
        {
            command.ReplyToCommand("[SM] usage: css_sm2webcap_assign <steamid64> <home|away> <GK|DEF|MID|WING>");
            return;
        }

        var steamIdText = command.GetArg(1);
        var team = command.GetArg(2).ToLowerInvariant();
        var role = command.GetArg(3).ToUpperInvariant();
        if (steamIdText.Length != 17
            || !ulong.TryParse(steamIdText, out var steamId64)
            || team is not ("home" or "away")
            || !IsWebsiteCapRole(role)
            || _websiteCapStore.Assignments.Any(entry => entry.SteamId64 == steamId64))
        {
            command.ReplyToCommand("[SM] invalid or duplicate KICKOFF website assignment");
            return;
        }

        _websiteCapStore.Assignments.Add(new WebsiteCapAssignment
        {
            SteamId64 = steamId64,
            Team = team,
            Role = role,
        });
        command.ReplyToCommand($"[SM] KICKOFF assignment staged ({_websiteCapStore.Assignments.Count}/12)");
    }

    private void OnWebsiteCapCommitCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsServerOnlyWebsiteCapCommand(player, command))
        {
            return;
        }
        if (!_websiteCapImporting
            || !WebsiteCapHalfSeconds.Contains(_websiteCapStore.HalfSeconds)
            || _websiteCapStore.Assignments.Count is < 1 or > 12)
        {
            command.ReplyToCommand("[SM] no valid KICKOFF assignment import is in progress");
            return;
        }

        _websiteCapImporting = false;
        _websiteCapStore.Active = true;
        _websiteCapStore.CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!SaveJsonAtomic(WebsiteCapFileName, _websiteCapStore))
        {
            _websiteCapStore.Active = false;
            command.ReplyToCommand("[SM] KICKOFF assignment persistence failed");
            return;
        }

        foreach (var connected in Utilities.GetPlayers())
        {
            ApplyWebsiteCapAssignment(connected);
            SpectateWebsiteCapNonParticipant(connected, "commit");
        }
        Logger.LogInformation(
            "[SM2DIAG] website_cap_committed assignments={Assignments} halfSeconds={HalfSeconds}",
            _websiteCapStore.Assignments.Count,
            _websiteCapStore.HalfSeconds);
        command.ReplyToCommand(
            $"[SM] KICKOFF website cap active with {_websiteCapStore.Assignments.Count} assignment(s), "
            + $"reference {FormatHalfMinutes(_websiteCapStore.HalfSeconds)} min/half");
    }

    private void OnWebsiteCapEvictCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsServerOnlyWebsiteCapCommand(player, command))
        {
            return;
        }
        if (!_websiteCapStore.Active)
        {
            command.ReplyToCommand("[SM] no KICKOFF website cap is active");
            return;
        }

        var spectated = 0;
        foreach (var connected in Utilities.GetPlayers())
        {
            if (!connected.IsValid || connected.IsBot)
            {
                continue;
            }
            if (SpectateWebsiteCapNonParticipant(connected, "bridge_command"))
            {
                spectated++;
            }
        }
        Logger.LogInformation("[SM2DIAG] website_cap_nonparticipants_spectated players={Players}", spectated);
        command.ReplyToCommand($"[SM] moved {spectated} non-cap player(s) to spectator; nobody was kicked");
    }

    private void OnWebsiteCapStatusCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsServerOnlyWebsiteCapCommand(player, command))
        {
            return;
        }
        ExpireWebsiteCapIfNeeded();
        command.ReplyToCommand(
            $"[SM] KICKOFF website cap active={_websiteCapStore.Active} assignments={_websiteCapStore.Assignments.Count} "
            + $"halfSeconds={_websiteCapStore.HalfSeconds} created={_websiteCapStore.CreatedAtUnix}");
        foreach (var connected in Utilities.GetPlayers())
        {
            if (!connected.IsValid || connected.IsBot)
            {
                continue;
            }

            var steamId64 = connected.AuthorizedSteamID?.SteamId64 ?? 0UL;
            var assignment = _websiteCapStore.Assignments.FirstOrDefault(entry => entry.SteamId64 == steamId64);
            if (assignment is null)
            {
                continue;
            }

            command.ReplyToCommand(
                $"[SM] online={connected.PlayerName} expected={assignment.Team}/{assignment.Role} "
                + $"actual={connected.Team} alive={IsAlive(connected.PlayerPawn.Value)} tag={connected.Clan}");
        }
    }

    private void WebsiteCapOnClientPutInServer(int playerSlot)
    {
        AddTimer(1.0f, () =>
        {
            if (Utilities.GetPlayerFromSlot(playerSlot) is { IsValid: true } player)
            {
                ApplyWebsiteCapAssignment(player);
            }
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void WebsiteCapOnPlayerDisconnect(int playerSlot) =>
        RemoveWebsiteCapSlotState(playerSlot);

    private void RemoveWebsiteCapSlotState(int playerSlot)
    {
        _websiteCapAppliedSlots.Remove(playerSlot);
        _websiteCapSpectatorNotifiedSlots.Remove(playerSlot);
    }

    private HookResult WebsiteCapOnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is not { IsValid: true } || !IsWebsiteCapActive())
        {
            return HookResult.Continue;
        }

        if (IsWebsiteCapParticipant(player)
            || (CsTeam)@event.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            return HookResult.Continue;
        }

        // EventPlayerTeam is a notification rather than a cancellable team
        // selector hook. Correct an unassigned player's native team-menu
        // choice on the next frame, and keep the OnTick guard below as a
        // fallback for engines that don't emit the event for every selector
        // path.
        Server.NextFrame(() => SpectateWebsiteCapNonParticipant(player, "team_change"));
        return HookResult.Continue;
    }

    private void WebsiteCapOnTick()
    {
        if (!IsWebsiteCapActive() || Server.TickCount < _nextWebsiteCapEnforceTick)
        {
            return;
        }

        _nextWebsiteCapEnforceTick = Server.TickCount + 16; // 250 ms at 64 tick
        foreach (var player in Utilities.GetPlayers())
        {
            SpectateWebsiteCapNonParticipant(player, "periodic_enforce");
        }
    }

    private bool IsWebsiteCapActive()
    {
        ExpireWebsiteCapIfNeeded();
        return _websiteCapStore.Active;
    }

    private bool IsWebsiteCapParticipant(CCSPlayerController player)
    {
        return TryGetWebsiteCapParticipantTeam(player, out _);
    }

    private bool TryGetWebsiteCapParticipantTeam(CCSPlayerController player, out CsTeam team)
    {
        var steamId64 = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        var assignment = steamId64 == 0UL
            ? null
            : _websiteCapStore.Assignments.FirstOrDefault(entry => entry.SteamId64 == steamId64);
        if (assignment is null)
        {
            team = CsTeam.None;
            return false;
        }

        team = assignment.Team == "home" ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
        return true;
    }

    private bool SpectateWebsiteCapNonParticipant(CCSPlayerController player, string reason)
    {
        if (!IsWebsiteCapActive()
            || !player.IsValid
            || player.IsBot
            || IsWebsiteCapParticipant(player)
            || player.Team == CsTeam.Spectator)
        {
            return false;
        }

        player.ChangeTeam(CsTeam.Spectator);
        if (_websiteCapSpectatorNotifiedSlots.Add(player.Slot))
        {
            player.PrintToChat(" \x04[KICKOFF]\x01 This CAP is already running; you are spectating until it ends.");
            Logger.LogInformation(
                "[SM2DIAG] website_cap_nonparticipant_spectated slot={Slot} steamid={SteamId} reason={Reason}",
                player.Slot,
                player.AuthorizedSteamID?.SteamId64 ?? 0UL,
                reason);
        }
        return true;
    }

    private static bool IsWebsiteCapPositionTag(string? tag) =>
        tag is "[GK]" or "[DEF]" or "[MID]" or "[WING]";

    private static void SetWebsiteCapClanTag(CCSPlayerController player, string tag)
    {
        if (string.Equals(player.Clan, tag, StringComparison.Ordinal))
        {
            return;
        }

        player.Clan = tag;
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_szClan");
    }

    private void ApplyWebsiteCapPositionTag(CCSPlayerController player, ulong steamId64, string role)
    {
        if (!_websiteCapOriginalClanTags.ContainsKey(steamId64))
        {
            _websiteCapOriginalClanTags[steamId64] = IsWebsiteCapPositionTag(player.Clan)
                ? string.Empty
                : player.Clan ?? string.Empty;
        }

        SetWebsiteCapClanTag(player, $"[{role}]");
    }

    private void ClearWebsiteCapPositionTags()
    {
        foreach (var connected in Utilities.GetPlayers())
        {
            if (!connected.IsValid || connected.IsBot)
            {
                continue;
            }

            var steamId64 = connected.AuthorizedSteamID?.SteamId64 ?? 0UL;
            if (_websiteCapOriginalClanTags.TryGetValue(steamId64, out var originalTag))
            {
                SetWebsiteCapClanTag(connected, originalTag);
            }
            else if (IsWebsiteCapPositionTag(connected.Clan))
            {
                SetWebsiteCapClanTag(connected, string.Empty);
            }
        }

        _websiteCapOriginalClanTags.Clear();
    }

    private void ClearWebsiteCapState(string reason)
    {
        ClearWebsiteCapPositionTags();
        foreach (var playerSlot in _websiteCapAppliedSlots)
        {
            _playerPositions.Remove(playerSlot);
        }

        _websiteCapStore = new WebsiteCapStore();
        _websiteCapImporting = false;
        _websiteCapAppliedSlots.Clear();
        _websiteCapSpectatorNotifiedSlots.Clear();
        _nextWebsiteCapEnforceTick = 0;
        SaveJsonAtomic(WebsiteCapFileName, _websiteCapStore);
        Logger.LogInformation("[SM2DIAG] website_cap_cleared reason={Reason}", reason);
    }

    private bool TryGetWebsiteCapReference(out float halfSeconds)
    {
        ExpireWebsiteCapIfNeeded();
        if (_websiteCapStore.Active && WebsiteCapHalfSeconds.Contains(_websiteCapStore.HalfSeconds))
        {
            halfSeconds = _websiteCapStore.HalfSeconds;
            return true;
        }

        halfSeconds = 0.0f;
        return false;
    }

    private static string FormatHalfMinutes(float halfSeconds) =>
        (halfSeconds / 60.0f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    private void EnsureWebsiteCapPlayerOnField(int playerSlot, ulong steamId64, CsTeam targetTeam)
    {
        var player = Utilities.GetPlayerFromSlot(playerSlot);
        if (!_websiteCapStore.Active
            || player is not { IsValid: true }
            || player.IsBot
            || player.AuthorizedSteamID?.SteamId64 != steamId64)
        {
            return;
        }

        var assignment = _websiteCapStore.Assignments.FirstOrDefault(entry => entry.SteamId64 == steamId64);
        if (assignment is null)
        {
            return;
        }

        if (player.Team != targetTeam)
        {
            player.SwitchTeam(targetTeam);
        }
        if (IsAlive(player.PlayerPawn.Value))
        {
            return;
        }

        player.Respawn();
        Logger.LogInformation(
            "[SM2DIAG] website_cap_player_respawned slot={Slot} steamid={SteamId} team={Team}",
            player.Slot,
            steamId64,
            assignment.Team);
    }

    private void WebsiteCapOnPlayerSpawn(CCSPlayerController player)
    {
        AddTimer(0.25f, () =>
        {
            if (player.IsValid)
            {
                ApplyWebsiteCapAssignment(player);
            }
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void ApplyWebsiteCapAssignment(CCSPlayerController player)
    {
        ExpireWebsiteCapIfNeeded();
        if (!_websiteCapStore.Active || !player.IsValid || player.IsBot)
        {
            return;
        }

        var steamId64 = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        var assignment = _websiteCapStore.Assignments.FirstOrDefault(entry => entry.SteamId64 == steamId64);
        if (assignment is null)
        {
            SpectateWebsiteCapNonParticipant(player, "apply_assignment");
            return;
        }

        _websiteCapSpectatorNotifiedSlots.Remove(player.Slot);
        var targetTeam = assignment.Team == "home" ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
        if (player.Team != targetTeam)
        {
            player.SwitchTeam(targetTeam);
        }
        _playerPositions[player.Slot] = assignment.Role;
        ApplyWebsiteCapPositionTag(player, steamId64, assignment.Role);
        if (!IsAlive(player.PlayerPawn.Value))
        {
            AddTimer(
                0.25f,
                () => EnsureWebsiteCapPlayerOnField(player.Slot, steamId64, targetTeam),
                TimerFlags.STOP_ON_MAPCHANGE);
        }
        if (_websiteCapAppliedSlots.Add(player.Slot))
        {
            player.PrintToChat($" \x04[KICKOFF]\x01 Website CAP: {assignment.Team.ToUpperInvariant()} team, position {assignment.Role}.");
            Logger.LogInformation(
                "[SM2DIAG] website_cap_assignment_applied slot={Slot} steamid={SteamId} team={Team} role={Role}",
                player.Slot,
                steamId64,
                assignment.Team,
                assignment.Role);
        }
    }
}

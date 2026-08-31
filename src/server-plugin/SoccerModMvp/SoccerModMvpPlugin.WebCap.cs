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

    private sealed class WebsiteCapAssignment
    {
        public ulong SteamId64 { get; set; }
        public string Team { get; set; } = "home";
        public string Role { get; set; } = "DEF";
    }

    private sealed class WebsiteCapStore
    {
        public int Version { get; set; } = 1;
        public bool Active { get; set; }
        public long CreatedAtUnix { get; set; }
        public List<WebsiteCapAssignment> Assignments { get; set; } = new();
    }

    private WebsiteCapStore _websiteCapStore = new();
    private bool _websiteCapImporting;
    private readonly HashSet<int> _websiteCapAppliedSlots = new();

    private void WebsiteCapOnLoad()
    {
        _websiteCapStore = LoadJsonOrNull<WebsiteCapStore>(WebsiteCapFileName) ?? new WebsiteCapStore();
        ExpireWebsiteCapIfNeeded();
        AddCommand("css_sm2webcap_begin", "Server only: begin a KICKOFF website assignment import.", OnWebsiteCapBeginCommand);
        AddCommand("css_sm2webcap_assign", "Server only: add a KICKOFF SteamID/team/role assignment.", OnWebsiteCapAssignCommand);
        AddCommand("css_sm2webcap_commit", "Server only: activate the imported KICKOFF assignments.", OnWebsiteCapCommitCommand);
        AddCommand("css_sm2webcap_evict", "Server only: remove current players before the website cap reconnect.", OnWebsiteCapEvictCommand);
        AddCommand("css_sm2webcap_status", "Server only: show the active KICKOFF assignment count.", OnWebsiteCapStatusCommand);
        RegisterListener<Listeners.OnClientPutInServer>(WebsiteCapOnClientPutInServer);
        RegisterListener<Listeners.OnClientDisconnect>(WebsiteCapOnPlayerDisconnect);
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

        _websiteCapStore = new WebsiteCapStore();
        _websiteCapAppliedSlots.Clear();
        SaveJsonAtomic(WebsiteCapFileName, _websiteCapStore);
        Logger.LogInformation("[SM2DIAG] website_cap_expired");
    }

    private void OnWebsiteCapBeginCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsServerOnlyWebsiteCapCommand(player, command))
        {
            return;
        }

        _websiteCapStore = new WebsiteCapStore();
        _websiteCapImporting = true;
        _websiteCapAppliedSlots.Clear();
        SaveJsonAtomic(WebsiteCapFileName, _websiteCapStore);
        command.ReplyToCommand("[SM] KICKOFF website assignment import started");
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
        if (!_websiteCapImporting || _websiteCapStore.Assignments.Count is < 1 or > 12)
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
        }
        Logger.LogInformation("[SM2DIAG] website_cap_committed assignments={Assignments}", _websiteCapStore.Assignments.Count);
        command.ReplyToCommand($"[SM] KICKOFF website cap active with {_websiteCapStore.Assignments.Count} assignment(s)");
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

        var evicted = 0;
        foreach (var connected in Utilities.GetPlayers())
        {
            if (!connected.IsValid || connected.IsBot || connected.UserId is not { } userId)
            {
                continue;
            }
            Server.ExecuteCommand($"kickid {userId} \"A new website CAP is ready\"");
            evicted++;
        }
        Logger.LogInformation("[SM2DIAG] website_cap_evicted players={Players}", evicted);
        command.ReplyToCommand($"[SM] evicted {evicted} current player(s) for the KICKOFF reconnect");
    }

    private void OnWebsiteCapStatusCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsServerOnlyWebsiteCapCommand(player, command))
        {
            return;
        }
        ExpireWebsiteCapIfNeeded();
        command.ReplyToCommand(
            $"[SM] KICKOFF website cap active={_websiteCapStore.Active} assignments={_websiteCapStore.Assignments.Count} created={_websiteCapStore.CreatedAtUnix}");
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
        _websiteCapAppliedSlots.Remove(playerSlot);

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
            return;
        }

        var targetTeam = assignment.Team == "home" ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
        if (player.Team != targetTeam)
        {
            player.SwitchTeam(targetTeam);
        }
        _playerPositions[player.Slot] = assignment.Role;
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

using System.Collections.Generic;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// CAP MVP: a mechanical C# port of the already-validated
// src/ball-lab/core/cap.js state machine (Idle -> Collecting -> Picking ->
// Ready). cap.js's team buckets "2"/"3" are CsTeam.Terrorist/
// CounterTerrorist verbatim (CS2's own enum values), so this ports as a
// straight rename rather than a new design. Chat-command picking
// (numbered list + !pick <n>) per the MVP plan - no CenterHtml/ChatMenu
// dependency to go wrong.
public sealed partial class SoccerModMvpPlugin
{
    private enum CapPhase
    {
        Idle,
        Collecting,
        Picking,
        Ready,
    }

    private const int CapMinPlayers = 2;
    private const int CapMaxPlayers = 10;

    private CapPhase _capPhase = CapPhase.Idle;
    private int? _capOwnerSlot;
    private readonly List<(int Slot, string Name)> _capPlayers = new();
    private readonly Dictionary<CsTeam, int?> _capCaptains = new() { [CsTeam.Terrorist] = null, [CsTeam.CounterTerrorist] = null };
    private readonly Dictionary<CsTeam, List<int>> _capTeams = new() { [CsTeam.Terrorist] = new(), [CsTeam.CounterTerrorist] = new() };
    private CsTeam? _capTurnTeam;

    private void CapOnLoad()
    {
        AddCommand("css_cap", "Open a captain-pick session (owner) or check status.", OnCapCommand);
        AddCommand("css_join", "Join the open cap pool.", OnCapJoinCommand);
        AddCommand("css_leave", "Leave the open cap pool.", OnCapLeaveCommand);
        AddCommand("css_draft", "Owner: begin picking captains/teams from the joined pool.", OnCapDraftCommand);
        AddCommand("css_pick", "Captain on turn: pick a player by pool number.", OnCapPickCommand);
        AddCommand("css_capcancel", "Owner or admin: cancel the open cap session.", OnCapCancelCommand);
    }

    private void ResetCapState()
    {
        _capPhase = CapPhase.Idle;
        _capOwnerSlot = null;
        _capPlayers.Clear();
        _capCaptains[CsTeam.Terrorist] = null;
        _capCaptains[CsTeam.CounterTerrorist] = null;
        _capTeams[CsTeam.Terrorist].Clear();
        _capTeams[CsTeam.CounterTerrorist].Clear();
        _capTurnTeam = null;
    }

    private void LogCapTransition(string reason)
    {
        Logger.LogInformation(
            "[SM2DIAG] cap_transition phase={Phase} reason={Reason} players={Players}",
            _capPhase,
            reason,
            _capPlayers.Count);
    }

    private void OnCapCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (_capPhase != CapPhase.Idle)
        {
            command.ReplyToCommand(FormatCapStatus());
            return;
        }

        if (!RequirePermission(player, command, "cap"))
        {
            return;
        }

        if (player is null)
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        // Opening a cap sends everyone to spectator so the pool starts clean.
        foreach (var p in Utilities.GetPlayers())
        {
            if (p.IsValid && p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
            {
                p.ChangeTeam(CsTeam.Spectator);
            }
        }

        _capPhase = CapPhase.Collecting;
        _capOwnerSlot = player.Slot;
        _capPlayers.Add((player.Slot, player.PlayerName));
        AfkArmServerlock();
        LogCapTransition("opened");
        AnnounceAll($" \x04[Cap]\x01 {player.PlayerName} opened captain-pick. Type !join to enter the pool.");
    }

    private void OnCapJoinCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            return;
        }

        if (_capPhase != CapPhase.Collecting)
        {
            command.ReplyToCommand("[SM] cap is not collecting players right now");
            return;
        }

        if (_capPlayers.Any(p => p.Slot == player.Slot))
        {
            command.ReplyToCommand("[SM] you already joined");
            return;
        }

        if (_capPlayers.Count >= CapMaxPlayers)
        {
            command.ReplyToCommand("[SM] the pool is full");
            return;
        }

        _capPlayers.Add((player.Slot, player.PlayerName));
        LogCapTransition("joined");
        AnnounceAll($" \x04[Cap]\x01 {player.PlayerName} joined ({_capPlayers.Count}/{CapMaxPlayers}).");
    }

    private void OnCapLeaveCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            return;
        }

        if (_capPhase != CapPhase.Collecting)
        {
            command.ReplyToCommand("[SM] cap is not collecting players right now");
            return;
        }

        var index = _capPlayers.FindIndex(p => p.Slot == player.Slot);
        if (index < 0)
        {
            command.ReplyToCommand("[SM] you haven't joined");
            return;
        }

        _capPlayers.RemoveAt(index);
        if (player.Slot == _capOwnerSlot)
        {
            if (_capPlayers.Count == 0)
            {
                ResetCapState();
                LogCapTransition("cancelled_owner_left_empty");
                AnnounceAll(" \x04[Cap]\x01 Owner left an empty pool - cap cancelled.");
                return;
            }

            _capOwnerSlot = _capPlayers[0].Slot;
            AnnounceAll($" \x04[Cap]\x01 Owner left - {_capPlayers[0].Name} is now the owner.");
        }

        LogCapTransition("left");
        AnnounceAll($" \x04[Cap]\x01 {player.PlayerName} left the pool ({_capPlayers.Count}/{CapMaxPlayers}).");
    }

    private void OnCapDraftCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            return;
        }

        if (_capPhase != CapPhase.Collecting)
        {
            command.ReplyToCommand("[SM] cap is not collecting players right now");
            return;
        }

        if (player.Slot != _capOwnerSlot)
        {
            command.ReplyToCommand("[SM] only the cap owner can start the draft");
            return;
        }

        if (_capPlayers.Count < CapMinPlayers)
        {
            command.ReplyToCommand($"[SM] need at least {CapMinPlayers} players (have {_capPlayers.Count})");
            return;
        }

        var captainT = _capPlayers[0];
        var captainCt = _capPlayers[1];
        _capCaptains[CsTeam.Terrorist] = captainT.Slot;
        _capCaptains[CsTeam.CounterTerrorist] = captainCt.Slot;
        _capTeams[CsTeam.Terrorist] = new List<int> { captainT.Slot };
        _capTeams[CsTeam.CounterTerrorist] = new List<int> { captainCt.Slot };

        MoveCapPlayerToTeam(captainT.Slot, CsTeam.Terrorist);
        MoveCapPlayerToTeam(captainCt.Slot, CsTeam.CounterTerrorist);

        if (_capPlayers.Count == 2)
        {
            _capPhase = CapPhase.Ready;
            _capTurnTeam = null;
            LogCapTransition("ready");
            AnnounceAll(" \x04[Cap]\x01 Teams set - !match start when ready.");
            return;
        }

        _capPhase = CapPhase.Picking;
        _capTurnTeam = CsTeam.Terrorist;
        LogCapTransition("draft_started");
        AnnounceAll($" \x04[Cap]\x01 Draft started! Captains: T={captainT.Name} CT={captainCt.Name}.");
        AnnouncePickTurn();
    }

    private void OnCapPickCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            return;
        }

        if (_capPhase != CapPhase.Picking)
        {
            command.ReplyToCommand("[SM] not currently picking");
            return;
        }

        var turnTeam = _capTurnTeam!.Value;
        if (_capCaptains[turnTeam] != player.Slot)
        {
            command.ReplyToCommand("[SM] it's not your turn to pick");
            return;
        }

        if (command.ArgCount < 2 || !int.TryParse(command.GetArg(1), out var poolNumber)
            || poolNumber < 1 || poolNumber > _capPlayers.Count)
        {
            command.ReplyToCommand("[SM] usage: css_pick <number> - " + FormatPoolList());
            return;
        }

        var target = _capPlayers[poolNumber - 1];
        if (_capTeams[CsTeam.Terrorist].Contains(target.Slot) || _capTeams[CsTeam.CounterTerrorist].Contains(target.Slot))
        {
            command.ReplyToCommand("[SM] that player is already picked");
            return;
        }

        _capTeams[turnTeam].Add(target.Slot);
        MoveCapPlayerToTeam(target.Slot, turnTeam);

        var pickedCount = _capTeams[CsTeam.Terrorist].Count + _capTeams[CsTeam.CounterTerrorist].Count;
        if (pickedCount == _capPlayers.Count)
        {
            _capPhase = CapPhase.Ready;
            _capTurnTeam = null;
            LogCapTransition("ready");
            AnnounceAll($" \x04[Cap]\x01 {target.Name} picked by {player.PlayerName}. Teams set - !match start when ready.");
            return;
        }

        _capTurnTeam = turnTeam == CsTeam.Terrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
        LogCapTransition("picked");
        AnnounceAll($" \x04[Cap]\x01 {target.Name} picked by {player.PlayerName} ({turnTeam}).");
        AnnouncePickTurn();
    }

    private void OnCapCancelCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (_capPhase == CapPhase.Idle)
        {
            command.ReplyToCommand("[SM] no cap session is open");
            return;
        }

        var isOwner = player is not null && player.Slot == _capOwnerSlot;
        if (!isOwner && !RequirePermission(player, command, "cap"))
        {
            return;
        }

        ResetCapState();
        LogCapTransition("cancelled");
        AnnounceAll(" \x04[Cap]\x01 Cap session cancelled.");
    }

    // A drafted player disconnecting mid-pick cancels the whole session
    // (cap.js semantics) rather than leaving a phantom pick behind; a
    // disconnect while still just in the pool (Collecting) is a normal leave.
    private void CapOnPlayerDisconnect(int slot)
    {
        if (_capPhase == CapPhase.Idle || !_capPlayers.Any(p => p.Slot == slot))
        {
            return;
        }

        if (_capPhase == CapPhase.Collecting)
        {
            var index = _capPlayers.FindIndex(p => p.Slot == slot);
            if (index >= 0)
            {
                var leaving = _capPlayers[index];
                _capPlayers.RemoveAt(index);
                if (slot == _capOwnerSlot)
                {
                    if (_capPlayers.Count == 0)
                    {
                        ResetCapState();
                        LogCapTransition("cancelled_owner_disconnected_empty");
                        return;
                    }
                    _capOwnerSlot = _capPlayers[0].Slot;
                }
                LogCapTransition("left_disconnect");
                AnnounceAll($" \x04[Cap]\x01 {leaving.Name} disconnected and left the pool.");
            }
            return;
        }

        ResetCapState();
        LogCapTransition("cancelled_drafted_player_disconnected");
        AnnounceAll(" \x04[Cap]\x01 A drafted player disconnected - cap session cancelled.");
    }

    private void MoveCapPlayerToTeam(int slot, CsTeam team)
    {
        var controller = Utilities.GetPlayerFromSlot(slot);
        if (controller is { IsValid: true })
        {
            controller.SwitchTeam(team);
        }
    }

    private string FormatPoolList()
    {
        var unpicked = _capPlayers
            .Select((p, i) => (Index: i + 1, p.Name, p.Slot))
            .Where(p => !_capTeams[CsTeam.Terrorist].Contains(p.Slot) && !_capTeams[CsTeam.CounterTerrorist].Contains(p.Slot))
            .Select(p => PlayerPositionTag(p.Slot) is { } pos ? $"{p.Index}:{p.Name}[{pos}]" : $"{p.Index}:{p.Name}");
        return string.Join(", ", unpicked);
    }

    private void AnnouncePickTurn()
    {
        var captainSlot = _capCaptains[_capTurnTeam!.Value];
        var captainName = captainSlot is { } slot && Utilities.GetPlayerFromSlot(slot) is { IsValid: true } c ? c.PlayerName : "?";
        AnnounceAll($" \x04[Cap]\x01 {_capTurnTeam} captain {captainName}'s turn: !pick <n> - {FormatPoolList()}");
    }

    private string FormatCapStatus() => _capPhase switch
    {
        CapPhase.Idle => "[SM] Cap | idle | !cap to open",
        CapPhase.Collecting => $"[SM] Cap | joining {_capPlayers.Count}/{CapMaxPlayers} | !join, owner: !draft",
        CapPhase.Picking => $"[SM] Cap | picking {_capTeams[CsTeam.Terrorist].Count}v{_capTeams[CsTeam.CounterTerrorist].Count} | {_capTurnTeam} captain: !pick <n>",
        CapPhase.Ready => $"[SM] Cap | ready {_capTeams[CsTeam.Terrorist].Count}v{_capTeams[CsTeam.CounterTerrorist].Count} | match starting",
        _ => "[SM] Cap state unavailable",
    };
}

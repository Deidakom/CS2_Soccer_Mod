using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

public sealed partial class SoccerModMvpPlugin
{
    private readonly Dictionary<CsTeam, int> _gkSlotByTeam = new();
    private readonly Dictionary<int, (CsTeam Team, double Until, uint Controller)> _gkSkinHalfSwaps = new();

    private void GkSkinOnLoad()
    {
        AddCommand("css_sm2gk", "Toggle your team's goalkeeper skin.", OnGkSkinToggleCommand);
        AddCommand("css_gk", "Chat alias: !gk toggles your team's goalkeeper skin.", OnGkSkinToggleCommand);
        RegisterEventHandler<EventPlayerTeam>(OnGkSkinPlayerTeam);
    }

    private void OnGkSkinToggleCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true }
            || player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            command.ReplyToCommand("[SM] join T or CT before claiming the goalkeeper skin");
            return;
        }

        var team = player.Team;
        if (IsGkSlot(player.Slot, team))
        {
            _gkSlotByTeam.Remove(team);
            RefreshGoalkeeperSprint(player);
            ApplyTeamAppearance(player, "gk_release_command");
            command.ReplyToCommand("[SM] goalkeeper skin: off");
            Logger.LogInformation(
                "[SM2DIAG] gk_skin_released slot={Slot} name={Name} team={Team} reason=command",
                player.Slot,
                player.PlayerName,
                team);
            return;
        }

        if (TryGetCurrentGk(team, out var currentGk))
        {
            command.ReplyToCommand(
                $"[SM] only one goalkeeper skin allowed per team — {currentGk.PlayerName} already has it");
            return;
        }

        _gkSlotByTeam[team] = player.Slot;
        ApplyTeamAppearance(player, "gk_claim_command");
        RefreshGoalkeeperSprint(player);
        command.ReplyToCommand("[SM] goalkeeper skin: on — unlimited 1.175x sprint inside your own small GK box; use your normal sprint controls.");
        Logger.LogInformation(
            "[SM2DIAG] gk_skin_claimed slot={Slot} name={Name} team={Team}",
            player.Slot,
            player.PlayerName,
            team);
    }

    private HookResult OnGkSkinPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is null)
        {
            return HookResult.Continue;
        }

        var oldTeam = (CsTeam)@event.Oldteam;
        if (_gkSkinHalfSwaps.Remove(player.Slot, out var swap)
            && swap.Team == (CsTeam)@event.Team && Server.TickedTime <= swap.Until
            && swap.Controller == player.EntityHandle.Raw && IsGkSlot(player.Slot, swap.Team))
        {
            RefreshGoalkeeperSprint(player);
            Server.NextFrame(() => { if (player.IsValid) ApplyTeamAppearance(player, "gk_halftime"); });
            return HookResult.Continue;
        }
        if (oldTeam is CsTeam.Terrorist or CsTeam.CounterTerrorist
            && IsGkSlot(player.Slot, oldTeam))
        {
            _gkSlotByTeam.Remove(oldTeam);
            Logger.LogInformation(
                "[SM2DIAG] gk_skin_released slot={Slot} name={Name} team={Team} reason=team_change",
                player.Slot,
                player.PlayerName,
                oldTeam);
        }

        RefreshGoalkeeperSprint(player);
        Server.NextFrame(() => { if (player.IsValid) ApplyTeamAppearance(player, "gk_team_change"); });
        return HookResult.Continue;
    }

    // Match side swaps preserve the designated skins for BOTH teams, without
    // requiring participation in any experimental feature.
    private void GkSkinPrepareHalfSwap()
    {
        var keepers = _gkSlotByTeam.ToArray()
            .Select(entry => (Team: entry.Key, Player: Utilities.GetPlayerFromSlot(entry.Value)))
            .Where(entry => entry.Player is { IsValid: true } && entry.Player.Team == entry.Team).ToArray();
        _gkSlotByTeam.Clear();
        _gkSkinHalfSwaps.Clear();
        foreach (var (team, keeper) in keepers)
        {
            var nextTeam = team == CsTeam.Terrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
            _gkSlotByTeam[nextTeam] = keeper!.Slot;
            _gkSkinHalfSwaps[keeper.Slot] = (nextTeam, Server.TickedTime + 2, keeper.EntityHandle.Raw);
        }
    }

    private void GkSkinOnPlayerDisconnect(int slot)
    {
        _gkSkinHalfSwaps.Remove(slot);
        _sprintStateBySlot.Remove(slot);
        foreach (var team in _gkSlotByTeam
                     .Where(entry => entry.Value == slot)
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            _gkSlotByTeam.Remove(team);
            Logger.LogInformation(
                "[SM2DIAG] gk_skin_released slot={Slot} team={Team} reason=disconnect",
                slot,
                team);
        }
    }

    private bool TryGetCurrentGk(CsTeam team, out CCSPlayerController currentGk)
    {
        currentGk = null!;
        if (!_gkSlotByTeam.TryGetValue(team, out var slot))
        {
            return false;
        }

        var player = Utilities.GetPlayerFromSlot(slot);
        if (player is { IsValid: true } && player.Team == team)
        {
            currentGk = player;
            return true;
        }

        _gkSlotByTeam.Remove(team);
        Logger.LogWarning(
            "[SM2DIAG] gk_skin_stale_slot_released slot={Slot} team={Team}",
            slot,
            team);
        return false;
    }

    private bool IsGkSlot(int slot, CsTeam team) =>
        _gkSlotByTeam.TryGetValue(team, out var gkSlot) && gkSlot == slot;

    // 2026-08-31 user feedback: pure white (255,255,255) is a no-op multiply
    // on Render -- it just shows the untouched base texture, which reads as
    // "no skin applied" rather than a deliberate GK color. A mid grey still
    // darkens the texture (visibly a tint, not an absence of one) while
    // staying clearly distinct from the cyan-blue CT team color.
    private static Color GkRenderColor(CsTeam team) => team == CsTeam.CounterTerrorist
        ? Color.FromArgb(170, 170, 170)
        : Color.FromArgb(255, 140, 0);
}

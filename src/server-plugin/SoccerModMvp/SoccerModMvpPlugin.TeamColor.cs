using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

public sealed partial class SoccerModMvpPlugin
{
    private const string ModelPathT = "characters/models/tm_phoenix/tm_phoenix.vmdl";
    private const string ModelPathCt = "characters/models/ctm_sas/ctm_sas.vmdl";

    private bool _teamColorEnabled = true;
    private bool _teamModelEnabled = true;
    private int _teamColorTr = 255;
    private int _teamColorTg = 40;
    private int _teamColorTb = 40;
    private int _teamColorCtr = 40;
    private int _teamColorCtg = 80;
    private int _teamColorCtb = 255;

    private void TeamColorOnLoad()
    {
        AddCommand(
            "css_sm2teamcolor",
            "Admin: enable or disable the red/blue team tint.",
            OnTeamColorToggleCommand);
        AddCommand(
            "css_sm2teammodel",
            "Admin: enable uniform stock player models per team.",
            OnTeamModelToggleCommand);

        Server.NextFrame(() => ApplyAllTeamAppearances("plugin_load"));
        AddTimer(
            0.25f,
            () => ApplyAllTeamAppearances("plugin_load_plus_0_25s"),
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void TeamColorOnRoundStart()
    {
        Server.NextFrame(() => ApplyAllTeamAppearances("round_start"));
    }

    private void TeamColorOnPlayerSpawn(CCSPlayerController player)
    {
        Server.NextFrame(() => ApplyTeamAppearance(player, "spawn_next_frame"));
    }

    private void ApplyAllTeamAppearances(string reason)
    {
        foreach (var player in Utilities.GetPlayers())
        {
            ApplyTeamAppearance(player, reason);
        }
    }

    private void ApplyTeamAppearance(CCSPlayerController? player, string reason)
    {
        if (player is null
            || !player.IsValid
            || player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)
            || player.PlayerPawn.Value is not { IsValid: true } pawn
            || !IsAlive(pawn))
        {
            return;
        }

        try
        {
            if (_teamModelEnabled)
            {
                pawn.SetModel(player.Team == CsTeam.Terrorist ? ModelPathT : ModelPathCt);
            }

            pawn.Render = _teamColorEnabled
                ? TeamRenderColor(player.Team)
                : Color.White;
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");

            Logger.LogDebug(
                "[SM2DIAG] team_appearance_applied slot={Slot} team={Team} colors={Colors} model={Model} reason={Reason}",
                player.Slot,
                player.Team,
                _teamColorEnabled,
                _teamModelEnabled,
                reason);
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "[SM2DIAG] team_appearance_failed slot={Slot} team={Team} reason={Reason}",
                player.Slot,
                player.Team,
                reason);
        }
    }

    private Color TeamRenderColor(CsTeam team) => team == CsTeam.Terrorist
        ? Color.FromArgb(_teamColorTr, _teamColorTg, _teamColorTb)
        : Color.FromArgb(_teamColorCtr, _teamColorCtg, _teamColorCtb);

    private void OnTeamColorToggleCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match")) return;

        if (command.ArgCount >= 2)
        {
            if (!TryParseTeamAppearanceToggle(command.GetArg(1), out var enabled))
            {
                command.ReplyToCommand("[SM] usage: css_sm2teamcolor <on|off>");
                return;
            }

            _teamColorEnabled = enabled;
            SaveMatchSettings("team_color_toggle_command");
            Server.NextFrame(() => ApplyAllTeamAppearances("team_color_toggle_command"));
        }

        command.ReplyToCommand(
            $"[SM] team color tint: {(_teamColorEnabled ? "on" : "off")} "
            + "(usage: css_sm2teamcolor <on|off>)");
    }

    private void OnTeamModelToggleCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match")) return;

        if (command.ArgCount >= 2)
        {
            if (!TryParseTeamAppearanceToggle(command.GetArg(1), out var enabled))
            {
                command.ReplyToCommand("[SM] usage: css_sm2teammodel <on|off>");
                return;
            }

            _teamModelEnabled = enabled;
            SaveMatchSettings("team_model_toggle_command");
            if (_teamModelEnabled)
            {
                Server.NextFrame(() => ApplyAllTeamAppearances("team_model_toggle_command"));
            }
        }

        command.ReplyToCommand(
            $"[SM] uniform team models: {(_teamModelEnabled ? "on" : "off")} "
            + "(usage: css_sm2teammodel <on|off>; off takes full effect on the next spawn)");
    }

    private static bool TryParseTeamAppearanceToggle(string value, out bool enabled)
    {
        if (value.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
            return true;
        }

        if (value.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            enabled = false;
            return true;
        }

        enabled = false;
        return false;
    }
}

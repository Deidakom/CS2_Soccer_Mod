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
    private const string DefaultTPlayerModel = "characters/models/tm_phoenix/tm_phoenix.vmdl";
    private const string DefaultCtPlayerModel = "characters/models/ctm_sas/ctm_sas.vmdl";

    private bool _teamColorEnabled = true;
    private bool _teamModelEnabled = true;
    private int _teamColorTRed = 255;
    private int _teamColorTGreen = 40;
    private int _teamColorTBlue = 40;
    private int _teamColorCtRed = 40;
    private int _teamColorCtGreen = 80;
    private int _teamColorCtBlue = 255;
    private string _teamModelT = DefaultTPlayerModel;
    private string _teamModelCt = DefaultCtPlayerModel;

    private void TeamAppearanceOnLoad()
    {
        AddCommand(
            "css_sm2teamcolor",
            "Admin: enable team colors or set <t|ct> <r> <g> <b>.",
            OnTeamColorCommand);
        AddCommand(
            "css_sm2teammodel",
            "Admin: enable uniform stock player models per team.",
            OnTeamModelCommand);

        Server.NextFrame(() => ApplyAllTeamAppearances("plugin_load"));
        AddTimer(
            0.25f,
            () => ApplyAllTeamAppearances("plugin_load_plus_0_25s"),
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void TeamAppearanceOnRoundStart()
    {
        Server.NextFrame(() => ApplyAllTeamAppearances("round_start"));
    }

    private void TeamAppearanceOnPlayerSpawn(CCSPlayerController player)
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
                pawn.SetModel(player.Team == CsTeam.Terrorist ? _teamModelT : _teamModelCt);
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
        ? Color.FromArgb(255, _teamColorTRed, _teamColorTGreen, _teamColorTBlue)
        : Color.FromArgb(255, _teamColorCtRed, _teamColorCtGreen, _teamColorCtBlue);

    private void OnTeamColorCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        var changed = false;
        if (command.ArgCount == 2 && TryParseOnOff(command.GetArg(1), out var enabled))
        {
            _teamColorEnabled = enabled;
            changed = true;
        }
        else if (command.ArgCount == 5
                 && TryParseTeam(command.GetArg(1), out var team)
                 && TryParseColorComponent(command.GetArg(2), out var red)
                 && TryParseColorComponent(command.GetArg(3), out var green)
                 && TryParseColorComponent(command.GetArg(4), out var blue))
        {
            if (team == CsTeam.Terrorist)
            {
                _teamColorTRed = red;
                _teamColorTGreen = green;
                _teamColorTBlue = blue;
            }
            else
            {
                _teamColorCtRed = red;
                _teamColorCtGreen = green;
                _teamColorCtBlue = blue;
            }

            changed = true;
        }

        if (changed)
        {
            SaveMatchSettings("team_color_command");
            ApplyAllTeamAppearances("team_color_command");
        }

        command.ReplyToCommand(
            $"[SM] team colors: {(_teamColorEnabled ? "on" : "off")} "
            + $"T={_teamColorTRed},{_teamColorTGreen},{_teamColorTBlue} "
            + $"CT={_teamColorCtRed},{_teamColorCtGreen},{_teamColorCtBlue} "
            + "(usage: css_sm2teamcolor <on|off> | <t|ct> <r> <g> <b>)");
    }

    private void OnTeamModelCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        if (command.ArgCount == 2 && TryParseOnOff(command.GetArg(1), out var enabled))
        {
            _teamModelEnabled = enabled;
            SaveMatchSettings("team_model_command");
            if (enabled)
            {
                ApplyAllTeamAppearances("team_model_command");
            }
        }

        command.ReplyToCommand(
            $"[SM] uniform team models: {(_teamModelEnabled ? "on" : "off")} "
            + $"T={_teamModelT} CT={_teamModelCt} "
            + "(usage: css_sm2teammodel <on|off>; disabling takes full effect on the next spawn)");
    }

    private static bool TryParseOnOff(string value, out bool enabled)
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

    private static bool TryParseTeam(string value, out CsTeam team)
    {
        if (value.Equals("t", StringComparison.OrdinalIgnoreCase))
        {
            team = CsTeam.Terrorist;
            return true;
        }

        if (value.Equals("ct", StringComparison.OrdinalIgnoreCase))
        {
            team = CsTeam.CounterTerrorist;
            return true;
        }

        team = CsTeam.None;
        return false;
    }

    private static bool TryParseColorComponent(string value, out int component) =>
        int.TryParse(value, out component) && component is >= 0 and <= 255;
}

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
    // "characters/models/..." is the legacy CS:GO path scheme. In current CS2 it
    // resolves to a ~4.8KB stub resource, not a real rigged character (confirmed
    // via VPK inspection: characters/models/tm_phoenix/tm_phoenix.vmdl_c is 4793
    // bytes vs. the real agents/models/tm_phoenix/tm_phoenix.vmdl_c at 560826
    // bytes) -- using the old path silently "succeeds" but renders as an inanimate
    // object instead of an animated player model. CS2's Agent system replaced it;
    // "agents/models/..." is the correct current path for stock team models.
    private const string ModelPathT = "agents/models/tm_phoenix/tm_phoenix.vmdl";
    private const string ModelPathCt = "agents/models/ctm_sas/ctm_sas.vmdl";

    private bool _teamColorEnabled = true;
    private bool _teamModelEnabled = true;
    // Neon-leaning saturated tones (T: neon red/pink, CT: neon cyan-blue) instead
    // of plain primaries -- Render is a multiply tint on the base texture, so it
    // can only darken toward these hues, never brighten past the source texture;
    // pure (255,0,0)/(0,0,255) crushed shadow detail too hard, these read as
    // strongly "neon" while keeping some model shading visible.
    private int _teamColorTr = 255;
    private int _teamColorTg = 7;
    private int _teamColorTb = 58;
    private int _teamColorCtr = 4;
    private int _teamColorCtg = 190;
    private int _teamColorCtb = 255;

    // 2026-09-01 user request (CS2-HideLowerBody-inspired, built natively):
    // per-player "hide my own legs in first person". Mechanism is the known
    // alpha-254 trick - a pawn Render alpha of 254 hides the first-person
    // lower body while other players still see the full model. It MUST live
    // here rather than as the external plugin, because ApplyTeamAppearance
    // rewrites pawn.Render on every spawn/round anyway; an external plugin
    // writing the same field would be overwritten seconds later. Session-only
    // state by design (no store).
    private const byte LegsVisibleAlpha = 255;
    private const byte LegsHiddenAlpha = 254;
    private readonly HashSet<int> _hideLegsSlots = new();

    private void TeamColorOnLoad()
    {
        AddCommand(
            "css_legs",
            "Toggle hiding your own legs in first person.",
            OnLegsToggleCommand);
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

            var isGk = IsGkSlot(player.Slot, player.Team);
            var color = !_teamColorEnabled
                ? Color.White
                : isGk
                    ? GkRenderColor(player.Team)
                    : TeamRenderColor(player.Team);
            // Alpha carries the per-player !legs preference.
            var renderAlpha = _hideLegsSlots.Contains(player.Slot) ? LegsHiddenAlpha : LegsVisibleAlpha;
            pawn.Render = Color.FromArgb(renderAlpha, color.R, color.G, color.B);
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");

            Logger.LogInformation(
                "[SM2DIAG] team_appearance_applied slot={Slot} team={Team} gk={Gk} colorOn={ColorOn} modelOn={ModelOn} appliedModel={AppliedModel} reason={Reason}",
                player.Slot,
                player.Team,
                isGk,
                _teamColorEnabled,
                _teamModelEnabled,
                _teamModelEnabled ? (player.Team == CsTeam.Terrorist ? ModelPathT : ModelPathCt) : "(unchanged)",
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

    private void TeamColorOnPlayerDisconnect(int slot)
    {
        _hideLegsSlots.Remove(slot);
    }

    private void OnLegsToggleCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true })
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        bool hidden;
        if (_hideLegsSlots.Remove(player.Slot))
        {
            hidden = false;
        }
        else
        {
            _hideLegsSlots.Add(player.Slot);
            hidden = true;
        }

        ApplyTeamAppearance(player, "legs_toggle_command");
        command.ReplyToCommand($"[SM] first-person legs: {(hidden ? "hidden" : "visible")} (type !legs to toggle)");
    }

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

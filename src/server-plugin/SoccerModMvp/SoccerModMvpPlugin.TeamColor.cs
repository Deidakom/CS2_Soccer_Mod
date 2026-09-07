using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Off = no server-forced model (client's own agent choice); Stock = one
// stock CT/T archetype per team, tinted (the original 2026-08-31 feature);
// Kits = football kits, one rigged tm_leet variant per kit, tint forced to
// white so the kit texture's own colors show through untouched.
public enum TeamModelMode
{
    Off,
    Stock,
    Kits,
}

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

    // 2026-09-07 football kits (docs/jerseys/2026-09-07-football-kits-plan.md):
    // a THIRD model mode, "Kits", assigns one shared rigged agent
    // (agents/models/tm_leet/tm_leet_variant<x>.vmdl) per kit instead of one
    // stock model per team. tm_leet is the only stock archetype without a
    // vest/plate-carrier baked into the mesh (verified 2026-09-01 roster
    // scan). Kit textures come from a separate Workshop addon overriding the
    // SAME stock material paths (content, not code) - these four fields are
    // just which stock rigged variant carries which kit's look; they work
    // today (as plain recolored tm_leet, no addon needed) and keep working
    // once the addon is mounted, because the addon shadows the paths these
    // already-shipped .vmdl files reference.
    private const string KitModelHomeDefault = "agents/models/tm_leet/tm_leet_varianta.vmdl";
    private const string KitModelAwayDefault = "agents/models/tm_leet/tm_leet_variantb.vmdl";
    private const string KitModelGkHomeDefault = "agents/models/tm_leet/tm_leet_variantc.vmdl";
    private const string KitModelGkAwayDefault = "agents/models/tm_leet/tm_leet_variantd.vmdl";
    private string _kitModelHome = KitModelHomeDefault;
    private string _kitModelAway = KitModelAwayDefault;
    private string _kitModelGkHome = KitModelGkHomeDefault;
    private string _kitModelGkAway = KitModelGkAwayDefault;

    private bool _teamColorEnabled = true;
    private TeamModelMode _teamModelMode = TeamModelMode.Stock;
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
            "Admin: uniform player models per team - off, stock, or football kits.",
            OnTeamModelToggleCommand);
        AddCommand(
            "css_sm2kit",
            "Admin: set the model for one kit (home|away|gkhome|gkaway).",
            OnKitModelCommand);

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
            var isGk = IsGkSlot(player.Slot, player.Team);
            var isHomeSquad = IsHomeSquad(player.Team);
            string? appliedModel = _teamModelMode switch
            {
                TeamModelMode.Stock => player.Team == CsTeam.Terrorist ? ModelPathT : ModelPathCt,
                TeamModelMode.Kits => ResolveKitModel(isHomeSquad, isGk, _kitModelHome, _kitModelAway, _kitModelGkHome, _kitModelGkAway),
                _ => null,
            };
            if (appliedModel is not null)
            {
                pawn.SetModel(appliedModel);
            }

            // Kits carry their own painted colors - forcing white keeps the
            // texture untouched instead of multiply-tinting it like Stock.
            var color = _teamModelMode == TeamModelMode.Kits
                ? Color.White
                : !_teamColorEnabled
                    ? Color.White
                    : isGk
                        ? GkRenderColor(player.Team)
                        : TeamRenderColor(player.Team);
            // Alpha carries the per-player !legs preference.
            var renderAlpha = _hideLegsSlots.Contains(player.Slot) ? LegsHiddenAlpha : LegsVisibleAlpha;
            pawn.Render = Color.FromArgb(renderAlpha, color.R, color.G, color.B);
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");

            Logger.LogInformation(
                "[SM2DIAG] team_appearance_applied slot={Slot} team={Team} gk={Gk} homeSquad={HomeSquad} colorOn={ColorOn} modelMode={ModelMode} appliedModel={AppliedModel} reason={Reason}",
                player.Slot,
                player.Team,
                isGk,
                isHomeSquad,
                _teamColorEnabled,
                _teamModelMode,
                appliedModel ?? "(unchanged)",
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
            if (!TryParseTeamModelMode(command.GetArg(1), out var mode))
            {
                command.ReplyToCommand("[SM] usage: css_sm2teammodel <off|stock|kits>");
                return;
            }

            _teamModelMode = mode;
            SaveMatchSettings("team_model_toggle_command");
            Server.NextFrame(() => ApplyAllTeamAppearances("team_model_toggle_command"));
        }

        command.ReplyToCommand(
            $"[SM] uniform team models: {_teamModelMode.ToString().ToLowerInvariant()} "
            + "(usage: css_sm2teammodel <off|stock|kits>; off takes full effect on the next spawn)");
    }

    private void OnKitModelCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match")) return;

        if (command.ArgCount < 2 || !TryResolveKitSlot(command.GetArg(1), out var slotName, out var getter, out var setter))
        {
            command.ReplyToCommand(
                $"[SM] usage: css_sm2kit <home|away|gkhome|gkaway> [model path] - "
                + $"home={_kitModelHome} away={_kitModelAway} gkhome={_kitModelGkHome} gkaway={_kitModelGkAway}");
            return;
        }

        if (command.ArgCount >= 3)
        {
            var path = command.GetArg(2);
            if (!path.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
            {
                command.ReplyToCommand("[SM] model path must end in .vmdl");
                return;
            }

            setter(path);
            SaveMatchSettings("kit_model_command");
            if (_teamModelMode == TeamModelMode.Kits)
            {
                Server.NextFrame(() => ApplyAllTeamAppearances("kit_model_command"));
            }
            Logger.LogInformation("[SM2DIAG] kit_model_set kit={Kit} path={Path}", slotName, path);
        }

        command.ReplyToCommand($"[SM] kit '{slotName}' model: {getter()}");
    }

    private bool TryResolveKitSlot(string arg, out string slotName, out Func<string> getter, out Action<string> setter)
    {
        switch (arg.ToLowerInvariant())
        {
            case "home":
                slotName = "home"; getter = () => _kitModelHome; setter = v => _kitModelHome = v;
                return true;
            case "away":
                slotName = "away"; getter = () => _kitModelAway; setter = v => _kitModelAway = v;
                return true;
            case "gkhome":
                slotName = "gkhome"; getter = () => _kitModelGkHome; setter = v => _kitModelGkHome = v;
                return true;
            case "gkaway":
                slotName = "gkaway"; getter = () => _kitModelGkAway; setter = v => _kitModelGkAway = v;
                return true;
            default:
                slotName = ""; getter = () => ""; setter = _ => { };
                return false;
        }
    }

    // True while the player's CURRENT team is the squad that started the
    // match as Home - i.e. it survives the halftime SwitchTeam swap, so a
    // kit follows its squad instead of flipping sides with the map's raw
    // T/CT teams. Home is defined as "T before any swap" (matches how
    // _teamsSwapped is initialised in StartMatch/ResetMatchFlow).
    private bool IsHomeSquad(CsTeam team) => _teamsSwapped
        ? team == CsTeam.CounterTerrorist
        : team == CsTeam.Terrorist;

    // Pure and static so the managed test suite can exhaustively cover every
    // squad x GK x swap combination without spinning up a plugin instance.
    private static string ResolveKitModel(
        bool isHomeSquad,
        bool isGk,
        string kitHome,
        string kitAway,
        string kitGkHome,
        string kitGkAway)
        => (isHomeSquad, isGk) switch
        {
            (true, true) => kitGkHome,
            (true, false) => kitHome,
            (false, true) => kitGkAway,
            (false, false) => kitAway,
        };

    private static TeamModelMode NextTeamModelMode(TeamModelMode mode) => mode switch
    {
        TeamModelMode.Off => TeamModelMode.Stock,
        TeamModelMode.Stock => TeamModelMode.Kits,
        _ => TeamModelMode.Off,
    };

    private static bool TryParseTeamModelMode(string value, out TeamModelMode mode)
    {
        switch (value.ToLowerInvariant())
        {
            case "off":
                mode = TeamModelMode.Off;
                return true;
            case "stock":
                mode = TeamModelMode.Stock;
                return true;
            case "kits":
                mode = TeamModelMode.Kits;
                return true;
            // Back-compat with the pre-2026-09-07 bool toggle.
            case "on":
                mode = TeamModelMode.Stock;
                return true;
            default:
                mode = TeamModelMode.Off;
                return false;
        }
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

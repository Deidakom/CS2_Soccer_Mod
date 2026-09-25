using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
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

    // These stock variants are compatibility defaults, not painted jerseys.
    // The attempted stock-material override did not render the kits. Custom
    // models under models/soccermod/kits require a complete Workshop package
    // on BOTH server and clients, including materials and textures.
    private const string KitModelHomeDefault = "agents/models/tm_leet/tm_leet_varianta.vmdl";
    private const string KitModelAwayDefault = "agents/models/tm_leet/tm_leet_variantb.vmdl";
    private const string KitModelGkHomeDefault = "agents/models/tm_leet/tm_leet_variantc.vmdl";
    private const string KitModelGkAwayDefault = "agents/models/tm_leet/tm_leet_variantd.vmdl";
    private string _kitModelHome = KitModelHomeDefault;
    private string _kitModelAway = KitModelAwayDefault;
    private string _kitModelGkHome = KitModelGkHomeDefault;
    private string _kitModelGkAway = KitModelGkAwayDefault;
    private sealed record KitModels(string Home, string Away, string GkHome, string GkAway);
    private KitModels? _mapKitModels;
    // Custom kit models that no mounted addon provides this map, or null.
    private string? _kitModelsMissing;
    // Replaced by the managed tests, which run without a game server.
    private Func<HashSet<string>> _mountedAddonFiles = MountedAddonFiles;

    private void CaptureKitResources(Action<string> precache)
    {
        _mapKitModels = null;
        var models = new KitModels(_kitModelHome, _kitModelAway, _kitModelGkHome, _kitModelGkAway);
        // A player switched to a model the server does not have ends up on a
        // black screen, so kits are only used when every custom model is in a
        // Workshop addon MultiAddonManager mounts (with MultiAddonManager off
        // or the jersey addon missing, stock models stay active).
        _kitModelsMissing = FindMissingKitModels(models, _mountedAddonFiles);
        if (_kitModelsMissing is not null)
        {
            Logger.LogWarning("[SM2DIAG] kit_models_missing paths={Missing}; stock models stay active this map", _kitModelsMissing);
            return;
        }

        foreach (var path in new[] { models.Home, models.Away, models.GkHome, models.GkAway }.Distinct(StringComparer.Ordinal))
            precache(path);
        _mapKitModels = models;
    }

    private static string? FindMissingKitModels(KitModels models, Func<HashSet<string>> mountedAddonFiles)
    {
        // agents/models/... ship with the game; anything else needs an addon.
        var custom = new[] { models.Home, models.Away, models.GkHome, models.GkAway }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !path.StartsWith("agents/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (custom.Length == 0) return null;
        var mounted = mountedAddonFiles();
        var missing = custom.Where(path => !mounted.Contains(path + "_c")).ToArray();
        return missing.Length == 0 ? null : string.Join(", ", missing);
    }

    // Files inside the Workshop addons MultiAddonManager mounts, which it keeps
    // in steamapps/workshop/content/730/<id>/ next to the server executable,
    // plus the running Workshop map's own VPK (2026-09-25: our own stadium
    // carries the sounds, menu and jerseys itself).
    private static HashSet<string> MountedAddonFiles()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = (ConVar.Find("mm_extra_addons")?.StringValue ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (MapWorkshopId(Server.MapName) is { } mapId) ids.Add(mapId);
        foreach (var id in ids.Distinct())
        {
            if (FindWorkshopVpk(id) is { } vpk && VpkDirectory.TryReadEntries(vpk, out var entries)) files.UnionWith(entries);
        }

        return files;
    }

    private static IEnumerable<string> WorkshopContentRoots() => new[]
    {
        Path.GetDirectoryName(Environment.ProcessPath),
        Path.GetFullPath(Path.Combine(Server.GameDirectory, "..", "bin", "linuxsteamrt64")),
    }.OfType<string>().Distinct().Select(dir => Path.Combine(dir, "steamapps", "workshop", "content", "730"));

    private static string? FindWorkshopVpk(string id) => WorkshopContentRoots()
        .SelectMany(root => new[] { Path.Combine(root, id, id + "_dir.vpk"), Path.Combine(root, id, id + ".vpk") })
        .FirstOrDefault(File.Exists);

    // The Workshop item that holds the running map: the downloaded VPK that
    // contains maps/<map>.vpk or maps/<map>.vmap_c. Null for a stock map.
    private static string? MapWorkshopId(string? mapName)
    {
        if (string.IsNullOrEmpty(mapName)) return null;
        foreach (var root in WorkshopContentRoots().Where(Directory.Exists))
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var id = Path.GetFileName(dir);
            if (!ulong.TryParse(id, out _) || FindWorkshopVpk(id) is not { } vpk
                || !VpkDirectory.TryReadEntries(vpk, out var entries)) continue;
            if (entries.Contains($"maps/{mapName}.vpk", StringComparer.OrdinalIgnoreCase)
                || entries.Contains($"maps/{mapName}.vmap_c", StringComparer.OrdinalIgnoreCase))
                return id;
        }
        return null;
    }

    private string? ResolveTeamModel(CsTeam team, bool isGk, out bool usingKit)
    {
        usingKit = _teamModelMode == TeamModelMode.Kits && _mapKitModels is not null;
        if (_teamModelMode == TeamModelMode.Off) return null;
        if (usingKit && _mapKitModels is { } models)
            return ResolveKitModel(IsHomeSquad(team), isGk, models.Home, models.Away, models.GkHome, models.GkAway);
        // A hot reload has not observed this map's precache pass. Use the
        // existing stock setup until the next map instead of assigning an
        // unregistered custom path (and do not apply the kit's white tint).
        return team == CsTeam.Terrorist ? ModelPathT : ModelPathCt;
    }

    private static bool TryNormalizeKitPath(string? value, out string path)
    {
        path = (value ?? "").Trim().Replace('\\', '/');
        if (path.Length is > 0 and <= 260 && path.Contains('/') && path.EndsWith(".vmdl", StringComparison.Ordinal)
            && path.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '-' or '.')
            && path.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".." or ".vmdl")))
            return true;
        path = "";
        return false;
    }

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

        string? appliedModel = null;
        try
        {
            var isGk = IsGkSlot(player.Slot, player.Team);
            var isHomeSquad = IsHomeSquad(player.Team);
            appliedModel = ResolveTeamModel(player.Team, isGk, out var usingKit);
            if (appliedModel is not null)
            {
                pawn.SetModel(appliedModel);
            }
            JerseyOnTeamAppearanceApplied(player, pawn, appliedModel);

            // Kits carry their own painted colors - forcing white keeps the
            // texture untouched instead of multiply-tinting it like Stock.
            var color = usingKit
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
            JerseyOnTeamAppearanceApplied(player, pawn, appliedModel: null);
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
            if (mode == TeamModelMode.Kits && _kitModelsMissing is not null)
                command.ReplyToCommand($"[SM] The kit models are not on this server ({_kitModelsMissing}); stock models stay active. Mount the jersey addon through MultiAddonManager, then reload the map.");
            else if (mode == TeamModelMode.Kits && _mapKitModels is null)
                command.ReplyToCommand("[SM] Kit precache is pending; stock models remain active until the next map.");
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
            if (command.ArgCount != 3 || !TryNormalizeKitPath(command.GetArg(2), out var path))
            {
                command.ReplyToCommand("[SM] Use a relative resource path such as models/soccermod/kits/kit_home.vmdl (no spaces or parent directories).");
                return;
            }

            setter(path);
            SaveMatchSettings("kit_model_command");
            Logger.LogInformation("[SM2DIAG] kit_model_set kit={Kit} path={Path}", slotName, path);
            command.ReplyToCommand("[SM] Saved for the next map's precache. Existing models stay active this map. Custom files must also be delivered to clients through Workshop.");
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

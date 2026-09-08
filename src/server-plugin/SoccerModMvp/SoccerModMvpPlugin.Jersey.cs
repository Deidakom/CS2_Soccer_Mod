using System.Drawing;
using System.Globalization;
using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using V3 = System.Numerics.Vector3;

namespace SoccerModMvp;

public sealed partial class SoccerModMvpPlugin
{
    // Source 1 had three small bonemerged glyph models (two name halves and a
    // number) that could be assigned bodygroups per player. The Source 2 kit
    // package deliberately contains only the four rigged kit models, so there
    // is no equivalent glyph asset to attach yet. A public point_worldtext is
    // the safe Source 2 equivalent for the first dynamic pass: it needs no
    // client-side files, follows the player's back every tick, and is visible
    // to every client without the CheckTransmit hook that used to crash this
    // server. The visual can later be replaced by real bonemerged glyph models
    // without changing number/name allocation.
    private const float DynamicJerseyBackOffset = 3.2f;
    private const float DynamicJerseyTextHeight = 56.0f;
    private const float DynamicJerseyFontSize = 24.0f;
    private const float DynamicJerseyWorldUnitsPerPixel = 0.035f;
    private const int DynamicJerseyUpdateEveryTicks = 2;
    private const int DynamicJerseyNameLength = 10;

    private sealed class DynamicJerseyState
    {
        public required uint Controller;
        public required ulong SteamId;
        public required bool HomeSquad;
        public required bool IsGoalkeeper;
        public required string Label;
        public required CPointWorldText Text;
    }

    private readonly Dictionary<int, DynamicJerseyState> _dynamicJerseys = new();
    private readonly Dictionary<ulong, (bool HomeSquad, int Number)> _jerseyNumbers = new();
    private bool _dynamicJerseysEnabled = true;

    private void JerseyOnLoad()
    {
        AddCommand(
            "css_sm2jerseydynamic",
            "Admin: enable or disable dynamic jersey names and numbers.",
            OnDynamicJerseyToggleCommand);
        AddCommand(
            "css_sm2jerseynumber",
            "Set your outfield jersey number (2-99) or random.",
            OnJerseyNumberCommand);
        RegisterListener<Listeners.OnClientDisconnect>(JerseyOnPlayerDisconnect);

        Server.NextFrame(() => JerseyRefreshAll("plugin_load"));
    }

    private void JerseyOnUnload() => ClearDynamicJerseys("plugin_unload");

    private void JerseyOnMapStart(string reason) => ClearDynamicJerseys($"map_start:{reason}");

    private void JerseyOnMapEnd() => ClearDynamicJerseys("map_end");

    private void JerseyOnPlayerSpawn(CCSPlayerController player) => RemoveDynamicJersey(player.Slot);

    private void JerseyOnPlayerDeath(CCSPlayerController? player)
    {
        if (player is not null) RemoveDynamicJersey(player.Slot);
    }

    private void JerseyOnPlayerDisconnect(int slot) => RemoveDynamicJersey(slot);

    private void JerseyOnTick()
    {
        if (Server.TickCount % DynamicJerseyUpdateEveryTicks != 0) return;

        // Dynamic text belongs to the custom kit mode. Removing it immediately
        // when an admin changes to stock/off prevents stale labels floating in
        // the arena for the rest of the round.
        if (!_dynamicJerseysEnabled
            || _teamModelMode != TeamModelMode.Kits
            || _mapKitModels is null)
        {
            if (_dynamicJerseys.Count > 0) ClearDynamicJerseys("disabled_or_stock_mode");
            return;
        }

        var seen = new HashSet<int>();
        foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot).OrderBy(p => p.Slot))
        {
            if (!IsEligiblePlayer(player)) continue;
            if (player.PlayerPawn.Value is not { IsValid: true } pawn || pawn.AbsOrigin is not { } origin) continue;

            seen.Add(player.Slot);
            try
            {
                UpdateDynamicJersey(player, pawn, origin);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SM2DIAG] dynamic_jersey_update_failed slot={Slot}", player.Slot);
                RemoveDynamicJersey(player.Slot);
            }
        }

        foreach (var slot in _dynamicJerseys.Keys.Where(slot => !seen.Contains(slot)).ToArray())
            RemoveDynamicJersey(slot);
    }

    private void JerseyRefreshAll(string reason)
    {
        if (!_dynamicJerseysEnabled || _teamModelMode != TeamModelMode.Kits || _mapKitModels is null) return;
        foreach (var player in Utilities.GetPlayers().Where(IsEligiblePlayer))
            RemoveDynamicJersey(player.Slot);
        Logger.LogInformation("[SM2DIAG] dynamic_jersey_refresh reason={Reason}", reason);
    }

    private void UpdateDynamicJersey(CCSPlayerController player, CCSPlayerPawn pawn, Vector origin)
    {
        var steamId = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        var homeSquad = IsHomeSquad(player.Team);
        var goalkeeper = IsGkSlot(player.Slot, player.Team);
        var number = goalkeeper ? 1 : EnsureJerseyNumber(player, steamId, homeSquad);
        var name = NormalizeJerseyName(player.PlayerName);
        var label = $"{name}\n{number.ToString(CultureInfo.InvariantCulture)}";

        if (!_dynamicJerseys.TryGetValue(player.Slot, out var state)
            || !state.Text.IsValid
            || state.Controller != player.EntityHandle.Raw
            || state.SteamId != steamId
            || state.HomeSquad != homeSquad
            || state.IsGoalkeeper != goalkeeper)
        {
            RemoveDynamicJersey(player.Slot);
            if (CreateDynamicJersey(player, steamId, homeSquad, goalkeeper, label) is not { } text)
                return;

            state = new DynamicJerseyState
            {
                Controller = player.EntityHandle.Raw,
                SteamId = steamId,
                HomeSquad = homeSquad,
                IsGoalkeeper = goalkeeper,
                Label = label,
                Text = text,
            };
            _dynamicJerseys[player.Slot] = state;
            Logger.LogInformation(
                "[SM2DIAG] dynamic_jersey_created slot={Slot} name={Name} number={Number} gk={Gk} homeSquad={HomeSquad}",
                player.Slot,
                name,
                number,
                goalkeeper,
                homeSquad);
        }

        if (!string.Equals(state.Label, label, StringComparison.Ordinal))
        {
            state.Label = label;
            state.Text.MessageText = label;
            Utilities.SetStateChanged(state.Text, "CPointWorldText", "m_messageText");
        }

        var yaw = PlayerBodyYaw(pawn);
        var yawRadians = yaw * (MathF.PI / 180.0f);
        var back = new V3(-MathF.Cos(yawRadians), -MathF.Sin(yawRadians), 0.0f);
        var textPosition = N(origin) + back * DynamicJerseyBackOffset;
        textPosition.Z += DynamicJerseyTextHeight;

        // ReorientMode=1 makes the text rotate around the vertical axis toward
        // the viewer. It stays physically on the back of the player, while the
        // body depth-tests it from the front instead of showing a mirrored
        // label. The small vertical offset keeps it on the shirt rather than
        // in the neck/head area for both standing and crouched poses.
        state.Text.Teleport(
            position: C(textPosition),
            angles: new QAngle(0.0f, yaw + 90.0f, 0.0f));
    }

    private CPointWorldText? CreateDynamicJersey(
        CCSPlayerController player,
        ulong steamId,
        bool homeSquad,
        bool goalkeeper,
        string label)
    {
        var text = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
        if (text is null || !text.IsValid) return null;

        text.MessageText = label;
        text.FontName = "Arial";
        text.FontSize = DynamicJerseyFontSize;
        text.WorldUnitsPerPx = DynamicJerseyWorldUnitsPerPixel;
        text.DepthOffset = 0.02f;
        text.Fullbright = true;
        text.Enabled = true;
        text.DrawBackground = false;
        text.JustifyHorizontal = (PointWorldTextJustifyHorizontal_t)1;
        text.JustifyVertical = (PointWorldTextJustifyVertical_t)1;
        text.ReorientMode = (PointWorldTextReorientMode_t)1;
        text.Color = DynamicJerseyTextColor(homeSquad, goalkeeper);

        try
        {
            text.DispatchSpawn();
            return text;
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "[SM2DIAG] dynamic_jersey_spawn_failed slot={Slot} steamId={SteamId}",
                player.Slot,
                steamId);
            if (text.IsValid) text.Remove();
            return null;
        }
    }

    private void RemoveDynamicJersey(int slot)
    {
        if (!_dynamicJerseys.Remove(slot, out var state)) return;
        try
        {
            if (state.Text.IsValid) state.Text.Remove();
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "[SM2DIAG] dynamic_jersey_remove_failed slot={Slot}", slot);
        }
    }

    private void ClearDynamicJerseys(string reason)
    {
        foreach (var slot in _dynamicJerseys.Keys.ToArray()) RemoveDynamicJersey(slot);
        if (_dynamicJerseys.Count == 0) Logger.LogDebug("[SM2DIAG] dynamic_jerseys_cleared reason={Reason}", reason);
    }

    private int EnsureJerseyNumber(CCSPlayerController player, ulong steamId, bool homeSquad)
    {
        if (steamId != 0
            && _jerseyNumbers.TryGetValue(steamId, out var existing)
            && existing.HomeSquad == homeSquad
            && existing.Number is >= 2 and <= 99
            && !JerseyNumberInUse(player, homeSquad, existing.Number))
        {
            return existing.Number;
        }

        var start = steamId == 0 ? 2 : 2 + (int)(steamId % 98UL);
        for (var offset = 0; offset < 98; offset++)
        {
            var candidate = 2 + ((start - 2 + offset) % 98);
            if (JerseyNumberInUse(player, homeSquad, candidate)) continue;
            if (steamId != 0) _jerseyNumbers[steamId] = (homeSquad, candidate);
            return candidate;
        }

        // A full 98-player squad is not realistic, but keeping a deterministic
        // fallback means the overlay never disappears if a stress test fills
        // every number.
        return 99;
    }

    private bool JerseyNumberInUse(CCSPlayerController current, bool homeSquad, int number)
    {
        foreach (var other in Utilities.GetPlayers())
        {
            if (!IsEligiblePlayer(other) || other.Slot == current.Slot || IsGkSlot(other.Slot, other.Team)) continue;
            if (IsHomeSquad(other.Team) != homeSquad) continue;
            var id = other.AuthorizedSteamID?.SteamId64 ?? 0UL;
            if (id != 0 && _jerseyNumbers.TryGetValue(id, out var assigned)
                && assigned.HomeSquad == homeSquad && assigned.Number == number)
                return true;
        }

        return false;
    }

    private void OnJerseyNumberCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true } || player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            command.ReplyToCommand("[SM] Join a team before choosing a jersey number.");
            return;
        }

        if (IsGkSlot(player.Slot, player.Team))
        {
            command.ReplyToCommand("[SM] Goalkeepers use the fixed jersey number 1.");
            return;
        }

        var steamId = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        var homeSquad = IsHomeSquad(player.Team);
        if (command.ArgCount < 2 || command.GetArg(1).Equals("random", StringComparison.OrdinalIgnoreCase))
        {
            _jerseyNumbers.Remove(steamId);
            var number = EnsureJerseyNumber(player, steamId, homeSquad);
            RemoveDynamicJersey(player.Slot);
            command.ReplyToCommand($"[SM] Your jersey number is now {number}.");
            return;
        }

        if (!int.TryParse(command.GetArg(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var requested)
            || requested is < 2 or > 99)
        {
            command.ReplyToCommand("[SM] Usage: !sm2jerseynumber <2-99|random>.");
            return;
        }

        if (JerseyNumberInUse(player, homeSquad, requested))
        {
            command.ReplyToCommand($"[SM] Number {requested} is already in use by your squad.");
            return;
        }

        if (steamId != 0) _jerseyNumbers[steamId] = (homeSquad, requested);
        RemoveDynamicJersey(player.Slot);
        command.ReplyToCommand($"[SM] Your jersey number is now {requested}.");
    }

    private void OnDynamicJerseyToggleCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match")) return;

        if (command.ArgCount >= 2)
        {
            var value = command.GetArg(1);
            if (!TryParseTeamAppearanceToggle(value, out var enabled))
            {
                command.ReplyToCommand("[SM] Usage: css_sm2jerseydynamic <on|off>.");
                return;
            }

            _dynamicJerseysEnabled = enabled;
            if (!enabled) ClearDynamicJerseys("admin_disabled");
            else JerseyRefreshAll("admin_enabled");
        }

        command.ReplyToCommand(
            $"[SM] Dynamic jersey names/numbers: {(_dynamicJerseysEnabled ? "on" : "off")} "
            + "(usage: css_sm2jerseydynamic <on|off>).");
    }

    private static string NormalizeJerseyName(string? value)
    {
        var input = value ?? string.Empty;
        var builder = new StringBuilder(DynamicJerseyNameLength);
        var lastDash = false;

        foreach (var raw in input)
        {
            if (builder.Length >= DynamicJerseyNameLength) break;
            var character = raw;
            if (character is >= 'a' and <= 'z') character = (char)(character - ('a' - 'A'));
            if (character is >= 'A' and <= 'Z')
            {
                builder.Append(character);
                lastDash = false;
            }
            else if (character is ' ' or '-' or '_'
                     && builder.Length > 0
                     && !lastDash)
            {
                builder.Append('-');
                lastDash = true;
            }
        }

        while (builder.Length > 0 && builder[^1] == '-') builder.Length--;
        return builder.Length == 0 ? "PLAYER" : builder.ToString();
    }

    private static float PlayerBodyYaw(CCSPlayerPawn pawn)
    {
        if (pawn.AbsRotation is { } rotation) return rotation.Y;
        return pawn.V_angle.Y;
    }

    private static Color DynamicJerseyTextColor(bool homeSquad, bool goalkeeper) =>
        homeSquad && !goalkeeper
            ? Color.White
            : Color.FromArgb(12, 25, 48);
}

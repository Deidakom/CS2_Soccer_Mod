using System.Drawing;
using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

public sealed partial class SoccerModMvpPlugin
{
    private const string JerseyNameEntityPrefix = "sm2_jersey_name";
    private const string JerseyNumberEntityPrefix = "sm2_jersey_number";
    private const string JerseyPrototypeEntityPrefix = "sm2_jersey_prototype_number";
    private const int DynamicJerseyReconcileEveryTicks = 16;

    private sealed class DynamicJerseyState
    {
        public required uint Controller;
        public required uint Pawn;
        public required ulong SteamId;
        public required string AppliedModel;
        public required bool HomeSquad;
        public required bool IsGoalkeeper;
        public required string Name;
        public required string? Number;
        public required CPointWorldText NameText;
        public CPointWorldText? NumberText;

        public bool IsValid => NameText.IsValid
            && (NumberText is null || NumberText.IsValid);

        public JerseyEntitySnapshot Snapshot() => new(
            Controller,
            Pawn,
            SteamId,
            AppliedModel,
            HomeSquad,
            IsGoalkeeper,
            Name,
            Number);
    }

    private sealed class JerseyPrototypeState
    {
        public required int Slot;
        public required uint Controller;
        public required uint Pawn;
        public required string AppliedModel;
        public required CPointWorldText NumberText;
    }

    private readonly Dictionary<int, DynamicJerseyState> _dynamicJerseys = new();
    private readonly Dictionary<ulong, (bool HomeSquad, int Number)> _jerseyNumbers = new();
    private readonly Dictionary<uint, string> _appliedJerseyModelsByPawn = new();
    private readonly HashSet<(int Slot, uint Pawn, string Model)> _jerseyBindFailures = new();
    private readonly HashSet<(int Slot, string Model, string Reason)> _jerseyUnavailableDiagnostics = new();
    private JerseyPrototypeState? _jerseyPrototype;
    private bool _jerseyPrototypeEnabled;

    // This is deliberately false when the field is missing from the match
    // settings file. The renderer is experimental and must never turn itself
    // on merely because the DLL was reloaded.
    private bool _dynamicJerseysEnabled;

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
        AddCommand(
            "css_sm2jerseyprototype",
            "Admin: run the one-model Home jersey number 88 attachment proof.",
            OnJerseyPrototypeCommand);
        RegisterListener<Listeners.OnClientDisconnect>(JerseyOnPlayerDisconnect);

        Server.NextFrame(() => JerseyRefreshAll("plugin_load"));
    }

    private void JerseyOnUnload()
    {
        ClearDynamicJerseys("plugin_unload");
        ClearJerseyPrototype("plugin_unload");
        _appliedJerseyModelsByPawn.Clear();
        _jerseyBindFailures.Clear();
        _jerseyUnavailableDiagnostics.Clear();
    }

    private void JerseyOnMapStart(string reason)
    {
        ClearDynamicJerseys($"map_start:{reason}");
        ClearJerseyPrototype($"map_start:{reason}");
        _appliedJerseyModelsByPawn.Clear();
        _jerseyBindFailures.Clear();
        _jerseyUnavailableDiagnostics.Clear();
    }

    private void JerseyOnMapEnd()
    {
        ClearDynamicJerseys("map_end");
        ClearJerseyPrototype("map_end");
        _appliedJerseyModelsByPawn.Clear();
        _jerseyBindFailures.Clear();
        _jerseyUnavailableDiagnostics.Clear();
    }

    private void JerseyOnPlayerSpawn(CCSPlayerController player)
    {
        RemoveDynamicJersey(player.Slot);
        RemoveJerseyBindFailures(player.Slot);
        RemoveJerseyUnavailableDiagnostics(player.Slot);
    }

    private void JerseyOnPlayerDeath(CCSPlayerController? player)
    {
        if (player is not null)
        {
            RemoveDynamicJersey(player.Slot);
            RemoveJerseyPrototypeForSlot(player.Slot);
        }
    }

    private void JerseyOnPlayerDisconnect(int slot)
    {
        RemoveDynamicJersey(slot);
        RemoveJerseyPrototypeForSlot(slot);
        RemoveJerseyBindFailures(slot);
        RemoveJerseyUnavailableDiagnostics(slot);
    }

    // TeamColor is the component that calls SetModel. This notification keeps
    // the renderer tied to the path that was actually selected for the pawn;
    // an arbitrary stock fallback therefore cannot accidentally receive a
    // jersey overlay just because the server is in Kits mode.
    private void JerseyOnTeamAppearanceApplied(
        CCSPlayerController player,
        CCSPlayerPawn pawn,
        string? appliedModel)
    {
        var pawnRaw = pawn.EntityHandle.Raw;
        if (string.IsNullOrWhiteSpace(appliedModel))
        {
            _appliedJerseyModelsByPawn.Remove(pawnRaw);
        }
        else
        {
            _appliedJerseyModelsByPawn[pawnRaw] = appliedModel;
        }

        RemoveJerseyBindFailures(player.Slot);
        RemoveJerseyUnavailableDiagnostics(player.Slot);
    }

    private void JerseyOnTick()
    {
        if (Server.TickCount % DynamicJerseyReconcileEveryTicks != 0)
        {
            return;
        }

        if (_jerseyPrototypeEnabled)
        {
            ReconcileJerseyPrototype();
            return;
        }

        if (!_dynamicJerseysEnabled
            || _teamModelMode != TeamModelMode.Kits
            || _mapKitModels is null)
        {
            if (_dynamicJerseys.Count > 0)
            {
                ClearDynamicJerseys("disabled_or_unavailable_mode");
            }

            return;
        }

        var seen = new HashSet<int>();
        foreach (var player in Utilities.GetPlayers()
                     .Where(p => p.IsValid && !p.IsBot)
                     .OrderBy(p => p.Slot))
        {
            if (!IsEligiblePlayer(player)
                || player.PlayerPawn.Value is not { IsValid: true } pawn)
            {
                continue;
            }

            seen.Add(player.Slot);
            try
            {
                ReconcileDynamicJersey(player, pawn);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SM2DIAG] dynamic_jersey_reconcile_failed slot={Slot}", player.Slot);
                RemoveDynamicJersey(player.Slot);
            }
        }

        foreach (var slot in _dynamicJerseys.Keys.Where(slot => !seen.Contains(slot)).ToArray())
        {
            RemoveDynamicJersey(slot);
        }
    }

    private void JerseyRefreshAll(string reason)
    {
        ClearDynamicJerseys(reason);
        if (_jerseyPrototypeEnabled)
        {
            ClearJerseyPrototype(reason);
        }

        Logger.LogInformation(
            "[SM2DIAG] dynamic_jersey_refresh reason={Reason} requestedEnabled={RequestedEnabled} renderer={Renderer}",
            reason,
            _dynamicJerseysEnabled,
            DynamicJerseyAvailabilityStatus());
    }

    private void ReconcileDynamicJersey(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        var appliedModel = GetAppliedJerseyModel(pawn);
        var homeSquad = IsHomeSquad(player.Team);
        var goalkeeper = IsGkSlot(player.Slot, player.Team);
        var steamId = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        var number = goalkeeper ? 1 : EnsureJerseyNumber(player, steamId, homeSquad);

        if (!JerseyRenderRules.TryBuildPlan(
                appliedModel,
                goalkeeper,
                player.PlayerName,
                number,
                out var plan,
                out var reason))
        {
            RemoveDynamicJersey(player.Slot);
            LogUnavailableOnce(player.Slot, appliedModel, reason);
            return;
        }

        var existing = _dynamicJerseys.TryGetValue(player.Slot, out var state)
            && state.IsValid
            ? state
            : null;
        var currentSnapshot = existing?.Snapshot();
        var decision = JerseyRenderRules.DecideLifecycle(
            currentSnapshot,
            plan,
            player.EntityHandle.Raw,
            pawn.EntityHandle.Raw,
            steamId,
            plan.ModelPath,
            homeSquad,
            goalkeeper);

        switch (decision.Action)
        {
            case JerseyLifecycleAction.Remove:
                RemoveDynamicJersey(player.Slot);
                return;
            case JerseyLifecycleAction.Create:
            case JerseyLifecycleAction.Recreate:
                RemoveDynamicJersey(player.Slot);
                if (IsKnownBindFailure(player.Slot, pawn, plan.ModelPath))
                {
                    return;
                }

                if (CreateDynamicJersey(player, pawn, steamId, homeSquad, plan) is { } created)
                {
                    _dynamicJerseys[player.Slot] = created;
                    Logger.LogInformation(
                        "[SM2DIAG] dynamic_jersey_created slot={Slot} controller={Controller} pawn={Pawn} "
                        + "steamId={SteamId} model={Model} nameChild={NameChild} numberChild={NumberChild} "
                        + "gk={Gk} homeSquad={HomeSquad}",
                        player.Slot,
                        player.EntityHandle.Raw,
                        pawn.EntityHandle.Raw,
                        steamId,
                        plan.ModelPath,
                        created.NameText.EntityHandle.Raw,
                        created.NumberText?.EntityHandle.Raw ?? 0,
                        goalkeeper,
                        homeSquad);
                }

                return;
            case JerseyLifecycleAction.UpdateContent:
                if (state is not null)
                {
                    UpdateDynamicJerseyContent(state, plan);
                }

                return;
            case JerseyLifecycleAction.None:
                return;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private DynamicJerseyState? CreateDynamicJersey(
        CCSPlayerController player,
        CCSPlayerPawn pawn,
        ulong steamId,
        bool homeSquad,
        JerseyRenderPlan plan)
    {
        var profile = JerseyRenderRules.Profiles.Single(p => p.ModelPath == plan.ModelPath);
        var textColor = DynamicJerseyTextColor(plan.IsGoalkeeper);
        var nameText = CreateAttachedJerseyText(
            player,
            pawn,
            plan.Name,
            textColor,
            JerseyNameEntityPrefix,
            steamId,
            plan.ModelPath);
        if (nameText is null)
        {
            return null;
        }

        CPointWorldText? numberText = null;
        if (plan.NumberText is not null)
        {
            numberText = CreateAttachedJerseyText(
                player,
                pawn,
                plan.NumberText,
                textColor,
                JerseyNumberEntityPrefix,
                steamId,
                plan.ModelPath);
            if (numberText is null)
            {
                RemoveJerseyText(nameText);
                MarkBindFailure(player.Slot, pawn, plan.ModelPath, profile.NumberSocket);
                return null;
            }
        }

        return new DynamicJerseyState
        {
            Controller = player.EntityHandle.Raw,
            Pawn = pawn.EntityHandle.Raw,
            SteamId = steamId,
            AppliedModel = plan.ModelPath,
            HomeSquad = homeSquad,
            IsGoalkeeper = plan.IsGoalkeeper,
            Name = plan.SanitizedName,
            Number = plan.Number?.ToString(CultureInfo.InvariantCulture),
            NameText = nameText,
            NumberText = numberText,
        };
    }

    private CPointWorldText? CreateAttachedJerseyText(
        CCSPlayerController player,
        CCSPlayerPawn pawn,
        JerseyTextPlan textPlan,
        Color color,
        string entityPrefix,
        ulong steamId,
        string modelPath)
    {
        var text = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
        if (text is null || !text.IsValid)
        {
            Logger.LogWarning(
                "[SM2DIAG] dynamic_jersey_entity_create_failed slot={Slot} model={Model} kind={Kind}",
                player.Slot,
                modelPath,
                entityPrefix);
            return null;
        }

        text.Entity!.Name = $"{entityPrefix}_{player.Slot}_{pawn.EntityHandle.Raw}";
        text.MessageText = textPlan.Text;
        text.FontName = "Arial";
        text.FontSize = textPlan.FontSize;
        text.WorldUnitsPerPx = textPlan.WorldUnitsPerPixel;
        text.DepthOffset = 0.0f;
        text.Fullbright = true;
        text.Enabled = false;
        text.DrawBackground = false;
        text.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
        text.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_CENTER;
        text.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE;
        text.Color = color;

        try
        {
            // The entity is disabled until the engine reports the requested
            // pawn/socket hierarchy. No world-space origin or old feet-relative
            // offset is ever rendered as a fallback.
            text.DispatchSpawn();
            if (!AttachJerseyText(text, pawn, textPlan.SocketName))
            {
                RemoveJerseyText(text);
                MarkBindFailure(player.Slot, pawn, modelPath, textPlan.SocketName);
                return null;
            }

            text.Enabled = true;
            Utilities.SetStateChanged(text, "CPointWorldText", "m_bEnabled");
            Logger.LogInformation(
                "[SM2DIAG] dynamic_jersey_bound slot={Slot} pawn={Pawn} child={Child} model={Model} socket={Socket} attachmentToken={AttachmentToken}",
                player.Slot,
                pawn.EntityHandle.Raw,
                text.EntityHandle.Raw,
                modelPath,
                textPlan.SocketName,
                text.CBodyComponent?.SceneNode?.HierarchyAttachName.Value ?? 0);
            return text;
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "[SM2DIAG] dynamic_jersey_spawn_failed slot={Slot} pawn={Pawn} steamId={SteamId} model={Model} socket={Socket}",
                player.Slot,
                pawn.EntityHandle.Raw,
                steamId,
                modelPath,
                textPlan.SocketName);
            RemoveJerseyText(text);
            MarkBindFailure(player.Slot, pawn, modelPath, textPlan.SocketName);
            return null;
        }
    }

    private static bool AttachJerseyText(
        CPointWorldText text,
        CCSPlayerPawn pawn,
        string socketName)
    {
        if (!text.IsValid
            || !pawn.IsValid
            || pawn.CBodyComponent?.SceneNode is not { } parentNode)
        {
            return false;
        }

        text.AcceptInput("SetParent", pawn, pawn, "!activator", 0);
        text.AcceptInput("SetParentAttachment", pawn, pawn, socketName, 0);

        var childNode = text.CBodyComponent?.SceneNode;
        var parentOwner = childNode?.PParent?.Owner;
        // AcceptInput is void, so this scene-node check is part of the bind
        // contract. It rejects silent no-ops and prevents origin rendering.
        return childNode is not null
            && childNode.PParent is not null
            && parentOwner is { IsValid: true }
            && parentOwner.Index == pawn.Index
            && parentOwner.Index == parentNode.Owner?.Index
            && childNode.HierarchyAttachName.Value != 0;
    }

    private void UpdateDynamicJerseyContent(DynamicJerseyState state, JerseyRenderPlan plan)
    {
        if (!string.Equals(state.Name, plan.SanitizedName, StringComparison.Ordinal))
        {
            UpdateJerseyText(state.NameText, plan.Name);
            state.Name = plan.SanitizedName;
        }

        var desiredNumber = plan.Number?.ToString(CultureInfo.InvariantCulture);
        if (state.NumberText is not null && plan.NumberText is not null
            && !string.Equals(state.Number, desiredNumber, StringComparison.Ordinal))
        {
            UpdateJerseyText(state.NumberText, plan.NumberText);
            state.Number = desiredNumber;
        }
    }

    private static void UpdateJerseyText(CPointWorldText entity, JerseyTextPlan textPlan)
    {
        entity.MessageText = textPlan.Text;
        entity.WorldUnitsPerPx = textPlan.WorldUnitsPerPixel;
        Utilities.SetStateChanged(entity, "CPointWorldText", "m_messageText");
        Utilities.SetStateChanged(entity, "CPointWorldText", "m_flWorldUnitsPerPx");
    }

    private void ReconcileJerseyPrototype()
    {
        if (_teamModelMode != TeamModelMode.Kits || _mapKitModels is null)
        {
            ClearJerseyPrototype("prototype_unavailable_mode");
            return;
        }

        var candidate = Utilities.GetPlayers()
            .Where(p => IsEligiblePlayer(p)
                        && !IsGkSlot(p.Slot, p.Team)
                        && p.PlayerPawn.Value is { IsValid: true })
            .OrderBy(p => p.Slot)
            .Select(p => (Player: p, Pawn: p.PlayerPawn.Value!))
            .FirstOrDefault(pair => string.Equals(
                GetAppliedJerseyModel(pair.Pawn),
                "models/soccermod/kits/kit_home.vmdl",
                StringComparison.Ordinal));

        if (candidate.Player is null)
        {
            ClearJerseyPrototype("prototype_no_home_player");
            return;
        }

        var modelPath = GetAppliedJerseyModel(candidate.Pawn)!;
        if (!JerseyRenderRules.TryGetProfile(modelPath, out var profile))
        {
            ClearJerseyPrototype("prototype_unknown_model");
            return;
        }

        var current = _jerseyPrototype;
        if (current is not null
            && current.NumberText.IsValid
            && current.Controller == candidate.Player.EntityHandle.Raw
            && current.Pawn == candidate.Pawn.EntityHandle.Raw
            && string.Equals(current.AppliedModel, modelPath, StringComparison.Ordinal))
        {
            return;
        }

        ClearJerseyPrototype("prototype_reconcile");
        if (IsKnownBindFailure(candidate.Player.Slot, candidate.Pawn, modelPath))
        {
            return;
        }

        var numberPlan = new JerseyTextPlan(
            "88",
            profile.NumberSocket,
            profile.NumberFontSize,
            profile.NumberWorldUnitsPerPixel,
            profile.NumberFontSize * profile.NumberWorldUnitsPerPixel,
            profile.NumberMaximumWidth);
        var text = CreateAttachedJerseyText(
            candidate.Player,
            candidate.Pawn,
            numberPlan,
            DynamicJerseyTextColor(goalkeeper: false),
            JerseyPrototypeEntityPrefix,
            candidate.Player.AuthorizedSteamID?.SteamId64 ?? 0UL,
            modelPath);
        if (text is not null)
        {
            _jerseyPrototype = new JerseyPrototypeState
            {
                Slot = candidate.Player.Slot,
                Controller = candidate.Player.EntityHandle.Raw,
                Pawn = candidate.Pawn.EntityHandle.Raw,
                AppliedModel = modelPath,
                NumberText = text,
            };
            Logger.LogInformation(
                "[SM2DIAG] dynamic_jersey_prototype_created slot={Slot} pawn={Pawn} model={Model} child={Child} number=88",
                candidate.Player.Slot,
                candidate.Pawn.EntityHandle.Raw,
                modelPath,
                text.EntityHandle.Raw);
        }
    }

    private void OnJerseyPrototypeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            if (!TryParseTeamAppearanceToggle(command.GetArg(1), out var enabled))
            {
                command.ReplyToCommand("[SM] Usage: css_sm2jerseyprototype <on|off>.");
                return;
            }

            _jerseyPrototypeEnabled = enabled;
            if (enabled)
            {
                ClearDynamicJerseys("prototype_enabled");
            }
            else
            {
                ClearJerseyPrototype("prototype_disabled");
            }
        }

        command.ReplyToCommand(
            $"[SM] Jersey attachment prototype 88: {(_jerseyPrototypeEnabled ? "on" : "off")}; "
            + $"renderer={DynamicJerseyAvailabilityStatus()}.");
    }

    private void RemoveDynamicJersey(int slot)
    {
        if (!_dynamicJerseys.Remove(slot, out var state))
        {
            return;
        }

        RemoveJerseyText(state.NameText);
        if (state.NumberText is not null)
        {
            RemoveJerseyText(state.NumberText);
        }
    }

    private void ClearDynamicJerseys(string reason)
    {
        foreach (var slot in _dynamicJerseys.Keys.ToArray())
        {
            RemoveDynamicJersey(slot);
        }

        Logger.LogDebug("[SM2DIAG] dynamic_jerseys_cleared reason={Reason}", reason);
    }

    private void ClearJerseyPrototype(string reason)
    {
        if (_jerseyPrototype is { } prototype)
        {
            RemoveJerseyText(prototype.NumberText);
            _jerseyPrototype = null;
        }

        Logger.LogDebug("[SM2DIAG] dynamic_jersey_prototype_cleared reason={Reason}", reason);
    }

    private void RemoveJerseyPrototypeForSlot(int slot)
    {
        if (_jerseyPrototype is not { } prototype)
        {
            return;
        }

        if (prototype.Slot == slot)
        {
            ClearJerseyPrototype("player_lifecycle");
        }
    }

    private static void RemoveJerseyText(CPointWorldText text)
    {
        try
        {
            if (text.IsValid)
            {
                text.Remove();
            }
        }
        catch
        {
            // Entity cleanup is best-effort during map end/unload. The owning
            // state is removed regardless, so no stale handle is reused.
        }
    }

    private string? GetAppliedJerseyModel(CCSPlayerPawn pawn) =>
        _appliedJerseyModelsByPawn.TryGetValue(pawn.EntityHandle.Raw, out var model)
            ? model
            : null;

    private string DynamicJerseyAvailabilityStatus()
    {
        if (_teamModelMode != TeamModelMode.Kits)
        {
            return "unavailable:team_model_mode_is_not_kits";
        }

        if (_mapKitModels is null)
        {
            return "unavailable:kit_precache_pending";
        }

        if (_jerseyBindFailures.Count > 0)
        {
            return "unavailable:socket_bind_failed_for_pawn_generation";
        }

        var models = new[] { _mapKitModels.Home, _mapKitModels.Away, _mapKitModels.GkHome, _mapKitModels.GkAway };
        var unsupported = models
            .Where(model => !JerseyRenderRules.TryGetProfile(model, out _))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return unsupported.Length == 0
            ? "ready:four_calibrated_models_and_sockets"
            : $"unavailable:uncalibrated_model={string.Join(',', unsupported)}";
    }

    private void LogUnavailableOnce(int slot, string? model, string reason)
    {
        var key = (slot, model ?? "<none>", reason);
        if (!_jerseyUnavailableDiagnostics.Add(key))
        {
            return;
        }

        Logger.LogWarning(
            "[SM2DIAG] dynamic_jersey_unavailable slot={Slot} model={Model} reason={Reason}; no children created",
            slot,
            model ?? "<none>",
            reason);
    }

    private void MarkBindFailure(int slot, CCSPlayerPawn pawn, string model, string socket)
    {
        var key = (slot, pawn.EntityHandle.Raw, model);
        if (!_jerseyBindFailures.Add(key))
        {
            return;
        }

        Logger.LogWarning(
            "[SM2DIAG] dynamic_jersey_bind_failed slot={Slot} pawn={Pawn} model={Model} socket={Socket}; "
            + "children removed and retries suppressed for this pawn/model generation",
            slot,
            pawn.EntityHandle.Raw,
            model,
            socket);
    }

    private bool IsKnownBindFailure(int slot, CCSPlayerPawn pawn, string model) =>
        _jerseyBindFailures.Contains((slot, pawn.EntityHandle.Raw, model));

    private void RemoveJerseyBindFailures(int slot) =>
        _jerseyBindFailures.RemoveWhere(failure => failure.Slot == slot);

    private void RemoveJerseyUnavailableDiagnostics(int slot) =>
        _jerseyUnavailableDiagnostics.RemoveWhere(diagnostic => diagnostic.Slot == slot);

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
            if (JerseyNumberInUse(player, homeSquad, candidate))
            {
                continue;
            }

            if (steamId != 0)
            {
                _jerseyNumbers[steamId] = (homeSquad, candidate);
            }

            return candidate;
        }

        // A full 98-player squad is not realistic, but keeping a deterministic
        // fallback means the overlay never disappears in a stress test.
        return 99;
    }

    private bool JerseyNumberInUse(CCSPlayerController current, bool homeSquad, int number)
    {
        foreach (var other in Utilities.GetPlayers())
        {
            if (!IsEligiblePlayer(other) || other.Slot == current.Slot || IsGkSlot(other.Slot, other.Team))
            {
                continue;
            }

            if (IsHomeSquad(other.Team) != homeSquad)
            {
                continue;
            }

            var id = other.AuthorizedSteamID?.SteamId64 ?? 0UL;
            if (id != 0
                && _jerseyNumbers.TryGetValue(id, out var assigned)
                && assigned.HomeSquad == homeSquad
                && assigned.Number == number)
            {
                return true;
            }
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
        if (command.ArgCount < 2)
        {
            var current = EnsureJerseyNumber(player, steamId, homeSquad);
            command.ReplyToCommand($"[SM] Your jersey number is {current}. Use !sm2jerseynumber random to change it.");
            return;
        }

        if (command.GetArg(1).Equals("random", StringComparison.OrdinalIgnoreCase))
        {
            _jerseyNumbers.Remove(steamId);
            var number = EnsureJerseyNumber(player, steamId, homeSquad);
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

        if (steamId != 0)
        {
            _jerseyNumbers[steamId] = (homeSquad, requested);
        }

        // Keep the attached children alive. The next reconciliation updates
        // only the number text; it does not spawn, reparent or move either
        // child.
        command.ReplyToCommand($"[SM] Your jersey number is now {requested}.");
    }

    private void OnDynamicJerseyToggleCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            var value = command.GetArg(1);
            if (!TryParseTeamAppearanceToggle(value, out var enabled))
            {
                command.ReplyToCommand("[SM] Usage: css_sm2jerseydynamic <on|off>.");
                return;
            }

            _dynamicJerseysEnabled = enabled;
            SaveMatchSettings("dynamic_jersey_toggle_command");
            if (!enabled)
            {
                ClearDynamicJerseys("admin_disabled");
            }
            else
            {
                // An explicit re-enable is a new bind generation. This is the
                // deliberate retry point after an asset/socket correction;
                // stable ticks still never retry a failed bind indefinitely.
                _jerseyBindFailures.Clear();
                _jerseyUnavailableDiagnostics.Clear();
                JerseyRefreshAll("admin_enabled");
            }
        }

        command.ReplyToCommand(
            $"[SM] Dynamic jersey names/numbers requested={(_dynamicJerseysEnabled ? "on" : "off")} "
            + $"renderer={DynamicJerseyAvailabilityStatus()} "
            + $"prototype={(_jerseyPrototypeEnabled ? "on" : "off")}; "
            + "usage: css_sm2jerseydynamic <on|off>. ");
    }

    private static Color DynamicJerseyTextColor(bool goalkeeper) =>
        goalkeeper
            ? Color.FromArgb(12, 25, 48)
            : Color.White;
}

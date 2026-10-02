using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-27 owner: an anthracite wall around the pitch (like the XSL stadium)
// instead of the map's red metal railings - chosen per player in
// !menu - Settings - Visuals ("Pitch border"), default black wall.
// The 28 map railings (models/props/de_nuke/hr_nuke/metal_railing_001/*) keep
// their collision for everyone but are made invisible (EF_NODRAW), so movement
// and prediction are the same for all. On top come two looks without
// collision: the wall (Feature Package model perimeter_wall, from
// tools/kickoff/generate-kickoff-curtain.mjs) and plain copies of the railings.
// Each player is sent only the look they chose (CheckTransmit Remove only -
// TransmitEntities.Add has crashed this server). Available where the flag file
// exists and the model is mounted; the round restart respawns the map props
// and deletes ours, so it is re-applied on round start and every 2 s.
public sealed partial class SoccerModMvpPlugin
{
    private const string PerimeterWallFlagFile = "soccermod_perimeter_wall.enabled";
    private const string PerimeterWallPrefsFile = "soccermod_perimeter_wall_prefs.json";
    private const string PerimeterWallModel = "models/soccermod/stadium/perimeter_wall.vmdl";
    private const string PerimeterWallTargetName = "sm2_perimeter_wall";
    private const string PerimeterRailingCopyName = "sm2_perimeter_railing";
    private const string PerimeterRailingModelPart = "metal_railing_001";
    private const uint EffectNoDraw = 0x20;

    private bool _perimeterWallPrecached;
    private CDynamicProp? _perimeterWall;
    private readonly List<CDynamicProp> _perimeterRailingCopies = new();
    private Dictionary<ulong, bool> _perimeterWallPrefs = new();

    private bool PerimeterWallAvailable => _perimeterWallPrecached && FlagFileOn(PerimeterWallFlagFile) && IsFoundationMap(_currentMapName);

    private bool PerimeterWallOn(CCSPlayerController player) => PitchBorderShown(player) != 0;

    // 2026-09-29 owner: the Arena Vision LED boards are a third pitch border and the default
    // ("they should act like real walls"): they stand on the wall line, where the invisible map
    // railings keep the collision for everyone. 0 = red railing, 1 = black wall, 2 = LED boards.
    // A choice made before this option existed (_perimeterWallPrefs) is kept.
    private const string PitchBorderPrefsFile = "soccermod_pitch_border_prefs.json";
    private static readonly string[] PitchBorderNames = { "Red railing", "Black wall", "LED boards" };
    private Dictionary<ulong, int> _pitchBorderPrefs = new();

    private int PitchBorderChoice(CCSPlayerController player)
    {
        var id = SteamIdOf(player);
        if (_pitchBorderPrefs.TryGetValue(id, out var mode) && mode is >= 0 and <= 2) return mode;
        // 2026-09-29 owner: LED boards are the default for everyone - older railing/wall choices
        // (_perimeterWallPrefs) no longer count; a new choice in the menu is saved as usual.
        return 2;
    }

    // What the player gets: the LED boards only while they exist (v8, stadium effects and
    // boards on), otherwise the black wall - never a pitch without a border.
    private int PitchBorderShown(CCSPlayerController player)
    {
        var mode = PitchBorderChoice(player);
        return mode == 2 && !AtmoBoardsShown ? 1 : mode;
    }

    private void CyclePitchBorder(CCSPlayerController player)
    {
        var id = SteamIdOf(player);
        if (id == 0) return;
        var next = (PitchBorderChoice(player) + 1) % PitchBorderNames.Length;
        _pitchBorderPrefs[id] = next;
        SaveJsonAtomic(PitchBorderPrefsFile, _pitchBorderPrefs);
        player.PrintToChat($" [SM] Pitch border: {PitchBorderNames[next]} (!menu - Settings - Stadium).");
    }

    private void SetPerimeterWallPref(CCSPlayerController player, bool on)
    {
        var id = SteamIdOf(player);
        if (id == 0) return;
        _perimeterWallPrefs[id] = on;
        SaveJsonAtomic(PerimeterWallPrefsFile, _perimeterWallPrefs);
        player.PrintToChat(on
            ? " \x04[SM]\x01 Pitch border: \x04black wall\x01 (!menu - Settings - Visuals)."
            : " \x04[SM]\x01 Pitch border: \x07red railing\x01 (!menu - Settings - Visuals).");
    }

    private void PerimeterWallOnLoad()
    {
        _perimeterWallPrefs = LoadJsonOrNull<Dictionary<ulong, bool>>(PerimeterWallPrefsFile) ?? new();
        _pitchBorderPrefs = LoadJsonOrNull<Dictionary<ulong, int>>(PitchBorderPrefsFile) ?? new();
        AddCommand("css_sm2wall", "Admin: perimeter wall status.", OnPerimeterWallCommand);
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            _perimeterWallPrecached = false;
            if (!File.Exists(ConfigPath(PerimeterWallFlagFile))) return;
            if (!MountedAddonFiles().Contains(PerimeterWallModel + "_c"))
            {
                Logger.LogInformation("[SM2DIAG] perimeter_wall_unavailable reason=model_not_in_mounted_workshop_items model={Model}", PerimeterWallModel);
                return;
            }
            manifest.AddResource(PerimeterWallModel);
            _perimeterWallPrecached = true;
        });
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _perimeterWall = null;
            _perimeterRailingCopies.Clear();
            AddTimer(1.0f, () => PerimeterWallEnsure("map_start"), TimerFlags.STOP_ON_MAPCHANGE);
            AddTimer(2.0f, () => PerimeterWallEnsure("maintenance"), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        });
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            Server.NextFrame(() => PerimeterWallEnsure("round_start"));
            return HookResult.Continue;
        });
        RegisterListener<Listeners.CheckTransmit>(PerimeterWallCheckTransmit);
    }

    private void OnPerimeterWallCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin")) return;
        var railings = PerimeterMapRailings().Count;
        command.ReplyToCommand($"[SM] Perimeter wall: flag={File.Exists(ConfigPath(PerimeterWallFlagFile))} precached={_perimeterWallPrecached} wall={_perimeterWall is { IsValid: true }} railing copies={_perimeterRailingCopies.Count(p => p.IsValid)} map railings={railings} prefs={_perimeterWallPrefs.Count}");
    }

    private List<CBaseModelEntity> PerimeterMapRailings()
    {
        var found = new List<CBaseModelEntity>();
        foreach (var designer in new[] { "prop_dynamic", "prop_dynamic_override" })
        {
            foreach (var prop in Utilities.FindAllEntitiesByDesignerName<CBaseModelEntity>(designer))
            {
                if (!prop.IsValid || prop.Entity?.Name == PerimeterRailingCopyName) continue;
                var model = prop.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName ?? "";
                if (model.Contains(PerimeterRailingModelPart, StringComparison.OrdinalIgnoreCase)) found.Add(prop);
            }
        }
        return found;
    }

    private void PerimeterWallEnsure(string reason)
    {
        // v8 stadium only (profile maps have other pitches, MapProfile.cs).
        if (!IsFoundationMap(_currentMapName)) return;
        if (!PerimeterWallAvailable) return;
        var railings = PerimeterMapRailings();
        var changed = 0;
        // Collision stays; only the drawing goes (for everyone).
        foreach (var railing in railings)
        {
            if ((railing.Effects & EffectNoDraw) != 0) continue;
            railing.Effects |= EffectNoDraw;
            Utilities.SetStateChanged(railing, "CBaseEntity", "m_fEffects");
            changed++;
        }

        _perimeterRailingCopies.RemoveAll(copy => !copy.IsValid);
        if (_perimeterRailingCopies.Count != railings.Count)
        {
            foreach (var copy in _perimeterRailingCopies) copy.Remove();
            _perimeterRailingCopies.Clear();
            foreach (var railing in railings)
            {
                var model = railing.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName ?? "";
                if (railing.AbsOrigin is not { } origin || railing.AbsRotation is not { } angles || model.Length == 0) continue;
                var copy = PerimeterSpawnLook(PerimeterRailingCopyName, model, new Vector(origin.X, origin.Y, origin.Z), new QAngle(angles.X, angles.Y, angles.Z));
                if (copy is null) continue;
                // The map colours the grey railing model red with its render
                // colour (rendercolor 255 0 0); the copy takes it over.
                copy.Render = railing.Render;
                Utilities.SetStateChanged(copy, "CBaseModelEntity", "m_clrRender");
                _perimeterRailingCopies.Add(copy);
            }
            changed++;
        }

        if (_perimeterWall is not { IsValid: true })
        {
            _perimeterWall = PerimeterSpawnLook(PerimeterWallTargetName, PerimeterWallModel, new Vector(0.0f, 0.0f, StadiumPitchPlaneZ), new QAngle(0.0f, 0.0f, 0.0f));
            changed++;
        }

        if (changed > 0)
            Logger.LogInformation("[SM2DIAG] perimeter_wall_applied reason={Reason} map_railings={Railings} copies={Copies} wall={Wall}",
                reason, railings.Count, _perimeterRailingCopies.Count, _perimeterWall is { IsValid: true });
    }

    private CDynamicProp? PerimeterSpawnLook(string name, string model, Vector origin, QAngle angles)
    {
        var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (prop is null || !prop.IsValid) return null;
        using var keyValues = new CEntityKeyValues();
        keyValues.SetString("targetname", name);
        keyValues.SetString("model", model);
        keyValues.SetInt("solid", 0);
        keyValues.SetVector("origin", origin);
        keyValues.SetAngle("angles", angles);
        prop.DispatchSpawn(keyValues);
        if (!prop.IsValid) return null;
        prop.Entity!.Name = name;
        prop.AcceptInput("DisableCollision");
        return prop;
    }

    private void PerimeterWallCheckTransmit(CCheckTransmitInfoList infoList)
    {
        var boards = AtmoBoardEntities;
        if (_perimeterWall is null && _perimeterRailingCopies.Count == 0 && boards.Count == 0) return;
        foreach ((CCheckTransmitInfo info, CCSPlayerController? receiver) in infoList)
        {
            if (receiver is not { IsValid: true }) continue;
            // Each player gets exactly one look (Remove only - TransmitEntities.Add crashes).
            // 2026-10-01 owner: on the arena the LED boards are the only border look (the map's own
            // boards behind them are the wall and show when the LED boards are switched off).
            var shown = OnArena ? 2 : PitchBorderShown(receiver);
            if (shown != 0) foreach (var copy in _perimeterRailingCopies) if (copy.IsValid) info.TransmitEntities.Remove(copy);
            if (shown != 1 && _perimeterWall is { IsValid: true } wall) info.TransmitEntities.Remove(wall);
            if (shown != 2) foreach (var board in boards) if (board.IsValid) info.TransmitEntities.Remove(board);
        }
    }
}

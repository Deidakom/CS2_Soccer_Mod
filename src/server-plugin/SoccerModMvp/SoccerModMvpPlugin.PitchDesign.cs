using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-28 owner: other mowing patterns for the pitch, chosen per player in
// !menu - Settings - Visuals ("Pitch design"): Classic (the map's own floor)
// or one of four Feature Package designs (tools/pitch/generate-pitch-designs.mjs:
// one flat model over the grass area, skin 0..3, unlit with the roof shadow
// baked into the vertex colours). All four props exist for everyone; each
// player is sent only the one they chose (CheckTransmit Remove only). The
// 3D grass on top carries the chosen design too (PitchGrass.cs). Server flag file; the
// round restart deletes our props, so they are re-applied on round start and
// every 2 s.
public sealed partial class SoccerModMvpPlugin
{
    private const string PitchDesignFlagFile = "soccermod_pitch_designs.enabled";
    private const string PitchDesignPrefsFile = "soccermod_pitch_design_prefs.json";
    private const string PitchDesignModel = "models/soccermod/pitch/pitch_designs.vmdl";
    private const string PitchDesignTargetName = "sm2_pitch_design";
    private const float PitchDesignLift = 0.4f; // above the floor, below the 2nd grass shell
    private static readonly string[] PitchDesignNames = { "Classic", "Stripes", "Lengthwise", "Diamond", "Circles" };

    private bool _pitchDesignPrecached;
    private readonly CDynamicProp?[] _pitchDesignProps = new CDynamicProp?[4];
    private Dictionary<ulong, int> _pitchDesignPrefs = new();

    private bool PitchDesignAvailable => _pitchDesignPrecached && FlagFileOn(PitchDesignFlagFile) && IsFoundationMap(_currentMapName);

    private int PitchDesignOf(CCSPlayerController player) =>
        _pitchDesignPrefs.TryGetValue(SteamIdOf(player), out var d) && d >= 0 && d < PitchDesignNames.Length ? d : 0;

    private void CyclePitchDesign(CCSPlayerController player)
    {
        var id = SteamIdOf(player);
        if (id == 0) return;
        var next = (PitchDesignOf(player) + 1) % PitchDesignNames.Length;
        _pitchDesignPrefs[id] = next;
        SaveJsonAtomic(PitchDesignPrefsFile, _pitchDesignPrefs);
        player.PrintToChat($" \x04[SM]\x01 Pitch design: \x04{PitchDesignNames[next]}\x01 (!menu - Settings - Visuals).");
    }

    private void PitchDesignOnLoad()
    {
        _pitchDesignPrefs = LoadJsonOrNull<Dictionary<ulong, int>>(PitchDesignPrefsFile) ?? new();
        AddCommand("css_sm2pitch", "Admin: pitch design status.", (player, command) =>
        {
            if (!RequirePermission(player, command, "admin")) return;
            command.ReplyToCommand($"[SM] Pitch designs: flag={File.Exists(ConfigPath(PitchDesignFlagFile))} precached={_pitchDesignPrecached} props={_pitchDesignProps.Count(p => p is { IsValid: true })} prefs={_pitchDesignPrefs.Count} grass_designs={_grassDesignPrecached}/{string.Join(',', _grassDesignChunks.Select(l => l.Count(c => c.IsValid)))}");
            foreach (var line in PitchGrassDiag()) command.ReplyToCommand(line);
            if (command.ArgCount >= 2 && command.GetArg(1) == "transmit") { _grassTransmitDiag = true; command.ReplyToCommand("[SM] transmit snapshot on the next tick -> server log grass_transmit_diag"); }
        });
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            _pitchDesignPrecached = false;
            if (!File.Exists(ConfigPath(PitchDesignFlagFile))) return;
            if (!MountedAddonFiles().Contains(PitchDesignModel + "_c"))
            {
                Logger.LogInformation("[SM2DIAG] pitch_design_unavailable reason=model_not_in_mounted_workshop_items model={Model}", PitchDesignModel);
                return;
            }
            manifest.AddResource(PitchDesignModel);
            _pitchDesignPrecached = true;
        });
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            Array.Clear(_pitchDesignProps);
            AddTimer(1.0f, () => PitchDesignEnsure("map_start"), TimerFlags.STOP_ON_MAPCHANGE);
            AddTimer(2.0f, () => PitchDesignEnsure("maintenance"), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        });
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            Server.NextFrame(() => PitchDesignEnsure("round_start"));
            return HookResult.Continue;
        });
        RegisterListener<Listeners.CheckTransmit>(PitchDesignCheckTransmit);
    }

    private void PitchDesignEnsure(string reason)
    {
        GrassDesignEnsure(reason);
        if (!PitchDesignAvailable || !IsFoundationMap(_currentMapName)) return;
        var spawned = 0;
        for (var skin = 0; skin < _pitchDesignProps.Length; skin++)
        {
            if (_pitchDesignProps[skin] is { IsValid: true }) continue;
            var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (prop is null || !prop.IsValid) return;
            using var keyValues = new CEntityKeyValues();
            keyValues.SetString("targetname", PitchDesignTargetName);
            keyValues.SetString("model", PitchDesignModel);
            keyValues.SetInt("solid", 0);
            keyValues.SetInt("disableshadows", 1);
            keyValues.SetVector("origin", new Vector(0.0f, 0.0f, StadiumPitchPlaneZ + PitchDesignLift));
            keyValues.SetAngle("angles", new QAngle(0.0f, 0.0f, 0.0f));
            prop.DispatchSpawn(keyValues);
            if (!prop.IsValid) continue;
            prop.Entity!.Name = PitchDesignTargetName;
            prop.AcceptInput("DisableCollision");
            if (skin > 0) prop.AcceptInput("Skin", value: skin.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _pitchDesignProps[skin] = prop;
            spawned++;
        }
        if (spawned > 0) Logger.LogInformation("[SM2DIAG] pitch_design_spawned reason={Reason} count={Count}", reason, spawned);
    }

    private void PitchDesignCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (_pitchDesignProps.All(p => p is null)) return;
        foreach ((CCheckTransmitInfo info, CCSPlayerController? receiver) in infoList)
        {
            if (receiver is not { IsValid: true }) continue;
            var chosen = PitchDesignOf(receiver) - 1; // 0 = Classic = none of ours
            for (var skin = 0; skin < _pitchDesignProps.Length; skin++)
            {
                if (skin == chosen || _pitchDesignProps[skin] is not { IsValid: true } prop) continue;
                info.TransmitEntities.Remove(prop);
            }
        }
    }

    // 2026-09-28: design grass chunks were not drawn for the owner; what the
    // entities really carry, next to a classic grass tile.
    private IEnumerable<string> PitchGrassDiag()
    {
        string Describe(string label, CDynamicProp? e)
        {
            if (e is not { IsValid: true }) return $"[SM] {label}: none";
            var model = e.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName ?? "?";
            return $"[SM] {label}: index={e.Index} model={model} origin={e.AbsOrigin} mins={e.Collision.Mins} maxs={e.Collision.Maxs} effects={e.Effects} solid={e.Collision.SolidType}";
        }
        yield return Describe("classic_tile", _grassTiles.FirstOrDefault());
        for (var d = 0; d < _grassDesignChunks.Length; d++) yield return Describe($"design_{d}_chunk0", _grassDesignChunks[d].FirstOrDefault());
    }
}

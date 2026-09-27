using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-27 owner picked mockup "B - light curtain" for the kickoff wall:
// one Feature Package model (tools/kickoff/generate-kickoff-curtain.mjs) along
// the halfway line and the centre-circle arc, an additive light curtain in the
// kicking team's colour (skin 0 red, skin 1 blue) that fades upward, with
// light streaks running up. Only the look changes: the wall itself is still
// KickoffBoundary / EnforceOutlinedKickoff. Per server flag file; without it
// (or without the model in the mounted package) the beams are drawn as before.
public sealed partial class SoccerModMvpPlugin
{
    private const string KickoffCurtainFlagFile = "soccermod_kickoff_curtain.enabled";
    private const string KickoffCurtainModel = "models/soccermod/kickoff/kickoff_curtain.vmdl";
    private const string KickoffCurtainTargetName = "sm2_kickoff_curtain";

    private bool _kickoffCurtainPrecached;
    private CDynamicProp? _kickoffCurtain;

    private bool KickoffCurtainActive => _kickoffCurtainPrecached && File.Exists(ConfigPath(KickoffCurtainFlagFile));

    private void KickoffCurtainOnLoad()
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            _kickoffCurtainPrecached = false;
            if (!File.Exists(ConfigPath(KickoffCurtainFlagFile))) return;
            if (!MountedAddonFiles().Contains(KickoffCurtainModel + "_c"))
            {
                Logger.LogInformation("[SM2DIAG] kickoff_curtain_unavailable reason=model_not_in_mounted_workshop_items model={Model}", KickoffCurtainModel);
                return;
            }
            manifest.AddResource(KickoffCurtainModel);
            _kickoffCurtainPrecached = true;
            Logger.LogInformation("[SM2DIAG] kickoff_curtain_precached model={Model}", KickoffCurtainModel);
        });
        RegisterListener<Listeners.OnMapEnd>(() => _kickoffCurtain = null);
    }

    private bool KickoffCurtainHealthy => _kickoffCurtain is { IsValid: true };

    private void ClearKickoffCurtain()
    {
        if (_kickoffCurtain is { IsValid: true } curtain) curtain.Remove();
        _kickoffCurtain = null;
    }

    // sign as in DrawKickoffOutline: the arc bulges to centre.Y - sign * r,
    // which is the model's local -y at yaw 0.
    private void DrawKickoffCurtain(System.Numerics.Vector3 centre, int sign, CsTeam team)
    {
        ClearKickoffCurtain();
        var curtain = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (curtain is null || !curtain.IsValid)
        {
            Logger.LogError("[SM2DIAG] kickoff_curtain_create_failed");
            return;
        }
        using var keyValues = new CEntityKeyValues();
        keyValues.SetString("targetname", KickoffCurtainTargetName);
        keyValues.SetString("model", KickoffCurtainModel);
        keyValues.SetInt("solid", 0);
        keyValues.SetInt("disableshadows", 1);
        keyValues.SetVector("origin", new Vector(centre.X, centre.Y, StadiumPitchPlaneZ + 0.5f));
        keyValues.SetAngle("angles", new QAngle(0.0f, sign > 0 ? 0.0f : 180.0f, 0.0f));
        curtain.DispatchSpawn(keyValues);
        if (!curtain.IsValid) return;
        curtain.Entity!.Name = KickoffCurtainTargetName;
        curtain.AcceptInput("DisableCollision");
        if (team == CsTeam.CounterTerrorist) curtain.AcceptInput("Skin", value: "1");
        _kickoffCurtain = curtain;
        Logger.LogInformation("[SM2DIAG] kickoff_curtain_spawned team={Team} yaw={Yaw:F0}", team, sign > 0 ? 0.0f : 180.0f);
    }
}

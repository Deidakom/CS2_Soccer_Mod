using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-28 owner: a goal frame behind both goals of soccer_cssl_stadium_v8 -
// white tubes along the net edges (a U on the grass, top bars, slanted back
// posts; Feature Package model goal_frame from
// tools/kickoff/generate-kickoff-curtain.mjs), no collision. Model origin = centre of the goal line
// on the floor, +y into the goal; the -y goal gets yaw 180. Server flag file;
// re-applied on round start and every 2 s (the round restart deletes it).
public sealed partial class SoccerModMvpPlugin
{
    private const string GoalFrameFlagFile = "soccermod_goal_frame.enabled";
    private const string GoalFrameModel = "models/soccermod/stadium/goal_frame.vmdl";
    private const string GoalFrameTargetName = "sm2_goal_frame";
    private const float GoalFrameLineY = 1384.0f;

    private bool _goalFramePrecached;
    private readonly CDynamicProp?[] _goalFrames = new CDynamicProp?[2];

    private void GoalFrameOnLoad()
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            _goalFramePrecached = false;
            if (!File.Exists(ConfigPath(GoalFrameFlagFile))) return;
            if (!MountedAddonFiles().Contains(GoalFrameModel + "_c"))
            {
                Logger.LogInformation("[SM2DIAG] goal_frame_unavailable reason=model_not_in_mounted_workshop_items model={Model}", GoalFrameModel);
                return;
            }
            manifest.AddResource(GoalFrameModel);
            _goalFramePrecached = true;
        });
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            Array.Clear(_goalFrames);
            AddTimer(1.0f, () => GoalFrameEnsure("map_start"), TimerFlags.STOP_ON_MAPCHANGE);
            AddTimer(2.0f, () => GoalFrameEnsure("maintenance"), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        });
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            Server.NextFrame(() => GoalFrameEnsure("round_start"));
            return HookResult.Continue;
        });
    }

    private void GoalFrameEnsure(string reason)
    {
        if (!_goalFramePrecached || !File.Exists(ConfigPath(GoalFrameFlagFile)) || !IsFoundationMap(_currentMapName)) return;
        var spawned = 0;
        for (var i = 0; i < 2; i++)
        {
            if (_goalFrames[i] is { IsValid: true }) continue;
            var side = i == 0 ? 1.0f : -1.0f;
            var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (prop is null || !prop.IsValid) return;
            using var keyValues = new CEntityKeyValues();
            keyValues.SetString("targetname", GoalFrameTargetName);
            keyValues.SetString("model", GoalFrameModel);
            keyValues.SetInt("solid", 0);
            keyValues.SetVector("origin", new Vector(0.0f, side * GoalFrameLineY, StadiumPitchPlaneZ));
            keyValues.SetAngle("angles", new QAngle(0.0f, side > 0 ? 0.0f : 180.0f, 0.0f));
            prop.DispatchSpawn(keyValues);
            if (!prop.IsValid) continue;
            prop.Entity!.Name = GoalFrameTargetName;
            prop.AcceptInput("DisableCollision");
            _goalFrames[i] = prop;
            spawned++;
        }
        if (spawned > 0) Logger.LogInformation("[SM2DIAG] goal_frame_spawned reason={Reason} count={Count}", reason, spawned);
    }
}

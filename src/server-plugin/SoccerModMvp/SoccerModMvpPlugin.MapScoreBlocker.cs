using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-27 owner: the roof screens show two white dots in the middle - the
// old scoreboard colon, baked into the map. Black point_worldtext text and a
// worldtext background (materials/dev/black_simple) did not cover them in
// game, so a small opaque black plate from the Feature Package
// (tools/kickoff/generate-kickoff-curtain.mjs, 56 x 40 units, x/z plane) sits
// just in front of each screen centre, behind the GOALLL!! text - only while
// the goal animation runs (the dots are the score colon otherwise).
// Part of the roof score text (MapScoreText.cs flag); needs the model mounted.
public sealed partial class SoccerModMvpPlugin
{
    private const string MapScoreBlockerModel = "models/soccermod/scoreboard/colon_blocker.vmdl";
    private const string MapScoreBlockerTargetName = "sm2_roofscore_blocker";
    private const float MapScoreBlockerInset = 0.8f; // numbers and GOALLL!! sit at 2.0

    private bool _mapScoreBlockerPrecached;
    private readonly List<CDynamicProp> _mapScoreBlockers = new();

    private void MapScoreBlockerPrecache(ResourceManifest manifest)
    {
        _mapScoreBlockerPrecached = false;
        _mapScoreBlockers.Clear();
        if (!MapScoreTextEnabled) return;
        if (!MountedAddonFiles().Contains(MapScoreBlockerModel + "_c"))
        {
            Logger.LogInformation("[SM2DIAG] roof_blocker_unavailable reason=model_not_in_mounted_workshop_items model={Model}", MapScoreBlockerModel);
            return;
        }
        manifest.AddResource(MapScoreBlockerModel);
        _mapScoreBlockerPrecached = true;
    }

    private void MapScoreBlockerEnsure()
    {
        _mapScoreBlockers.RemoveAll(prop => !prop.IsValid);
        // Owner, same day: the dots are the score colon and stay; only the
        // GOALLL!! / OWN GOAL!! animation hides them.
        if (!_goalFxActive)
        {
            foreach (var prop in _mapScoreBlockers) prop.Remove();
            _mapScoreBlockers.Clear();
            return;
        }
        if (!_mapScoreBlockerPrecached || _mapScoreBlockers.Count == 2) return;
        foreach (var prop in _mapScoreBlockers) prop.Remove();
        _mapScoreBlockers.Clear();
        foreach (var side in new[] { 1, -1 })
        {
            var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (prop is null || !prop.IsValid) return;
            using var keyValues = new CEntityKeyValues();
            keyValues.SetString("targetname", MapScoreBlockerTargetName);
            keyValues.SetString("model", MapScoreBlockerModel);
            keyValues.SetInt("solid", 0);
            keyValues.SetInt("disableshadows", 1);
            keyValues.SetVector("origin", new Vector(0.0f, side * (MapScoreTextScreenY - MapScoreBlockerInset), MapScoreTextCenterZ));
            keyValues.SetAngle("angles", new QAngle(0.0f, 0.0f, 0.0f));
            prop.DispatchSpawn(keyValues);
            if (!prop.IsValid) continue;
            prop.Entity!.Name = MapScoreBlockerTargetName;
            prop.AcceptInput("DisableCollision");
            _mapScoreBlockers.Add(prop);
        }
        Logger.LogInformation("[SM2DIAG] roof_blocker_spawned count={Count}", _mapScoreBlockers.Count);
    }
}

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-28 owner: "make the 3D grass work for all 4 pitch design variants".
// The bake grass blades have one colour, so a pitch design only showed in the
// gaps between them. Each design has its own bake grass in the Feature
// Package (tools/grass/generate-shell-grass.mjs --design <name>): the same
// blades with the design's mowing pattern in their vertex colours, as 4 x 4
// chunks models/soccermod/grass_design_<name>_<cx>_<cy>. A player with 3D
// grass on and a design chosen is sent that design's chunks instead of the
// classic grass tiles (GrassCheckTransmit, Remove only). Needs the bake grass,
// the pitch designs and the chunks in the mounted package; otherwise the
// classic grass stays on top of the design as before.
public sealed partial class SoccerModMvpPlugin
{
    private static readonly string[] GrassDesignKeys = { "stripes", "lengthwise", "diamond", "circles" };
    private const int GrassDesignChunksX = 4, GrassDesignChunksY = 4;
    private const string GrassDesignTargetName = "sm2_grass_design";
    private static string GrassDesignModel(int design, int cx, int cy) => $"models/soccermod/grass_design_{GrassDesignKeys[design]}_{cx}_{cy}.vmdl";

    private bool _grassDesignPrecached;
    private readonly List<CDynamicProp>[] _grassDesignChunks = { new(), new(), new(), new() };

    private bool GrassDesignReady(int design) =>
        design >= 0 && design < _grassDesignChunks.Length
        && _grassDesignChunks[design].Count == GrassDesignChunksX * GrassDesignChunksY
        && _grassDesignChunks[design].All(c => c.IsValid);

    private void PitchGrassOnLoad(bool hotReload)
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            _grassDesignPrecached = false;
            if (File.Exists(ConfigPath(GrassFineFlagFile)) || !File.Exists(ConfigPath(PitchDesignFlagFile))) return;
            if (!MountedAddonFiles().Contains(GrassDesignModel(0, 0, 0) + "_c"))
            {
                Logger.LogInformation("[SM2DIAG] grass_design_unavailable reason=model_not_in_mounted_workshop_items model={Model}", GrassDesignModel(0, 0, 0));
                return;
            }
            for (var d = 0; d < GrassDesignKeys.Length; d++)
            for (var cy = 0; cy < GrassDesignChunksY; cy++)
            for (var cx = 0; cx < GrassDesignChunksX; cx++) manifest.AddResource(GrassDesignModel(d, cx, cy));
            _grassDesignPrecached = true;
        });
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            foreach (var list in _grassDesignChunks) list.Clear();
        });
        if (hotReload)
        {
            foreach (var old in Utilities.FindAllEntitiesByDesignerName<CDynamicProp>("prop_dynamic")
                         .Where(e => e.IsValid && e.Entity?.Name == GrassDesignTargetName).ToList())
                old.Remove();
        }
    }

    // From PitchDesignEnsure (map start, round start, every 2 s): the chunks
    // follow the classic grass - present while it is, gone when it is removed.
    private void GrassDesignEnsure(string reason)
    {
        if (!_grassDesignPrecached || !GrassBakeActive || !GrassSpawned || _grassFloorZ is not { } floorZ || !PitchDesignAvailable)
        {
            RemoveGrassDesigns(reason);
            return;
        }
        var chunkW = 2 * GrassHalfX / GrassDesignChunksX;
        var chunkH = 2 * GrassHalfY / GrassDesignChunksY;
        var spawned = 0;
        for (var d = 0; d < GrassDesignKeys.Length; d++)
        {
            if (GrassDesignReady(d)) continue;
            foreach (var old in _grassDesignChunks[d]) if (old.IsValid) old.Remove();
            _grassDesignChunks[d].Clear();
            for (var cy = 0; cy < GrassDesignChunksY; cy++)
            for (var cx = 0; cx < GrassDesignChunksX; cx++)
            {
                var chunk = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
                if (chunk is null || !chunk.IsValid) return;
                using var keyValues = new CEntityKeyValues();
                keyValues.SetString("targetname", GrassDesignTargetName);
                keyValues.SetString("model", GrassDesignModel(d, cx, cy));
                keyValues.SetInt("solid", 0);
                keyValues.SetInt("disableshadows", 1);
                keyValues.SetVector("origin", new Vector(-GrassHalfX + (cx + 0.5f) * chunkW, -GrassHalfY + (cy + 0.5f) * chunkH, floorZ));
                keyValues.SetAngle("angles", new QAngle(0.0f, 0.0f, 0.0f));
                chunk.DispatchSpawn(keyValues);
                if (!chunk.IsValid) continue;
                chunk.Entity!.Name = GrassDesignTargetName;
                chunk.AcceptInput("DisableCollision");
                _grassDesignChunks[d].Add(chunk);
                spawned++;
            }
        }
        if (spawned > 0) Logger.LogInformation("[SM2DIAG] grass_design_spawned reason={Reason} chunks={Chunks}", reason, spawned);
    }

    private void RemoveGrassDesigns(string reason)
    {
        var removed = 0;
        foreach (var list in _grassDesignChunks)
        {
            foreach (var chunk in list)
            {
                if (!chunk.IsValid) continue;
                chunk.Remove();
                removed++;
            }
            list.Clear();
        }
        if (removed > 0) Logger.LogInformation("[SM2DIAG] grass_design_removed reason={Reason} chunks={Chunks}", reason, removed);
    }
}

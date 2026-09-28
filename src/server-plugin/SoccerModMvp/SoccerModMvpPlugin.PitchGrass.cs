using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-28 owner: "make the 3D grass work for all 4 pitch design variants".
// The bake grass blades have one colour, so a pitch design only showed in the
// gaps between them. Each design has its own bake grass in the Feature
// Package (tools/grass/generate-shell-grass.mjs --design <name>): the same
// 16 x 20 tiles and materials as the classic bake grass, with the design's
// mowing pattern in the vertex colours - models/soccermod/grass_dtile_<name>_<tx>_<ty>.
// (A first 4 x 4 chunk version with its own material was transmitted but
// never drawn by the owner's client.) A player with 3D grass on and a design
// chosen is sent that design's tiles instead of the classic ones
// (GrassCheckTransmit, Remove only). A design's 320 tiles only exist while a
// player on the server has chosen it. Needs the bake grass, the pitch designs
// and the tiles in the mounted package; otherwise the classic grass stays on
// top of the design as before.
public sealed partial class SoccerModMvpPlugin
{
    private static readonly string[] GrassDesignKeys = { "stripes", "lengthwise", "diamond", "circles" };
    private const string GrassDesignTargetName = "sm2_grass_design";
    private static string GrassDesignModel(int design, int tx, int ty) => $"models/soccermod/grass_dtile_{GrassDesignKeys[design]}_{tx}_{ty}.vmdl";

    private bool _grassDesignPrecached;
    // Per design: its tiles (empty while nobody has chosen it).
    private readonly List<CDynamicProp>[] _grassDesignChunks = { new(), new(), new(), new() };

    private bool GrassDesignReady(int design) =>
        design >= 0 && design < _grassDesignChunks.Length
        && _grassDesignChunks[design].Count == GrassTilesX * GrassTilesY
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
            for (var ty = 0; ty < GrassTilesY; ty++)
            for (var tx = 0; tx < GrassTilesX; tx++) manifest.AddResource(GrassDesignModel(d, tx, ty));
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

    // Designs chosen by a player who sees the 3D grass.
    private HashSet<int> WantedGrassDesigns()
    {
        var wanted = new HashSet<int>();
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot || !GrassOn(player)) continue;
            var design = PitchDesignOf(player) - 1;
            if (design >= 0 && design < GrassDesignKeys.Length) wanted.Add(design);
        }
        return wanted;
    }

    // From GrassEnsure and PitchDesignEnsure (map start, round start, every
    // 2 s): the design tiles follow the classic grass and the players' choices.
    private void GrassDesignEnsure(string reason)
    {
        if (!_grassDesignPrecached || !GrassBakeActive || !GrassSpawned || _grassFloorZ is not { } floorZ || !PitchDesignAvailable)
        {
            RemoveGrassDesigns(reason);
            return;
        }
        var wanted = WantedGrassDesigns();
        var tileW = 2 * GrassHalfX / GrassTilesX;
        var tileH = 2 * GrassHalfY / GrassTilesY;
        var spawned = 0;
        for (var d = 0; d < GrassDesignKeys.Length; d++)
        {
            if (!wanted.Contains(d))
            {
                RemoveGrassDesign(d);
                continue;
            }
            if (GrassDesignReady(d)) continue;
            RemoveGrassDesign(d);
            for (var ty = 0; ty < GrassTilesY; ty++)
            for (var tx = 0; tx < GrassTilesX; tx++)
            {
                var tile = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
                if (tile is null || !tile.IsValid) return;
                using var keyValues = new CEntityKeyValues();
                keyValues.SetString("targetname", GrassDesignTargetName);
                keyValues.SetString("model", GrassDesignModel(d, tx, ty));
                keyValues.SetInt("solid", 0);
                keyValues.SetInt("disableshadows", 1);
                keyValues.SetVector("origin", new Vector(-GrassHalfX + (tx + 0.5f) * tileW, -GrassHalfY + (ty + 0.5f) * tileH, floorZ));
                keyValues.SetAngle("angles", new QAngle(0.0f, 0.0f, 0.0f));
                tile.DispatchSpawn(keyValues);
                if (!tile.IsValid) continue;
                tile.Entity!.Name = GrassDesignTargetName;
                tile.AcceptInput("DisableCollision");
                _grassDesignChunks[d].Add(tile);
                spawned++;
            }
        }
        if (spawned > 0) Logger.LogInformation("[SM2DIAG] grass_design_spawned reason={Reason} tiles={Tiles} designs={Designs}", reason, spawned, string.Join(',', wanted));
    }

    private int RemoveGrassDesign(int design)
    {
        var removed = 0;
        foreach (var tile in _grassDesignChunks[design])
        {
            if (!tile.IsValid) continue;
            tile.Remove();
            removed++;
        }
        _grassDesignChunks[design].Clear();
        return removed;
    }

    private void RemoveGrassDesigns(string reason)
    {
        var removed = 0;
        for (var d = 0; d < _grassDesignChunks.Length; d++) removed += RemoveGrassDesign(d);
        if (removed > 0) Logger.LogInformation("[SM2DIAG] grass_design_removed reason={Reason} tiles={Tiles}", reason, removed);
    }
}

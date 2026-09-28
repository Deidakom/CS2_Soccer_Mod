using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-28 owner: "make the 3D grass work for all 4 pitch design variants".
// The bake grass is an unlit csgo_static_overlay: it writes no depth and is
// painted over by any prop drawn after it - the design floor (first an
// overlay, then an opaque csgo_complex) hid it every time; only the map's own
// floor is drawn before it (tested in game three ways). So a player with 3D
// grass on and a design chosen gets the other grass of the Feature Package:
// the lit, alpha-tested "fine" tiles (models/soccermod/grass_fine_<x>_<y>,
// skin 1 = cutout, csgo_complex), drawn with depth like the design floor
// under them - the design shows between the blades as the map floor does for
// the classic grass, and both are lit the same way. The set exists only while
// such a player is on the server; GrassCheckTransmit sends it instead of the
// bake tiles (Remove only). (grass_dtile_* in the package: an earlier
// attempt with the pattern in the overlay blades, unused.)
public sealed partial class SoccerModMvpPlugin
{
    private const string GrassDesignTargetName = "sm2_grass_design";
    private const int GrassCutoutSkin = 1;
    // 2026-09-28 glow test (owner: soft shadows on the design grass too):
    // models/soccermod/grass_glow_<x>_<y> - the bake geometry with its soft
    // baked shadow, opaque alpha-tested csgo_complex lit almost not at all and
    // coloured by self-illumination (skin 0 = A, 1 = B), and the design floor
    // skins 4-7 (A) / 8-11 (B). css_sm2pitch glow off|a|b (flag files).
    private const string GrassGlowAFlag = "soccermod_design_glow_a.enabled", GrassGlowBFlag = "soccermod_design_glow_b.enabled";
    private static string GrassGlowModel(int tx, int ty) => $"models/soccermod/grass_glow_{tx}_{ty}.vmdl";
    private bool _grassGlowPrecached;
    private int _grassCutMode = -1;
    // 0 = fine cutout, 1 = glow A, 2 = glow B
    private int GrassDesignMode => !_grassGlowPrecached ? 0 : FlagFileOn(GrassGlowBFlag) ? 2 : FlagFileOn(GrassGlowAFlag) ? 1 : 0;

    private bool _grassDesignPrecached;
    private readonly List<CDynamicProp> _grassCutTiles = new();

    // The cutout set serves every design.
    private bool GrassDesignReady(int design) =>
        design >= 0 && _grassCutTiles.Count == GrassTilesX * GrassTilesY && _grassCutTiles.All(c => c.IsValid);

    private void PitchGrassOnLoad(bool hotReload)
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            _grassDesignPrecached = false;
            if (File.Exists(ConfigPath(GrassFineFlagFile)) || !File.Exists(ConfigPath(PitchDesignFlagFile))) return;
            if (!MountedAddonFiles().Contains(GrassTileModel(0, 0) + "_c"))
            {
                Logger.LogInformation("[SM2DIAG] grass_design_unavailable reason=model_not_in_mounted_workshop_items model={Model}", GrassTileModel(0, 0));
                return;
            }
            for (var ty = 0; ty < GrassTilesY; ty++)
            for (var tx = 0; tx < GrassTilesX; tx++) manifest.AddResource(GrassTileModel(tx, ty));
            _grassDesignPrecached = true;
            _grassGlowPrecached = false;
            if (!MountedAddonFiles().Contains(GrassGlowModel(0, 0) + "_c")) return;
            for (var ty = 0; ty < GrassTilesY; ty++)
            for (var tx = 0; tx < GrassTilesX; tx++) manifest.AddResource(GrassGlowModel(tx, ty));
            _grassGlowPrecached = true;
        });
        RegisterListener<Listeners.OnMapStart>(_ => _grassCutTiles.Clear());
        if (hotReload)
        {
            foreach (var old in Utilities.FindAllEntitiesByDesignerName<CDynamicProp>("prop_dynamic")
                         .Where(e => e.IsValid && e.Entity?.Name == GrassDesignTargetName).ToList())
                old.Remove();
        }
    }

    // A player who sees the 3D grass has a pitch design chosen.
    private bool GrassDesignWanted() => Utilities.GetPlayers()
        .Any(p => p.IsValid && !p.IsBot && GrassOn(p) && PitchDesignOf(p) > 0);

    // From GrassEnsure and PitchDesignEnsure (map start, round start, every
    // 2 s): the cutout set follows the classic grass and the players' choices.
    private void GrassDesignEnsure(string reason)
    {
        if (!_grassDesignPrecached || !GrassBakeActive || !GrassSpawned || _grassFloorZ is not { } floorZ
            || !PitchDesignAvailable || !GrassDesignWanted())
        {
            RemoveGrassDesigns(reason);
            return;
        }
        var mode = GrassDesignMode;
        if (GrassDesignReady(0) && _grassCutMode == mode) return;
        RemoveGrassDesigns("respawn");
        _grassCutMode = mode;
        var tileW = 2 * GrassHalfX / GrassTilesX;
        var tileH = 2 * GrassHalfY / GrassTilesY;
        for (var ty = 0; ty < GrassTilesY; ty++)
        for (var tx = 0; tx < GrassTilesX; tx++)
        {
            var tile = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (tile is null || !tile.IsValid) return;
            using var keyValues = new CEntityKeyValues();
            keyValues.SetString("targetname", GrassDesignTargetName);
            keyValues.SetString("model", mode == 0 ? GrassTileModel(tx, ty) : GrassGlowModel(tx, ty));
            keyValues.SetInt("solid", 0);
            keyValues.SetInt("disableshadows", 1);
            keyValues.SetVector("origin", new Vector(-GrassHalfX + (tx + 0.5f) * tileW, -GrassHalfY + (ty + 0.5f) * tileH, floorZ));
            keyValues.SetAngle("angles", new QAngle(0.0f, 0.0f, 0.0f));
            tile.DispatchSpawn(keyValues);
            if (!tile.IsValid) continue;
            tile.Entity!.Name = GrassDesignTargetName;
            tile.AcceptInput("DisableCollision");
            var skin = mode == 0 ? GrassCutoutSkin : mode - 1;
            if (skin > 0) tile.AcceptInput("Skin", value: skin.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _grassCutTiles.Add(tile);
        }
        Logger.LogInformation("[SM2DIAG] grass_design_spawned reason={Reason} tiles={Tiles} variant={Variant}", reason, _grassCutTiles.Count, mode == 0 ? "fine_cutout" : mode == 1 ? "glow_a" : "glow_b");
    }

    private void RemoveGrassDesigns(string reason)
    {
        var removed = 0;
        foreach (var tile in _grassCutTiles)
        {
            if (!tile.IsValid) continue;
            tile.Remove();
            removed++;
        }
        _grassCutTiles.Clear();
        if (removed > 0) Logger.LogInformation("[SM2DIAG] grass_design_removed reason={Reason} tiles={Tiles}", reason, removed);
    }
}

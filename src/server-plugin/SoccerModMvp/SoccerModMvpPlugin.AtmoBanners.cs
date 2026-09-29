using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-29 owner: "let the banners wave a little, as if the wind moves
// them" (v8 only, admin toggle "Waving banners"). The eight roof banners are
// two func_brush entities of the map (one per end, origin (0.5, +-1956,
// 790.5), 4 banners each at x = +-546 / +-1100, 64 x 180). They are disabled
// and replaced by models/soccermod/atmo/banner_wave (tools/atmo/generate-
// banners.mjs): same shape, hung from the same top edge (z 880.5), bone chain
// with three looping wind animations so neighbours never move in step.
// The map recreates its entities on a new round, so this runs every round start.
public sealed partial class SoccerModMvpPlugin
{
    private const string AtmoBannerModel = "models/soccermod/atmo/banner_wave.vmdl";
    private const string AtmoBannerName = "sm2_atmo_banner";
    private static readonly float[] AtmoBannerXs = { -1100f, -546f, 546f, 1100f };
    private static readonly string[] AtmoBannerWinds = { "wind_a", "wind_b", "wind_c" };
    private readonly List<CDynamicProp> _atmoBanners = new();

    private bool AtmoBannersWanted => AtmoOn && AtmoSet.WavingBanners;

    private void AtmoBannersOnLoad()
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            if (File.Exists(ConfigPath(AtmoFlagFile))) manifest.AddResource(AtmoBannerModel);
        });
        RegisterListener<Listeners.OnMapStart>(_ => AtmoBannersMapStart());
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            _atmoBanners.Clear();
            Server.NextFrame(() => AtmoBannersEnsure("round_start"));
            return HookResult.Continue;
        });
    }

    // The map's banner brushes: func_brush at (0.5, +-1956, 790.5).
    private IEnumerable<CFuncBrush> AtmoMapBannerBrushes() =>
        Utilities.FindAllEntitiesByDesignerName<CFuncBrush>("func_brush").Where(b => b.IsValid && b.AbsOrigin is { } o
            && MathF.Abs(o.X - 0.5f) < 2f && MathF.Abs(MathF.Abs(o.Y) - 1956f) < 2f && MathF.Abs(o.Z - 790.5f) < 2f);

    private void AtmoBannersEnsure(string reason)
    {
        var want = AtmoBannersWanted;
        var brushes = AtmoMapBannerBrushes().ToList();
        foreach (var brush in brushes) brush.AcceptInput(want ? "Disable" : "Enable");
        if (!want)
        {
            foreach (var banner in _atmoBanners) if (banner.IsValid) banner.Remove();
            _atmoBanners.Clear();
            return;
        }
        if (_atmoBanners.Count == 8 && _atmoBanners.All(b => b.IsValid)) return;
        foreach (var banner in _atmoBanners) if (banner.IsValid) banner.Remove();
        _atmoBanners.Clear();
        var i = 0;
        foreach (var side in new[] { 1, -1 })
        foreach (var x in AtmoBannerXs)
        {
            var banner = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (banner is null || !banner.IsValid) continue;
            var wind = AtmoBannerWinds[i++ % AtmoBannerWinds.Length];
            using var kv = new CEntityKeyValues();
            kv.SetString("targetname", AtmoBannerName);
            kv.SetString("model", AtmoBannerModel);
            kv.SetString("DefaultAnim", wind);
            kv.SetInt("solid", 0);
            kv.SetInt("disableshadows", 1);
            kv.SetVector("origin", new Vector(x, side * 1956f, 880.5f));
            kv.SetAngle("angles", new QAngle(0, side > 0 ? 0 : 180, 0));
            banner.DispatchSpawn(kv);
            if (!banner.IsValid) continue;
            banner.AcceptInput("DisableCollision");
            if (side < 0) banner.AcceptInput("Skin", value: "1");
            banner.AcceptInput("SetAnimation", value: wind);
            _atmoBanners.Add(banner);
        }
        Logger.LogInformation("[SM2DIAG] atmo_banners reason={Reason} mapBrushes={Brushes} spawned={Spawned}", reason, brushes.Count, _atmoBanners.Count);
    }

    // Map start (also run after a plugin hot reload, AtmoHotReload).
    private void AtmoBannersMapStart()
    {
        _atmoBanners.Clear();
        AddTimer(1.0f, () => AtmoBannersEnsure("map_start"), TimerFlags.STOP_ON_MAPCHANGE);
    }
}

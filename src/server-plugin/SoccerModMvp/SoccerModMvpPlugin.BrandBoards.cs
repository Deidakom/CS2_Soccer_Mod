using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-10-02 owner: "can we add real brands as well, such as Coca Cola, Nike, Adidas ... mix them
// in", then "put that on all maps and servers". Everything here needs the brand models (tools/brands,
// in the stadium's Workshop item from update 6) among the mounted Workshop items; without them
// nothing changes. The stadium's LED boards then use the 16-page board (AtmoBoards.cs asks here),
// and the two halls get overlay models in front of their own boards (world coordinates, spawned at
// the origin).
public sealed partial class SoccerModMvpPlugin
{
    private const string BrandLedModel = "models/soccermod_brands/led_board.vmdl";
    private const string BrandHallBoardsModel = "models/soccermod_brands/hall_boards.vmdl";
    private const string Brand2v2BoardsModel = "models/soccermod_brands/2v2_boards.vmdl";
    private const string Brand2v2BannersModel = "models/soccermod_brands/2v2_banners.vmdl";
    private const string BrandPropName = "sm2_brand_boards";
    private bool _brandsMounted;
    private readonly List<CDynamicProp> _brandProps = new();

    // Registered before AtmoBoardsOnLoad, so the LED boards' precache already knows the answer.
    private void BrandBoardsOnLoad()
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            _brandsMounted = MountedAddonFiles().Contains(BrandLedModel + "_c");
            if (!_brandsMounted) return;
            foreach (var model in new[] { BrandLedModel, BrandHallBoardsModel, Brand2v2BoardsModel, Brand2v2BannersModel }) manifest.AddResource(model);
            Logger.LogInformation("[SM2DIAG] brand_boards_precached map={Map}", Server.MapName);
        });
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _brandProps.Clear();
            AddTimer(1.0f, () => BrandBoardsEnsure("map_start"), TimerFlags.STOP_ON_MAPCHANGE);
        });
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            _brandProps.Clear();
            Server.NextFrame(() => BrandBoardsEnsure("round_start"));
            return HookResult.Continue;
        });
    }

    private void BrandBoardsEnsure(string reason)
    {
        if (!_brandsMounted || !OnHall || OnStreet || OnPool) return;   // the street arena and the pool have no advert boards
        if (_brandProps.Count > 0 && _brandProps.All(p => p.IsValid)) return;
        foreach (var prop in _brandProps) if (prop.IsValid) prop.Remove();
        _brandProps.Clear();
        foreach (var model in OnGym ? new[] { Brand2v2BoardsModel, Brand2v2BannersModel } : new[] { BrandHallBoardsModel })
        {
            var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (prop is null || !prop.IsValid) continue;
            using var kv = new CEntityKeyValues();
            kv.SetString("targetname", BrandPropName);
            kv.SetString("model", model);
            kv.SetInt("solid", 0);
            kv.SetInt("disableshadows", 1);
            kv.SetVector("origin", new Vector(0f, 0f, 0f));
            kv.SetAngle("angles", new QAngle(0, 0, 0));
            prop.DispatchSpawn(kv);
            if (!prop.IsValid) continue;
            prop.AcceptInput("DisableCollision");
            _brandProps.Add(prop);
        }
        Logger.LogInformation("[SM2DIAG] brand_boards reason={Reason} spawned={Spawned}", reason, _brandProps.Count);
    }
}

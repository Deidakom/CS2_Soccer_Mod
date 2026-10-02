#!/usr/bin/env python3
"""Brand boards (owner 2026-10-02: "can we add real brands as well, such as Coca Cola, Nike, Adidas
... mix them in"; first for the test server, then: "put that on all maps and servers", inside the
stadium's Workshop item - his choice, he allowed the publication).

Only when the brand models are among the mounted Workshop items (stadium item 3811382872 from
update 6, or the test item 3811741091):
- the stadium's LED boards use the 16-page board (12 brands instead of 6, real ones mixed in,
  every brand board shows another brand),
- on soccer_indoor_hall an overlay with LED adverts stands 0.6 in front of the boards,
- on soccer_2v2_arena printed adverts lie on the kick boards and two cloth banners hang on the
  east wall.
Without the models nothing changes (a server on an older revision keeps the old boards).
Writes SoccerModMvpPlugin.BrandBoards.cs and applies exact-match edits, idempotent, with backups.

  python3 apply_brand_boards.py /root/football-build/src/server-plugin/SoccerModMvp
"""
import shutil, sys, time
from pathlib import Path

src = Path(sys.argv[1])
backup = Path("/root/arena-plugin") / ("backup-brands-" + time.strftime("%Y%m%d-%H%M%S"))

BRAND_BOARDS = '''using CounterStrikeSharp.API;
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
        if (!_brandsMounted || !OnHall) return;
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
'''

EDITS = {
    "SoccerModMvpPlugin.Atmosphere.cs": [
        ("        AtmoBoardsOnLoad();\n", "        BrandBoardsOnLoad();   // brand boards, before the LED boards (BrandBoards.cs)\n        AtmoBoardsOnLoad();\n"),
    ],
    "SoccerModMvpPlugin.AtmoBoards.cs": [
        (
            '    private const string AtmoBoardModel = "models/soccermod/atmo/led_board.vmdl";\n',
            '    private const string AtmoBoardStockModel = "models/soccermod/atmo/led_board.vmdl";\n'
            "    // with the brand models mounted the boards are the 16-page model (BrandBoards.cs)\n"
            "    private string AtmoBoardModel => _brandsMounted ? BrandLedModel : AtmoBoardStockModel;\n",
        ),
        (
            "    private const int AtmoBoardBrands = 6, AtmoBoardTextFirst = 6, AtmoBoardTexts = 2;\n"
            "    private const int AtmoBoardBanner = 5, AtmoBoardFairPlay = 6, AtmoBoardGoalRed = 8, AtmoBoardGoalBlue = 9;\n"
            "    private const int AtmoBoardDissolve70 = 10, AtmoBoardDissolve30 = 20, AtmoBoardDark = 30;\n",
            "    // The brand board: pages 0-11 brands (5 = the SoccerMod banner, as here), 12-13 texts,\n"
            "    // 14 / 15 goal red / blue, then + 16 = 70 %, + 32 = 30 %, 48 = dark.\n"
            "    private const int AtmoBoardTexts = 2, AtmoBoardBanner = 5;\n"
            "    private int AtmoBoardBrands => _brandsMounted ? 12 : 6;\n"
            "    private int AtmoBoardTextFirst => _brandsMounted ? 12 : 6;\n"
            "    private int AtmoBoardFairPlay => AtmoBoardTextFirst;\n"
            "    private int AtmoBoardGoalRed => _brandsMounted ? 14 : 8;\n"
            "    private int AtmoBoardGoalBlue => _brandsMounted ? 15 : 9;\n"
            "    private int AtmoBoardDissolve70 => _brandsMounted ? 16 : 10;\n"
            "    private int AtmoBoardDissolve30 => _brandsMounted ? 32 : 20;\n"
            "    private int AtmoBoardDark => _brandsMounted ? 48 : 30;\n",
        ),
        (
            "        : i % 2 == 0 ? _atmoBoardPage % AtmoBoardBrands : AtmoBoardTextFirst + _atmoBoardPage % AtmoBoardTexts;\n",
            "        // brand board: every brand board shows another brand, so the real ones are mixed in all round\n"
            "        : i % 2 == 0 ? (_atmoBoardPage + (_brandsMounted ? i / 2 : 0)) % AtmoBoardBrands : AtmoBoardTextFirst + _atmoBoardPage % AtmoBoardTexts;\n",
        ),
    ],
}

plan = {}
for name, edits in EDITS.items():
    path = src / name
    raw = path.read_bytes().decode("utf-8")
    eol = "\r\n" if "\r\n" in raw else "\n"
    new = raw
    for old, repl in edits:
        old, repl = old.replace("\n", eol), repl.replace("\n", eol)
        if repl in new:
            continue
        if new.count(old) != 1:
            sys.exit(f"{name}: expected exactly one match for:\n{old}")
        new = new.replace(old, repl)
    if new != raw:
        plan[path] = new
target = src / "SoccerModMvpPlugin.BrandBoards.cs"
if not target.exists() or target.read_text(encoding="utf-8") != BRAND_BOARDS:
    plan[target] = BRAND_BOARDS
for path, new in plan.items():
    backup.mkdir(parents=True, exist_ok=True)
    if path.exists():
        shutil.copy2(path, backup / path.name)
    path.write_bytes(new.encode("utf-8"))
    print("wrote", path.name)
print(f"{len(plan)} file(s) changed" + (f", backups in {backup}" if plan else " (already applied)"))

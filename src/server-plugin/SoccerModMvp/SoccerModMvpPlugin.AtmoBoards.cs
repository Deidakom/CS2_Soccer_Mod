using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-29 "Arena Vision" LED perimeter boards (v8 only, admin toggle "LED
// boards"). Owner: rotating advertising like in football games, English
// fantasy brands with their own logos. One model (tools/atmo/generate-boards.mjs),
// one skin per page; 36 boards (320 x 50) on the perimeter wall line (a pitch border
// option, the invisible map railings keep the collision - PerimeterWall.cs). Every 9 s the next page wipes round the stadium board by
// board; a goal takes all boards over in the scorer's colour for 8 s.
public sealed partial class SoccerModMvpPlugin
{
    private const string AtmoBoardModel = "models/soccermod/atmo/led_board.vmdl";
    private const string AtmoBoardName = "sm2_atmo_board";
    // Skins (tools/atmo/generate-led-boards.mjs from render-board-pages.ps1): pages 0-9, then the
    // pixel-dissolve frames - page + 10 = 70 % of the LEDs lit, page + 20 = 30 %, 30 = all dark.
    // 2026-09-29 owner: real advertising look, brand - text - brand - text along the wall.
    private const int AtmoBoardBrands = 6, AtmoBoardTextFirst = 6, AtmoBoardTexts = 2;
    private const int AtmoBoardBanner = 5, AtmoBoardFairPlay = 6, AtmoBoardGoalRed = 8, AtmoBoardGoalBlue = 9;
    private const int AtmoBoardDissolve70 = 10, AtmoBoardDissolve30 = 20, AtmoBoardDark = 30;
    private const float AtmoBoardWidth = 320f;
    private const double AtmoBoardPageSeconds = 9.0, AtmoBoardTakeoverSeconds = 8.0, AtmoBoardFrameSeconds = 0.07;
    private readonly List<CDynamicProp> _atmoBoards = new();
    private readonly List<int> _atmoBoardShown = new();   // page each board shows now
    private int _atmoBoardPage;
    private int _atmoBoardHold = -1;                       // takeover / show page, -1 = rotation
    private double _atmoBoardTakeoverUntil;
    private int _atmoBoardGeneration;

    private bool AtmoBoardsWanted => AtmoOn && AtmoSet.LedBoards;

    private void AtmoBoardsOnLoad()
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            if (File.Exists(ConfigPath(AtmoFlagFile))) manifest.AddResource(AtmoBoardModel);
        });
        RegisterListener<Listeners.OnMapStart>(_ => AtmoBoardsMapStart());
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            _atmoBoards.Clear();
            Server.NextFrame(() => AtmoBoardsEnsure("round_start"));
            return HookResult.Continue;
        });
    }

    // Board spots in perimeter order (anticlockwise), so a page change runs round the stadium.
    // 2026-09-29 owner: the boards are a pitch border option (Settings - Stadium - Pitch border)
    // standing ON the perimeter wall line, where the invisible map railings keep the collision:
    // front face on the inner side of the black wall footprint (sides x +-1282 from |y| 129 to
    // 1665 - the halfway-line gap stays open - ends y +-1666). Each board is scaled to its share
    // of the run (sides 1536 / 5 = 307.2 u -> 0.96, ends 2560 / 8 = 320 u -> 1.0), so boards
    // meet edge to edge. 2026-09-29 owner: overlapping 320 u boards cut into each other's text.
    private const float AtmoBoardSideFaceX = 1278f, AtmoBoardEndFaceY = 1662f;
    private const float AtmoBoardSideScale = (1665f - 129f) / 5f / AtmoBoardWidth, AtmoBoardEndScale = 2560f / 8f / AtmoBoardWidth;
    // inset: how far the boards stand in front of the wall line. The arena has its own low boards
    // there (world geometry, the wall); on the same plane the two flickered (owner 2026-10-01).
    private const float ArenaBoardInset = 2f;
    private static IEnumerable<(Vector At, float Yaw, float Scale)> AtmoBoardSpots(float inset = 0f)
    {
        static IEnumerable<float> Centres(float from, float to, int count)
        {
            var width = (to - from) / count;
            for (var i = 0; i < count; i++) yield return from + width / 2 + i * width;
        }
        float Depth() => -inset;
        // Stadium (inset > 0): clean corners - the side boards end at the end boards' front face, the end
        // boards run across the side boards' depth (6 u), and the end boards sit lower by the height
        // difference of the two scales, so the top frames are level (owner 2026-10-01).
        var sideTo = inset > 0f ? AtmoBoardEndFaceY - inset : 1665f;
        var endHalf = inset > 0f ? AtmoBoardSideFaceX - inset + 6f : 1280f;
        var sideScale = inset > 0f ? (sideTo - 129f) / 5f / AtmoBoardWidth : AtmoBoardSideScale;
        var endScale = inset > 0f ? 2f * endHalf / 8f / AtmoBoardWidth : AtmoBoardEndScale;
        var endZ = StadiumPitchPlaneZ - (inset > 0f ? 50f * (endScale - sideScale) : 0f);
        // +x side, south to north (gap at the halfway line)
        foreach (var y in Centres(-sideTo, -129f, 5).Concat(Centres(129f, sideTo, 5)))
            yield return (new Vector(AtmoBoardSideFaceX + Depth(), y, StadiumPitchPlaneZ), 180f, sideScale);
        // +y end, east to west
        foreach (var x in Centres(-endHalf, endHalf, 8).Reverse())
            yield return (new Vector(x, AtmoBoardEndFaceY + Depth(), endZ), 270f, endScale);
        // -x side, north to south
        foreach (var y in Centres(-sideTo, -129f, 5).Concat(Centres(129f, sideTo, 5)).Reverse())
            yield return (new Vector(-AtmoBoardSideFaceX - Depth(), y, StadiumPitchPlaneZ), 0f, sideScale);
        // -y end, west to east
        foreach (var x in Centres(-endHalf, endHalf, 8))
            yield return (new Vector(x, -AtmoBoardEndFaceY - Depth(), endZ), 90f, endScale);
    }

    // For the pitch border choice (PerimeterWall.cs): the boards exist and can be shown.
    private bool AtmoBoardsShown => _atmoBoards.Count > 0 && _atmoBoards.All(b => b.IsValid);
    private IReadOnlyList<CDynamicProp> AtmoBoardEntities => _atmoBoards;

    private void AtmoBoardsEnsure(string reason)
    {
        if (!AtmoBoardsWanted)
        {
            foreach (var board in _atmoBoards) if (board.IsValid) board.Remove();
            _atmoBoards.Clear();
            _atmoBoardShown.Clear();
            return;
        }
        if (_atmoBoards.Count > 0 && _atmoBoards.All(b => b.IsValid)) return;
        foreach (var board in _atmoBoards) if (board.IsValid) board.Remove();
        _atmoBoards.Clear();
        _atmoBoardShown.Clear();
        var i = 0;
        foreach (var (at, yaw, scale) in AtmoBoardSpots(OnArena ? ArenaBoardInset : 0f))
        {
            var board = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (board is null || !board.IsValid) continue;
            using var kv = new CEntityKeyValues();
            kv.SetString("targetname", AtmoBoardName);
            kv.SetString("model", AtmoBoardModel);
            kv.SetInt("solid", 0);
            kv.SetInt("disableshadows", 1);
            kv.SetVector("origin", at);
            kv.SetAngle("angles", new QAngle(0, yaw, 0));
            board.DispatchSpawn(kv);
            if (!board.IsValid) continue;
            board.AcceptInput("DisableCollision");
            if (MathF.Abs(scale - 1f) > 0.001f) board.AcceptInput("SetScale", value: scale.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
            // a takeover / show page still running survives the round restart
            var page = AtmoBoardTarget(i++);
            board.AcceptInput("Skin", value: page.ToString());
            _atmoBoards.Add(board);
            _atmoBoardShown.Add(page);
        }
        Logger.LogInformation("[SM2DIAG] atmo_boards reason={Reason} spawned={Spawned}", reason, _atmoBoards.Count);
    }

    // Brand on the even boards, text on the odd ones - unless a takeover holds every board.
    private int AtmoBoardTarget(int i) => _atmoBoardHold >= 0 ? _atmoBoardHold
        : i % 2 == 0 ? _atmoBoardPage % AtmoBoardBrands : AtmoBoardTextFirst + _atmoBoardPage % AtmoBoardTexts;

    // Every board that changes runs the LED dissolve: old page -> 70 % -> 30 % -> dark -> 30 % ->
    // 70 % -> new page, board i starting i x wipe seconds after the first (runs round the stadium).
    private void AtmoBoardsShow(double wipeSeconds = 0.02)
    {
        var generation = ++_atmoBoardGeneration;   // a newer change cancels the rest of an older one
        for (var i = 0; i < _atmoBoards.Count && i < _atmoBoardShown.Count; i++)
        {
            var from = _atmoBoardShown[i]; var to = AtmoBoardTarget(i);
            if (from == to) continue;
            _atmoBoardShown[i] = to;
            var board = _atmoBoards[i];
            var frames = new[] { from + AtmoBoardDissolve70, from + AtmoBoardDissolve30, AtmoBoardDark, to + AtmoBoardDissolve30, to + AtmoBoardDissolve70, to };
            for (var f = 0; f < frames.Length; f++)
            {
                var skin = frames[f].ToString();
                AddTimer((float)(i * wipeSeconds + f * AtmoBoardFrameSeconds + 0.01), () =>
                {
                    if (generation == _atmoBoardGeneration && board.IsValid) board.AcceptInput("Skin", value: skin);
                }, TimerFlags.STOP_ON_MAPCHANGE);
            }
        }
    }

    private void AtmoBoardsNextPage()
    {
        if (_atmoBoards.Count == 0 || Server.TickedTime < _atmoBoardTakeoverUntil) return;
        _atmoBoardPage++;
        AtmoBoardsShow();
    }

    // From AtmoGoalShow: every board in the scorer's colour, then back to the ads.
    private void AtmoBoardsTakeover(bool red) => AtmoBoardsHold(red ? AtmoBoardGoalRed : AtmoBoardGoalBlue, AtmoBoardTakeoverSeconds, wipeSeconds: 0.005);

    // Show one page on every board for a while (rotation paused), optionally a second page after it.
    private void AtmoBoardsHold(int page, double seconds, int then = -1, double thenSeconds = 0, double wipeSeconds = 0.02)
    {
        if (_atmoBoards.Count == 0) return;
        var until = Server.TickedTime + seconds + (then >= 0 ? thenSeconds : 0);
        _atmoBoardTakeoverUntil = until;
        _atmoBoardHold = page;
        AtmoBoardsShow(wipeSeconds);
        if (then >= 0) AtmoLater(seconds, () => { if (_atmoBoardTakeoverUntil != until) return; _atmoBoardHold = then; AtmoBoardsShow(); });
        AtmoLater(until - Server.TickedTime, () =>
        {
            if (_atmoBoardTakeoverUntil != until) return;
            _atmoBoardHold = -1;
            _atmoBoardPage++;
            AtmoBoardsShow();
        });
    }

    // Map start (also run after a plugin hot reload, AtmoHotReload).
    private void AtmoBoardsMapStart()
    {
        _atmoBoards.Clear();
        _atmoBoardTakeoverUntil = 0;
        _atmoBoardHold = -1;
        _atmoBoardShown.Clear();
        AddTimer(1.0f, () => AtmoBoardsEnsure("map_start"), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer((float)AtmoBoardPageSeconds, AtmoBoardsNextPage, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }
}

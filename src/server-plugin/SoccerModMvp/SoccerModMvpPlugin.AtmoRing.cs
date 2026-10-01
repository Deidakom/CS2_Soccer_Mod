using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-29 "Arena Vision" light ring (v8 only, admin toggle "Light ring"):
// 80 self-lit LED strip segments round the roof fascia (|x| 1572 / |y| 1956,
// just under the roof screens), coloured through their render colour
// (tools/atmo/generate-ring.mjs). Idle: the leading team's colour (warm white
// on a draw), breathing faster and brighter with the director's hype. Goal:
// comets chase round in the scorer's colour; near miss / post / save: a white
// flash; kickoff: the ring fills from both ends. Updates at most 10 Hz and
// only for segments whose colour changed (idle refreshes at 2 Hz).
public sealed partial class SoccerModMvpPlugin
{
    private const string AtmoRingModelV8 = "models/soccermod/atmo/light_ring.vmdl";
    private static string AtmoRingModel => ArenaLoading ? ArenaRingModel : AtmoRingModelV8;   // ArenaLayout.cs
    private const string AtmoRingName = "sm2_atmo_ring";
    // 2026-09-29 owner: the ring looked like it floated in the air - it hung in front of the
    // recessed part of the roof edge. Now a 14 u LED band flush (0.5 u proud) on the flat front
    // strip of the fascia, measured from the map: z 880-896 at |y| 1948 (ends) / |x| 1564 (sides).
    private const float AtmoRingZ = 888f;
    private static readonly Color AtmoRingRed = Color.FromArgb(255, 235, 40, 30);
    private static readonly Color AtmoRingBlue = Color.FromArgb(255, 40, 105, 250);
    private static readonly Color AtmoRingNeutral = Color.FromArgb(255, 255, 215, 150);
    private enum AtmoRingMode { Idle, Chase, Flash, Sweep, Solid, Split }

    private readonly List<CDynamicProp> _atmoRing = new();
    private readonly List<Color> _atmoRingShown = new();
    private readonly List<float> _atmoRingY = new();
    private AtmoRingMode _atmoRingMode = AtmoRingMode.Idle;
    private Color _atmoRingColour = AtmoRingNeutral;
    private double _atmoRingModeStart, _atmoRingModeUntil;
    private int _atmoRingTick;

    private bool AtmoRingWanted => AtmoOn && AtmoSet.LightRing;

    private void AtmoRingOnLoad()
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            if (File.Exists(ConfigPath(AtmoFlagFile))) manifest.AddResource(AtmoRingModel);
        });
        RegisterListener<Listeners.OnMapStart>(_ => AtmoRingMapStart());
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            _atmoRing.Clear();
            _atmoRingShown.Clear();
            _atmoRingY.Clear();
            Server.NextFrame(() => AtmoRingEnsure("round_start"));
            return HookResult.Continue;
        });
    }

    // Segment spots in perimeter order (anticlockwise from the +x side), facing the pitch.
    // 2026-09-29 owner: the ring stops at the clock pill on each end (housing x +-228 on the
    // fascia) and carries on past it - each end half (x 236..1564) gets 8 segments of 166 u,
    // scaled to fit (AtmoRingEndScale).
    private const float AtmoRingPillHalf = 236f, AtmoRingEndSegment = (1564f - AtmoRingPillHalf) / 8f, AtmoRingEndScale = AtmoRingEndSegment / (2 * 1564f / 18f);
    private static IEnumerable<(Vector At, float Yaw, float Scale)> AtmoRingSpots() => ArenaLoading ? ArenaRingSpots() : AtmoRingSpotsV8();
    private static IEnumerable<(Vector At, float Yaw, float Scale)> AtmoRingSpotsV8()
    {
        const float fx = 1563.5f, fy = 1947.5f;
        const int sideCount = 22;
        float Step(int i, int n, float half) => -half + (i + 0.5f) * (2 * half / n);
        // the end's x positions from +x to -x, skipping the pill
        IEnumerable<float> EndXs()
        {
            for (var i = 0; i < 8; i++) yield return 1564f - (i + 0.5f) * AtmoRingEndSegment;
            for (var i = 0; i < 8; i++) yield return -AtmoRingPillHalf - (i + 0.5f) * AtmoRingEndSegment;
        }
        for (var i = 0; i < sideCount; i++) yield return (new Vector(fx, Step(i, sideCount, 1948f), AtmoRingZ), 180f, 1f);
        foreach (var x in EndXs()) yield return (new Vector(x, fy, AtmoRingZ), 270f, AtmoRingEndScale);
        for (var i = 0; i < sideCount; i++) yield return (new Vector(-fx, -Step(i, sideCount, 1948f), AtmoRingZ), 0f, 1f);
        foreach (var x in EndXs()) yield return (new Vector(-x, -fy, AtmoRingZ), 90f, AtmoRingEndScale);
    }

    private void AtmoRingEnsure(string reason)
    {
        if (!AtmoRingWanted)
        {
            foreach (var seg in _atmoRing) if (seg.IsValid) seg.Remove();
            _atmoRing.Clear();
            _atmoRingShown.Clear();
            _atmoRingY.Clear();
            return;
        }
        if (_atmoRing.Count > 0 && _atmoRing.All(s => s.IsValid)) return;
        foreach (var seg in _atmoRing) if (seg.IsValid) seg.Remove();
        _atmoRing.Clear();
        _atmoRingShown.Clear();
        _atmoRingY.Clear();
        foreach (var (at, yaw, scale) in AtmoRingSpots())
        {
            var seg = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (seg is null || !seg.IsValid) continue;
            using var kv = new CEntityKeyValues();
            kv.SetString("targetname", AtmoRingName);
            kv.SetString("model", AtmoRingModel);
            kv.SetInt("solid", 0);
            kv.SetInt("disableshadows", 1);
            kv.SetVector("origin", at);
            kv.SetAngle("angles", new QAngle(0, yaw, 0));
            seg.DispatchSpawn(kv);
            if (!seg.IsValid) continue;
            seg.AcceptInput("DisableCollision");
            if (MathF.Abs(scale - 1f) > 0.001f) seg.AcceptInput("SetScale", value: scale.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
            _atmoRing.Add(seg);
            _atmoRingShown.Add(Color.Empty);
            _atmoRingY.Add(at.Y);
        }
        Logger.LogInformation("[SM2DIAG] atmo_ring reason={Reason} segments={Count}", reason, _atmoRing.Count);
    }

    private void AtmoRingPlay(AtmoRingMode mode, Color colour, double seconds)
    {
        if (_atmoRing.Count == 0) return;
        // A flash never cuts a goal chase short.
        if (mode == AtmoRingMode.Flash && _atmoRingMode == AtmoRingMode.Chase && Server.TickedTime < _atmoRingModeUntil) return;
        _atmoRingMode = mode;
        _atmoRingColour = colour;
        _atmoRingModeStart = Server.TickedTime;
        _atmoRingModeUntil = Server.TickedTime + seconds;
    }

    private Color AtmoRingIdleColour() => _scoreT > _scoreCt ? AtmoRingRed : _scoreCt > _scoreT ? AtmoRingBlue : AtmoRingNeutral;

    private static Color AtmoRingScale(Color c, double k)
    {
        k = Math.Clamp(k, 0.0, 1.0);
        return Color.FromArgb(255, (int)(c.R * k), (int)(c.G * k), (int)(c.B * k));
    }

    private void AtmoRingUpdate()
    {
        if (_atmoRing.Count == 0) return;
        var now = (double)Server.TickedTime;
        if (_atmoRingMode != AtmoRingMode.Idle && now >= _atmoRingModeUntil) _atmoRingMode = AtmoRingMode.Idle;
        _atmoRingTick++;
        if (_atmoRingMode == AtmoRingMode.Idle && _atmoRingTick % 5 != 0) return;
        var t = now - _atmoRingModeStart;
        var n = _atmoRing.Count;
        for (var i = 0; i < n; i++)
        {
            Color c;
            switch (_atmoRingMode)
            {
                case AtmoRingMode.Chase:
                {
                    // 4 comets, one lap every 2 s, tails of 8 segments.
                    var head = t * n / 2.0;
                    var best = 0.2;
                    for (var k = 0; k < 4; k++)
                    {
                        var d = ((head + k * n / 4.0 - i) % n + n) % n;
                        if (d < 8) best = Math.Max(best, 1.0 - d / 8.0);
                    }
                    c = AtmoRingScale(_atmoRingColour, best);
                    break;
                }
                case AtmoRingMode.Split:
                    // red fans at +y, blue at -y; the halfway line ends stay white
                    c = Math.Abs(_atmoRingY[i]) < 120f ? Color.White : _atmoRingY[i] > 0 ? AtmoRingRed : AtmoRingBlue;
                    break;
                case AtmoRingMode.Solid:
                    c = _atmoRingColour;
                    break;
                case AtmoRingMode.Flash:
                    c = AtmoRingScale(Color.White, t < 0.3 ? 1.0 : 0.4);
                    break;
                case AtmoRingMode.Sweep:
                {
                    // Fills from segment 0 and n/2 both ways over 1.5 s.
                    var d = Math.Min(Math.Min(i, n - i), Math.Abs(i - n / 2));
                    c = d <= t / 1.5 * (n / 4.0) ? _atmoRingColour : Color.FromArgb(255, 10, 10, 10);
                    break;
                }
                default:
                {
                    var hype = Math.Clamp(_atmoHype / 100.0, 0.0, 1.0);
                    var period = 4.0 - 2.5 * hype;
                    var breath = 0.5 + 0.5 * Math.Sin(2 * Math.PI * now / period);
                    c = AtmoRingScale(AtmoRingIdleColour(), 0.5 + 0.25 * hype + 0.25 * breath * (0.4 + 0.6 * hype));
                    break;
                }
            }
            if (_atmoRingShown[i] == c || !_atmoRing[i].IsValid) continue;
            _atmoRingShown[i] = c;
            _atmoRing[i].Render = c;
            Utilities.SetStateChanged(_atmoRing[i], "CBaseModelEntity", "m_clrRender");
        }
    }

    // From AtmoFire: every moment the director sees.
    private void AtmoRingMoment(AtmoMoment moment)
    {
        if (!AtmoSet.LightRing) return;
        switch (moment)
        {
            case AtmoMoment.NearMiss or AtmoMoment.Post or AtmoMoment.Save:
                AtmoRingPlay(AtmoRingMode.Flash, Color.White, 0.8);
                break;
            case AtmoMoment.Kickoff or AtmoMoment.MatchStart:
                AtmoRingPlay(AtmoRingMode.Sweep, AtmoRingIdleColour(), 1.8);
                break;
        }
    }

    // Map start (also run after a plugin hot reload, AtmoHotReload).
    private void AtmoRingMapStart()
    {
        _atmoRing.Clear();
        _atmoRingShown.Clear();
        _atmoRingY.Clear();
        _atmoRingMode = AtmoRingMode.Idle;
        AddTimer(1.0f, () => AtmoRingEnsure("map_start"), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(0.1f, AtmoRingUpdate, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }
}

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-29 "Arena Vision" dugouts (v8 only, admin toggle "Dugouts" in Stadium effects -
// Stands). Owner: "on these benches I want players sitting and waiting, and a coach standing
// on either side watching the game". Models from tools/atmo/generate-dugouts.mjs: six seated
// substitutes per bench (map coordinates, red in the north dugout y 212..470, blue in the
// south one), and one coach per team standing in front of his dugout, behind the LED boards
// (owner 2026-09-29: not on the pitch). The coaches turn to follow the ball;
// on a goal the scoring team's bench and coach celebrate. No collision anywhere.
public sealed partial class SoccerModMvpPlugin
{
    private static readonly (string Model, Vector At)[] AtmoDugoutProps =
    {
        ("models/soccermod/atmo/dugout_subs_red.vmdl", new Vector(0, 0, 0)),
        ("models/soccermod/atmo/dugout_subs_blue.vmdl", new Vector(0, 0, 0)),
        ("models/soccermod/atmo/coach_red.vmdl", new Vector(-1330f, 340f, StadiumPitchPlaneZ)),
        ("models/soccermod/atmo/coach_blue.vmdl", new Vector(-1330f, -340f, StadiumPitchPlaneZ)),
    };
    // 2026-10-01 owner: on the stadium the bench and the coach look like the fans - cut-out cards from
    // the fan atlas (tools/arena/generate-arena-bench.mjs), same places and clips. Used when the
    // Workshop item mounted on the server has them; otherwise the 3D figures above stay.
    private static readonly string[] ArenaDugoutModels =
    {
        "models/soccermod/atmo/crowd_arena/bench_red.vmdl", "models/soccermod/atmo/crowd_arena/bench_blue.vmdl",
        "models/soccermod/atmo/crowd_arena/coach_red.vmdl", "models/soccermod/atmo/crowd_arena/coach_blue.vmdl",
    };
    private bool _arenaDugoutModels;
    private string AtmoDugoutModel(int i) => _arenaDugoutModels ? ArenaDugoutModels[i] : AtmoDugoutProps[i].Model;
    private const string AtmoDugoutName = "sm2_atmo_dugout";
    private const float AtmoCoachMaxTurn = 80f;
    private readonly CDynamicProp?[] _atmoDugouts = new CDynamicProp?[4];
    private readonly float[] _atmoCoachYaw = new float[2];
    private readonly double[] _atmoDugoutCheerUntil = new double[2];   // 0 red, 1 blue

    private bool AtmoDugoutsWanted => AtmoOn && !OnHall && AtmoSet.Dugouts;

    // 2026-10-01 owner: "an option to toggle on/off the bench + coach" - per player, !menu -
    // Settings - Stadium; players who switched it off are not sent the four props.
    private const string AtmoDugoutPrefsFile = "soccermod_dugout_prefs.json";
    private HashSet<ulong> _atmoDugoutsHiddenFor = new();
    private bool AtmoDugoutsShownFor(CCSPlayerController p) => !_atmoDugoutsHiddenFor.Contains(SteamIdOf(p));

    private void ToggleAtmoDugoutsFor(CCSPlayerController p)
    {
        var id = SteamIdOf(p);
        if (id == 0) return;
        if (!_atmoDugoutsHiddenFor.Remove(id)) _atmoDugoutsHiddenFor.Add(id);
        SaveJsonAtomic(AtmoDugoutPrefsFile, _atmoDugoutsHiddenFor.ToList());
    }

    private void AtmoDugoutsCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (_atmoDugoutsHiddenFor.Count == 0) return;
        foreach ((CCheckTransmitInfo info, CCSPlayerController? receiver) in infoList)
        {
            if (receiver is not { IsValid: true } || AtmoDugoutsShownFor(receiver)) continue;
            foreach (var prop in _atmoDugouts) if (prop is { IsValid: true }) info.TransmitEntities.Remove(prop);
        }
    }

    private void AtmoDugoutsOnLoad()
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            if (!File.Exists(ConfigPath(AtmoFlagFile))) return;
            _arenaDugoutModels = ArenaLoading && MountedAddonFiles().Contains(ArenaDugoutModels[0] + "_c");
            for (var i = 0; i < AtmoDugoutProps.Length; i++) manifest.AddResource(AtmoDugoutModel(i));
        });
        _atmoDugoutsHiddenFor = (LoadJsonOrNull<List<ulong>>(AtmoDugoutPrefsFile) ?? new()).ToHashSet();
        RegisterListener<Listeners.CheckTransmit>(AtmoDugoutsCheckTransmit);
        RegisterListener<Listeners.OnMapStart>(_ => AtmoDugoutsMapStart());
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            Array.Clear(_atmoDugouts);
            Server.NextFrame(() => AtmoDugoutsEnsure("round_start"));
            return HookResult.Continue;
        });
    }

    private void AtmoDugoutsMapStart()
    {
        Array.Clear(_atmoDugouts);
        Array.Clear(_atmoCoachYaw);
        Array.Clear(_atmoDugoutCheerUntil);
        AddTimer(1.0f, () => AtmoDugoutsEnsure("map_start"), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(5.0f, () => AtmoDugoutsEnsure("periodic"), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void AtmoDugoutsEnsure(string reason)
    {
        if (!AtmoDugoutsWanted)
        {
            foreach (var prop in _atmoDugouts) if (prop is { IsValid: true }) prop.Remove();
            Array.Clear(_atmoDugouts);
            return;
        }
        var spawned = 0;
        for (var i = 0; i < AtmoDugoutProps.Length; i++)
        {
            if (_atmoDugouts[i] is { IsValid: true }) continue;
            var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (prop is null || !prop.IsValid) continue;
            using var kv = new CEntityKeyValues();
            kv.SetString("targetname", AtmoDugoutName);
            kv.SetString("model", AtmoDugoutModel(i));
            kv.SetString("DefaultAnim", "idle");
            kv.SetInt("solid", 0);
            kv.SetVector("origin", AtmoDugoutProps[i].At);
            kv.SetAngle("angles", new QAngle(0, i >= 2 ? _atmoCoachYaw[i - 2] : 0, 0));
            prop.DispatchSpawn(kv);
            if (!prop.IsValid) continue;
            prop.AcceptInput("DisableCollision");
            var team = i % 2;   // 0 red, 1 blue
            prop.AcceptInput("SetAnimation", value: Server.TickedTime < _atmoDugoutCheerUntil[team] ? "celebrate" : "idle");
            _atmoDugouts[i] = prop;
            spawned++;
        }
        if (spawned > 0) Logger.LogInformation("[SM2DIAG] atmo_dugouts reason={Reason} spawned={Spawned}", reason, spawned);
    }

    // From AtmoGoalShow: the scoring team's subs jump up, its coach pumps his fist.
    private void AtmoDugoutsGoal(bool red, double seconds = 8.0)
    {
        var team = red ? 0 : 1;
        var until = Server.TickedTime + seconds;
        _atmoDugoutCheerUntil[team] = until;
        foreach (var i in new[] { team, team + 2 })
            if (_atmoDugouts[i] is { IsValid: true } prop) prop.AcceptInput("SetAnimation", value: "celebrate");
        AtmoLater(seconds, () =>
        {
            if (_atmoDugoutCheerUntil[team] != until) return;
            foreach (var i in new[] { team, team + 2 })
                if (_atmoDugouts[i] is { IsValid: true } prop) prop.AcceptInput("SetAnimation", value: "idle");
        });
    }

    // Every tick (cheap: two teleports): the coaches turn towards the ball, eased, at most
    // AtmoCoachMaxTurn degrees away from facing the pitch (+x).
    private void AtmoDugoutsOnTick()
    {
        if (_ball is not { IsValid: true } ball || ball.AbsOrigin is not { } target) return;
        for (var c = 0; c < 2; c++)
        {
            if (_atmoDugouts[c + 2] is not { IsValid: true } coach) continue;
            var at = AtmoDugoutProps[c + 2].At;
            var want = Math.Clamp(MathF.Atan2(target.Y - at.Y, target.X - at.X) * 180f / MathF.PI, -AtmoCoachMaxTurn, AtmoCoachMaxTurn);
            var yaw = _atmoCoachYaw[c] + (want - _atmoCoachYaw[c]) * 0.08f;
            if (MathF.Abs(yaw - _atmoCoachYaw[c]) < 0.05f) continue;
            _atmoCoachYaw[c] = yaw;
            coach.Teleport(null, new QAngle(0, yaw, 0), null);
        }
    }
}

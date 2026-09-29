using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-29 "Arena Vision" crowd (v8 only, admin toggle "Crowd"): the whole
// stadium, ~12,000 fan cards in 8 sections (tools/atmo/generate-crowd-stadium.mjs,
// geometry in map coordinates, so every prop sits at the origin). Owner: red
// fans behind the red goal (+y), blue behind the away goal (-y), the sides
// mixed like in real life. Idle bob; the scoring end cheers (the sides join
// in); a Mexican wave runs round the stadium section by section. Crowd fill
// (owner): full is the default, dynamic fills with the player count - 1 player:
// lower-tier ends, 2-5: the whole lower tier, 6+: everything.
public sealed partial class SoccerModMvpPlugin
{
    private const string AtmoCrowdDir = "models/soccermod/atmo/crowd/";
    // Order = Mexican wave order round the stadium (anticlockwise from the +x side).
    private static readonly (string Name, int End, bool Upper)[] AtmoCrowdSections =
    {
        ("side_east_lower", -1, false), ("side_east_upper", -1, true),
        ("end_red_lower", 0, false), ("end_red_upper", 0, true),
        ("side_west_lower", -1, false), ("side_west_upper", -1, true),
        ("end_blue_lower", 1, false), ("end_blue_upper", 1, true),
    };
    private readonly CDynamicProp?[] _atmoCrowds = new CDynamicProp?[AtmoCrowdSections.Length];
    private readonly double[] _atmoCrowdCheerUntil = new double[AtmoCrowdSections.Length];
    private readonly string?[] _atmoCrowdCheerClip = new string?[AtmoCrowdSections.Length];   // clip a respawn resumes

    private bool AtmoCrowdSectionWanted(int i)
    {
        if (!AtmoOn || !AtmoSet.Crowd) return false;
        if (!AtmoSet.CrowdFillDynamic) return true;
        var players = Utilities.GetPlayers().Count(p => p.IsValid && !p.IsBot);
        var (_, end, upper) = AtmoCrowdSections[i];
        return players >= 6 || players >= 2 && !upper || players >= 1 && !upper && end >= 0;
    }

    private void AtmoCrowdOnLoad()
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            if (!File.Exists(ConfigPath(AtmoFlagFile))) return;
            foreach (var s in AtmoCrowdSections) manifest.AddResource(AtmoCrowdDir + s.Name + ".vmdl");
        });
        RegisterListener<Listeners.OnMapStart>(_ => AtmoCrowdMapStart());
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            Array.Clear(_atmoCrowds);
            Server.NextFrame(() => AtmoCrowdEnsure("round_start"));
            return HookResult.Continue;
        });
    }

    private void AtmoCrowdEnsure(string reason)
    {
        var spawned = 0;
        for (var i = 0; i < AtmoCrowdSections.Length; i++)
        {
            if (!AtmoCrowdSectionWanted(i))
            {
                if (_atmoCrowds[i] is { IsValid: true } old) old.Remove();
                _atmoCrowds[i] = null;
                continue;
            }
            if (_atmoCrowds[i] is { IsValid: true }) continue;
            var crowd = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (crowd is null || !crowd.IsValid) continue;
            using var kv = new CEntityKeyValues();
            kv.SetString("targetname", "sm2_atmo_crowd");
            kv.SetString("model", AtmoCrowdDir + AtmoCrowdSections[i].Name + ".vmdl");
            kv.SetString("DefaultAnim", "idle");
            kv.SetInt("solid", 0);
            kv.SetInt("disableshadows", 1);
            kv.SetVector("origin", new Vector(0, 0, 0));
            kv.SetAngle("angles", new QAngle(0, 0, 0));
            crowd.DispatchSpawn(kv);
            if (!crowd.IsValid) continue;
            crowd.AcceptInput("DisableCollision");
            crowd.AcceptInput("SetAnimation", value: Server.TickedTime < _atmoCrowdCheerUntil[i] ? _atmoCrowdCheerClip[i] ?? "cheer" : "idle");
            _atmoCrowds[i] = crowd;
            spawned++;
        }
        if (spawned > 0)
            Logger.LogInformation("[SM2DIAG] atmo_crowd reason={Reason} spawned={Spawned} active={Active}", reason, spawned, _atmoCrowds.Count(c => c is { IsValid: true }));
    }

    private void AtmoCrowdSet(int i, string anim, double seconds)
    {
        if (_atmoCrowds[i] is not { IsValid: true } crowd) return;
        crowd.AcceptInput("SetAnimation", value: anim);
        if (seconds <= 0) return;
        var until = Server.TickedTime + seconds;
        _atmoCrowdCheerUntil[i] = until;
        _atmoCrowdCheerClip[i] = anim;
        AtmoLater(seconds, () => { if (_atmoCrowdCheerUntil[i] == until && _atmoCrowds[i] is { IsValid: true } c) c.AcceptInput("SetAnimation", value: "idle"); });
    }

    // end: 0 red, 1 blue, -1 everyone. The neutral sides join in a little later.
    private void AtmoCrowdPlay(string anim, double seconds = 0, int end = -1)
    {
        if (anim == "wave") { AtmoCrowdWave(); return; }
        for (var i = 0; i < AtmoCrowdSections.Length; i++)
        {
            var sectionEnd = AtmoCrowdSections[i].End;
            if (end < 0 || sectionEnd == end) AtmoCrowdSet(i, anim, seconds);
            else if (sectionEnd < 0) { var index = i; AtmoLater(0.6, () => AtmoCrowdSet(index, anim, seconds * 0.6)); }
        }
    }

    // 2026-09-29 owner: "when CT scores the blue fans cheer even more, the same for T". The
    // goal_red / goal_blue clips (generate-crowd-stadium.mjs) run on every section at once: the
    // scoring team's end and its supporters on the sides jump twice as high, the neutral side
    // fans cheer, the other team's fans stand still. team: 0 red, 1 blue.
    private void AtmoCrowdGoal(int team, double seconds)
    {
        var clip = team == 0 ? "goal_red" : "goal_blue";
        for (var i = 0; i < AtmoCrowdSections.Length; i++) AtmoCrowdSet(i, clip, seconds);
    }

    // Mexican wave: each section's 2 s wave clip, started one after another round the stadium.
    private void AtmoCrowdWave(int laps = 1)
    {
        var step = 0;
        for (var lap = 0; lap < laps; lap++)
            for (var i = 0; i < AtmoCrowdSections.Length; i += 2, step++)
            {
                var index = i;
                AtmoLater(step * 1.1, () => { AtmoCrowdSet(index, "wave", 2.1); AtmoCrowdSet(index + 1, "wave", 2.1); });
            }
    }

    // Map start (also run after a plugin hot reload, AtmoHotReload).
    private void AtmoCrowdMapStart()
    {
        Array.Clear(_atmoCrowds);
        Array.Clear(_atmoCrowdCheerUntil);
        Array.Clear(_atmoCrowdCheerClip);
        AddTimer(1.0f, () => AtmoCrowdEnsure("map_start"), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(5.0f, () => AtmoCrowdEnsure("periodic"), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }
}

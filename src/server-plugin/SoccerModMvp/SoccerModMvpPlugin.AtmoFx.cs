using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-29 "Arena Vision": the goal pyro show, v8 only. Particles are ours
// (tools/atmo/generate-particles.mjs, CS2 textures only). Engine-lab finding:
// info_particle_system must get EffectName set on the entity, then spawn,
// teleport and "Start" - the effect_name keyvalue alone shows nothing.
// Stands measured from the v8 map: lower tier front row y = +-2074, z 107;
// red (T, home) fans stand behind +y, blue behind -y.
public sealed partial class SoccerModMvpPlugin
{
    private const string AtmoParticleDir = "particles/soccermod/atmo/";
    private static readonly CsTeam CsTeamRed = CsTeam.Terrorist;
    private static readonly CsTeam CsTeamBlue = CsTeam.CounterTerrorist;
    private static readonly string[] AtmoParticles =
    {
        "firework_red", "firework_blue", "firework_gold", "confetti_cannon_red", "confetti_cannon_blue",
        "confetti_rain_red", "confetti_rain_blue", "flare_red", "flare_blue", "camera_flashes",
    };
    private static readonly (float X, float Y)[] AtmoStandCentresV8 = { (1950f, 0f), (-1950f, 0f), (0f, 2350f), (0f, -2350f) };
    private (float X, float Y)[] AtmoStandCentres => OnArena ? ArenaStandCentres : AtmoStandCentresV8;   // ArenaLayout.cs

    private void AtmoFxPrecache(ResourceManifest manifest)
    {
        foreach (var name in AtmoParticles) manifest.AddResource(AtmoParticleDir + name + ".vpcf");
    }

    private CParticleSystem? AtmoParticle(string effect, Vector at, double lifeSeconds)
    {
        if (!AtmoOn) return null;   // queued steps must not play after the switch went off
        if (!effect.StartsWith("particles/", StringComparison.Ordinal)) effect = AtmoParticleDir + effect + ".vpcf";
        var fx = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
        if (fx is null || !fx.IsValid) return null;
        fx.EffectName = effect;
        fx.StartActive = true;
        fx.DispatchSpawn();
        fx.Teleport(at, new QAngle(0, 0, 0), new Vector(0, 0, 0));
        fx.AcceptInput("Start");
        fx.Entity!.Name = AtmoLabTargetName + "_fx";
        _atmoLabEntities.Add(fx);
        _atmoLabExpiry[fx] = Server.TickedTime + lifeSeconds;
        return fx;
    }

    private void AtmoLater(double seconds, Action run) => _atmoLabQueue.Add((Server.TickedTime + seconds, run));

    // goalSign: +1 = the goal at +y, -1 = the goal at -y.
    private void AtmoGoalShow(CsTeam team, int goalSign, int scorerSlot = -1)
    {
        if (!AtmoOn) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var red = team == CsTeamRed;
        AtmoCrowdGoal(red ? 0 : 1, 9.0);   // the scoring team's fans go wild, even with pyro off
        AtmoDugoutsGoal(red);
        if (AtmoSet.LedBoards) AtmoBoardsTakeover(red);
        if (AtmoSet.LightRing) AtmoRingPlay(AtmoRingMode.Chase, red ? AtmoRingRed : AtmoRingBlue, 7.0);
        AtmoGoalCelebration(scorerSlot, red, red ? 1 : -1, goalSign);
        if (!AtmoSet.Pyro) return;
        var colour = red ? "red" : "blue";
        var fanSign = red ? 1 : -1;
        for (var x = -1000f; x <= 1000f; x += 250f)
            AtmoParticle($"flare_{colour}", AtmoEndRowSpot(x, fanSign), 11.0);
        AtmoLater(0.1, () =>
        {
            AtmoParticle($"confetti_cannon_{colour}", AtmoCannonSpot(1f, fanSign), 10.0);
            AtmoParticle($"confetti_cannon_{colour}", AtmoCannonSpot(-1f, fanSign), 10.0);
        });
        AtmoLater(0.9, () => AtmoParticle($"confetti_rain_{colour}", new Vector(0f, goalSign * 1250f, 1050f), 14.0));
        if (AtmoSet.CameraFlashes)
            foreach (var (x, y) in AtmoStandCentres) AtmoParticle("camera_flashes", new Vector(x, y, 420f), 5.0);
        if (AtmoSet.Fireworks)
        {
            var spots = new[] { (1434f, 1300f), (-1434f, 1450f), (700f, 1600f), (-700f, 1350f), (0f, 1700f) };
            for (var i = 0; i < spots.Length; i++)
            {
                var (x, z) = spots[i];
                var effect = i == spots.Length - 1 ? "firework_gold" : $"firework_{colour}";
                AtmoLater(0.3 + i * 0.35, () => AtmoParticle(effect, new Vector(x, fanSign * 1818f, z), 5.0));
            }
        }
        Logger.LogInformation("[SM2DIAG] atmo_goal_show team={Team} fanEnd={Fan} goal={Goal} syncMs={Ms:F2}", colour, fanSign, goalSign, watch.Elapsed.TotalMilliseconds);
    }
}

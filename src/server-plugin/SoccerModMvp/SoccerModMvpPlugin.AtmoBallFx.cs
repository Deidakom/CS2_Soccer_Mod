using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;

// 2026-09-29 "Arena Vision" ball effects (v8 only, admin toggle "Ball effects"):
// a glow trail in the shooter's team colour for hard shots (parented to the
// ball, stops after 1 s so it never follows a rolling ball) and hot sparks
// where the ball hits a post or the crossbar. Particles: generate-particles.mjs.
public sealed partial class SoccerModMvpPlugin
{
    private const float AtmoTrailMinSpeed = 1700f;
    private CParticleSystem? _atmoTrail;

    private void AtmoBallFxPrecache(ResourceManifest manifest)
    {
        foreach (var name in new[] { "ball_trail_red", "ball_trail_blue", "post_sparks", "confetti_rain_gold", "flare_gold" })
            manifest.AddResource(AtmoParticleDir + name + ".vpcf");
    }

    // From AtmoKick.
    private void AtmoBallTrail(CsTeam team, float speed)
    {
        if (!AtmoOn || !AtmoSet.BallFx || speed < AtmoTrailMinSpeed || _ball is not { IsValid: true } ball || ball.AbsOrigin is not { } at) return;
        if (_atmoTrail is { IsValid: true } old) old.Remove();
        var fx = AtmoParticle(team == CsTeamRed ? "ball_trail_red" : "ball_trail_blue", new Vector(at.X, at.Y, at.Z), 1.8);
        if (fx is null) return;
        fx.AcceptInput("SetParent", ball, fx, "!activator");
        _atmoTrail = fx;
        AtmoLater(1.0, () => { if (fx.IsValid) fx.AcceptInput("Stop"); });
    }

    // From AtmoPost.
    private void AtmoPostSparks(Vector at)
    {
        if (!AtmoOn || !AtmoSet.BallFx) return;
        AtmoParticle("post_sparks", new Vector(at.X, at.Y, at.Z), 1.5);
    }
}

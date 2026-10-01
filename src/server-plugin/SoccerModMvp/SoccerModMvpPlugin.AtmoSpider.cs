using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-29 "Arena Vision" spidercam (v8 only, admin toggle "Spidercam"): a
// TV camera pod (tools/atmo/generate-spidercam.mjs) on 4 cables (CBeams) to
// the roof corners, gliding after the ball at 420+ units (always 200 above
// the ball, so it can never be hit) and aiming its lens at it. 32 Hz, 5
// entities moved; no collision.
public sealed partial class SoccerModMvpPlugin
{
    private const string AtmoSpiderModel = "models/soccermod/atmo/spidercam.vmdl";
    private Vector[] AtmoSpiderAnchors => OnArena ? ArenaSpiderAnchors : AtmoSpiderAnchorsV8;   // ArenaLayout.cs
    private static readonly Vector[] AtmoSpiderAnchorsV8 =
    {
        new(1434f, 1818f, 900f), new(-1434f, 1818f, 900f), new(-1434f, -1818f, 900f), new(1434f, -1818f, 900f),
    };
    private CDynamicProp? _atmoSpider;
    private readonly List<CBeam> _atmoSpiderCables = new();
    private Vector _atmoSpiderPos = new(0f, 0f, 520f);
    private int _atmoSpiderTick;
    private double _atmoSpiderHoldUntil;
    private List<(float Z, float Vz)>? _atmoSpiderProbe;
    private float _atmoSpiderProbePodZ, _atmoSpiderLastBallZ;

    private void AtmoSpiderOnLoad()
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            if (File.Exists(ConfigPath(AtmoFlagFile))) manifest.AddResource(AtmoSpiderModel);
        });
        RegisterListener<Listeners.OnMapStart>(_ => AtmoSpiderMapStart());
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            _atmoSpider = null;
            _atmoSpiderCables.Clear();
            Server.NextFrame(() => AtmoSpiderEnsure("round_start"));
            return HookResult.Continue;
        });
    }

    private void AtmoSpiderEnsure(string reason)
    {
        var want = AtmoOn && !OnHall && AtmoSet.Spidercam;
        if (!want || _atmoSpider is not { IsValid: true } || _atmoSpiderCables.Any(c => !c.IsValid))
        {
            if (_atmoSpider is { IsValid: true } old) old.Remove();
            foreach (var cable in _atmoSpiderCables) if (cable.IsValid) cable.Remove();
            _atmoSpider = null;
            _atmoSpiderCables.Clear();
        }
        if (!want || _atmoSpider is not null) return;
        var pod = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (pod is null || !pod.IsValid) return;
        using (var kv = new CEntityKeyValues())
        {
            kv.SetString("targetname", "sm2_atmo_spidercam");
            kv.SetString("model", AtmoSpiderModel);
            kv.SetInt("solid", 0);
            kv.SetVector("origin", _atmoSpiderPos);
            pod.DispatchSpawn(kv);
        }
        if (!pod.IsValid) return;
        pod.AcceptInput("DisableCollision");
        _atmoSpider = pod;
        var grey = Color.FromArgb(255, 95, 98, 105);
        foreach (var anchor in AtmoSpiderAnchors)
        {
            var beam = Utilities.CreateEntityByName<CBeam>("beam");
            if (beam is not { IsValid: true }) continue;
            beam.Render = grey; beam.Width = 2.2f; beam.EndWidth = 2.2f;
            beam.Teleport(_atmoSpiderPos);
            beam.EndPos.X = anchor.X; beam.EndPos.Y = anchor.Y; beam.EndPos.Z = anchor.Z;
            beam.DispatchSpawn();
            beam.Render = grey;
            beam.Entity!.Name = "sm2_atmo_cable";
            _atmoSpiderCables.Add(beam);
        }
        Logger.LogInformation("[SM2DIAG] atmo_spidercam reason={Reason} cables={Cables}", reason, _atmoSpiderCables.Count);
    }

    // Every tick from AtmoOnTick; moves at 32 Hz. The pod has no collision at all
    // (solid 0, DisableCollision, no physics hull in the model) - a ball that
    // reaches it flies straight through; on top of that it dodges: when the
    // ball comes up towards it, it climbs and swings away fast (owner 2026-09-29).
    private void AtmoSpiderOnTick()
    {
        AtmoSpiderProbeTick();
        if (++_atmoSpiderTick % 2 != 0 || _atmoSpider is not { IsValid: true } pod || !AtmoOn || !AtmoSet.Spidercam) return;
        if (_ball is not { IsValid: true } ball || ball.AbsOrigin is not { } b) return;
        if (Server.TickedTime < _atmoSpiderHoldUntil) return;
        // Hover a little towards the halfway line from the ball, high enough to never touch play.
        var target = new Vector(Math.Clamp(b.X * 0.85f, -1150f, 1150f), Math.Clamp(b.Y * 0.8f, -1350f, 1350f), MathF.Min(820f, MathF.Max(420f, b.Z + 200f)));
        var dt = 2f * Server.TickInterval;
        var k = MathF.Min(1f, dt * 1.6f);
        var maxSpeed = 900f;
        var hx = b.X - _atmoSpiderPos.X; var hy = b.Y - _atmoSpiderPos.Y;
        var horiz = MathF.Sqrt(hx * hx + hy * hy);
        // AbsVelocity reads 0 on the physics ball: rising speed from the height change instead
        var vz = (b.Z - _atmoSpiderLastBallZ) / dt;
        _atmoSpiderLastBallZ = b.Z;
        if (horiz < 450f && (vz > 150f || b.Z + 260f > _atmoSpiderPos.Z))
        {
            // dodge: up (below the roof) and sideways away from the ball
            var away = horiz > 1f ? 320f / horiz : 0f;
            target = new Vector(Math.Clamp(_atmoSpiderPos.X - hx * away, -1250f, 1250f), Math.Clamp(_atmoSpiderPos.Y - hy * away, -1450f, 1450f), MathF.Min(820f, MathF.Max(b.Z + 320f, _atmoSpiderPos.Z)));
            k = 1f;
            maxSpeed = 2600f;
        }
        var step = new Vector((target.X - _atmoSpiderPos.X) * k, (target.Y - _atmoSpiderPos.Y) * k, (target.Z - _atmoSpiderPos.Z) * k);
        var len = MathF.Sqrt(step.X * step.X + step.Y * step.Y + step.Z * step.Z);
        var max = maxSpeed * dt;
        if (len > max) { step.X *= max / len; step.Y *= max / len; step.Z *= max / len; }
        _atmoSpiderPos = new Vector(_atmoSpiderPos.X + step.X, _atmoSpiderPos.Y + step.Y, _atmoSpiderPos.Z + step.Z);
        var dx = b.X - _atmoSpiderPos.X; var dy = b.Y - _atmoSpiderPos.Y; var dz = b.Z - (_atmoSpiderPos.Z - 25f);
        var yaw = MathF.Atan2(dy, dx) * 180f / MathF.PI;
        var pitch = -MathF.Atan2(dz, MathF.Sqrt(dx * dx + dy * dy)) * 180f / MathF.PI;
        pod.Teleport(_atmoSpiderPos, new QAngle(pitch, yaw, 0), null);
        foreach (var cable in _atmoSpiderCables) if (cable.IsValid) cable.Teleport(_atmoSpiderPos);
    }

    // Admin test (css_sm2atmo spiderhit): hold the pod still, fire the ball straight up
    // into it and log whether it passes through untouched (gravity only, no bounce).
    private string AtmoSpiderHitTest()
    {
        if (_atmoSpider is not { IsValid: true } || _ball is not { IsValid: true } ball) return "no spidercam or ball";
        _atmoSpiderHoldUntil = Server.TickedTime + 3.0;
        var p = _atmoSpiderPos;
        UnfreezeBallForPlay("atmo_spider_hit_test");
        ball.Teleport(new Vector(p.X, p.Y, p.Z - 170f), null, new Vector(0f, 0f, 1100f));
        _atmoSpiderProbe = new();
        _atmoSpiderProbePodZ = p.Z;
        return $"fired at the pod ({p.X:F0}, {p.Y:F0}, {p.Z:F0})";
    }

    private void AtmoSpiderProbeTick()
    {
        if (_atmoSpiderProbe is not { } probe || _ball is not { IsValid: true } ball || ball.AbsOrigin is not { } o) return;
        probe.Add((o.Z, probe.Count > 0 ? (o.Z - probe[^1].Z) / Server.TickInterval : 0f));
        if (probe.Count < 40) return;
        _atmoSpiderProbe = null;
        var worst = 0f;
        for (var i = 1; i < probe.Count; i++) worst = MathF.Max(worst, MathF.Abs(probe[i].Vz - probe[i - 1].Vz));
        var through = probe.Any(s => s.Z > _atmoSpiderProbePodZ + 30f);
        Logger.LogInformation("[SM2DIAG] atmo_spider_hit_test podZ={Pod:F0} maxBallZ={Max:F0} passedThrough={Through} largestVzJumpPerTick={Jump:F1} startVz={V0:F0} endVz={V1:F0}",
            _atmoSpiderProbePodZ, probe.Max(s => s.Z), through, worst, probe[0].Vz, probe[^1].Vz);
    }

    // Map start (also run after a plugin hot reload, AtmoHotReload).
    private void AtmoSpiderMapStart()
    {
        _atmoSpider = null;
        _atmoSpiderCables.Clear();
        _atmoSpiderHoldUntil = 0;
        _atmoSpiderProbe = null;
        AddTimer(1.0f, () => AtmoSpiderEnsure("map_start"), TimerFlags.STOP_ON_MAPCHANGE);
    }
}

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-28 owner: goal nets that move in waves like real ones when the ball
// hits them. The map's nets (func_brush at (0, +-1425.5, 21) on
// soccer_cssl_stadium_v8) keep their collision but are no longer drawn; in
// their place the Feature Package model goal_net_dynamic
// (tools/net/generate-dynamic-net.mjs): the same net shape, skinned, with one
// baked wave animation per impact spot (back 5 x 3, roof 5, each side 2) and
// strength (soft/hard). The ball is followed every tick in the net's frame
// (origin = centre of the goal line on the floor, +y into the goal; the -y
// goal is turned 180 deg); when it touches a panel from inside moving into
// it, the animation of the nearest spot plays. Server flag file, v8 only;
// re-applied on round start and every 2 s (the round restart deletes it).
public sealed partial class SoccerModMvpPlugin
{
    private const string DynamicNetFlagFile = "soccermod_dynamic_net.enabled";
    private const string DynamicNetModel = "models/soccermod/stadium/goal_net_dynamic.vmdl";
    private const string DynamicNetTargetName = "sm2_goal_net";
    private const float DynamicNetBrushY = 1425.5f, DynamicNetBrushZ = 21.0f;
    // Net shape in the model frame (see the generator).
    private const float DynNetHalf = 128f, DynNetBot = 4f, DynNetTop = 101f, DynNetTopDepth = 47.5f, DynNetBotDepth = 83f;
    // Back panel: outward normal (0, 97, 35.5) and slant (bottom -> top) (0, -35.5, 97), length 103.29.
    private const float DynNetBackNy = 0.93910f, DynNetBackNz = 0.34369f, DynNetBackLen = 103.29f;
    private const float DynNetMinSpeed = 90f, DynNetHardSpeed = 450f, DynNetCooldown = 0.35f;

    private bool _dynamicNetPrecached;
    private readonly CDynamicProp?[] _dynamicNets = new CDynamicProp?[2];
    private readonly double[] _dynamicNetLastHit = new double[2];
    private readonly float[] _dynamicNetLastSpeed = new float[2];
    private Vector? _dynamicNetPrevPos;
    private Vector _dynamicNetPrevVel = new(0, 0, 0);
    private double _dynamicNetPrevTime;
    private int _dynamicNetHits;

    private void DynamicNetOnLoad()
    {
        AddCommand("css_sm2net", "Admin: dynamic goal net status, or play <anim> on both nets.", OnDynamicNetCommand);
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            _dynamicNetPrecached = false;
            if (!File.Exists(ConfigPath(DynamicNetFlagFile))) return;
            if (!MountedAddonFiles().Contains(DynamicNetModel + "_c"))
            {
                Logger.LogInformation("[SM2DIAG] dynamic_net_unavailable reason=model_not_in_mounted_workshop_items model={Model}", DynamicNetModel);
                return;
            }
            manifest.AddResource(DynamicNetModel);
            _dynamicNetPrecached = true;
        });
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            Array.Clear(_dynamicNets);
            _dynamicNetPrevPos = null;
            AddTimer(1.0f, () => DynamicNetEnsure("map_start"), TimerFlags.STOP_ON_MAPCHANGE);
            AddTimer(2.0f, () => DynamicNetEnsure("maintenance"), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        });
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            Server.NextFrame(() => DynamicNetEnsure("round_start"));
            return HookResult.Continue;
        });
        RegisterListener<Listeners.OnTick>(DynamicNetTick);
    }

    private void OnDynamicNetCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin")) return;
        var anim = command.ArgCount > 1 ? command.GetArg(1) : "";
        // Test shot without a player: css_sm2net shoot <speed> [x] [height]
        // [start] - the match ball from <start> units in front of the +y goal
        // line (default 150; negative = already inside the goal) straight at
        // its back net.
        if (anim == "shoot")
        {
            if (_ball is not { IsValid: true } shotBall)
            {
                command.ReplyToCommand("[SM] Dynamic net: no match ball.");
                return;
            }
            float Arg(int i, float fallback) => command.ArgCount > i && float.TryParse(command.GetArg(i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
            var speed = Arg(2, 900f);
            var from = new Vector(Arg(3, 0f), GoalFrameLineY - Arg(5, 150f), StadiumPitchPlaneZ + Arg(4, 40f));
            shotBall.Teleport(from, null, new Vector(0f, speed, 0f));
            command.ReplyToCommand($"[SM] Dynamic net: shot at {speed:F0} u/s from ({from.X:F0}, {from.Y:F0}, {from.Z:F0}).");
            return;
        }
        if (anim.Length > 0)
            foreach (var net in _dynamicNets) if (net is { IsValid: true }) net.AcceptInput("SetAnimation", value: anim);
        command.ReplyToCommand($"[SM] Dynamic net: flag={File.Exists(ConfigPath(DynamicNetFlagFile))} precached={_dynamicNetPrecached} nets={_dynamicNets.Count(n => n is { IsValid: true })} hits={_dynamicNetHits}{(anim.Length > 0 ? $" played={anim}" : "")} {DynamicNetSequenceText()}");
    }

    // Server-side animation state of both nets (sequence index, start time),
    // to check SetAnimation without a client.
    private string DynamicNetSequenceText()
    {
        var parts = new List<string>();
        foreach (var net in _dynamicNets)
        {
            if (net is not { IsValid: true } || net.CBodyComponent is not { } body)
            {
                parts.Add("-");
                continue;
            }
            var ctrl = new CBodyComponentBaseAnimGraph(body.Handle).AnimationController;
            var seq = Schema.GetSchemaValue<int>(ctrl.Handle, "CBaseAnimGraphController", "m_hSequence");
            var start = Schema.GetSchemaValue<float>(ctrl.Handle, "CBaseAnimGraphController", "m_flSeqStartTime");
            parts.Add($"seq={seq}@{start:F2}");
        }
        return $"now={Server.CurrentTime:F2} nets=[{string.Join(", ", parts)}]";
    }

    private void DynamicNetEnsure(string reason)
    {
        if (!_dynamicNetPrecached || !File.Exists(ConfigPath(DynamicNetFlagFile)) || !IsFoundationMap(_currentMapName)) return;
        // The map's net brushes: collision stays, only the drawing goes.
        var hidden = 0;
        foreach (var brush in Utilities.FindAllEntitiesByDesignerName<CBaseModelEntity>("func_brush"))
        {
            if (!brush.IsValid || brush.AbsOrigin is not { } o) continue;
            if (MathF.Abs(o.X) > 1f || MathF.Abs(MathF.Abs(o.Y) - DynamicNetBrushY) > 1f || MathF.Abs(o.Z - DynamicNetBrushZ) > 1f) continue;
            if ((brush.Effects & EffectNoDraw) != 0) continue;
            brush.Effects |= EffectNoDraw;
            Utilities.SetStateChanged(brush, "CBaseEntity", "m_fEffects");
            hidden++;
        }
        var spawned = 0;
        for (var i = 0; i < 2; i++)
        {
            if (_dynamicNets[i] is { IsValid: true }) continue;
            var side = i == 0 ? 1.0f : -1.0f;
            var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (prop is null || !prop.IsValid) return;
            using var keyValues = new CEntityKeyValues();
            keyValues.SetString("targetname", DynamicNetTargetName);
            keyValues.SetString("model", DynamicNetModel);
            keyValues.SetInt("solid", 0);
            keyValues.SetBool("use_animgraph", false);
            keyValues.SetString("DefaultAnim", "idle");
            keyValues.SetString("IdleAnimationLoopMode", "ANIM_LOOP_MODE_LOOPING");
            keyValues.SetVector("origin", new Vector(0.0f, side * GoalFrameLineY, StadiumPitchPlaneZ));
            keyValues.SetAngle("angles", new QAngle(0.0f, side > 0 ? 0.0f : 180.0f, 0.0f));
            prop.DispatchSpawn(keyValues);
            if (!prop.IsValid) continue;
            prop.Entity!.Name = DynamicNetTargetName;
            prop.AcceptInput("DisableCollision");
            _dynamicNets[i] = prop;
            spawned++;
        }
        if (hidden + spawned > 0)
            Logger.LogInformation("[SM2DIAG] dynamic_net_applied reason={Reason} brushes_hidden={Hidden} nets_spawned={Spawned}", reason, hidden, spawned);
    }

    private void DynamicNetTick()
    {
        if (_dynamicNets[0] is not { IsValid: true } && _dynamicNets[1] is not { IsValid: true }) return;
        if (_ball is not { IsValid: true } ball || ball.AbsOrigin is not { } origin)
        {
            _dynamicNetPrevPos = null;
            return;
        }
        var now = (double)Server.TickedTime;
        var pos = new Vector(origin.X, origin.Y, origin.Z);
        var dt = (float)(now - _dynamicNetPrevTime);
        if (_dynamicNetPrevPos is not { } prev || dt <= 0.0001f || dt > 0.5f)
        {
            _dynamicNetPrevPos = pos;
            _dynamicNetPrevTime = now;
            _dynamicNetPrevVel = new Vector(0, 0, 0);
            return;
        }
        var vel = new Vector((pos.X - prev.X) / dt, (pos.Y - prev.Y) / dt, (pos.Z - prev.Z) / dt);
        // A teleport (goal reset, respawn, test shot) is no hit: more than 80
        // units in one sample (a real ball moves at most ~50 per tick).
        var jump = MathF.Sqrt((pos.X - prev.X) * (pos.X - prev.X) + (pos.Y - prev.Y) * (pos.Y - prev.Y) + (pos.Z - prev.Z) * (pos.Z - prev.Z));
        if (jump > 80f)
        {
            _dynamicNetPrevPos = pos;
            _dynamicNetPrevTime = now;
            _dynamicNetPrevVel = new Vector(0, 0, 0);
            return;
        }
        for (var g = 0; g < 2; g++)
        {
            if (_dynamicNets[g] is not { IsValid: true } net) continue;
            var side = g == 0 ? 1.0f : -1.0f;
            if (DynamicNetSpot(side, pos, vel, _dynamicNetPrevVel) is not { } hit) continue;
            if (now - _dynamicNetLastHit[g] < DynNetCooldown && hit.Speed < _dynamicNetLastSpeed[g] * 1.5f) continue;
            _dynamicNetLastHit[g] = now;
            _dynamicNetLastSpeed[g] = hit.Speed;
            _dynamicNetHits++;
            net.AcceptInput("SetAnimation", value: hit.Anim);
            Logger.LogInformation("[SM2DIAG] dynamic_net_hit goal={Goal} anim={Anim} speed={Speed:F0}", g == 0 ? "+y" : "-y", hit.Anim, hit.Speed);
            AddTimer(0.1f, () => Logger.LogInformation("[SM2DIAG] dynamic_net_state {State}", DynamicNetSequenceText()), TimerFlags.STOP_ON_MAPCHANGE);
        }
        _dynamicNetPrevPos = pos;
        _dynamicNetPrevTime = now;
        _dynamicNetPrevVel = vel;
    }

    // The panel the ball presses into (the deepest contact) and the animation
    // of the nearest impact spot, or null. side = +1 / -1 for the goal end.
    private (string Anim, float Speed)? DynamicNetSpot(float side, Vector pos, Vector vel, Vector prevVel)
    {
        // Into the net frame: the -y goal is the +y one turned 180 deg.
        var x = side * pos.X;
        var y = side * pos.Y - GoalFrameLineY;
        var z = pos.Z - StadiumPitchPlaneZ;
        if (y <= 0.0f) return null; // still in front of the goal line
        float Vx(Vector v) => side * v.X;
        float Vy(Vector v) => side * v.Y;
        var radius = BallCollisionRadius;
        (char Key, float Depth, float U, float V, float Speed)? best = null;
        void Consider(char key, float d, float u, float v, float speedNow, float speedPrev)
        {
            // d: signed distance of the ball centre past the panel (outward +).
            if (d < -(radius + 4.0f) || d > radius * 0.5f || u < -0.05f || u > 1.05f || v < -0.05f || v > 1.05f) return;
            var speed = MathF.Max(speedNow, speedPrev);
            if (speed < DynNetMinSpeed) return;
            if (best is null || d > best.Value.Depth) best = (key, d, u, v, speed);
        }
        var across = (x + DynNetHalf) / (2.0f * DynNetHalf);
        // back: slanted, from (y 83, z 4) up to (y 47.5, z 101)
        var by = y - DynNetBotDepth;
        var bz = z - DynNetBot;
        Consider('b', by * DynNetBackNy + bz * DynNetBackNz, across, (-by * DynNetBackNz + bz * DynNetBackNy) / DynNetBackLen,
            Vy(vel) * DynNetBackNy + vel.Z * DynNetBackNz, Vy(prevVel) * DynNetBackNy + prevVel.Z * DynNetBackNz);
        // roof
        Consider('r', z - DynNetTop, across, y / DynNetTopDepth, vel.Z, prevVel.Z);
        // sides: trapezoids, front edge at the goal line, back edge along the back net
        var up = (z - DynNetBot) / (DynNetTop - DynNetBot);
        var depthAt = DynNetBotDepth + (DynNetTopDepth - DynNetBotDepth) * Math.Clamp(up, 0.0f, 1.0f);
        Consider('p', x - DynNetHalf, y / depthAt, up, Vx(vel), Vx(prevVel));
        Consider('n', -x - DynNetHalf, y / depthAt, up, -Vx(vel), -Vx(prevVel));
        if (best is not { } hit) return null;
        int col, row;
        switch (hit.Key)
        {
            case 'b':
                col = Math.Clamp((int)(hit.U * 5.0f), 0, 4);
                row = hit.V < 0.34f ? 0 : hit.V < 0.66f ? 1 : 2;
                break;
            case 'r':
                col = Math.Clamp((int)(hit.U * 5.0f), 0, 4);
                row = 0;
                break;
            default:
                col = 0;
                row = hit.V < 0.5f ? 0 : 1;
                break;
        }
        var strength = hit.Speed >= DynNetHardSpeed ? 'h' : 's';
        return ($"hit_{hit.Key}_{col}_{row}_{strength}", hit.Speed);
    }
}

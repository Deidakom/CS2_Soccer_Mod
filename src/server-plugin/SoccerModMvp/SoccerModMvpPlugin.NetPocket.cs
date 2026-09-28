using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-28 owner ("Variante 1"): the ball sinks into the back net like in
// real life and rolls back out. With the flag file (on top of the moving
// nets, DynamicNet.cs) the map's net brushes are turned off completely and
// the Feature Package model goal_net_collision takes their place (drawn with
// EF_NODRAW): roof and sides where the map had them, the back wall
// NetPocketShell units behind the visible net as the hard stop. Between the
// visible back net and that wall the plugin is the net: once a ball's
// surface passes the net from inside the goal it is braked like a damped
// spring (NetPocketOmega, NetPocketZeta, tighter near the frame) and pushed
// back out, and the "pk_" animation of that spot and speed wraps the net
// around it. Every playable ball (match, training, cannon) is handled. The
// constants match tools/net/generate-dynamic-net.mjs (POCKET).
// Review 2026-09-28: balls behind or above the goal (outside the shell) and
// balls that did not come through the net are left alone; the curve and the
// wall assist / ground bounce are held off while the net has the ball.
public sealed partial class SoccerModMvpPlugin
{
    private const string NetPocketFlagFile = "soccermod_net_pocket.enabled";
    private const string NetPocketCollisionModel = "models/soccermod/stadium/goal_net_collision.vmdl";
    private const string NetPocketCollisionName = "sm2_goal_net_collision";
    private const float NetPocketOmega = 40f, NetPocketZeta = 0.45f, NetPocketMaxOmega = 80f;
    private const float NetPocketEdge = 45f, NetPocketMinEdge = 0.3f, NetPocketShell = 28f;
    private const float NetPocketGrip = 6f; // 1/s: the net holds the ball's sideways motion
    private static readonly float[] NetPocketRows = { 0.19f, 0.42f, 0.66f, 0.89f };

    private bool _netPocketPrecached;
    private readonly CDynamicProp?[] _netPocketShells = new CDynamicProp?[2];
    private double _netPocketBusyUntil;
    private readonly HashSet<uint> _netPocketBrushesOff = new();

    private bool NetPocketActive => _netPocketPrecached && _dynamicNetPrecached
        && FlagFileOn(DynamicNetFlagFile) && FlagFileOn(NetPocketFlagFile);

    // True while (and shortly after) the plugin moves a ball in a net, so the
    // wall assist and the ground bounce do not add their own rebound on top.
    private bool NetPocketBusy => Server.TickedTime < _netPocketBusyUntil;

    private void NetPocketOnLoad()
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            _netPocketPrecached = false;
            if (!File.Exists(ConfigPath(NetPocketFlagFile))) return;
            if (!MountedAddonFiles().Contains(NetPocketCollisionModel + "_c"))
            {
                Logger.LogInformation("[SM2DIAG] net_pocket_unavailable reason=model_not_in_mounted_workshop_items model={Model}", NetPocketCollisionModel);
                return;
            }
            manifest.AddResource(NetPocketCollisionModel);
            _netPocketPrecached = true;
        });
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            Array.Clear(_netPocketShells);
            _netPocketBrushesOff.Clear();
            AddTimer(1.0f, () => NetPocketEnsure("map_start"), TimerFlags.STOP_ON_MAPCHANGE);
            AddTimer(2.0f, () => NetPocketEnsure("maintenance"), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        });
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            Server.NextFrame(() => NetPocketEnsure("round_start"));
            return HookResult.Continue;
        });
    }

    private void NetPocketEnsure(string reason)
    {
        if (!NetPocketActive || !IsFoundationMap(_currentMapName)) return;
        var disabled = 0;
        foreach (var brush in Utilities.FindAllEntitiesByDesignerName<CBaseModelEntity>("func_brush"))
        {
            if (!brush.IsValid || brush.AbsOrigin is not { } o) continue;
            if (MathF.Abs(o.X) > 1f || MathF.Abs(MathF.Abs(o.Y) - DynamicNetBrushY) > 1f || MathF.Abs(o.Z - DynamicNetBrushZ) > 1f) continue;
            if (!_netPocketBrushesOff.Add(brush.EntityHandle.Raw)) continue;
            // func_brush with Solidity "toggle": off = not solid (and not drawn).
            brush.AcceptInput("Disable");
            brush.AcceptInput("TurnOff");
            disabled++;
        }
        var spawned = 0;
        for (var i = 0; i < 2; i++)
        {
            if (_netPocketShells[i] is { IsValid: true }) continue;
            var side = i == 0 ? 1.0f : -1.0f;
            var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (prop is null || !prop.IsValid) return;
            using var keyValues = new CEntityKeyValues();
            keyValues.SetString("targetname", NetPocketCollisionName);
            keyValues.SetString("model", NetPocketCollisionModel);
            keyValues.SetInt("solid", 6);
            keyValues.SetVector("origin", new Vector(0.0f, side * GoalFrameLineY, StadiumPitchPlaneZ));
            keyValues.SetAngle("angles", new QAngle(0.0f, side > 0 ? 0.0f : 180.0f, 0.0f));
            prop.DispatchSpawn(keyValues);
            if (!prop.IsValid) continue;
            prop.Entity!.Name = NetPocketCollisionName;
            prop.Effects |= EffectNoDraw;
            Utilities.SetStateChanged(prop, "CBaseEntity", "m_fEffects");
            _netPocketShells[i] = prop;
            spawned++;
        }
        if (disabled + spawned > 0)
            Logger.LogInformation("[SM2DIAG] net_pocket_applied reason={Reason} brushes_disabled={Disabled} shells_spawned={Spawned}", reason, disabled, spawned);
    }

    // One tick of the net acting on one ball for goal g (side +1/-1).
    // Returns true while the ball is in that goal's back-net pocket.
    private bool NetPocketStep(int g, float side, CPhysicsPropMultiplayer ball, Vector pos, Vector vel, float dt, CDynamicProp net, NetBallTrack track)
    {
        if (_netPocketShells[g] is not { IsValid: true })
        {
            track.InPocket[g] = false;
            return false;
        }
        // net frame: origin on the goal line on the floor, +y into the goal
        var x = side * pos.X;
        var y = side * pos.Y - GoalFrameLineY;
        var z = pos.Z - StadiumPitchPlaneZ;
        var radius = BallCollisionRadius;
        var by = y - DynNetBotDepth;
        var bz = z - DynNetBot;
        var depth = by * DynNetBackNy + bz * DynNetBackNz + radius; // ball surface past the rest net
        var u = (x + DynNetHalf) / (2.0f * DynNetHalf);
        var v = (-by * DynNetBackNz + bz * DynNetBackNy) / DynNetBackLen;
        var inGoalArea = y > 0.0f && u >= 0.0f && u <= 1.0f && v >= 0.0f && v <= 1.05f;
        var previousDepth = track.LastDepth[g];
        track.LastDepth[g] = inGoalArea ? depth : -1f;
        // Past the shell = behind or above the goal, outside the net.
        if (!inGoalArea || depth <= 0.0f || depth > NetPocketShell + 3.0f)
        {
            track.InPocket[g] = false;
            return false;
        }
        // Only a ball that came through the net from inside the goal.
        if (!track.InPocket[g] && previousDepth > 0.0f) return false;
        // A kick this tick wins over the net.
        if (KnifeKickOwnsTick(ball)) return true;

        // local velocity: normal (out of the goal +) and the rest
        var lvx = side * vel.X;
        var lvy = side * vel.Y;
        var lvz = vel.Z;
        var vn = lvy * DynNetBackNy + lvz * DynNetBackNz;
        var tx = lvx;
        var ty = lvy - vn * DynNetBackNy;
        var tz = lvz - vn * DynNetBackNz;

        var edge = MathF.Min(MathF.Min(u, 1.0f - u) * 2.0f * DynNetHalf, MathF.Min(v, 1.0f - v) * DynNetBackLen);
        var f = Math.Clamp(edge / NetPocketEdge, NetPocketMinEdge, 1.0f);
        var omega = MathF.Min(NetPocketMaxOmega, NetPocketOmega / f);

        if (!track.InPocket[g])
        {
            track.InPocket[g] = true;
            if (vn >= DynNetMinSpeed)
            {
                var col = Math.Clamp((int)MathF.Round((u - 0.064f) / (0.872f / 8.0f)), 0, 8);
                var row = 0;
                for (var r = 1; r < NetPocketRows.Length; r++)
                    if (MathF.Abs(v - NetPocketRows[r]) < MathF.Abs(v - NetPocketRows[row])) row = r;
                var strength = vn < 650f ? 's' : vn < 1100f ? 'm' : 'h';
                var anim = $"pk_{col}_{row}_{strength}";
                net.AcceptInput("SetAnimation", value: anim);
                _dynamicNetLastHit[g] = Server.TickedTime;
                _dynamicNetLastSpeed[g] = vn;
                _dynamicNetHits++;
                Logger.LogInformation("[SM2DIAG] net_pocket_hit goal={Goal} anim={Anim} speed={Speed:F0} edge={Edge:F2}", g == 0 ? "+y" : "-y", anim, vn, f);
            }
        }

        // damped spring, implicit in the new velocity (stable at any stiffness)
        var newVn = (vn - dt * omega * omega * depth) / (1.0f + 2.0f * NetPocketZeta * omega * dt + omega * omega * dt * dt);
        var grip = MathF.Exp(-NetPocketGrip * dt);
        tx *= grip;
        ty *= grip;
        tz *= grip;
        var nlx = tx;
        var nly = ty + newVn * DynNetBackNy;
        var nlz = tz + newVn * DynNetBackNz;
        ball.Teleport(null, null, new Vector(side * nlx, side * nly, nlz));
        // The net owns the ball now: no curve, no wall assist on top.
        var contact = State(ball);
        contact.Curve = 0;
        contact.LastWall = Server.TickedTime;
        _netPocketBusyUntil = Server.TickedTime + 0.4;
        return true;
    }
}

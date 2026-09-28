using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-28 owner ("Variante 1"): the ball sinks into the goal net like in
// real life and rolls back out - the back net and (since the same evening,
// owner: "20 % more, and the side nets too", then 40 %) both side nets. With the flag
// file (on top of the moving nets, DynamicNet.cs) the map's net brushes are
// turned off completely and Feature Package models take their place (drawn
// with EF_NODRAW): goal_net_shell = roof + back wall NetPocketShell units
// behind the visible back net, goal_net_shell_side = one side wall each,
// which moves NetPocketSideShell units out while a ball is inside the goal
// and back onto the visible side net when none is (so a ball from outside
// still meets the side net where it is drawn). Between a visible net and its
// wall the plugin is the net: once a ball's surface passes the net from
// inside the goal it is braked like a damped spring (NetPocketOmega,
// NetPocketZeta, tighter near the frame) and pushed back out, and the
// "pk_"/"pks_" animation of that spot and speed wraps the net around it.
// Every playable ball (match, training, cannon) is handled. The constants
// match tools/net/generate-dynamic-net.mjs (POCKET, SIDE_POCKET).
public sealed partial class SoccerModMvpPlugin
{
    private const string NetPocketFlagFile = "soccermod_net_pocket.enabled";
    private const string NetPocketShellModel = "models/soccermod/stadium/goal_net_shell.vmdl";
    private const string NetPocketSideModel = "models/soccermod/stadium/goal_net_shell_side.vmdl";
    private const string NetPocketCollisionName = "sm2_goal_net_collision";
    private const float NetPocketOmega = 28.6f, NetPocketZeta = 0.45f, NetPocketMaxOmega = 57f;
    private const float NetPocketEdge = 45f, NetPocketMinEdge = 0.3f;
    private const float NetPocketShell = 39f, NetPocketSideShell = 28f;
    private const float NetPocketGrip = 6f; // 1/s: the net holds the ball's motion along it
    private static readonly float[] NetPocketRows = { 0.19f, 0.42f, 0.66f, 0.89f };
    private static readonly float[] NetPocketSideCols = { 0.3f, 0.55f, 0.8f };
    private static readonly float[] NetPocketSideRows = { 0.2f, 0.5f, 0.8f };

    private bool _netPocketPrecached;
    private readonly CDynamicProp?[] _netPocketShells = new CDynamicProp?[2];
    // side walls: [goal * 2 + (0 = -x, 1 = +x)]
    private readonly CDynamicProp?[] _netPocketSides = new CDynamicProp?[4];
    private readonly bool[] _netPocketSidesOut = new bool[2];
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
            var mounted = MountedAddonFiles();
            if (!mounted.Contains(NetPocketShellModel + "_c") || !mounted.Contains(NetPocketSideModel + "_c"))
            {
                Logger.LogInformation("[SM2DIAG] net_pocket_unavailable reason=model_not_in_mounted_workshop_items model={Model}", NetPocketShellModel);
                return;
            }
            manifest.AddResource(NetPocketShellModel);
            manifest.AddResource(NetPocketSideModel);
            _netPocketPrecached = true;
        });
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            Array.Clear(_netPocketShells);
            Array.Clear(_netPocketSides);
            Array.Clear(_netPocketSidesOut);
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
        for (var g = 0; g < 2; g++)
        {
            if (_netPocketShells[g] is not { IsValid: true })
            {
                _netPocketShells[g] = NetPocketSpawnWall(NetPocketShellModel, NetPocketWallOrigin(g, 0.0f), g);
                if (_netPocketShells[g] is not null) spawned++;
            }
            for (var s = 0; s < 2; s++)
            {
                var i = g * 2 + s;
                if (_netPocketSides[i] is { IsValid: true }) continue;
                _netPocketSides[i] = NetPocketSpawnWall(NetPocketSideModel, NetPocketWallOrigin(g, NetPocketSideX(s, _netPocketSidesOut[g])), g);
                if (_netPocketSides[i] is not null) spawned++;
            }
        }
        if (disabled + spawned > 0)
            Logger.LogInformation("[SM2DIAG] net_pocket_applied reason={Reason} brushes_disabled={Disabled} walls_spawned={Spawned}", reason, disabled, spawned);
    }

    // local x of a side wall: on the visible side net, or moved out
    private static float NetPocketSideX(int s, bool outside) => (s == 0 ? -1f : 1f) * (DynNetHalf + (outside ? NetPocketSideShell : 0f));

    // world origin of a wall with local x offset in goal g's frame
    private Vector NetPocketWallOrigin(int g, float localX)
    {
        var side = g == 0 ? 1.0f : -1.0f;
        return new Vector(side * localX, side * GoalFrameLineY, StadiumPitchPlaneZ);
    }

    private CDynamicProp? NetPocketSpawnWall(string model, Vector origin, int g)
    {
        var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (prop is null || !prop.IsValid) return null;
        using var keyValues = new CEntityKeyValues();
        keyValues.SetString("targetname", NetPocketCollisionName);
        keyValues.SetString("model", model);
        keyValues.SetInt("solid", 6);
        keyValues.SetVector("origin", origin);
        keyValues.SetAngle("angles", new QAngle(0.0f, g == 0 ? 0.0f : 180.0f, 0.0f));
        prop.DispatchSpawn(keyValues);
        if (!prop.IsValid) return null;
        prop.Entity!.Name = NetPocketCollisionName;
        prop.Effects |= EffectNoDraw;
        Utilities.SetStateChanged(prop, "CBaseEntity", "m_fEffects");
        return prop;
    }

    // Side walls out while any ball is inside goal g, back onto the net when
    // none is (DynamicNet.cs calls this once per tick per goal).
    private void NetPocketUpdateSides(int g, bool ballInside)
    {
        if (_netPocketSidesOut[g] == ballInside) return;
        _netPocketSidesOut[g] = ballInside;
        for (var s = 0; s < 2; s++)
        {
            if (_netPocketSides[g * 2 + s] is not { IsValid: true } wall) continue;
            wall.Teleport(NetPocketWallOrigin(g, NetPocketSideX(s, ballInside)), new QAngle(0.0f, g == 0 ? 0.0f : 180.0f, 0.0f), null);
        }
    }

    // One tick of the nets acting on one ball for goal g (side +1/-1).
    // Returns true while the ball is in one of that goal's pockets.
    private bool NetPocketStep(int g, float side, CPhysicsPropMultiplayer ball, Vector pos, Vector vel, float dt, CDynamicProp net, NetBallTrack track)
    {
        if (_netPocketShells[g] is not { IsValid: true })
        {
            for (var p = 0; p < 3; p++) track.InPocket[g * 3 + p] = false;
            return false;
        }
        // net frame: origin on the goal line on the floor, +y into the goal
        var x = side * pos.X;
        var y = side * pos.Y - GoalFrameLineY;
        var z = pos.Z - StadiumPitchPlaneZ;
        var radius = BallCollisionRadius;
        var lvx = side * vel.X;
        var lvy = side * vel.Y;
        var lvz = vel.Z;

        // candidates: 0 = back, 1 = side -x, 2 = side +x
        var best = -1;
        float bestDepth = 0f, bestU = 0f, bestV = 0f, nX = 0f, nY = 0f, nZ = 0f, bestEdge = 1f;
        for (var p = 0; p < 3; p++)
        {
            float depth, u, v, px, py, pz, edge, shell;
            if (p == 0)
            {
                var by = y - DynNetBotDepth;
                var bz = z - DynNetBot;
                depth = by * DynNetBackNy + bz * DynNetBackNz + radius;
                u = (x + DynNetHalf) / (2.0f * DynNetHalf);
                v = (-by * DynNetBackNz + bz * DynNetBackNy) / DynNetBackLen;
                (px, py, pz) = (0f, DynNetBackNy, DynNetBackNz);
                edge = MathF.Min(MathF.Min(u, 1.0f - u) * 2.0f * DynNetHalf, MathF.Min(v, 1.0f - v) * DynNetBackLen);
                shell = NetPocketShell;
            }
            else
            {
                var sx = p == 1 ? -1f : 1f;
                depth = sx * x - DynNetHalf + radius;
                v = (z - DynNetBot) / (DynNetTop - DynNetBot);
                var width = DynNetBotDepth + (DynNetTopDepth - DynNetBotDepth) * Math.Clamp(v, 0f, 1f);
                u = y / width;
                (px, py, pz) = (sx, 0f, 0f);
                edge = MathF.Min(MathF.Min(u, 1.0f - u) * width, MathF.Min(v, 1.0f - v) * (DynNetTop - DynNetBot));
                shell = NetPocketSideShell;
                if (!_netPocketSidesOut[g]) shell = 0f; // walls on the net: no room
            }
            var index = g * 3 + p;
            var inArea = y > 0.0f && u >= 0.0f && u <= 1.0f && v >= 0.0f && v <= 1.05f;
            var previous = track.LastDepth[index];
            track.LastDepth[index] = inArea ? depth : -1f;
            // Past the wall = outside the goal, not in the net.
            if (!inArea || depth <= 0.0f || shell <= 0f || depth > shell + 3.0f)
            {
                track.InPocket[index] = false;
                continue;
            }
            // Only a ball that came through the net from inside the goal.
            if (!track.InPocket[index] && previous > 0.0f) continue;
            if (best < 0 || depth > bestDepth)
            {
                best = p; bestDepth = depth; bestU = u; bestV = v; nX = px; nY = py; nZ = pz;
                bestEdge = Math.Clamp(edge / NetPocketEdge, NetPocketMinEdge, 1.0f);
            }
        }
        if (best < 0) return false;
        // A kick this tick wins over the net.
        if (KnifeKickOwnsTick(ball)) return true;

        var vn = lvx * nX + lvy * nY + lvz * nZ;
        var tx = lvx - vn * nX;
        var ty = lvy - vn * nY;
        var tz = lvz - vn * nZ;
        var omega = MathF.Min(NetPocketMaxOmega, NetPocketOmega / bestEdge);

        var pocketIndex = g * 3 + best;
        if (!track.InPocket[pocketIndex])
        {
            track.InPocket[pocketIndex] = true;
            if (vn >= DynNetMinSpeed)
            {
                var strength = vn < 650f ? 's' : vn < 1100f ? 'm' : 'h';
                string anim;
                if (best == 0)
                {
                    var col = Math.Clamp((int)MathF.Round((bestU - 0.064f) / (0.872f / 8.0f)), 0, 8);
                    anim = $"pk_{col}_{Nearest(NetPocketRows, bestV)}_{strength}";
                }
                else
                {
                    anim = $"pks_{(best == 1 ? 'n' : 'p')}_{Nearest(NetPocketSideCols, bestU)}_{Nearest(NetPocketSideRows, bestV)}_{strength}";
                }
                net.AcceptInput("SetAnimation", value: anim);
                _dynamicNetLastHit[g] = Server.TickedTime;
                _dynamicNetLastSpeed[g] = vn;
                _dynamicNetHits++;
                Logger.LogInformation("[SM2DIAG] net_pocket_hit goal={Goal} anim={Anim} speed={Speed:F0} edge={Edge:F2}", g == 0 ? "+y" : "-y", anim, vn, bestEdge);
            }
        }

        // damped spring, implicit in the new velocity (stable at any stiffness)
        var newVn = (vn - dt * omega * omega * bestDepth) / (1.0f + 2.0f * NetPocketZeta * omega * dt + omega * omega * dt * dt);
        var grip = MathF.Exp(-NetPocketGrip * dt);
        tx *= grip;
        ty *= grip;
        tz *= grip;
        var nlx = tx + newVn * nX;
        var nly = ty + newVn * nY;
        var nlz = tz + newVn * nZ;
        ball.Teleport(null, null, new Vector(side * nlx, side * nly, nlz));
        // The net owns the ball now: no curve, no wall assist on top.
        var contact = State(ball);
        contact.Curve = 0;
        contact.LastWall = Server.TickedTime;
        _netPocketBusyUntil = Server.TickedTime + 0.4;
        return true;
    }

    private static int Nearest(float[] values, float v)
    {
        var best = 0;
        for (var i = 1; i < values.Length; i++)
            if (MathF.Abs(v - values[i]) < MathF.Abs(v - values[best])) best = i;
        return best;
    }
}

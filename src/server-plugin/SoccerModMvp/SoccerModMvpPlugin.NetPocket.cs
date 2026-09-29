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
    // 2026-09-29 owner: "the pocket deeper and bigger the harder the shot".
    // Flag file soccermod_net_deep.enabled (read at map start, with the deep
    // models mounted): the *_deep net and shell models (generate-dynamic-net.mjs
    // --deep), back stop 95 u, sides 50 u, stiffness from the entry speed
    // (NetDeepOmega, same formula as the generator's deepOmega).
    private const string NetDeepFlagFile = "soccermod_net_deep.enabled";
    private bool _netDeep;
    private string NetPocketShellModel => _netDeep ? "models/soccermod/stadium/goal_net_shell_deep.vmdl" : "models/soccermod/stadium/goal_net_shell.vmdl";
    private string NetPocketSideModel => _netDeep ? "models/soccermod/stadium/goal_net_shell_deep_side.vmdl" : "models/soccermod/stadium/goal_net_shell_side.vmdl";
    private static float NetDeepTarget(float v) => v <= 650f ? 16f * v / 650f : v <= 1100f ? 16f + (v - 650f) / 450f * 34f : 50f + (v - 1100f) / 800f * 40f;
    // Measured 2026-09-29 on 27018 (net_pocket_depth): the tick integration with
    // the tangential grip reaches 0.63 of the generator's analytic peak at every
    // speed, so the plugin's spring is softer by that factor to hit the targets.
    private const float NetDeepCalibration = 0.63f;
    private static float NetDeepOmega(float v) => NetDeepCalibration * 0.522f * MathF.Max(v, 1f) / MathF.Max(NetDeepTarget(MathF.Max(v, 1f)), 0.01f);
    private const string NetPocketCollisionName = "sm2_goal_net_collision";
    private const float NetPocketOmega = 28.6f;
    private float NetPocketZeta => _netDeep ? 0.55f : 0.45f;
    private float NetPocketMaxOmega => _netDeep ? 70f : 57f;
    private const float NetPocketEdge = 45f, NetPocketMinEdge = 0.3f;
    private float NetPocketShell => _netDeep ? 95f : 39f;
    private float NetPocketSideShell => _netDeep ? 50f : 28f;
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
    // 2026-09-28 owner video: with the pocket the map's net brushes were off
    // for good and the only collision was the pocket walls (the back one 39 u
    // behind the visible net) - balls from outside bounced off thin air
    // behind the goal. Now per goal: no ball inside -> the map's net brushes
    // collide as without the pocket and the pocket walls are off; a ball that
    // came in through the mouth (DynamicNet.cs latch) -> brushes off, walls on.
    private readonly List<CBaseModelEntity> _netPocketBrushList = new();
    private readonly Dictionary<uint, Vector> _netPocketBrushHome = new(); // map origin of each net brush
    private readonly bool?[] _netPocketCollisionOn = new bool?[2];

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
            _netDeep = File.Exists(ConfigPath(NetDeepFlagFile)) && mounted.Contains("models/soccermod/stadium/goal_net_shell_deep.vmdl_c") && mounted.Contains("models/soccermod/stadium/goal_net_dynamic_deep.vmdl_c");
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
            _netPocketKey = null;
            _netPocketBrushesOff.Clear();
            _netPocketBrushList.Clear();
            _netPocketBrushHome.Clear();
            Array.Clear(_netPocketCollisionOn);
            AddTimer(1.0f, () => NetPocketEnsure("map_start"), TimerFlags.STOP_ON_MAPCHANGE);
            AddTimer(2.0f, () => NetPocketEnsure("maintenance"), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        });
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            Server.NextFrame(() => NetPocketEnsure("round_start"));
            return HookResult.Continue;
        });
    }

    private string? _netPocketKey;

    private void NetPocketRemove()
    {
        foreach (var wall in _netPocketShells.Concat(_netPocketSides)) if (wall is { IsValid: true }) wall.Remove();
        Array.Clear(_netPocketShells);
        Array.Clear(_netPocketSides);
        Array.Clear(_netPocketSidesOut);
        _netPocketKey = null;
    }

    private void NetPocketEnsure(string reason)
    {
        if (!NetPocketActive || NetGoalsHere is not { } goals) return;
        if (_netPocketKey != goals.Key) { NetPocketRemove(); _netPocketKey = goals.Key; }
        var disabled = 0;
        foreach (var brush in MapNetBrushes())
        {
            if (!_netPocketBrushesOff.Add(brush.EntityHandle.Raw)) continue;
            // func_brush with Solidity "toggle": off = not solid (and not drawn).
            brush.AcceptInput("Disable");
            _netPocketBrushList.Add(brush);
            if (brush.AbsOrigin is { } home) _netPocketBrushHome[brush.EntityHandle.Raw] = new Vector(home.X, home.Y, home.Z);
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
        if (disabled + spawned > 0) Array.Clear(_netPocketCollisionOn); // re-apply on the next tick
        if (disabled + spawned > 0)
            Logger.LogInformation("[SM2DIAG] net_pocket_applied reason={Reason} brushes_disabled={Disabled} walls_spawned={Spawned}", reason, disabled, spawned);
    }

    // local x of a side wall: on the visible side net, or moved out
    private float NetPocketSideX(int s, bool outside) => (s == 0 ? -1f : 1f) * (DynNetHalf + (outside ? NetPocketSideShell : 0f));

    // world origin of a wall with local x offset in goal g's frame
    private Vector NetPocketWallOrigin(int g, float localX)
    {
        var side = g == 0 ? 1.0f : -1.0f;
        return NetGoalsHere is { } goals ? NetGoalOrigin(goals, side, localX) : new Vector(side * localX, side * GoalFrameLineY, StadiumPitchPlaneZ);
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
        if (_netPocketCollisionOn[g] != ballInside) NetPocketSetCollision(g, ballInside);
        if (_netPocketSidesOut[g] == ballInside) return;
        _netPocketSidesOut[g] = ballInside;
        NetPocketPlace(g);
    }

    // Goal g's collision: the pocket walls (pocketOn) or the map's net brushes.
    private void NetPocketSetCollision(int g, bool pocketOn)
    {
        _netPocketCollisionOn[g] = pocketOn;
        var here = NetPocketWallOrigin(g, 0.0f);
        var other = NetPocketWallOrigin(1 - g, 0.0f);
        static float Dist2(Vector a, Vector b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
        _netPocketBrushList.RemoveAll(b => !b.IsValid);
        foreach (var brush in _netPocketBrushList)
        {
            // Which goal's net: by the world centre of the brush's collision box, not
            // its origin (the indoor map's net brushes have origin 0,0,0 with the
            // geometry in world coordinates).
            if (BrushCentre(brush) is not { } o || Dist2(o, here) > Dist2(o, other)) continue; // the other goal's net
            // The same two inputs that switched the brush off in NetPocketEnsure
            // (Disable alone left it solid after an Enable: shots from inside
            // bounced off the visible net, no pocket).
            brush.AcceptInput(pocketOn ? "Disable" : "Enable");
            brush.AcceptInput(pocketOn ? "TurnOff" : "TurnOn");
            // The inputs alone left the brush solid after an Enable (inside
            // shots bounced off the visible net, no pocket); like the pocket
            // walls it is parked below the map while the pocket is on.
            if (_netPocketBrushHome.TryGetValue(brush.EntityHandle.Raw, out var home))
                brush.Teleport(pocketOn ? new Vector(home.X, home.Y, home.Z + NetPocketParkZ) : home, null, null);
            // Enable shows the brush again; the dynamic net model is drawn instead.
            brush.Effects |= EffectNoDraw;
            Utilities.SetStateChanged(brush, "CBaseEntity", "m_fEffects");
        }
        // 2026-09-28 owner: a ball kicked at the back net from right behind it
        // got stuck and jittered between the net and the pocket shell 39 u
        // behind it - the DisableCollision input did not take the walls out of
        // the physics. Off = parked far below the map (a teleport always works).
        NetPocketPlace(g);
    }

    private const float NetPocketParkZ = -6000.0f;

    // 2026-09-29 owner: with a ball in the goal a player walked through the net
    // from inside - the pocket walls stand 28 u (sides) / 39 u (back) behind the
    // visible net so the ball can sink in, and the map's net brushes are off.
    // While goal g's pocket is on, players are kept on their side of the visible
    // net (in the goal or out of it; decided when they are clearly on one side).
    private readonly HashSet<int>[] _netPocketPlayersIn = { new(), new() };
    private const float NetPlayerHalf = 16f, NetPlayerHeight = 72f;

    private void NetPocketKeepPlayers(int g, float side, NetGoals goals)
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.PlayerPawn.Value is not { IsValid: true } pawn || pawn.LifeState != 0 || pawn.AbsOrigin is not { } o) continue;
            var lx = side * (o.X - goals.Cx);
            var ly = side * (o.Y - goals.Cy) - goals.LineY;
            var z = o.Z - goals.FloorZ;
            var ax = MathF.Abs(lx);
            // centre's signed distance past the slanted back net (outward +)
            var zc = z + NetPlayerHeight * 0.5f;
            var back = (ly - DynNetBotDepth) * DynNetBackNy + (zc - DynNetBot) * DynNetBackNz;
            var reach = NetPlayerHalf * DynNetBackNy + NetPlayerHeight * 0.5f * DynNetBackNz; // box support along the normal
            var set = _netPocketPlayersIn[g];
            if (ly > 0f && ax < DynNetHalf && back < 0f && z < DynNetTop) set.Add(player.Slot);
            else if (ly < -24f || ax > DynNetHalf + 64f || back > 80f || z > DynNetTop + 40f) { set.Remove(player.Slot); continue; }
            if (ly <= -NetPlayerHalf || z > DynNetTop || ax > DynNetHalf + NetPocketSideShell + NetPlayerHalf || back > NetPocketShell + reach) continue;
            float nlx = lx, nly = ly;
            if (set.Contains(player.Slot))
            {
                if (ax + NetPlayerHalf > DynNetHalf) nlx = MathF.CopySign(DynNetHalf - NetPlayerHalf, lx);
                if (back + reach > 0f) nly = ly - (back + reach) / DynNetBackNy;
            }
            else if (ly > 0f)
            {
                var sideBand = ax - NetPlayerHalf < DynNetHalf && ax > DynNetHalf - NetPlayerHalf && back < 0f;
                if (sideBand) nlx = MathF.CopySign(DynNetHalf + NetPlayerHalf, lx);
                else if (ax < DynNetHalf && back - reach < 0f && back > -reach * 2f) nly = ly + (reach - back) / DynNetBackNy;
            }
            if (nlx == lx && nly == ly) continue;
            var pos = new Vector(goals.Cx + side * nlx, goals.Cy + side * (nly + goals.LineY), o.Z);
            var v = pawn.AbsVelocity;
            var vx = nlx != lx ? 0f : v.X;
            var vy = nly != ly ? 0f : v.Y;
            pawn.Teleport(pos, null, new Vector(vx, vy, v.Z));
        }
    }

    private string NetPocketDiag()
    {
        string P(CDynamicProp? e) => e is { IsValid: true } && e.AbsOrigin is { } o ? $"({o.X:F0},{o.Y:F0},{o.Z:F0})" : "-";
        return $"pocket walls: on=[{_netPocketCollisionOn[0]},{_netPocketCollisionOn[1]}] shells={P(_netPocketShells[0])},{P(_netPocketShells[1])} sides={string.Join(',', _netPocketSides.Select(P))} brushes={_netPocketBrushList.Count(b => b.IsValid)}";
    }

    private static Vector? BrushCentre(CBaseModelEntity brush)
    {
        if (brush.AbsOrigin is not { } o) return null;
        var c = brush.Collision;
        return new Vector(o.X + (c.Mins.X + c.Maxs.X) * 0.5f, o.Y + (c.Mins.Y + c.Maxs.Y) * 0.5f, o.Z + (c.Mins.Z + c.Maxs.Z) * 0.5f);
    }

    // Both side walls and the shell of goal g: at the net when its pocket is
    // on, parked below the map otherwise.
    private void NetPocketPlace(int g)
    {
        var on = _netPocketCollisionOn[g] == true;
        var angles = new QAngle(0.0f, g == 0 ? 0.0f : 180.0f, 0.0f);
        Vector At(float localX)
        {
            var o = NetPocketWallOrigin(g, localX);
            return on ? o : new Vector(o.X, o.Y, o.Z + NetPocketParkZ);
        }
        if (_netPocketShells[g] is { IsValid: true } shell) shell.Teleport(At(0.0f), angles, null);
        for (var s = 0; s < 2; s++)
            if (_netPocketSides[g * 2 + s] is { IsValid: true } wall) wall.Teleport(At(NetPocketSideX(s, _netPocketSidesOut[g])), angles, null);
    }

    // One tick of the nets acting on one ball for goal g (side +1/-1).
    // Returns true while the ball is in one of that goal's pockets.
    private bool NetPocketStep(int g, float side, CPhysicsPropMultiplayer ball, Vector pos, Vector vel, float dt, CDynamicProp net, NetBallTrack track, NetGoals goals)
    {
        if (_netPocketShells[g] is not { IsValid: true })
        {
            for (var p = 0; p < 3; p++) track.InPocket[g * 3 + p] = false;
            return false;
        }
        // net frame: origin on the goal line on the floor, +y into the goal
        var x = side * (pos.X - goals.Cx);
        var y = side * (pos.Y - goals.Cy) - goals.LineY;
        var z = pos.Z - goals.FloorZ;
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
                if (track.InPocket[index] && track.MaxDepth[index] > 0f)
                    Logger.LogInformation("[SM2DIAG] net_pocket_depth panel={Panel} entry={Entry:F0} max={Max:F1} deep={Deep}", index % 3, track.EntrySpeed[index], track.MaxDepth[index], _netDeep);
                track.MaxDepth[index] = 0f;
                track.InPocket[index] = false;
                continue;
            }
            // Only a ball that came into the goal through its mouth (the latch
            // of DynamicNet.cs). 2026-09-28 owner: a ball kicked at the net from
            // right behind it jittered - one resting at the floor behind the net
            // is off the panel (no previous depth) and was taken for one from
            // inside, so the spring acted on it from outside.
            if (!track.InPocket[index] && !_netPocketEntered[g].Contains(ball.Index)) continue;
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
        track.MaxDepth[g * 3 + best] = MathF.Max(track.MaxDepth[g * 3 + best], bestDepth);
        var omega = MathF.Min(NetPocketMaxOmega, (_netDeep ? NetDeepOmega(track.EntrySpeed[g * 3 + best]) : NetPocketOmega) / bestEdge);

        var pocketIndex = g * 3 + best;
        if (!track.InPocket[pocketIndex])
        {
            track.InPocket[pocketIndex] = true;
            track.EntrySpeed[pocketIndex] = MathF.Max(vn, 1f);
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

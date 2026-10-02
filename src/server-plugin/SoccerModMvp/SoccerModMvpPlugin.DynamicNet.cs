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
// goal is turned 180 deg); when a ball (match, training or cannon) touches a
// panel from inside moving into it, the animation of the nearest spot plays.
// Server flag file, v8 only;
// re-applied on round start and every 2 s (the round restart deletes it).
public sealed partial class SoccerModMvpPlugin
{
    private const string DynamicNetFlagFile = "soccermod_dynamic_net.enabled";
    private string DynamicNetModel => _netDeep ? "models/soccermod/stadium/goal_net_dynamic_deep.vmdl" : "models/soccermod/stadium/goal_net_dynamic.vmdl";
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
    private int _dynamicNetHits;

    // Motion of one ball as the nets see it (every playable ball: match,
    // training, cannon - review 2026-09-28), keyed by entity index.
    private sealed class NetBallTrack
    {
        public double LastShed; // last push off the back net from outside
        public Vector? PrevPos;
        public Vector PrevVel = new(0, 0, 0);
        public double PrevTime;
        public double SeenAt;
        // [goal * 3 + panel], panel 0 = back, 1 = side -x, 2 = side +x (NetPocket.cs)
        public readonly bool[] InPocket = new bool[6];
        public readonly float[] MaxDepth = new float[6]; // deepest point of the current pocket (log)
        public readonly float[] EntrySpeed = new float[6]; // speed into the net when the pocket started (deep net)
        public readonly float[] LastDepth = { -1f, -1f, -1f, -1f, -1f, -1f };
    }
    private readonly Dictionary<uint, NetBallTrack> _netBallTracks = new();

    private void DynamicNetOnLoad()
    {
        AddCommand("css_sm2net", "Admin: dynamic goal net status, or play <anim> on both nets.", OnDynamicNetCommand);
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            _dynamicNetPrecached = false;
            if (!File.Exists(ConfigPath(DynamicNetFlagFile))) return;
            // the deep set (NetPocket.cs) is decided here first: this listener runs before the pocket's
            var mountedDeep = MountedAddonFiles();
            _netDeep = File.Exists(ConfigPath(NetDeepFlagFile)) && mountedDeep.Contains("models/soccermod/stadium/goal_net_shell_deep.vmdl_c") && mountedDeep.Contains("models/soccermod/stadium/goal_net_dynamic_deep.vmdl_c");
            if (_netDeep) Logger.LogInformation("[SM2DIAG] net_deep_on");
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
            _dynamicNetsKey = null;
            _netBallTracks.Clear();
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
        // css_sm2net probe x y z tx ty tz [sphere radius]: what solid geometry is on the way
        if (anim == "probe")
        {
            float P(int i) => command.ArgCount > i && float.TryParse(command.GetArg(i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
            var from = new Vector(P(2), P(3), P(4));
            var to = new Vector(P(5), P(6), P(7));
            var trace = Trace.TraceEndShape(from, to, null, new TraceOptions { InteractsWith = command.ArgCount > 8 && command.GetArg(8) == "player" ? Masks.PlayerSolid : Masks.Solid });
            command.ReplyToCommand($"[SM] probe hit={trace.DidHit()} frac={trace.Fraction:F3} end={FormatVector(trace.EndPos)} normal={FormatVector(trace.Normal)} class={TraceHitClass(trace)}");
            return;
        }
        if (anim == "shoot")
        {
            if (_ball is not { IsValid: true } shotBall)
            {
                command.ReplyToCommand("[SM] Dynamic net: no match ball.");
                return;
            }
            float Arg(int i, float fallback) => command.ArgCount > i && float.TryParse(command.GetArg(i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
            var speed = Arg(2, 900f);
            if (NetGoalsHere is not { } shotGoals)
            {
                command.ReplyToCommand("[SM] Dynamic net: no v8 goals on this pitch.");
                return;
            }
            var from = new Vector(shotGoals.Cx + Arg(3, 0f), shotGoals.Cy + shotGoals.LineY - Arg(5, 150f), shotGoals.FloorZ + Arg(4, 40f));
            UnfreezeBallForPlay("net_test_shot"); // a round-start ball is DisableMotion-frozen
            var shotVelocity = new Vector(Arg(6, 0f), speed, 0f); // [vx]: sideways speed
            if (!CssBallLaunch(from, null, shotVelocity)) shotBall.Teleport(from, null, shotVelocity);
            // negative speed = from behind the goal towards the field (hits from outside)
            command.ReplyToCommand($"[SM] Dynamic net: shot at {speed:F0} u/s from ({from.X:F0}, {from.Y:F0}, {from.Z:F0}).");
            return;
        }
        if (anim.Length > 0)
            foreach (var net in _dynamicNets) if (net is { IsValid: true }) net.AcceptInput("SetAnimation", value: anim);
        command.ReplyToCommand($"[SM] Dynamic net: flag={File.Exists(ConfigPath(DynamicNetFlagFile))} precached={_dynamicNetPrecached} nets={_dynamicNets.Count(n => n is { IsValid: true })} hits={_dynamicNetHits}{(anim.Length > 0 ? $" played={anim}" : "")} {DynamicNetSequenceText()} {NetPocketDiag()}");
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

    private string? _dynamicNetsKey;

    private void DynamicNetRemove()
    {
        foreach (var net in _dynamicNets) if (net is { IsValid: true }) net.Remove();
        Array.Clear(_dynamicNets);
        _dynamicNetsKey = null;
    }

    // The map's own net brushes: on v8 found by position, on a profile map
    // with v8 goals by name (tools/port/multiindoor-rework.mjs).
    private const string IndoorGoalNetName = "sm2_indoor_goalnet";
    private IEnumerable<CBaseModelEntity> MapNetBrushes() =>
        Utilities.FindAllEntitiesByDesignerName<CBaseModelEntity>("func_brush").Where(brush =>
            brush.IsValid && (IsFoundationMap(_currentMapName)
                ? brush.AbsOrigin is { } o && MathF.Abs(o.X) <= 1f && MathF.Abs(MathF.Abs(o.Y) - DynamicNetBrushY - GoalShiftY) <= 1f && MathF.Abs(o.Z - DynamicNetBrushZ) <= 1f
                : brush.Entity?.Name == IndoorGoalNetName));

    private void DynamicNetEnsure(string reason)
    {
        if (!_dynamicNetPrecached || !File.Exists(ConfigPath(DynamicNetFlagFile)) || NetGoalsHere is not { } goals) return;
        if (_dynamicNetsKey != goals.Key) { DynamicNetRemove(); _dynamicNetsKey = goals.Key; }
        // The map's net brushes: collision stays, only the drawing goes.
        var hidden = 0;
        foreach (var brush in MapNetBrushes())
        {
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
            if (OnStreet) keyValues.SetInt("disableshadows", 1);   // low sun into the goal containers: the net's shadow flickered
            keyValues.SetBool("use_animgraph", false);
            keyValues.SetString("DefaultAnim", "idle");
            keyValues.SetString("IdleAnimationLoopMode", "ANIM_LOOP_MODE_LOOPING");
            keyValues.SetVector("origin", NetGoalOrigin(goals, side));
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

    // Balls that entered goal g through its mouth (for the pocket side walls).
    private readonly HashSet<uint>[] _netPocketEntered = { new(), new() };

    private void DynamicNetTick()
    {
        if (_dynamicNets[0] is not { IsValid: true } && _dynamicNets[1] is not { IsValid: true }) return;
        if (NetGoalsHere is not { } goals) return;
        var now = (double)Server.TickedTime;
        var pocket = NetPocketActive;
        var balls = VisibleBalls().ToList();
        if (pocket)
        {
            // Side walls of the pocket move out while a ball is inside the goal.
            for (var g = 0; g < 2; g++)
            {
                var side = g == 0 ? 1.0f : -1.0f;
                var inside = false;
                foreach (var b in balls)
                {
                    var ly = side * (b.Origin.Y - goals.Cy) - goals.LineY;
                    // 2026-09-28 owner video: balls hitting the side net or a back post from
                    // outside made the side walls jump out into them (the zone reached 40 u
                    // past the side nets). The walls only go out for a ball that came in
                    // through the goal mouth (centre inside the goal) and stay out while it
                    // is still in the pocket zone.
                    var ax = MathF.Abs(b.Origin.X - goals.Cx);
                    var bz = b.Origin.Z - goals.FloorZ;
                    // inside = in front of the slanted back net (2026-09-28: the old box test
                    // (depth 83 at every height) counted a ball resting BEHIND the net at the
                    // floor as inside; the pocket then acted on it from outside and it jittered)
                    var behindBackNet = (ly - DynNetBotDepth) * DynNetBackNy + (bz - DynNetBot) * DynNetBackNz >= -2.0f;
                    var core = ly > 0.0f && ly < DynNetBotDepth && ax < DynNetHalf - 2f && bz < DynNetTop && !behindBackNet;
                    var zone = ly > 0.0f && ly < DynNetBotDepth + 40f && ax < DynNetHalf + 40f && bz < DynNetTop + 20f;
                    if (core) _netPocketEntered[g].Add(b.Ball.Index); else if (!zone) _netPocketEntered[g].Remove(b.Ball.Index);
                    if (zone && _netPocketEntered[g].Contains(b.Ball.Index)) inside = true;
                }
                _netPocketEntered[g].RemoveWhere(id => !balls.Any(bb => bb.Ball.Index == id));
                NetPocketUpdateSides(g, inside);
                if (_netPocketCollisionOn[g] == true) NetPocketKeepPlayers(g, side, goals);
                else _netPocketPlayersIn[g].Clear();
            }
        }
        // the CS:S ball has its own nets (CssBall.cs): the net only shows the hit
        foreach (var playable in balls) DynamicNetTrackBall(playable.Ball, playable.Origin, now, pocket && !(playable.IsMatchBall && CssBallActive), goals);
        // forget balls that are gone (removed training / cannon balls)
        if (Server.TickCount % 64 == 0 && _netBallTracks.Count > 0)
            foreach (var key in _netBallTracks.Where(kv => now - kv.Value.SeenAt > 1.0).Select(kv => kv.Key).ToList())
                _netBallTracks.Remove(key);
    }

    private void DynamicNetTrackBall(CPhysicsPropMultiplayer ball, Vector origin, double now, bool pocket, NetGoals goals)
    {
        if (!_netBallTracks.TryGetValue(ball.Index, out var track)) _netBallTracks[ball.Index] = track = new NetBallTrack();
        track.SeenAt = now;
        var pos = new Vector(origin.X, origin.Y, origin.Z);
        var prev = track.PrevPos;
        var dt = (float)(now - track.PrevTime);
        track.PrevPos = pos;
        track.PrevTime = now;
        if (prev is null || dt <= 0.0001f || dt > 0.5f)
        {
            track.PrevVel = new Vector(0, 0, 0);
            return;
        }
        // A teleport (goal reset, respawn, test shot) is no hit: more than 80
        // units in one sample (a real ball moves at most ~50 per tick).
        var dx = pos.X - prev.X;
        var dy = pos.Y - prev.Y;
        var dz = pos.Z - prev.Z;
        if (MathF.Sqrt(dx * dx + dy * dy + dz * dz) > 80f)
        {
            track.PrevVel = new Vector(0, 0, 0);
            Array.Clear(track.InPocket);
            return;
        }
        var vel = new Vector(dx / dt, dy / dt, dz / dt);
        for (var g = 0; g < 2; g++)
        {
            if (_dynamicNets[g] is not { IsValid: true } net) continue;
            var side = g == 0 ? 1.0f : -1.0f;
            // 2026-09-28 owner: a ball kicked at the back net from right behind it hung on
            // the slanted net and jittered (a sphere hull does not roll off a 70 degree,
            // 2-unit slab). An almost resting ball in contact with the back net from
            // outside gets a small push away from it until it drops.
            {
                var oly = side * (pos.Y - goals.Cy) - goals.LineY;
                var oby = oly - DynNetBotDepth;
                var obz = pos.Z - goals.FloorZ - DynNetBot;
                var odist = oby * DynNetBackNy + obz * DynNetBackNz;
                var oalong = (-oby * DynNetBackNz + obz * DynNetBackNy) / DynNetBackLen;
                var ospeed = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y + vel.Z * vel.Z);
                var ovn = side * vel.Y * DynNetBackNy + vel.Z * DynNetBackNz; // < 0: moving into the net
                if (odist > 0.0f && odist < BallCollisionRadius + 3.0f && MathF.Abs(side * (pos.X - goals.Cx)) < DynNetHalf && oalong > -0.1f && oalong < 1.05f
                    && ospeed < 260.0f && ovn > -200.0f && now - track.LastShed > 0.4 && !KnifeKickOwnsTick(ball) && !_netPocketEntered[g].Contains(ball.Index))
                {
                    track.LastShed = now;
                    ball.AcceptInput("Wake");
                    ball.Teleport(null, null, new Vector(vel.X, vel.Y + side * DynNetBackNy * 160.0f, vel.Z + DynNetBackNz * 160.0f));
                    Logger.LogInformation("[SM2DIAG] net_shed goal={Goal} dist={Dist:F1} speed={Speed:F0}", g == 0 ? "+y" : "-y", odist, ospeed);
                }
            }
            // Ball pocket on (NetPocket.cs): the back net is handled there.
            if (pocket && NetPocketStep(g, side, ball, pos, vel, dt, net, track, goals)) continue;
            if (DynamicNetSpot(side, pos, vel, track.PrevVel, goals, pocket) is not { } hit) continue;
            if (now - _dynamicNetLastHit[g] < DynNetCooldown && hit.Speed < _dynamicNetLastSpeed[g] * 1.5f) continue;
            _dynamicNetLastHit[g] = now;
            _dynamicNetLastSpeed[g] = hit.Speed;
            _dynamicNetHits++;
            net.AcceptInput("SetAnimation", value: hit.Anim);
            Logger.LogInformation("[SM2DIAG] dynamic_net_hit goal={Goal} anim={Anim} speed={Speed:F0}", g == 0 ? "+y" : "-y", hit.Anim, hit.Speed);
        }
        track.PrevVel = vel;
    }

    // The panel the ball presses into (the deepest contact) and the animation
    // of the nearest impact spot, or null. side = +1 / -1 for the goal end.
    private (string Anim, float Speed)? DynamicNetSpot(float side, Vector pos, Vector vel, Vector prevVel, NetGoals goals, bool skipBack = false)
    {
        // Into the net frame: the -y goal is the +y one turned 180 deg.
        var x = side * (pos.X - goals.Cx);
        var y = side * (pos.Y - goals.Cy) - goals.LineY;
        var z = pos.Z - goals.FloorZ;
        if (y <= 0.0f) return null; // still in front of the goal line
        float Vx(Vector v) => side * v.X;
        float Vy(Vector v) => side * v.Y;
        var radius = BallCollisionRadius;
        (char Key, float Depth, float U, float V, float Speed, bool In)? best = null;
        void Consider(char key, float d, float u, float v, float speedNow, float speedPrev, bool fromInside = true)
        {
            if (u < -0.05f || u > 1.05f || v < -0.05f || v > 1.05f) return;
            // d: signed distance of the ball centre past the panel (outward +).
            // From inside: the ball presses outward (hit_). 2026-09-28 owner:
            // from outside too - the ball on the outer side moving inward, the
            // net dented inward (hitin_, the same spots mirrored).
            if (fromInside && d >= -(radius + 4.0f) && d <= radius * 0.5f)
            {
                var speed = MathF.Max(speedNow, speedPrev);
                if (speed >= DynNetMinSpeed && (best is null || d > best.Value.Depth)) best = (key, d, u, v, speed, false);
            }
            if (d <= radius + 4.0f && d >= -radius * 0.5f)
            {
                var speed = MathF.Max(-speedNow, -speedPrev);
                if (speed >= DynNetMinSpeed && (best is null || -d > best.Value.Depth)) best = (key, -d, u, v, speed, true);
            }
        }
        var across = (x + DynNetHalf) / (2.0f * DynNetHalf);
        // back: slanted, from (y 83, z 4) up to (y 47.5, z 101)
        var by = y - DynNetBotDepth;
        var bz = z - DynNetBot;
        Consider('b', by * DynNetBackNy + bz * DynNetBackNz, across, (-by * DynNetBackNz + bz * DynNetBackNy) / DynNetBackLen,
            Vy(vel) * DynNetBackNy + vel.Z * DynNetBackNz, Vy(prevVel) * DynNetBackNy + prevVel.Z * DynNetBackNz, !skipBack);
        // roof
        Consider('r', z - DynNetTop, across, y / DynNetTopDepth, vel.Z, prevVel.Z);
        // sides: trapezoids, front edge at the goal line, back edge along the back net
        var up = (z - DynNetBot) / (DynNetTop - DynNetBot);
        var depthAt = DynNetBotDepth + (DynNetTopDepth - DynNetBotDepth) * Math.Clamp(up, 0.0f, 1.0f);
        // with the pocket (NetPocket.cs) the side nets are handled there as well
        Consider('p', x - DynNetHalf, y / depthAt, up, Vx(vel), Vx(prevVel), !skipBack);
        Consider('n', -x - DynNetHalf, y / depthAt, up, -Vx(vel), -Vx(prevVel), !skipBack);
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
        return ($"{(hit.In ? "hitin" : "hit")}_{hit.Key}_{col}_{row}_{strength}", hit.Speed);
    }
}

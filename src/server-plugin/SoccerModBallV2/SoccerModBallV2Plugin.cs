using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModBallV2;

[MinimumApiVersion(373)]
public sealed class SoccerModBallV2Plugin : BasePlugin
{
    private const string FoundationMapName = "soccer_cssl_stadium_v8";
    private const string PhysicsDesignerName = "prop_physics_multiplayer";
    private const string DynamicDesignerName = "prop_dynamic";
    private const string MapBallTargetName = "filter_ball";
    private const string QueryTargetName = "sm2_xslv2_query";
    private const string VisualTargetName = "sm2_xslv2_visual";
    private const string LegacyOwnedBallTargetName = "sm2_owned_ball";
    private const string LegacyVisualTargetName = "sm2_owned_ball_visual";
    private const string CtLegacyKillTriggerName = "ct_killer";
    private const string TLegacyKillTriggerName = "terro_killer";
    private const string PhysicsModelName = "models/soccermod/xsl_gameplay_ball_physics.vmdl";
    private const string VisualModelName = "models/ball/jabulani_edit.vmdl";

    private const float ResetX = 7.730350f;
    private const float ResetY = 2.597906f;
    private const float ResetZ = -16.997691f;
    private const float QueryParkZ = ResetZ - 4096.0f;
    private const float CollisionSkin = 0.12f;
    private const float GroundProbeDistance = 3.0f;
    private const float GroundSnapMaximumUpwardSpeed = 80.0f;
    private const float NearGroundHeight = 5.0f;
    private const int MaximumWorldCollisionIterations = 5;

    // The workshop Jabulani has a diameter of about 37.61 units. XSL gameplay
    // uses a 30-unit ball, so the visual is scaled to the logical sphere.
    private const float VisualModelScale = 30.0f / 37.61f;

    private const float KickMaximumReach = 108.0f;
    private const float KickMinimumAimDot = 0.42f;
    private const double KickCooldownSeconds = 0.32;
    private const float PlayerContactRadius = 17.0f;
    private const float PlayerContactMinimumZ = -10.0f;
    private const float PlayerContactMaximumZ = 64.0f;

    private readonly Dictionary<int, double> _lastKickTimeBySlot = new();
    private readonly XslBallProfile _profile = new();
    private XslBallEngine? _engine;
    private CPhysicsPropMultiplayer? _queryShape;
    private CDynamicProp? _visual;
    private CPhysicsPropMultiplayer? _parkedMapBall;
    private Vector? _parkedMapBallOrigin;
    private QAngle? _parkedMapBallAngles;
    private QAngle _visualAngles = new(0.0f, 0.0f, 0.0f);
    private BallVec3 _lastVisualPosition;
    private string _currentMapName = string.Empty;
    private int _nextMaintenanceTick;

    public override string ModuleName => "CS2 SoccerMod XSL Ball Engine v2";
    public override string ModuleVersion => "2.0.0-alpha1";
    public override string ModuleAuthor => "Sergi + Codex";
    public override string ModuleDescription =>
        "Deterministic server-authoritative XSL-style ball simulation for CS2.";

    public override void Load(bool hotReload)
    {
        _currentMapName = Server.MapName;
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            manifest.AddResource(PhysicsModelName);
            manifest.AddResource(VisualModelName);
        });
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawned);
        RegisterListener<Listeners.OnPlayerButtonsChanged>(OnPlayerButtonsChanged);
        RegisterListener<Listeners.CheckTransmit>(OnCheckTransmit);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);

        AddCommand("css_xslv2_status", "Show XSL Ball Engine v2 state.", OnStatusCommand);
        AddCommand("css_xslv2_reset", "Reset the XSL Ball Engine v2 ball.", OnResetCommand);
        AddCommand("css_xslreset", "Reset the XSL Ball Engine v2 ball.", OnResetCommand);
        AddCommand("css_xslv2_impulse", "Server only: set a test velocity.", OnImpulseCommand);

        Logger.LogInformation(
            "[XSLV2] load version={Version} hotReload={HotReload} map={Map}",
            ModuleVersion,
            hotReload,
            _currentMapName);

        Server.NextFrame(() => ActivateFoundation("plugin_load"));
        AddTimer(0.25f, () => ActivateFoundation("plugin_load_plus_0_25s"), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(1.0f, () => ActivateFoundation("plugin_load_plus_1s"), TimerFlags.STOP_ON_MAPCHANGE);
    }

    public override void Unload(bool hotReload)
    {
        RemoveOwnedEntities();
        RestoreParkedMapBall();
        _engine = null;
        _lastKickTimeBySlot.Clear();
    }

    private void OnMapStart(string mapName)
    {
        _currentMapName = mapName;
        _engine = null;
        _queryShape = null;
        _visual = null;
        _parkedMapBall = null;
        _parkedMapBallOrigin = null;
        _parkedMapBallAngles = null;
        _lastKickTimeBySlot.Clear();
        _nextMaintenanceTick = 0;
        _visualAngles = new QAngle(0.0f, 0.0f, 0.0f);

        Server.NextFrame(() => ActivateFoundation("map_start"));
        AddTimer(0.25f, () => ActivateFoundation("map_start_plus_0_25s"), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(1.0f, () => ActivateFoundation("map_start_plus_1s"), TimerFlags.STOP_ON_MAPCHANGE);
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _lastKickTimeBySlot.Clear();
        Server.NextFrame(() =>
        {
            NeutralizeLegacyMapKillTriggers("round_start");
            ParkMapBallIfPresent("round_start");
            EnsureAllPlayerKnives("round_start");
        });
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is { IsValid: true })
        {
            AddTimer(
                0.25f,
                () => EnsurePlayerKnife(player, "player_spawn"),
                TimerFlags.STOP_ON_MAPCHANGE);
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        // Safety only. A normal death must not reset a live football match.
        Server.NextFrame(() => NeutralizeLegacyMapKillTriggers("player_death"));
        return HookResult.Continue;
    }

    private void OnEntitySpawned(CEntityInstance entity)
    {
        if (string.Equals(entity.DesignerName, "trigger_hurt", StringComparison.OrdinalIgnoreCase))
        {
            Server.NextFrame(() => NeutralizeLegacyMapKillTriggers("trigger_hurt_spawned"));
        }
    }

    private void OnTick()
    {
        if (Server.TickCount >= _nextMaintenanceTick)
        {
            _nextMaintenanceTick = Server.TickCount + 64;
            ActivateFoundation("maintenance");
            ParkMapBallIfPresent("maintenance");
            NeutralizeLegacyMapKillTriggers("maintenance");
        }

        if (_engine is null
            || _queryShape is not { IsValid: true }
            || _visual is not { IsValid: true })
        {
            return;
        }

        var deltaTime = Math.Clamp(Server.TickInterval, 1.0f / 256.0f, 0.05f);
        ApplyPlayerContacts();
        SimulateWorld(deltaTime);
        UpdateVisual();

        if (!IsFinite(_engine.Position)
            || MathF.Abs(_engine.Position.X) > 10000.0f
            || MathF.Abs(_engine.Position.Y) > 10000.0f
            || _engine.Position.Z < -2048.0f)
        {
            ResetBall("out_of_bounds");
        }
    }

    private void SimulateWorld(float deltaTime)
    {
        if (_engine is null || _queryShape is not { IsValid: true })
        {
            return;
        }

        _engine.BeginStep(deltaTime);
        var remainingTime = deltaTime;
        var options = new TraceOptions { InteractsWith = Masks.SolidBrushOnly };

        for (var iteration = 0;
             iteration < MaximumWorldCollisionIterations && remainingTime > 0.00001f;
             iteration++)
        {
            var start = _engine.Position;
            var displacement = _engine.RequestedDisplacement(remainingTime);
            if (displacement.LengthSquared < 0.000001f)
            {
                break;
            }

            var end = start + displacement;
            var trace = TraceBallHull(start, end, options);
            if (!trace.DidHit() || trace.Fraction >= 0.9999f)
            {
                _engine.SetPosition(end);
                remainingTime = 0.0f;
                break;
            }

            var normal = FromVector(trace.Normal).Normalized();
            if (trace.IsAllSolid || normal.LengthSquared < 0.5f)
            {
                Logger.LogWarning(
                    "[XSLV2] world_trace_stuck position={Position} iteration={Iteration}",
                    Format(_engine.Position),
                    iteration);
                _engine.SetPosition(start + new BallVec3(0.0f, 0.0f, CollisionSkin));
                _engine.ApplyDebugImpulse(BallVec3.Zero);
                remainingTime = 0.0f;
                break;
            }

            // Reconstruct the swept hull centre from Fraction instead of
            // depending on engine-specific EndPos semantics.
            var contactPosition = start
                + displacement * Math.Clamp(trace.Fraction, 0.0f, 1.0f)
                + normal * CollisionSkin;
            _engine.SetPosition(contactPosition);
            var nearGround = contactPosition.Z <= ResetZ + NearGroundHeight;
            _engine.ResolveWorldCollision(normal, nearGround);
            remainingTime *= Math.Clamp(1.0f - trace.Fraction, 0.0f, 1.0f);
        }

        var grounded = ProbeAndSnapToGround(options);
        _engine.EndStep(deltaTime, grounded);
    }

    private bool ProbeAndSnapToGround(TraceOptions options)
    {
        if (_engine is null
            || _queryShape is not { IsValid: true }
            || _engine.Velocity.Z > GroundSnapMaximumUpwardSpeed)
        {
            return false;
        }

        var start = _engine.Position;
        var end = start + new BallVec3(0.0f, 0.0f, -GroundProbeDistance);
        var trace = TraceBallHull(start, end, options);
        if (!trace.DidHit()
            || trace.IsAllSolid
            || trace.Normal.Z < _profile.FloorNormalThreshold)
        {
            return false;
        }

        var normal = FromVector(trace.Normal).Normalized();
        var snapped = start
            + (end - start) * Math.Clamp(trace.Fraction, 0.0f, 1.0f)
            + normal * CollisionSkin;
        _engine.SetPosition(snapped);
        _engine.SetGrounded(true);
        return true;
    }

    private void ApplyPlayerContacts()
    {
        if (_engine is null)
        {
            return;
        }

        var ballPosition = _engine.Position;
        var contactDistance = _profile.Radius + PlayerContactRadius;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsEligiblePlayer(player)
                || player.PlayerPawn.Value?.AbsOrigin is not { } playerOrigin)
            {
                continue;
            }

            var relativeZ = ballPosition.Z - playerOrigin.Z;
            if (relativeZ < PlayerContactMinimumZ || relativeZ > PlayerContactMaximumZ)
            {
                continue;
            }

            var delta = new BallVec3(
                ballPosition.X - playerOrigin.X,
                ballPosition.Y - playerOrigin.Y,
                0.0f);
            var distance = delta.Length;
            if (distance > contactDistance)
            {
                continue;
            }

            var normal = distance > 0.001f
                ? delta / distance
                : ForwardFromAngles(player.PlayerPawn.Value!.EyeAngles).WithZ(0.0f).Normalized();
            var playerVelocity = player.PlayerPawn.Value!.AbsVelocity;
            _engine.ApplyBodyContact(
                normal,
                playerVelocity is null ? BallVec3.Zero : FromVector(playerVelocity));

            // Small correction keeps slow dribbling responsive without letting
            // a player teleport the ball through a nearby wall.
            if (distance < contactDistance - 0.25f)
            {
                var correction = MathF.Min(2.0f, contactDistance - distance);
                _engine.SetPosition(_engine.Position + normal * correction);
                ballPosition = _engine.Position;
            }
        }
    }

    private TraceResult TraceBallHull(BallVec3 start, BallVec3 end, TraceOptions options)
    {
        var radius = _profile.Radius;
        return Trace.TraceHullShape(
            ToVector(start),
            ToVector(end),
            new Vector(-radius, -radius, -radius),
            new Vector(radius, radius, radius),
            _queryShape!,
            options);
    }

    private void OnPlayerButtonsChanged(
        CCSPlayerController player,
        PlayerButtons pressed,
        PlayerButtons released)
    {
        if ((pressed & PlayerButtons.Attack) == 0)
        {
            return;
        }

        TryApplyKnifeKick(player);
    }

    private void TryApplyKnifeKick(CCSPlayerController player)
    {
        if (_engine is null || !IsEligiblePlayer(player))
        {
            return;
        }

        var pawn = player.PlayerPawn.Value!;
        var activeWeapon = pawn.WeaponServices?.ActiveWeapon.Value;
        if (activeWeapon is null
            || !activeWeapon.IsValid
            || !activeWeapon.DesignerName.Contains("knife", StringComparison.OrdinalIgnoreCase)
            || pawn.AbsOrigin is not { } playerOrigin)
        {
            return;
        }

        var now = (double)Server.TickedTime;
        if (_lastKickTimeBySlot.TryGetValue(player.Slot, out var lastKick)
            && now - lastKick < KickCooldownSeconds)
        {
            return;
        }

        var eyePosition = new BallVec3(
            playerOrigin.X + pawn.ViewOffset.X,
            playerOrigin.Y + pawn.ViewOffset.Y,
            playerOrigin.Z + pawn.ViewOffset.Z);
        var eyeToBall = _engine.Position - eyePosition;
        var distance = eyeToBall.Length;
        if (distance <= 0.001f || distance > KickMaximumReach)
        {
            return;
        }

        var forward = ForwardFromAngles(pawn.EyeAngles);
        var aimDot = BallVec3.Dot(forward, eyeToBall / distance);
        if (!float.IsFinite(aimDot) || aimDot < KickMinimumAimDot)
        {
            return;
        }

        var playerVelocity = pawn.AbsVelocity;
        _engine.ApplyKnifeKick(
            forward,
            playerVelocity is null ? BallVec3.Zero : FromVector(playerVelocity));
        _lastKickTimeBySlot[player.Slot] = now;

        Logger.LogInformation(
            "[XSLV2] knife_kick slot={Slot} name={Name} distance={Distance:F1} aimDot={AimDot:F3} forward={Forward} velocity={Velocity}",
            player.Slot,
            player.PlayerName,
            distance,
            aimDot,
            Format(forward),
            Format(_engine.Velocity));
    }

    private void ActivateFoundation(string reason)
    {
        if (!string.Equals(_currentMapName, FoundationMapName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        NeutralizeLegacyMapKillTriggers(reason);
        RemoveLegacyV1Entities();
        ParkMapBallIfPresent(reason);

        if (_queryShape is not { IsValid: true })
        {
            _queryShape = Utilities
                .FindAllEntitiesByDesignerName<CPhysicsPropMultiplayer>(PhysicsDesignerName)
                .FirstOrDefault(candidate =>
                    candidate.IsValid && candidate.Entity?.Name == QueryTargetName);
        }

        if (_queryShape is not { IsValid: true } && !CreateQueryShape(reason))
        {
            return;
        }

        if (_engine is null)
        {
            var resetPosition = ResetPosition();
            _engine = new XslBallEngine(_profile, resetPosition);
            _lastVisualPosition = resetPosition;
        }

        EnsureVisual(reason);
        EnsureAllPlayerKnives(reason);
    }

    private bool CreateQueryShape(string reason)
    {
        var query = Utilities.CreateEntityByName<CPhysicsPropMultiplayer>(PhysicsDesignerName);
        if (query is null || !query.IsValid)
        {
            Logger.LogError("[XSLV2] query_create_failed reason={Reason}", reason);
            return false;
        }

        using var keyValues = new CEntityKeyValues();
        keyValues.SetString("targetname", QueryTargetName);
        keyValues.SetString("model", PhysicsModelName);
        keyValues.SetUInt("spawnflags", 1);
        keyValues.SetInt("physicsmode", 1);
        keyValues.SetVector("origin", new Vector(ResetX, ResetY, QueryParkZ));
        keyValues.SetAngle("angles", new QAngle(0.0f, 0.0f, 0.0f));
        query.DispatchSpawn(keyValues);
        if (!query.IsValid)
        {
            Logger.LogError("[XSLV2] query_spawn_failed reason={Reason}", reason);
            return false;
        }

        query.Entity!.Name = QueryTargetName;
        query.AcceptInput("DisableMotion");
        query.Teleport(
            position: new Vector(ResetX, ResetY, QueryParkZ),
            angles: new QAngle(0.0f, 0.0f, 0.0f),
            velocity: new Vector(0.0f, 0.0f, 0.0f));
        _queryShape = query;
        Logger.LogInformation(
            "[XSLV2] query_ready reason={Reason} index={Index} model={Model}",
            reason,
            query.Index,
            PhysicsModelName);
        return true;
    }

    private void EnsureVisual(string reason)
    {
        if (_engine is null)
        {
            return;
        }

        if (_visual is not { IsValid: true })
        {
            _visual = Utilities
                .FindAllEntitiesByDesignerName<CDynamicProp>(DynamicDesignerName)
                .FirstOrDefault(candidate =>
                    candidate.IsValid && candidate.Entity?.Name == VisualTargetName);
        }

        if (_visual is { IsValid: true })
        {
            return;
        }

        var visual = Utilities.CreateEntityByName<CDynamicProp>(DynamicDesignerName);
        if (visual is null || !visual.IsValid)
        {
            Logger.LogError("[XSLV2] visual_create_failed reason={Reason}", reason);
            return;
        }

        using var keyValues = new CEntityKeyValues();
        keyValues.SetString("targetname", VisualTargetName);
        keyValues.SetString("model", VisualModelName);
        keyValues.SetInt("solid", 0);
        keyValues.SetVector("origin", ToVector(_engine.Position));
        keyValues.SetAngle("angles", _visualAngles);
        visual.DispatchSpawn(keyValues);
        if (!visual.IsValid)
        {
            Logger.LogError("[XSLV2] visual_spawn_failed reason={Reason}", reason);
            return;
        }

        visual.Entity!.Name = VisualTargetName;
        visual.AcceptInput("DisableCollision");
        var sceneNode = visual.CBodyComponent?.SceneNode;
        if (sceneNode is not null)
        {
            sceneNode.Scale = VisualModelScale;
            sceneNode.ClientLocalScale = VisualModelScale;
        }

        _visual = visual;
        UpdateVisual();
        Logger.LogInformation(
            "[XSLV2] visual_ready reason={Reason} index={Index} model={Model} scale={Scale:F4}",
            reason,
            visual.Index,
            VisualModelName,
            VisualModelScale);
    }

    private void UpdateVisual()
    {
        if (_engine is null || _visual is not { IsValid: true })
        {
            return;
        }

        var displacement = _engine.Position - _lastVisualPosition;
        var radiansToDegrees = 180.0f / MathF.PI;
        _visualAngles.X += -displacement.Y / _profile.Radius * radiansToDegrees;
        _visualAngles.Y += displacement.X / _profile.Radius * radiansToDegrees;
        _visual.Teleport(
            position: ToVector(_engine.Position),
            angles: _visualAngles,
            velocity: ToVector(_engine.Velocity));
        _lastVisualPosition = _engine.Position;
    }

    private void ParkMapBallIfPresent(string reason)
    {
        var mapBall = Utilities
            .FindAllEntitiesByDesignerName<CPhysicsPropMultiplayer>(PhysicsDesignerName)
            .FirstOrDefault(candidate =>
                candidate.IsValid && candidate.Entity?.Name == MapBallTargetName);
        if (mapBall is null || !mapBall.IsValid)
        {
            return;
        }

        if (_parkedMapBall is null || !_parkedMapBall.IsValid)
        {
            _parkedMapBall = mapBall;
            if (mapBall.AbsOrigin is { } origin)
            {
                _parkedMapBallOrigin = new Vector(origin.X, origin.Y, origin.Z);
            }
            if (mapBall.AbsRotation is { } angles)
            {
                _parkedMapBallAngles = new QAngle(angles.X, angles.Y, angles.Z);
            }
        }

        mapBall.AcceptInput("DisableMotion");
        mapBall.Teleport(
            position: new Vector(ResetX, ResetY, QueryParkZ),
            velocity: new Vector(0.0f, 0.0f, 0.0f));

        if (reason != "maintenance")
        {
            Logger.LogInformation("[XSLV2] map_ball_parked reason={Reason}", reason);
        }
    }

    private void RestoreParkedMapBall()
    {
        if (_parkedMapBall is not { IsValid: true })
        {
            return;
        }

        _parkedMapBall.Teleport(
            position: _parkedMapBallOrigin ?? new Vector(ResetX, ResetY, ResetZ),
            angles: _parkedMapBallAngles ?? new QAngle(0.0f, 0.0f, 0.0f),
            velocity: new Vector(0.0f, 0.0f, 0.0f));
        _parkedMapBall.AcceptInput("EnableMotion");
        _parkedMapBall.AcceptInput("Wake");
    }

    private void RemoveLegacyV1Entities()
    {
        foreach (var candidate in Utilities.FindAllEntitiesByDesignerName<CPhysicsPropMultiplayer>(PhysicsDesignerName))
        {
            if (candidate.IsValid && candidate.Entity?.Name == LegacyOwnedBallTargetName)
            {
                candidate.AcceptInput("Kill");
            }
        }

        foreach (var candidate in Utilities.FindAllEntitiesByDesignerName<CDynamicProp>(DynamicDesignerName))
        {
            if (candidate.IsValid && candidate.Entity?.Name == LegacyVisualTargetName)
            {
                candidate.AcceptInput("Kill");
            }
        }
    }

    private void RemoveOwnedEntities()
    {
        if (_visual is { IsValid: true })
        {
            _visual.AcceptInput("Kill");
        }
        if (_queryShape is { IsValid: true })
        {
            _queryShape.AcceptInput("Kill");
        }
        _visual = null;
        _queryShape = null;
    }

    private void NeutralizeLegacyMapKillTriggers(string reason)
    {
        var count = 0;
        foreach (var trigger in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("trigger_hurt"))
        {
            if (!trigger.IsValid || trigger.Entity?.Name is not { } name)
            {
                continue;
            }

            if (!string.Equals(name, CtLegacyKillTriggerName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, TLegacyKillTriggerName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            trigger.AcceptInput("Disable");
            trigger.AcceptInput("Kill");
            count++;
        }

        if (count > 0 && reason != "maintenance")
        {
            Logger.LogInformation(
                "[XSLV2] legacy_kill_triggers_removed reason={Reason} count={Count}",
                reason,
                count);
        }
    }

    private void EnsureAllPlayerKnives(string reason)
    {
        foreach (var player in Utilities.GetPlayers())
        {
            EnsurePlayerKnife(player, reason);
        }
    }

    private void EnsurePlayerKnife(CCSPlayerController player, string reason)
    {
        if (!IsEligiblePlayer(player))
        {
            return;
        }

        var weapons = player.PlayerPawn.Value?.WeaponServices?.MyWeapons;
        var hasKnife = weapons is not null && weapons.Any(handle =>
        {
            var weapon = handle.Value;
            return weapon is { IsValid: true }
                && weapon.DesignerName.Contains("knife", StringComparison.OrdinalIgnoreCase);
        });
        if (hasKnife)
        {
            return;
        }

        var itemName = player.Team == CsTeam.Terrorist ? "weapon_knife_t" : "weapon_knife";
        var result = player.GiveNamedItem(itemName);
        Logger.LogInformation(
            "[XSLV2] knife_grant reason={Reason} slot={Slot} item={Item} result=0x{Result:X}",
            reason,
            player.Slot,
            itemName,
            result.ToInt64());
    }

    private void OnStatusCommand(CCSPlayerController? player, CommandInfo command)
    {
        var message = _engine is null
            ? "[XSLV2] inactive"
            : $"[XSLV2] position={Format(_engine.Position)} velocity={Format(_engine.Velocity)} speed={_engine.Velocity.Length:F1} grounded={_engine.IsGrounded}";
        command.ReplyToCommand(message);
    }

    private void OnResetCommand(CCSPlayerController? player, CommandInfo command)
    {
        ResetBall(player is null ? "server_command" : $"player_{player.Slot}");
        command.ReplyToCommand("[XSLV2] ball reset to center");
    }

    private void OnImpulseCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null)
        {
            command.ReplyToCommand("[XSLV2] server console/RCON only");
            return;
        }

        if (_engine is null || command.ArgCount < 4
            || !TryParse(command.GetArg(1), out var x)
            || !TryParse(command.GetArg(2), out var y)
            || !TryParse(command.GetArg(3), out var z))
        {
            command.ReplyToCommand("[XSLV2] usage: css_xslv2_impulse <x> <y> <z>");
            return;
        }

        _engine.ApplyDebugImpulse(new BallVec3(x, y, z));
        command.ReplyToCommand($"[XSLV2] velocity={Format(_engine.Velocity)}");
    }

    private void ResetBall(string reason)
    {
        if (_engine is null)
        {
            ActivateFoundation(reason);
            return;
        }

        var resetPosition = ResetPosition();
        _engine.Reset(resetPosition);
        _lastVisualPosition = resetPosition;
        _visualAngles = new QAngle(0.0f, 0.0f, 0.0f);
        UpdateVisual();
        Logger.LogInformation("[XSLV2] ball_reset reason={Reason}", reason);
    }

    private void OnCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (_queryShape is not { IsValid: true })
        {
            return;
        }

        foreach ((CCheckTransmitInfo info, CCSPlayerController? _) in infoList)
        {
            info.TransmitEntities.Remove(_queryShape);
        }
    }

    private static bool IsEligiblePlayer(CCSPlayerController? player)
    {
        if (player is null || !player.IsValid || player.IsBot)
        {
            return false;
        }

        if (player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            return false;
        }

        var pawn = player.PlayerPawn.Value;
        return pawn is { IsValid: true } && pawn.LifeState == (byte)LifeState_t.LIFE_ALIVE;
    }

    private static BallVec3 ForwardFromAngles(QAngle angles)
    {
        var pitch = angles.X * (MathF.PI / 180.0f);
        var yaw = angles.Y * (MathF.PI / 180.0f);
        var cosPitch = MathF.Cos(pitch);
        return new BallVec3(
            cosPitch * MathF.Cos(yaw),
            cosPitch * MathF.Sin(yaw),
            -MathF.Sin(pitch));
    }

    private static BallVec3 ResetPosition() =>
        new(ResetX, ResetY, ResetZ + CollisionSkin);

    private static Vector ToVector(BallVec3 value) =>
        new(value.X, value.Y, value.Z);

    private static BallVec3 FromVector(Vector value) =>
        new(value.X, value.Y, value.Z);

    private static bool IsFinite(BallVec3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool TryParse(string raw, out float value) =>
        float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && float.IsFinite(value);

    private static string Format(BallVec3 value) =>
        $"({value.X:F1},{value.Y:F1},{value.Z:F1})";
}

using System.Globalization;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace SoccerModMvp;

// 2026-09-01 user request: the SoMoE-19 training menu (soccer_mod/modules/
// training.sp + training_personalcannon.sp), re-added for CS2:
//   Cannon          - commandeers the MATCH ball: "Set cannon position" and
//                     "Set cannon aim" take the point under the admin's
//                     crosshair, "Cannon on" teleports the ball to the
//                     position and fires it at the aim point every
//                     fire_rate seconds, "Cannon off" stops it in place.
//   Personal Cannon - the same, but per player with its OWN ball entity and
//                     per-SteamID persisted settings, so several people can
//                     drill at once without fighting over the match ball.
//   Disable/Enable  - goal detection off while training (a cannon aimed at
//   Goals             the goal would otherwise trigger the warmup goal kill
//                     every shot).
//   Spawn/Remove    - toggle: a private ball at the crosshair, one per
//   Ball              player, 5 s cooldown after a removal (SoMoE quirk:
//                     cooldown on removal only). Shares the entity with the
//                     personal cannon, exactly as in the original.
// "Spawn Prop Menu" and "Advanced Training" need training models the CS2
// port does not have and are deliberately absent.
//
// Extra balls are real prop_physics_multiplayer entities spawned with the
// live match ball's model (same recipe as SoccerModBallV2's query shape,
// which was proven physically on this map). They are kickable and pushable
// through the PlayableBalls() seam in the main file, get the same no-damage
// treatment as the match ball, and never trigger goals or kickoff logic. Improved handling shares wall
// assist and settling; legacy mode retains the older training behavior.
// AbsVelocity reads as zero for this entity type on this build (documented
// in the ball-foundation root-cause doc), so each training ball keeps its
// own origin-difference velocity, exactly like _derivedBallVelocity.
public sealed partial class SoccerModMvpPlugin
{
    private const string TrainingBallTargetPrefix = "sm2_training_ball_";
    private const float TrainingCannonPositionLift = 15.0f;
    private const float TrainingSpawnCooldownSeconds = 5.0f;
    private const float TrainingAimTraceDistance = 8192.0f;
    private const int TrainingCannonMaxMissedShots = 3;

    // SoMoE globals.sp defaults and the ranges its chat prompts enforced.
    private const float DefaultCannonFireRate = 2.5f;
    private const float DefaultCannonPower = 10000.0f;
    private const float DefaultCannonRandomness = 0.0f;
    private const float CannonRandomnessMin = 0.0f;
    private const float CannonRandomnessMax = 500.0f;
    private const float CannonFireRateMin = 0.1f;
    private const float CannonFireRateMax = 10.0f;
    private const float CannonPowerMin = 0.001f;
    private const float CannonPowerMax = 10000.0f;

    private sealed class TrainingBall
    {
        public required CPhysicsPropMultiplayer Entity;
        public required uint Index;
        public required int OwnerSlot;
        public Vector? PreviousOrigin;
        public double PreviousSampleTime;
        public Vector DerivedVelocity = new(0.0f, 0.0f, 0.0f);
        public Vector? PreviousImpactOrigin;
        public Vector? PreviousImpactVelocity;
        public readonly HashSet<int> PushingSlots = new();
    }

    // Everything the kick/push/impact code needs about one ball, so the
    // match ball and the training balls share one code path.
    private readonly record struct PlayableBall(
        CPhysicsPropMultiplayer Ball,
        Vector Origin,
        bool IsMatchBall,
        Vector Inherited,
        HashSet<int> PushingSlots);

    private sealed class PersonalCannonState
    {
        public Vector? Position;
        public Vector? Aim;
        public Timer? Timer;
        public int MissedShots;
        public float FireRate = DefaultCannonFireRate;
        public float Power = DefaultCannonPower;
        public float Randomness = DefaultCannonRandomness;
    }

    private const string TrainingPersonalFileName = "soccermod_training_personal.json";

    private sealed class PersonalCannonSettingsEntry
    {
        public ulong SteamId64 { get; set; }
        public float Randomness { get; set; } = DefaultCannonRandomness;
        public float FireRate { get; set; } = DefaultCannonFireRate;
        public float Power { get; set; } = DefaultCannonPower;
    }

    private sealed class PersonalCannonSettingsStore
    {
        public int Version { get; set; } = 1;
        public List<PersonalCannonSettingsEntry> Entries { get; set; } = new();
    }

    private PersonalCannonSettingsStore _personalCannonStore = new();

    private readonly Dictionary<uint, TrainingBall> _trainingBalls = new();
    private readonly Dictionary<int, PersonalCannonState> _personalCannons = new();
    private readonly Dictionary<int, double> _trainingSpawnCooldownUntil = new();

    private Vector? _cannonPosition;
    private Vector? _cannonAim;
    private Timer? _cannonTimer;
    private int _cannonMissedShots;
    private float _cannonFireRate = DefaultCannonFireRate;
    private float _cannonPower = DefaultCannonPower;
    private float _cannonRandomness = DefaultCannonRandomness;

    private bool _trainingGoalsDisabled;

    private void TrainingOnLoad()
    {
        _personalCannonStore = LoadJsonOrNull<PersonalCannonSettingsStore>(TrainingPersonalFileName) ?? new PersonalCannonSettingsStore();
        AddCommand("css_training", "Opens the Soccer Mod training menu.", OnTrainingCommand);
        RegisterListener<Listeners.OnClientDisconnect>(TrainingOnPlayerDisconnect);
    }

    // --- playable-ball seam -------------------------------------------------
    private IEnumerable<PlayableBall> PlayableBalls()
    {
        if (_ball is { IsValid: true }
            && _ball.Entity?.Name == OwnedBallTargetName
            && _ball.AbsOrigin is { } matchOrigin)
        {
            yield return new PlayableBall(_ball, matchOrigin, true, _derivedBallVelocity, _playersPushingBall);
        }

        foreach (var training in _trainingBalls.Values.ToArray())
        {
            if (!training.Entity.IsValid || training.Entity.AbsOrigin is not { } origin)
            {
                continue;
            }

            yield return new PlayableBall(training.Entity, origin, false, training.DerivedVelocity, training.PushingSlots);
        }
    }

    private bool IsTrainingBallIndex(uint index) => _trainingBalls.ContainsKey(index);

    // Called every tick right after UpdateDerivedMotion (same formula).
    private void UpdateTrainingBallMotion()
    {
        if (_trainingBalls.Count == 0)
        {
            return;
        }

        var now = (double)Server.TickedTime;
        foreach (var training in _trainingBalls.Values.ToArray())
        {
            if (!training.Entity.IsValid || training.Entity.AbsOrigin is not { } origin)
            {
                _trainingBalls.Remove(training.Index);
                continue;
            }

            if (training.PreviousOrigin is { } previous)
            {
                var elapsed = now - training.PreviousSampleTime;
                if (elapsed > 0.000001)
                {
                    training.DerivedVelocity = new Vector(
                        (float)((origin.X - previous.X) / elapsed),
                        (float)((origin.Y - previous.Y) / elapsed),
                        (float)((origin.Z - previous.Z) / elapsed));
                }
            }

            training.PreviousOrigin = new Vector(origin.X, origin.Y, origin.Z);
            training.PreviousSampleTime = now;
        }
    }

    private TrainingBall? FindTrainingBall(int ownerSlot) =>
        _trainingBalls.Values.FirstOrDefault(t => t.OwnerSlot == ownerSlot && t.Entity.IsValid);

    private void RemoveTrainingBall(TrainingBall training, string reason)
    {
        if (training.Entity.IsValid)
        {
            training.Entity.AcceptInput("Kill");
        }

        _trainingBalls.Remove(training.Index);
        Logger.LogInformation("[SM2DIAG] training_ball_removed owner={Owner} index={Index} reason={Reason}", training.OwnerSlot, training.Index, reason);
    }

    private void RemoveAllTrainingBalls(string reason)
    {
        foreach (var training in _trainingBalls.Values.ToArray())
        {
            RemoveTrainingBall(training, reason);
        }
        _trainingBalls.Clear();
    }

    // Spawn recipe: prop_physics_multiplayer with the live match ball's
    // model set in the KeyValues BEFORE DispatchSpawn (the Rubikon body is
    // built from the model at spawn - SetModel afterwards is too late).
    // Never called from inside OnRoundStart (client visibility risk in the
    // restart frame, see docs/2026-08-31-session-handoff.md).
    private bool TrySpawnTrainingBall(CCSPlayerController owner, Vector origin, string reason)
    {
        if (!string.Equals(_currentMapName, FoundationMapName, StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogWarning("[SM2DIAG] training_ball_spawn_refused reason=wrong_map map={Map}", _currentMapName);
            return false;
        }

        var model = _ball is { IsValid: true }
            ? _ball.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName
            : null;
        if (string.IsNullOrEmpty(model))
        {
            model = BallVisualModelName;
        }

        var ball = Utilities.CreateEntityByName<CPhysicsPropMultiplayer>(BallDesignerName);
        if (ball is null || !ball.IsValid)
        {
            Logger.LogWarning("[SM2DIAG] training_ball_spawn_failed stage=create owner={Owner}", owner.Slot);
            return false;
        }

        var name = $"{TrainingBallTargetPrefix}{owner.Slot}";
        using (var keyValues = new CEntityKeyValues())
        {
            keyValues.SetString("targetname", name);
            keyValues.SetString("model", model);
            keyValues.SetUInt("spawnflags", _ball is { IsValid: true } ? _ball.Spawnflags : 1u);
            keyValues.SetInt("physicsmode", 1);
            keyValues.SetVector("origin", origin);
            keyValues.SetAngle("angles", new QAngle(0.0f, 0.0f, 0.0f));
            ball.DispatchSpawn(keyValues);
        }

        if (!ball.IsValid)
        {
            Logger.LogWarning("[SM2DIAG] training_ball_spawn_failed stage=dispatch owner={Owner}", owner.Slot);
            return false;
        }

        ball.Entity!.Name = name;
        ball.AcceptInput("EnableCollision");
        ball.AcceptInput("EnableMotion");
        ApplyGameplayPhysicsProfile(ball, reason);
        ball.Teleport(position: origin, angles: new QAngle(0.0f, 0.0f, 0.0f), velocity: new Vector(0.0f, 0.0f, 0.0f));
        ball.AcceptInput("Wake");
        _trainingBalls[ball.Index] = new TrainingBall { Entity = ball, Index = ball.Index, OwnerSlot = owner.Slot };
        Logger.LogInformation(
            "[SM2DIAG] training_ball_spawned owner={Owner} index={Index} model={Model} origin={Origin} reason={Reason}",
            owner.Slot,
            ball.Index,
            model,
            FormatVector(origin),
            reason);
        return true;
    }

    // --- lifecycle ---------------------------------------------------------
    private void TrainingOnRoundStart()
    {
        // mp_restartgame wipes every runtime entity; SoMoE also switched all
        // personal cannons off on round start.
        _trainingBalls.Clear();
        StopCannon("round_start", zeroBall: false);
        foreach (var slot in _personalCannons.Keys.ToArray())
        {
            StopPersonalCannon(slot, "round_start", killBall: false);
        }
    }

    private void TrainingOnMapStart()
    {
        _trainingBalls.Clear();
        _personalCannons.Clear();
        _trainingSpawnCooldownUntil.Clear();
        _cannonTimer?.Kill();
        _cannonTimer = null;
        _cannonPosition = null;
        _cannonAim = null;
        _trainingGoalsDisabled = false;
    }

    private void TrainingOnUnload()
    {
        StopCannon("unload", zeroBall: false);
        foreach (var slot in _personalCannons.Keys.ToArray())
        {
            StopPersonalCannon(slot, "unload", killBall: true);
        }
        RemoveAllTrainingBalls("unload");
    }

    private void TrainingOnMatchStart()
    {
        StopCannon("match_start", zeroBall: false);
        foreach (var slot in _personalCannons.Keys.ToArray())
        {
            StopPersonalCannon(slot, "match_start", killBall: true);
        }
        RemoveAllTrainingBalls("match_start");
    }

    private void TrainingOnPlayerDisconnect(int slot)
    {
        StopPersonalCannon(slot, "disconnect", killBall: true);
        _personalCannons.Remove(slot);
        _trainingSpawnCooldownUntil.Remove(slot);
        if (FindTrainingBall(slot) is { } ball)
        {
            RemoveTrainingBall(ball, "owner_disconnected");
        }
    }

    // --- helpers -----------------------------------------------------------
    private static void TrainingChat(CCSPlayerController player, string message) =>
        player.PrintToChat($" \x04[SM]\x01 {message}");

    private static string TrainingNumber(float value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture);

    // GetAimOrigin equivalent: the world point under the crosshair.
    private bool TryGetAimHitPoint(CCSPlayerController player, out Vector hit)
    {
        hit = new Vector(0.0f, 0.0f, 0.0f);
        var pawn = player.PlayerPawn.Value;
        if (pawn is not { IsValid: true } || pawn.AbsOrigin is not { } origin)
        {
            return false;
        }

        var viewOffset = pawn.ViewOffset;
        var eye = new Vector(origin.X + viewOffset.X, origin.Y + viewOffset.Y, origin.Z + viewOffset.Z);
        var angles = pawn.V_angle;
        var pitchRadians = angles.X * (MathF.PI / 180.0f);
        var yawRadians = angles.Y * (MathF.PI / 180.0f);
        var cosPitch = MathF.Cos(pitchRadians);
        var forward = new Vector(
            cosPitch * MathF.Cos(yawRadians),
            cosPitch * MathF.Sin(yawRadians),
            -MathF.Sin(pitchRadians));
        var end = new Vector(
            eye.X + forward.X * TrainingAimTraceDistance,
            eye.Y + forward.Y * TrainingAimTraceDistance,
            eye.Z + forward.Z * TrainingAimTraceDistance);
        var trace = Trace.TraceEndShape(eye, end, pawn, new TraceOptions { InteractsWith = Masks.Solid });
        hit = trace.DidHit()
            ? new Vector(trace.EndPos.X, trace.EndPos.Y, trace.EndPos.Z)
            : end;
        return true;
    }

    // training.sp TrainingCannonShoot maths: (aim - position), per-axis
    // jitter of +-randomness/2, scaled by power. SoMoE's default power
    // 10000 was effectively "sv_maxvelocity"; the ball's configured speed
    // ceiling plays that role here.
    private Vector CannonVelocity(Vector position, Vector aim, float randomness, float power)
    {
        var x = aim.X - position.X + randomness / 2.0f - randomness * Random.Shared.NextSingle();
        var y = aim.Y - position.Y + randomness / 2.0f - randomness * Random.Shared.NextSingle();
        var z = aim.Z - position.Z + randomness / 2.0f - randomness * Random.Shared.NextSingle();
        x *= power;
        y *= power;
        z *= power;
        var speed = MathF.Sqrt(x * x + y * y + z * z);
        if (speed > _kickMaximumBallSpeed && speed > 0.0f)
        {
            var scale = _kickMaximumBallSpeed / speed;
            x *= scale;
            y *= scale;
            z *= scale;
        }

        return new Vector(x, y, z);
    }

    private bool TrainingRequireTeamAlive(CCSPlayerController player)
    {
        if (IsEligiblePlayer(player))
        {
            return true;
        }

        TrainingChat(player, "You have to be in a team to use this option.");
        return false;
    }

    private bool TrainingHasAccess(CCSPlayerController player) =>
        HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0UL, "admin");

    private void OnTrainingCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true })
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        if (!TrainingHasAccess(player))
        {
            command.ReplyToCommand("[SM] You are not allowed to use this command");
            return;
        }

        OpenTrainingMenu(player);
    }

    // --- menus (training.sp OpenTrainingMenu and friends) -------------------
    private void OpenTrainingMenu(CCSPlayerController player)
    {
        if (!TrainingHasAccess(player))
        {
            TrainingChat(player, "You are not allowed to use this command");
            return;
        }

        var menu = new NumberMenu { Title = "Soccer Mod - Admin - Training", OnBack = OpenAdminMenu };
        menu.Add("Cannon", p => TrainingGuard(p, OpenTrainingCannonMenu));
        menu.Add("Personal Cannon", p => TrainingGuard(p, OpenPersonalCannonMenu));
        menu.Add("Shot Drills / Replay", p => TrainingGuard(p, OpenTrainingDrillsMenu));
        menu.Add(_trainingGoalsDisabled ? "Enable Goals" : "Disable Goals", p => TrainingGuard(p, pl =>
        {
            _trainingGoalsDisabled = !_trainingGoalsDisabled;
            AnnounceAll($" \x04[SM]\x01 {pl.PlayerName} has {(_trainingGoalsDisabled ? "disabled" : "enabled")} the goals");
            Logger.LogInformation("[SM2DIAG] training_goals disabled={Disabled} by={By}", _trainingGoalsDisabled, pl.PlayerName);
            OpenTrainingMenu(pl);
        }));
        menu.Add("Spawn/Remove Ball", p => TrainingGuard(p, pl =>
        {
            if (pl.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist) || !IsAlive(pl.PlayerPawn.Value))
            {
                TrainingChat(pl, "Only alive players can spawn a ball.");
            }
            else
            {
                TrainingSpawnBall(pl);
            }
            OpenTrainingMenu(pl);
        }));
        OpenNumberMenu(player, menu);
    }

    // training.sp TrainingMenuHandler: everything except "Cannon off" is
    // refused while a match is running.
    private void TrainingGuard(CCSPlayerController player, Action<CCSPlayerController> action)
    {
        if (MatchRunning)
        {
            TrainingChat(player, "You can not use this option during a match");
            OpenTrainingMenu(player);
            return;
        }

        action(player);
    }

    private void OpenTrainingCannonMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Admin - Training - Cannon", OnBack = OpenTrainingMenu };
        menu.Add("Set cannon position", p => CannonMenuAction(p, OpenTrainingCannonMenu, TrainingCannonPosition));
        menu.Add("Set cannon aim", p => CannonMenuAction(p, OpenTrainingCannonMenu, TrainingCannonAim));
        menu.Add("Cannon on", p => CannonMenuAction(p, OpenTrainingCannonMenu, TrainingCannonOn));
        menu.Add("Cannon off", p =>
        {
            if (TrainingRequireTeamAlive(p))
            {
                TrainingCannonOff(p);
            }
            OpenTrainingCannonMenu(p);
        });
        menu.Add("Settings", p => OpenTrainingCannonSettingsMenu(p));
        OpenNumberMenu(player, menu);
    }

    private void CannonMenuAction(CCSPlayerController player, Action<CCSPlayerController> reopen, Action<CCSPlayerController> action)
    {
        if (MatchRunning)
        {
            TrainingChat(player, "You can not use this option during a match");
        }
        else if (TrainingRequireTeamAlive(player))
        {
            action(player);
        }

        reopen(player);
    }

    private void OpenTrainingCannonSettingsMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Admin - Training - Cannon - Settings", OnBack = OpenTrainingCannonMenu };
        menu.Add($"Randomness: {TrainingNumber(_cannonRandomness, "F0")}", p => BeginChatNumberInput(
            p,
            "Cannon randomness",
            CannonRandomnessMin,
            CannonRandomnessMax,
            (pl, value) =>
            {
                _cannonRandomness = value;
                AnnounceAll($" \x04[SM]\x01 {pl.PlayerName} has set the cannon randomness to {TrainingNumber(value, "F1")}");
                ReopenNextFrame(pl, OpenTrainingCannonSettingsMenu);
            },
            pl => OpenTrainingCannonSettingsMenu(pl)));
        menu.Add($"Fire rate: {TrainingNumber(_cannonFireRate, "F1")}", p => BeginChatNumberInput(
            p,
            "Cannon fire rate (seconds between shots)",
            CannonFireRateMin,
            CannonFireRateMax,
            (pl, value) =>
            {
                _cannonFireRate = value;
                if (_cannonTimer is not null)
                {
                    RestartCannonTimer();
                }
                AnnounceAll($" \x04[SM]\x01 {pl.PlayerName} has set the cannon fire rate to {TrainingNumber(value, "F1")}");
                ReopenNextFrame(pl, OpenTrainingCannonSettingsMenu);
            },
            pl => OpenTrainingCannonSettingsMenu(pl)));
        menu.Add($"Power: {TrainingNumber(_cannonPower, "F3")}", p => BeginChatNumberInput(
            p,
            "Cannon power",
            CannonPowerMin,
            CannonPowerMax,
            (pl, value) =>
            {
                _cannonPower = value;
                AnnounceAll($" \x04[SM]\x01 {pl.PlayerName} has set the cannon power to {TrainingNumber(value, "F3")}");
                ReopenNextFrame(pl, OpenTrainingCannonSettingsMenu);
            },
            pl => OpenTrainingCannonSettingsMenu(pl)));
        OpenNumberMenu(player, menu);
    }

    private void OpenPersonalCannonMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Admin - Training - Personal Cannon", OnBack = OpenTrainingMenu };
        menu.Add("Set cannon position", p => CannonMenuAction(p, OpenPersonalCannonMenu, PersonalCannonPosition));
        menu.Add("Set cannon aim", p => CannonMenuAction(p, OpenPersonalCannonMenu, PersonalCannonAim));
        menu.Add("Cannon on", p => CannonMenuAction(p, OpenPersonalCannonMenu, PersonalCannonOn));
        menu.Add("Cannon off", p =>
        {
            if (TrainingRequireTeamAlive(p))
            {
                PersonalCannonOff(p);
            }
            OpenPersonalCannonMenu(p);
        });
        menu.Add("Settings", p => OpenPersonalCannonSettingsMenu(p));
        OpenNumberMenu(player, menu);
    }

    private void OpenPersonalCannonSettingsMenu(CCSPlayerController player)
    {
        var state = GetPersonalCannon(player);
        var menu = new NumberMenu { Title = "Soccer Mod - Admin - Training - Cannon - Personal Settings", OnBack = OpenPersonalCannonMenu };
        menu.Add($"Randomness: {TrainingNumber(state.Randomness, "F0")}", p => BeginChatNumberInput(
            p,
            "Personal cannon randomness",
            CannonRandomnessMin,
            CannonRandomnessMax,
            (pl, value) =>
            {
                GetPersonalCannon(pl).Randomness = value;
                SavePersonalCannonSettings(pl);
                TrainingChat(pl, $"Personal cannon randomness set to {TrainingNumber(value, "F1")}");
                ReopenNextFrame(pl, OpenPersonalCannonSettingsMenu);
            },
            pl => OpenPersonalCannonSettingsMenu(pl)));
        menu.Add($"Fire rate: {TrainingNumber(state.FireRate, "F1")}", p => BeginChatNumberInput(
            p,
            "Personal cannon fire rate (seconds between shots)",
            CannonFireRateMin,
            CannonFireRateMax,
            (pl, value) =>
            {
                var s = GetPersonalCannon(pl);
                s.FireRate = value;
                if (s.Timer is not null)
                {
                    RestartPersonalCannonTimer(pl.Slot, s);
                }
                SavePersonalCannonSettings(pl);
                TrainingChat(pl, $"Personal cannon fire rate set to {TrainingNumber(value, "F1")}");
                ReopenNextFrame(pl, OpenPersonalCannonSettingsMenu);
            },
            pl => OpenPersonalCannonSettingsMenu(pl)));
        menu.Add($"Power: {TrainingNumber(state.Power, "F3")}", p => BeginChatNumberInput(
            p,
            "Personal cannon power",
            CannonPowerMin,
            CannonPowerMax,
            (pl, value) =>
            {
                GetPersonalCannon(pl).Power = value;
                SavePersonalCannonSettings(pl);
                TrainingChat(pl, $"Personal cannon power set to {TrainingNumber(value, "F3")}");
                ReopenNextFrame(pl, OpenPersonalCannonSettingsMenu);
            },
            pl => OpenPersonalCannonSettingsMenu(pl)));
        OpenNumberMenu(player, menu);
    }

    // --- global cannon -----------------------------------------------------
    private void TrainingCannonPosition(CCSPlayerController player)
    {
        if (!TryGetAimHitPoint(player, out var hit))
        {
            return;
        }

        _cannonPosition = new Vector(hit.X, hit.Y, hit.Z + TrainingCannonPositionLift);
        AnnounceAll($" \x04[SM]\x01 {player.PlayerName} has set the cannon position");
        Logger.LogInformation("[SM2DIAG] training_cannon_position by={By} position={Position}", player.PlayerName, FormatVector(_cannonPosition));
    }

    private void TrainingCannonAim(CCSPlayerController player)
    {
        if (!TryGetAimHitPoint(player, out var hit))
        {
            return;
        }

        _cannonAim = new Vector(hit.X, hit.Y, hit.Z);
        AnnounceAll($" \x04[SM]\x01 {player.PlayerName} has set the cannon aim");
        Logger.LogInformation("[SM2DIAG] training_cannon_aim by={By} aim={Aim}", player.PlayerName, FormatVector(_cannonAim));
    }

    private void TrainingCannonOn(CCSPlayerController player)
    {
        if (_cannonTimer is not null)
        {
            TrainingChat(player, "Cannon is already on");
            return;
        }

        if (_cannonPosition is null || _cannonAim is null)
        {
            TrainingChat(player, "Set the cannon position and aim first");
            return;
        }

        if (!BindBall("cannon_on") || _ball is not { IsValid: true })
        {
            TrainingChat(player, "Ball cannon entity is invalid");
            return;
        }

        _cannonMissedShots = 0;
        AnnounceAll($" \x04[SM]\x01 {player.PlayerName} has turned the cannon on");
        Logger.LogInformation("[SM2DIAG] training_cannon_on by={By} fireRate={FireRate:F1} power={Power:F3} randomness={Randomness:F0}", player.PlayerName, _cannonFireRate, _cannonPower, _cannonRandomness);
        // First shot immediately (SoMoE: CreateTimer(0.0, ...)).
        TrainingCannonShoot();
        RestartCannonTimer();
    }

    private void RestartCannonTimer()
    {
        _cannonTimer?.Kill();
        _cannonTimer = AddTimer(_cannonFireRate, TrainingCannonShoot, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void TrainingCannonOff(CCSPlayerController player)
    {
        if (_cannonTimer is null)
        {
            TrainingChat(player, "Cannon is not on");
            return;
        }

        StopCannon("menu_off", zeroBall: true);
        AnnounceAll($" \x04[SM]\x01 {player.PlayerName} has turned the cannon off");
    }

    private void StopCannon(string reason, bool zeroBall)
    {
        if (_cannonTimer is null)
        {
            return;
        }

        _cannonTimer.Kill();
        _cannonTimer = null;
        if (zeroBall && _ball is { IsValid: true })
        {
            _ball.Teleport(velocity: new Vector(0.0f, 0.0f, 0.0f));
        }

        Logger.LogInformation("[SM2DIAG] training_cannon_off reason={Reason}", reason);
    }

    private void TrainingCannonShoot()
    {
        if (_cannonTimer is null && _cannonMissedShots < 0)
        {
            return;
        }

        if (_cannonPosition is not { } position || _cannonAim is not { } aim)
        {
            StopCannon("no_aim", zeroBall: false);
            return;
        }

        if (!BindBall("cannon") || _ball is not { IsValid: true } || _ball.Entity?.Name != OwnedBallTargetName)
        {
            // Round restart nulls and re-promotes the ball for a frame or
            // two - skip the shot; give up only if it stays gone.
            _cannonMissedShots++;
            if (_cannonMissedShots >= TrainingCannonMaxMissedShots)
            {
                AnnounceAll(" \x04[SM]\x01 Ball cannon entity is invalid");
                StopCannon("ball_invalid", zeroBall: false);
            }
            return;
        }

        _cannonMissedShots = 0;
        var velocity = CannonVelocity(position, aim, _cannonRandomness, _cannonPower);
        UnfreezeBallForPlay("cannon");
        _ball.AcceptInput("Wake");
        _ball.Teleport(position: position, angles: new QAngle(0.0f, 0.0f, 0.0f), velocity: velocity);
        // The teleport must not read as a movement segment (a ball resting
        // in the net teleported to the cannon would "cross" the goal plane
        // inside the aperture) - same reset OnGoalTestCommand does.
        ResetDerivedMotion();
    }

    // --- personal cannon ---------------------------------------------------
    private PersonalCannonState GetPersonalCannon(CCSPlayerController player)
    {
        if (_personalCannons.TryGetValue(player.Slot, out var state))
        {
            return state;
        }

        state = new PersonalCannonState();
        var steamId = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        if (steamId != 0 && _personalCannonStore.Entries.FirstOrDefault(e => e.SteamId64 == steamId) is { } stored)
        {
            if (stored.Randomness is >= CannonRandomnessMin and <= CannonRandomnessMax) state.Randomness = stored.Randomness;
            if (stored.FireRate is >= CannonFireRateMin and <= CannonFireRateMax) state.FireRate = stored.FireRate;
            if (stored.Power is >= CannonPowerMin and <= CannonPowerMax) state.Power = stored.Power;
        }

        _personalCannons[player.Slot] = state;
        return state;
    }

    private void SavePersonalCannonSettings(CCSPlayerController player)
    {
        var steamId = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        if (steamId == 0 || !_personalCannons.TryGetValue(player.Slot, out var state))
        {
            return;
        }

        var entry = _personalCannonStore.Entries.FirstOrDefault(e => e.SteamId64 == steamId);
        if (entry is null)
        {
            entry = new PersonalCannonSettingsEntry { SteamId64 = steamId };
            _personalCannonStore.Entries.Add(entry);
        }

        entry.Randomness = state.Randomness;
        entry.FireRate = state.FireRate;
        entry.Power = state.Power;
        SaveJsonAtomic(TrainingPersonalFileName, _personalCannonStore);
    }

    private void PersonalCannonPosition(CCSPlayerController player)
    {
        if (!TryGetAimHitPoint(player, out var hit))
        {
            return;
        }

        GetPersonalCannon(player).Position = new Vector(hit.X, hit.Y, hit.Z + TrainingCannonPositionLift);
        TrainingChat(player, "Set your personal cannon position");
    }

    private void PersonalCannonAim(CCSPlayerController player)
    {
        if (!TryGetAimHitPoint(player, out var hit))
        {
            return;
        }

        GetPersonalCannon(player).Aim = new Vector(hit.X, hit.Y, hit.Z);
        TrainingChat(player, "Set your personal cannon aim");
    }

    private void PersonalCannonOn(CCSPlayerController player)
    {
        var state = GetPersonalCannon(player);
        if (state.Timer is not null)
        {
            TrainingChat(player, "Cannon is already on");
            return;
        }

        if (state.Position is not { } position || state.Aim is null)
        {
            TrainingChat(player, "Set your personal cannon position and aim first");
            return;
        }

        if (FindTrainingBall(player.Slot) is null
            && !TrySpawnTrainingBall(player, new Vector(position.X, position.Y, position.Z), "personal_cannon"))
        {
            TrainingChat(player, "Training ball could not be spawned");
            return;
        }

        state.MissedShots = 0;
        TrainingChat(player, "Your personal cannon is on");
        Logger.LogInformation("[SM2DIAG] training_personal_cannon_on slot={Slot}", player.Slot);
        var slot = player.Slot;
        PersonalCannonShoot(slot);
        RestartPersonalCannonTimer(slot, state);
    }

    private void RestartPersonalCannonTimer(int slot, PersonalCannonState state)
    {
        state.Timer?.Kill();
        state.Timer = AddTimer(state.FireRate, () => PersonalCannonShoot(slot), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void PersonalCannonOff(CCSPlayerController player)
    {
        if (!_personalCannons.TryGetValue(player.Slot, out var state) || state.Timer is null)
        {
            TrainingChat(player, "Your personal cannon is not on");
            return;
        }

        StopPersonalCannon(player.Slot, "menu_off", killBall: true);
        TrainingChat(player, "Your personal cannon is off");
    }

    private void StopPersonalCannon(int slot, string reason, bool killBall)
    {
        if (_personalCannons.TryGetValue(slot, out var state))
        {
            state.Timer?.Kill();
            state.Timer = null;
        }

        if (killBall && FindTrainingBall(slot) is { } ball)
        {
            RemoveTrainingBall(ball, $"personal_cannon_{reason}");
        }
    }

    private void PersonalCannonShoot(int slot)
    {
        if (!_personalCannons.TryGetValue(slot, out var state) || state.Position is not { } position || state.Aim is not { } aim)
        {
            StopPersonalCannon(slot, "no_aim", killBall: false);
            return;
        }

        var training = FindTrainingBall(slot);
        if (training is null)
        {
            state.MissedShots++;
            if (state.MissedShots >= TrainingCannonMaxMissedShots)
            {
                if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } owner)
                {
                    TrainingChat(owner, "Ball cannon entity is invalid");
                }
                StopPersonalCannon(slot, "ball_invalid", killBall: false);
            }
            return;
        }

        state.MissedShots = 0;
        var velocity = CannonVelocity(position, aim, state.Randomness, state.Power);
        training.Entity.AcceptInput("Wake");
        _contacts.Remove(training.Entity.EntityHandle.Raw);
        training.Entity.Teleport(position: position, angles: new QAngle(0.0f, 0.0f, 0.0f), velocity: velocity);
        training.PreviousOrigin = null;
        training.DerivedVelocity = new Vector(0.0f, 0.0f, 0.0f);
        training.PreviousImpactOrigin = null;
        training.PreviousImpactVelocity = null;
    }

    // --- Spawn/Remove Ball (training.sp TrainingSpawnBall) -----------------
    private void TrainingSpawnBall(CCSPlayerController player)
    {
        var now = (double)Server.TickedTime;
        if (_trainingSpawnCooldownUntil.TryGetValue(player.Slot, out var until) && until > now)
        {
            TrainingChat(player, $"Spawning a ball is on cooldown, {(int)Math.Ceiling(until - now)} seconds left.");
            return;
        }

        // A running personal cannon is stopped first (shared ball entity).
        if (_personalCannons.TryGetValue(player.Slot, out var state) && state.Timer is not null)
        {
            StopPersonalCannon(player.Slot, "spawn_toggle", killBall: false);
            _trainingSpawnCooldownUntil[player.Slot] = now + TrainingSpawnCooldownSeconds;
        }

        if (FindTrainingBall(player.Slot) is { } existing)
        {
            RemoveTrainingBall(existing, "spawn_toggle");
            _trainingSpawnCooldownUntil[player.Slot] = now + TrainingSpawnCooldownSeconds;
            TrainingChat(player, "Your training ball was removed");
            return;
        }

        if (!TryGetAimHitPoint(player, out var hit))
        {
            return;
        }

        var origin = new Vector(hit.X, hit.Y, hit.Z + BallCollisionRadius + 1.0f);
        if (!TrySpawnTrainingBall(player, origin, "spawn_menu"))
        {
            TrainingChat(player, "Training ball could not be spawned");
            return;
        }

        TrainingChat(player, "Training ball spawned");
    }
}

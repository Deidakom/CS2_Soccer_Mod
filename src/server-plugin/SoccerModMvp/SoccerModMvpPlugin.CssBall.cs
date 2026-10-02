using Stopwatch = System.Diagnostics.Stopwatch;
using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using V3 = System.Numerics.Vector3;

namespace SoccerModMvp;

// 2026-10-01 owner: "replicate the CS:S ball 1:1 ... the player model collision is very important
// as well as you have a lot of duels".
//
// The CS:S ball: with the switch on, the match ball is no longer simulated by CS2. A helper process
// (CssBallHost.cs, tools/css-vphysics-host) runs the ball on the CS:S server's own physics library,
// inside the CS:S stadium's collision, together with what CS:S did between player and ball: the
// player's move against the ball, his physics shadow pushing it, the ball pushing him back, and the
// knife. That code was checked against a live CS:S server until both agreed in every bit.
//
// Every tick the plugin tells the helper where the players are and what they press, the helper runs
// one tick, and the plugin puts the CS2 ball where the CS:S ball is (the CS2 entity is held still and
// moved, CS2 physics do not touch it) and applies what the ball did to the players.
//
// Everything the CS2 ball code adds on its own (body push, knife kick model, rolling, bounce, wall
// and landing assists, spin, net pocket) stays away from the match ball while this is on; training
// and cannon balls remain ordinary CS2 balls.
public sealed partial class SoccerModMvpPlugin
{
    private const string CssBallFlagFile = "soccermod_css_ball.enabled";
    private const string CssBallSettingsFile = "soccermod_css_ball.json";
    // func_physbox "ballon" of ka_soccer_xsl_stadium_b1: 15 units radius, placed 17 above the pitch
    // and asleep until something touches it.
    private const float CssBallRadius = 15.0f;
    private const float CssBallSpawnHeight = 17.0f;
    // CS2 height minus CS:S height of the same spot: the CS:S pitch is at 0, the v8 pitch at -32.
    private const float CssBallHeightOffset = -32.0f;
    // The v8 pitch has its goals 7 units closer to the centre line than the CS:S stadium; the helper
    // moves the CS:S goals (frame, nets, goal triggers) by that much. A map with its goals where
    // CS:S has them (the arena, GoalShiftY) gets the CS:S collision as it is.
    private const float CssBallGoalShift = 7.0f;
    private const float CssBallKnifeSpeed = 250.0f;
    private const int CssBallMaxFailures = 3;

    private sealed class CssBallSettings
    {
        public string Directory { get; set; } = "/home/gameserver/cssball";
        public string Map { get; set; } = "ka_soccer_xsl_stadium_b1.bsp";
        // The CS:S result replaces the CS2 player's own move at the ball when they differ by more.
        public float PositionTolerance { get; set; } = 0.25f;
        public float VelocityTolerance { get; set; } = 2.0f;
        // Moving an entity normally tells the clients not to blend the step; this keeps them blending.
        public bool SmoothTeleports { get; set; } = true;
        // CS:S movement values on the server while the CS:S ball is on.
        public bool CssMovement { get; set; } = true;
        // The CS2 knife follows the rhythm of the CS:S knife that hits the ball.
        public bool SyncKnife { get; set; } = true;
        public int StepTimeoutMs { get; set; } = 250;
    }

    private sealed class CssBallPlayer
    {
        public uint Pawn;
        public CCSPlayerPawn? Entity;
        public V3 Origin;           // where the plugin last saw or put him
        public int Tick = -1000;    // ... and when
        public int Pressed;         // keys pressed since the last tick (a tap shorter than a tick still counts)
        public bool Resync;         // CS2 did not take the helper's position: the helper starts over from CS2's
        public float Modifier = 1.0f;   // the CS:S slowdown after a hard hit by the ball
        public float Sprint = 1.0f;     // the sprint factor his last move ran with
        public bool ModifierApplied;
        public bool Ducked;
    }

    private static readonly (string Cvar, string Helper, float Css)[] CssBallMoveCvars =
    {
        ("sv_accelerate", "accelerate", 5.0f),
        ("sv_airaccelerate", "airaccelerate", 10.0f),
        ("sv_friction", "friction", 4.0f),
        ("sv_stopspeed", "stopspeed", 75.0f),
    };

    private CssBallSettings _cssBallSettings = new();
    private CssBallHost? _cssBall;
    private bool _cssBallReady;
    private string _cssBallMap = "";
    private long _cssBallStartedAt, _cssBallRetryAt;
    private int _cssBallFailures;
    private string _cssBallStatus = "off";
    private uint _cssBallEntity;
    private bool _cssBallCarryOver;
    private (V3 Origin, V3 Angles, V3 Velocity)? _cssBallWritten;
    private V3 _cssBallSpin;
    private bool _cssBallAsleep;
    private bool _cssBallFrozen;
    private (V3 Velocity, V3 Spin)? _cssBallHeld;
    private bool _cssBallRestoreHeld, _cssBallStopRequested;
    private readonly Dictionary<int, CssBallPlayer> _cssBallPlayers = new();
    private readonly Dictionary<string, float> _cssBallCvarsBefore = new();
    private readonly Dictionary<string, float> _cssBallSentSettings = new();
    private readonly Dictionary<string, int> _cssBallCvarAttempts = new();
    private readonly HashSet<int> _cssBallTouching = new();
    private double _cssBallStepMsSum, _cssBallStepMsMax;
    private int _cssBallSteps, _cssBallPlayerMoves, _cssBallKicks;
    private bool _cssBallInterpolationBroken;
    private bool _cssBallSolid = true;
    // Server-side tests (css_sm2cssball drive): a bot that runs in a straight line holding forward,
    // with every tick of it and of the ball written to cssball_drive.csv.
    private sealed record CssBallDrive(float Yaw, float Pitch, float Speed, int Until, int Buttons);
    private readonly Dictionary<int, CssBallDrive> _cssBallDrives = new();
    private readonly List<(int Slot, V3 Origin, V3 Velocity, int Flags, int Buttons)> _cssBallDriveSeen = new();
    private StreamWriter? _cssBallDriveLog;
    private int _cssBallDriveLogUntil;

    // The helper moves the match ball: the CS2 ball code keeps its hands off it.
    private bool CssBallActive => _cssBall is not null && _cssBallReady;
    private bool CssBallWanted => FlagFileOn(CssBallFlagFile);
    // Every ball has the CS:S size while the CS:S ball is on (BallSize.cs scales look and collision).
    private float EffectiveBallSize => CssBallActive ? CssBallRadius / DefaultBallCollisionRadius : _ballSize;

    private void CssBallOnLoad()
    {
        _cssBallSettings = LoadJsonOrNull<CssBallSettings>(CssBallSettingsFile) ?? new CssBallSettings();
        AddCommand("css_sm2cssball", "Root: the CS:S ball (on|off|status|probe|tolerance <units> <speed>|movement on|off|smooth on|off|knife on|off).", OnCssBallCommand);
        RegisterListener<Listeners.OnClientDisconnect>(slot => _cssBallPlayers.Remove(slot));
        RegisterListener<Listeners.OnMapEnd>(() =>
        {
            CssBallStop("map end", release: false);         // the entities go with the map
            _cssBallRetryAt = Environment.TickCount64 + 3000;   // not during the level change
        });
    }

    // ---- the helper process ---------------------------------------------------------------

    private bool CssBallManage()
    {
        var wanted = CssBallWanted && IsFoundationMap(_currentMapName) && !FootballMode;
        if (!wanted)
        {
            if (_cssBall is not null) CssBallStop("switched off");
            _cssBallFailures = 0;
            _cssBallStatus = !CssBallWanted ? "off" : FootballMode ? "off (football mode is on)" : "off (not a stadium map)";
            return false;
        }
        if (_cssBall is not null && _cssBallMap != _currentMapName) CssBallStop("map change");
        var now = Environment.TickCount64;
        if (_cssBall is null)
        {
            if (_cssBallMap != _currentMapName) { _cssBallMap = _currentMapName; _cssBallFailures = 0; _cssBallRetryAt = 0; }
            if (_cssBallFailures >= CssBallMaxFailures || now < _cssBallRetryAt) return false;
            CssBallStart(now);
            return false;
        }
        if (_cssBallReady) return true;
        if (_cssBall.PollReady())
        {
            _cssBallReady = true;
            _cssBallCarryOver = true;
            _cssBallEntity = 0;
            _cssBallWritten = null;
            _cssBallFrozen = false;
            _cssBallSentSettings.Clear();
            _cssBallPlayers.Clear();
            _cssBallStepMsSum = _cssBallStepMsMax = 0; _cssBallSteps = 0;
            _cssBall.Setting("posthreshold", _cssBallSettings.PositionTolerance);
            _cssBall.Setting("velthreshold", _cssBallSettings.VelocityTolerance);
            _cssBallStatus = "running";
            Logger.LogInformation("[SM2DIAG] css_ball_ready map={Map} loadMs={Ms} {Greeting}", _currentMapName, now - _cssBallStartedAt, _cssBall.Greeting);
            return true;
        }
        if (_cssBall.Failed) CssBallFail("start: " + _cssBall.LastError);
        else if (now - _cssBallStartedAt > 20000) CssBallFail("start: the helper did not get ready within 20 s");
        return false;
    }

    private void CssBallStart(long now)
    {
        var dir = _cssBallSettings.Directory;
        var executable = Path.Combine(dir, "cssball");
        var map = Path.Combine(dir, _cssBallSettings.Map);
        var bin = Path.Combine(dir, "bin");
        if (!File.Exists(executable) || !File.Exists(map) || !File.Exists(Path.Combine(bin, "vphysics_srv.so")))
        {
            _cssBallFailures = CssBallMaxFailures;
            _cssBallStatus = "not installed on this server";
            Logger.LogWarning("[SM2DIAG] css_ball_not_installed dir={Dir} (needs cssball, bin/vphysics_srv.so and {Map})", dir, _cssBallSettings.Map);
            return;
        }
        var arguments = new[]
        {
            bin, Path.Combine(dir, "surfaceproperties.txt"), Path.Combine(dir, "surfaceproperties_cs.txt"), map,
            CssBallHeightOffset.ToString(CultureInfo.InvariantCulture), (CssBallGoalShift - GoalShiftY).ToString(CultureInfo.InvariantCulture),
        };
        _cssBall = CssBallHost.Start(executable, arguments, bin, out var error);
        _cssBallReady = false;
        _cssBallStartedAt = now;
        if (_cssBall is null) { CssBallFail("start: " + error); return; }
        _cssBallStatus = "starting";
        Logger.LogInformation("[SM2DIAG] css_ball_starting map={Map} goalShift={Shift} helper={Helper}", _currentMapName, CssBallGoalShift - GoalShiftY, executable);
    }

    private void CssBallFail(string reason)
    {
        _cssBallFailures++;
        _cssBallRetryAt = Environment.TickCount64 + 10000;
        var errors = _cssBall?.Errors() ?? "";
        CssBallStop("failure");
        _cssBallStatus = _cssBallFailures >= CssBallMaxFailures ? "stopped after errors: " + reason : "restarting after an error: " + reason;
        Logger.LogError("[SM2DIAG] css_ball_failed failures={Failures} reason={Reason} stderr={Errors}", _cssBallFailures, reason, errors);
    }

    // Stops the helper and hands the ball back to CS2 where it is, with its speed.
    private void CssBallStop(string reason, bool release = true)
    {
        if (_cssBall is null) return;
        var wasReady = _cssBallReady;
        var host = _cssBall;
        _cssBall = null;
        _cssBallReady = false;
        try { host.Dispose(); } catch { }
        _cssBallStatus = "off";
        if (!wasReady) return;
        CssBallRestoreMovement();
        if (!release)
        {
            _cssBallPlayers.Clear(); _cssBallTouching.Clear(); _cssBallWritten = null; _cssBallEntity = 0;
            Logger.LogInformation("[SM2DIAG] css_ball_stopped reason={Reason}", reason);
            return;
        }
        foreach (var (_, track) in _cssBallPlayers)
        {
            if (!track.ModifierApplied || track.Entity is not { IsValid: true } pawn) continue;
            if (pawn.VelocityModifier < 1.0f)
            {
                pawn.VelocityModifier = 1.0f;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
            }
        }
        _cssBallPlayers.Clear();
        _cssBallTouching.Clear();
        var velocity = _cssBallWritten?.Velocity ?? V3.Zero;
        _cssBallWritten = null;
        _cssBallEntity = 0;
        if (_ball is { IsValid: true } ball && ball.Entity?.Name == OwnedBallTargetName)
        {
            _ballMotionFrozen = _pausedBallHandle != 0;
            if (!_cssBallSolid) { ball.AcceptInput("EnableCollision"); _cssBallSolid = true; }
            if (_pausedBallHandle == 0)
            {
                ball.AcceptInput("EnableMotion");
                ball.AcceptInput("Wake");
                ball.Teleport(velocity: C(velocity));
            }
            ResetDerivedMotion(clearTouchHistory: false);
            ApplyGameplayPhysicsProfile(ball, "css_ball_off");
        }
        Logger.LogInformation("[SM2DIAG] css_ball_stopped reason={Reason}", reason);
    }

    // ---- one server tick ------------------------------------------------------------------

    // Called first in OnTick, in place of the CS2 ball sampling (UpdateDerivedMotion).
    private void CssBallTick()
    {
        try
        {
            if (CssBallManage()) CssBallStep();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[SM2DIAG] css_ball_exception");
            CssBallFail("exception: " + ex.Message);
        }
    }

    private void CssBallStep()
    {
        var host = _cssBall!;
        if (_ball is not { IsValid: true } ball || ball.Entity?.Name != OwnedBallTargetName) return;
        var tick = Server.TickCount;

        // the ball entity: a new one (round restart, map start, the switch), or one the game moved
        if (ball.EntityHandle.Raw != _cssBallEntity) CssBallTakeOver(host, ball);
        else if (_cssBallWritten is { } written && ball.AbsOrigin is { } at && V3.DistanceSquared(N(at), written.Origin) > 0.05f * 0.05f)
            CssBallPlacedByGame(host, ball, written);
        if (_cssBallStopRequested)
        {
            _cssBallStopRequested = false;
            if (_cssBallWritten is { } w)
                host.SetBall(w.Origin.X, w.Origin.Y, w.Origin.Z, w.Angles.X, w.Angles.Y, w.Angles.Z, 0, 0, 0, 0, 0, 0, CssBallRestsAt(w.Origin));
        }

        // match pause / admin freeze (PausedBall.cs)
        var frozen = _pausedBallHandle != 0;
        if (frozen != _cssBallFrozen)
        {
            _cssBallFrozen = frozen;
            host.Freeze(frozen);
            if (frozen) _cssBallHeld = _cssBallWritten is { } held ? (held.Velocity, _cssBallSpin) : null;
            else if (_cssBallRestoreHeld && _cssBallHeld is { } motion && _cssBallWritten is { } pose)
                host.SetBall(pose.Origin.X, pose.Origin.Y, pose.Origin.Z, pose.Angles.X, pose.Angles.Y, pose.Angles.Z,
                    motion.Velocity.X, motion.Velocity.Y, motion.Velocity.Z, motion.Spin.X, motion.Spin.Y, motion.Spin.Z, false);
            _cssBallRestoreHeld = false;
        }

        if (tick % 64 == 0 || _cssBallSentSettings.Count == 0) CssBallMovementTick(host);

        // the players as CS2 has them after their move of this tick
        var seen = 0;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsHLTV) continue;
            var slot = player.Slot;
            if (slot < 0 || slot >= 64) continue;
            var pawn = player.PlayerPawn.Value;
            if (pawn is not { IsValid: true } || pawn.AbsOrigin is not { } origin
                || player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist) || !IsAlive(pawn))
            {
                _cssBallPlayers.Remove(slot);
                continue;
            }
            if (!_cssBallPlayers.TryGetValue(slot, out var track)) _cssBallPlayers[slot] = track = new CssBallPlayer();
            var here = N(origin);
            var handle = pawn.EntityHandle.Raw;
            var moved = track.Resync || track.Pawn != handle || track.Tick != tick - 1 || V3.DistanceSquared(here, track.Origin) > 80.0f * 80.0f;
            track.Pawn = handle; track.Entity = pawn; track.Tick = tick; track.Origin = here; track.Resync = false;

            var movement = pawn.MovementServices;
            var humanoid = movement is not null ? new CCSPlayer_MovementServices(movement.Handle) : null;
            var flags = 0;
            if ((pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0) flags |= 1;
            if (humanoid?.Ducked == true) flags |= 2;
            track.Ducked = (flags & 2) != 0;
            if (pawn.ActualMoveType is MoveType_t.MOVETYPE_NOCLIP or MoveType_t.MOVETYPE_OBSERVER) flags |= 4;
            var buttons = CssBallButtonBits(player.Buttons) | track.Pressed;
            track.Pressed = 0;
            var velocity = pawn.AbsVelocity;
            var angles = pawn.EyeAngles;
            // sprint (Sprint.cs) is a speed factor in CS2; CS:S stretched the player's time by it
            var sprint = track.Sprint;
            var weapon = pawn.WeaponServices?.ActiveWeapon.Value;
            var knife = weapon is { IsValid: true } && weapon.DesignerName.Contains("knife", StringComparison.OrdinalIgnoreCase)
                && _matchPhase != MatchPhase.Paused;
            // the team that does not kick off may not reach the ball (KickoffOutline.cs): not there for the ball
            var present = IsKickoffTouchAllowed(player);
            float yaw = angles.Y, pitch = angles.X;
            if (_cssBallDrives.Count > 0 && _cssBallDrives.TryGetValue(slot, out var drive))
            {
                if (tick > drive.Until) _cssBallDrives.Remove(slot);
                else
                {
                    yaw = drive.Yaw; pitch = drive.Pitch; buttons = drive.Buttons;
                    if ((buttons & (1 | 2048)) != 0) knife = true;
                    if (drive.Speed > 0.0f && (flags & 1) != 0)
                    {
                        // what holding forward gives him for his next move: one tick of acceleration, friction made up for
                        var radians = yaw * (MathF.PI / 180.0f);
                        var (dirX, dirY) = (MathF.Cos(radians), MathF.Sin(radians));
                        var along = MathF.Max(0.0f, velocity.X * dirX + velocity.Y * dirY);
                        var friction = ConVar.Find("sv_friction")?.GetPrimitiveValue<float>() ?? 5.2f;
                        var accelerate = ConVar.Find("sv_accelerate")?.GetPrimitiveValue<float>() ?? 5.5f;
                        var next = MathF.Min(drive.Speed, along + accelerate * drive.Speed * Server.TickInterval) / (1.0f - friction * Server.TickInterval);
                        pawn.Teleport(velocity: new Vector(dirX * next, dirY * next, velocity.Z));
                    }
                    _cssBallDriveSeen.Add((slot, here, N(velocity), flags, buttons));
                }
            }
            host.Player(new CssBallHost.PlayerIn(slot, present, here.X, here.Y, here.Z, velocity.X, velocity.Y, velocity.Z, flags, buttons,
                yaw, pitch, humanoid?.DuckAmount ?? 0.0f, CssBallKnifeSpeed, sprint, moved, knife));
            seen++;
        }
        foreach (var slot in _cssBallPlayers.Where(kv => kv.Value.Tick != tick).Select(kv => kv.Key).ToArray()) _cssBallPlayers.Remove(slot);

        var clock = Stopwatch.GetTimestamp();
        var result = host.Step(Server.TickInterval, _cssBallSettings.StepTimeoutMs);
        var ms = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
        if (result is null) { CssBallFail("step: " + host.LastError); return; }
        _cssBallSteps++; _cssBallStepMsSum += ms; if (ms > _cssBallStepMsMax) _cssBallStepMsMax = ms;

        // the ball
        var ballOrigin = new V3(result.X, result.Y, result.Z);
        var ballAngles = new V3(result.Pitch, result.Yaw, result.Roll);
        var ballVelocity = new V3(result.Vx, result.Vy, result.Vz);
        if (_cssBallWritten is not { } last || last.Origin != ballOrigin || last.Angles != ballAngles || last.Velocity != ballVelocity)
            CssBallMove(ball, C(ballOrigin), new QAngle(ballAngles.X, ballAngles.Y, ballAngles.Z), C(ballVelocity));
        ball.AcceptInput("DisableMotion");
        _cssBallWritten = (ballOrigin, ballAngles, ballVelocity);
        _cssBallSpin = new V3(result.Wx, result.Wy, result.Wz);
        _cssBallAsleep = result.Asleep;
        _derivedBallVelocity = C(ballVelocity);
        _previousBallOrigin = C(ballOrigin);
        _previousBallSampleTime = Server.TickedTime;

        // what the ball did to the players
        var touching = _cssBallTouching;
        touching.Clear();
        foreach (var p in result.Players)
        {
            if (!_cssBallPlayers.TryGetValue(p.Slot, out var track) || track.Entity is not { IsValid: true } pawn) continue;
            track.Modifier = p.VelocityModifier;
            if (p.Touched) touching.Add(p.Slot);
            if (p.Mask == 0) continue;
            Vector? position = null, velocity = null;
            if ((p.Mask & 1) != 0)
            {
                var target = new V3(p.X, p.Y, p.Z);
                if (CssBallSpotFree(pawn, target)) { position = C(target); track.Origin = target; }
                else track.Resync = true;
            }
            if ((p.Mask & 2) != 0) velocity = new Vector(p.Vx, p.Vy, p.Vz);
            if (position is null && velocity is null) continue;
            CssBallMove(pawn, position, null, velocity);
            _cssBallPlayerMoves++;
        }
        CssBallSolidity(ball, ballOrigin);
        foreach (var hit in result.Hits) if (hit.What == 3) touching.Add(hit.Slot);
        foreach (var slot in touching)
        {
            if (Utilities.GetPlayerFromSlot(slot) is not { IsValid: true } toucher) continue;
            if (_playersPushingBall.Add(slot)) RecordBallTouch(toucher, C(ballOrigin));
            else
            {
                // still at the ball: the last toucher stays current, it is no new touch for the statistics
                _lastKickerSlot = slot;
                _lastKickerTeam = toucher.Team;
                ClearKickoffRestrictionOnTouch(toucher.Team);
            }
        }
        _playersPushingBall.IntersectWith(touching);

        // the knife
        foreach (var swing in result.Swings)
        {
            if (Utilities.GetPlayerFromSlot(swing.Slot) is not { IsValid: true } kicker || kicker.PlayerPawn.Value is not { IsValid: true } kickerPawn) continue;
            if (_cssBallSettings.SyncKnife) CssBallSyncKnife(kickerPawn, swing);
            if (!swing.Hit || swing.What != 1) continue;
            _cssBallKicks++;
            RecordBallTouch(kicker, C(ballOrigin));
            PlayKickSound(ball);
            _lastAcceptedKickTimeBySlot[swing.Slot] = Server.TickedTime;
            AtmoKick(kicker, ballVelocity.Length());
            Logger.LogInformation("[SM2DIAG] css_ball_kick slot={Slot} name={Name} stab={Stab} speed={Speed:F0} velocity={Velocity}",
                swing.Slot, kicker.PlayerName, swing.Stab, ballVelocity.Length(), FormatVector(C(ballVelocity)));
        }

        if (_cssBallDriveSeen.Count > 0 || _cssBallDriveLog is not null) CssBallDriveLog(tick, result);

        // a goal last: it may reset the ball
        foreach (var goal in result.Goals)
        {
            if (result.Vy * goal.End <= 0.0f) continue;     // only on the way in
            CssBallGoal(goal);
            break;
        }
    }

    // Applied after the sprint code of the same tick: the CS:S slowdown on top of the sprint factor.
    private void CssBallLateTick()
    {
        if (!CssBallActive) return;
        foreach (var (_, track) in _cssBallPlayers)
        {
            if (track.Entity is not { IsValid: true } pawn) continue;
            var current = pawn.VelocityModifier;
            // the sprint code rewrites its factor every tick while he sprints: his next move runs with it
            track.Sprint = current > 1.0f ? current : 1.0f;
            var slowed = track.Modifier < 0.999f;
            if (!slowed && !track.ModifierApplied) continue;
            var value = slowed ? track.Sprint * track.Modifier : MathF.Max(current, 1.0f);
            if (MathF.Abs(current - value) > 0.0001f)
            {
                pawn.VelocityModifier = value;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
            }
            track.ModifierApplied = slowed;
        }
    }

    // ---- the ball entity ------------------------------------------------------------------

    private void CssBallTakeOver(CssBallHost host, CPhysicsPropMultiplayer ball)
    {
        _cssBallEntity = ball.EntityHandle.Raw;
        _cssBallSolid = true;
        ball.AcceptInput("EnableCollision");
        ApplyGameplayPhysicsProfile(ball, "css_ball");      // the CS:S size
        ball.AcceptInput("DisableMotion");
        var waiting = _ballMotionFrozen;                    // the kickoff ball nobody has touched yet
        _ballMotionFrozen = false;
        if (_cssBallCarryOver && !waiting && ball.AbsOrigin is { } origin)
        {
            // switched on during play: the ball stays where it is and keeps its speed
            var angles = ball.AbsRotation;
            var o = N(origin);
            o.Z = MathF.Max(o.Z, CssBallHeightOffset + CssBallRadius);
            var v = N(_derivedBallVelocity);
            // lying still: asleep, as the CS:S ball is until something touches it (awake it rolls off by itself)
            var still = v.Length() < 5.0f && CssBallRestsAt(o);
            if (still) v = V3.Zero;
            host.SetBall(o.X, o.Y, o.Z, angles?.X ?? 0, angles?.Y ?? 0, angles?.Z ?? 0, v.X, v.Y, v.Z, 0, 0, 0, still);
            _cssBallWritten = null;
            Logger.LogInformation("[SM2DIAG] css_ball_takeover index={Index} origin={Origin} speed={Speed:F0}", ball.Index, FormatVector(C(o)), v.Length());
        }
        else CssBallReset("new_ball");
        _cssBallCarryOver = false;
    }

    // Kickoff, goal reset, round restart: the CS:S spawn - on the kickoff spot, two units above its
    // resting height, asleep until something touches it.
    private bool CssBallReset(string reason)
    {
        if (!CssBallActive || _ball is not { IsValid: true } ball) return false;
        var spot = CreateBallResetOrigin();
        var origin = new V3(spot.X, spot.Y, CssBallHeightOffset + CssBallSpawnHeight);
        _cssBall!.SetBall(origin.X, origin.Y, origin.Z, 0, 0, 0, 0, 0, 0, 0, 0, 0, true);
        if (ball.EntityHandle.Raw != _cssBallEntity)
        {
            _cssBallEntity = ball.EntityHandle.Raw;
            _cssBallSolid = true;
            ball.AcceptInput("EnableCollision");
            ApplyGameplayPhysicsProfile(ball, "css_ball");
        }
        ball.Teleport(C(origin), new QAngle(0.0f, 0.0f, 0.0f), new Vector(0.0f, 0.0f, 0.0f));
        ball.AcceptInput("DisableMotion");
        _cssBallWritten = (origin, V3.Zero, V3.Zero);
        _cssBallSpin = V3.Zero;
        _cssBallCarryOver = false;
        _cssBallStopRequested = false;
        _ballMotionFrozen = false;
        _goalNetSoundLastTime = -100f; // a reset ball can score (and sound) again
        ResetDerivedMotion();
        Logger.LogInformation("[SM2DIAG] css_ball_reset reason={Reason} origin={Origin}", reason, FormatVector(C(origin)));
        return true;
    }

    // Something else in the game put the ball somewhere (placing it by hand, a new ball size): the
    // CS:S ball is there now, without speed - CS2 does not tell what speed a held-still ball was given
    // (measured: the entity's velocity reads 0 right after the teleport). What launches the ball
    // comes through CssBallLaunch.
    private void CssBallPlacedByGame(CssBallHost host, CPhysicsPropMultiplayer ball, (V3 Origin, V3 Angles, V3 Velocity) written)
    {
        var o = N(ball.AbsOrigin!);
        var a = ball.AbsRotation is { } rotation ? new V3(rotation.X, rotation.Y, rotation.Z) : written.Angles;
        host.SetBall(o.X, o.Y, o.Z, a.X, a.Y, a.Z, 0, 0, 0, 0, 0, 0, CssBallRestsAt(o));
        _cssBallWritten = (o, a, V3.Zero);
        Logger.LogInformation("[SM2DIAG] css_ball_placed origin={Origin}", FormatVector(C(o)));
    }

    // On the pitch (or at the CS:S spawn height just above it): a ball put there without speed sleeps.
    private static bool CssBallRestsAt(V3 origin) => origin.Z <= CssBallHeightOffset + CssBallSpawnHeight + 0.5f;

    // The game launches the ball (training cannon, test shots): position (null: where it is) and speed.
    // False: the CS:S ball is off, the caller moves the CS2 ball itself.
    private bool CssBallLaunch(Vector? position, QAngle? angles, Vector velocity)
    {
        if (!CssBallActive || _ball is not { IsValid: true } ball || ball.EntityHandle.Raw != _cssBallEntity) return false;
        var o = position is not null ? N(position) : _cssBallWritten?.Origin ?? N(ball.AbsOrigin!);
        var a = angles is not null ? new V3(angles.X, angles.Y, angles.Z) : _cssBallWritten?.Angles ?? V3.Zero;
        var v = N(velocity);
        var spin = position is null ? _cssBallSpin : V3.Zero;
        _cssBall!.SetBall(o.X, o.Y, o.Z, a.X, a.Y, a.Z, v.X, v.Y, v.Z, spin.X, spin.Y, spin.Z, false);
        if (position is not null)
        {
            ball.Teleport(C(o), new QAngle(a.X, a.Y, a.Z), new Vector(0.0f, 0.0f, 0.0f));
            ball.AcceptInput("DisableMotion");
        }
        _cssBallWritten = (o, a, v);
        _cssBallStopRequested = false;
        Logger.LogInformation("[SM2DIAG] css_ball_launch origin={Origin} velocity={Velocity}", FormatVector(C(o)), FormatVector(C(v)));
        return true;
    }

    // The CS2 player is 72 units tall (54 ducked), the CS:S player 62 (45). A ball over the CS:S
    // player's head - lying on it, or passing just above - is inside the CS2 player, and CS2 would
    // hold him there as stuck. While the ball is in that band above a player it is not solid for CS2
    // (the CS:S contact itself still happens, in the helper).
    private void CssBallSolidity(CPhysicsPropMultiplayer ball, V3 ballOrigin)
    {
        var solid = true;
        var bottom = ballOrigin.Z - CssBallRadius;
        foreach (var (_, track) in _cssBallPlayers)
        {
            var feet = track.Origin;
            if (MathF.Abs(ballOrigin.X - feet.X) > 16.0f + CssBallRadius + 24.0f || MathF.Abs(ballOrigin.Y - feet.Y) > 16.0f + CssBallRadius + 24.0f) continue;
            var cssTop = feet.Z + (track.Ducked ? 45.0f : 62.0f);
            var cs2Top = feet.Z + (track.Ducked ? 54.0f : 72.0f);
            if (bottom >= cssTop - 1.0f && bottom < cs2Top + 1.0f) { solid = false; break; }
        }
        if (solid == _cssBallSolid) return;
        _cssBallSolid = solid;
        ball.AcceptInput(solid ? "EnableCollision" : "DisableCollision");
    }

    // Moves an entity without telling the clients to drop their blending: a teleport counts up the
    // entity's interpolation frame, which makes clients jump to the new place.
    private void CssBallMove(CBaseEntity entity, Vector? position, QAngle? angles, Vector? velocity)
    {
        if (position is null || !_cssBallSettings.SmoothTeleports || _cssBallInterpolationBroken)
        {
            entity.Teleport(position, angles, velocity);
            return;
        }
        try
        {
            ref var frame = ref Schema.GetRef<byte>(entity.Handle, "CBaseEntity", "m_ubInterpolationFrame");
            var before = frame;
            entity.Teleport(position, angles, velocity);
            frame = before;
        }
        catch (Exception ex)
        {
            _cssBallInterpolationBroken = true;
            Logger.LogWarning(ex, "[SM2DIAG] css_ball_interpolation_unavailable");
            entity.Teleport(position, angles, velocity);
        }
    }

    // The helper knows the CS:S stadium, the player stands in the CS2 map: never put him into a wall
    // of the CS2 map (the two differ by a unit here and there).
    private static bool CssBallSpotFree(CCSPlayerPawn pawn, V3 target)
    {
        if (pawn.AbsOrigin is not { } origin) return false;
        var ducked = pawn.MovementServices is { } movement && new CCSPlayer_MovementServices(movement.Handle).Ducked;
        var trace = Trace.TraceHullShape(origin, C(target), new Vector(-15.5f, -15.5f, 0.5f), new Vector(15.5f, 15.5f, ducked ? 53.5f : 71.5f),
            pawn, new TraceOptions { InteractsWith = Masks.PlayerSolidBrushOnly });
        return !trace.DidHit();
    }

    private void CssBallSyncKnife(CCSPlayerPawn pawn, in CssBallHost.KnifeSwing swing)
    {
        var weapon = pawn.WeaponServices?.ActiveWeapon.Value;
        if (weapon is not { IsValid: true } || !weapon.DesignerName.Contains("knife", StringComparison.OrdinalIgnoreCase)) return;
        var now = Server.TickCount;
        weapon.NextPrimaryAttackTick = now + (int)MathF.Round(swing.NextPrimary / Server.TickInterval);
        weapon.NextPrimaryAttackTickRatio = 0.0f;
        weapon.NextSecondaryAttackTick = now + (int)MathF.Round(swing.NextSecondary / Server.TickInterval);
        weapon.NextSecondaryAttackTickRatio = 0.0f;
        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_flNextPrimaryAttackTickRatio");
        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_flNextSecondaryAttackTickRatio");
    }

    // The CS:S goal: the ball touches the goal trigger behind the line (the map's terro_But / ct_But).
    // What a goal then does is the CS2 mod's own (Match.cs), with the same conditions.
    private void CssBallGoal(CssBallHost.GoalTouch goal)
    {
        if (_matchPhase is not (MatchPhase.Live or MatchPhase.Warmup) || _goalLocked) return;
        if (CannonGoalsSuppressed || (_trainingGoalsDisabled && _matchPhase == MatchPhase.Warmup)) return;
        if (!GoalsOnThisPitch) return;
        var enteredPositiveEnd = goal.End > 0;
        var enteredCtGoal = enteredPositiveEnd != _ctDefendsNegativeY;
        var scoringTeam = enteredCtGoal ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
        Logger.LogInformation("[SM2DIAG] css_ball_goal end={End} x={X:F1} y={Y:F1} z={Z:F1}", goal.End, goal.X, goal.Y, goal.Z);
        OnGoalScored(scoringTeam, goal.X, goal.Z, MathF.CopySign(GoalPlaneNow, goal.End));
    }

    // PausedBall.cs: the pause ends. True while the helper has the ball (CS2 must not enable its motion).
    private bool CssBallOnPauseRelease(bool restoreMotion)
    {
        if (!CssBallActive) return false;
        _ballMotionFrozen = false;
        _cssBallRestoreHeld = restoreMotion;
        if (!restoreMotion) _cssBallStopRequested = true;
        return true;
    }

    // ---- players --------------------------------------------------------------------------

    private static int CssBallButtonBits(PlayerButtons buttons)
    {
        var bits = 0;
        if ((buttons & PlayerButtons.Attack) != 0) bits |= 1;
        if ((buttons & PlayerButtons.Jump) != 0) bits |= 2;
        if ((buttons & PlayerButtons.Duck) != 0) bits |= 4;
        if ((buttons & PlayerButtons.Forward) != 0) bits |= 8;
        if ((buttons & PlayerButtons.Back) != 0) bits |= 16;
        if ((buttons & PlayerButtons.Moveleft) != 0) bits |= 512;
        if ((buttons & PlayerButtons.Moveright) != 0) bits |= 1024;
        if ((buttons & PlayerButtons.Attack2) != 0) bits |= 2048;
        if ((buttons & PlayerButtons.Speed) != 0) bits |= 131072;      // the walk key
        return bits;
    }

    // OnPlayerButtonsChanged: a click or jump shorter than a tick is no longer held when the tick
    // samples the keys. True: the CS2 knife kick has nothing to do (no training balls out).
    private bool CssBallOnButtons(CCSPlayerController player, PlayerButtons pressed)
    {
        if (!CssBallActive) return false;
        var bits = CssBallButtonBits(pressed) & (1 | 2 | 2048);
        if (bits != 0)
        {
            if (!_cssBallPlayers.TryGetValue(player.Slot, out var track)) _cssBallPlayers[player.Slot] = track = new CssBallPlayer();
            track.Pressed |= bits;
        }
        return _trainingBalls.Count == 0;
    }

    // ---- movement values ------------------------------------------------------------------

    // Once a second: the CS:S movement values on the server (when wanted), and the helper told what
    // the server really runs, so the move at the ball and the move away from it are the same.
    private void CssBallMovementTick(CssBallHost host)
    {
        foreach (var (name, helper, css) in CssBallMoveCvars)
        {
            if (ConVar.Find(name) is not { } cvar) continue;
            var value = cvar.GetPrimitiveValue<float>();
            if (_cssBallSettings.CssMovement)
            {
                if (MathF.Abs(value - css) > 0.0001f && _cssBallCvarAttempts.GetValueOrDefault(name) < 3)
                {
                    _cssBallCvarsBefore.TryAdd(name, value);
                    _cssBallCvarAttempts[name] = _cssBallCvarAttempts.GetValueOrDefault(name) + 1;
                    Server.ExecuteCommand($"{name} {css.ToString(CultureInfo.InvariantCulture)}");
                    if (_cssBallCvarAttempts[name] == 3) Logger.LogWarning("[SM2DIAG] css_ball_cvar_not_settable cvar={Cvar} value={Value}", name, value);
                }
                else if (MathF.Abs(value - css) <= 0.0001f) _cssBallCvarAttempts.Remove(name);
            }
            else if (_cssBallCvarsBefore.Remove(name, out var before))
            {
                Server.ExecuteCommand($"{name} {before.ToString(CultureInfo.InvariantCulture)}");
                _cssBallCvarAttempts.Remove(name);
            }
            if (!_cssBallSentSettings.TryGetValue(helper, out var sent) || sent != value)
            {
                host.Setting(helper, value);
                _cssBallSentSettings[helper] = value;
            }
        }
    }

    private void CssBallRestoreMovement()
    {
        foreach (var (name, before) in _cssBallCvarsBefore)
            Server.ExecuteCommand($"{name} {before.ToString(CultureInfo.InvariantCulture)}");
        _cssBallCvarsBefore.Clear();
        _cssBallCvarAttempts.Clear();
    }

    // ---- switch, menu, console ------------------------------------------------------------

    private bool CssBallSetEnabled(bool on)
    {
        try
        {
            if (on) File.WriteAllText(ConfigPath(CssBallFlagFile), "");
            else if (File.Exists(ConfigPath(CssBallFlagFile))) File.Delete(ConfigPath(CssBallFlagFile));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[SM2DIAG] css_ball_switch_failed");
            return false;
        }
        _flagFileCache.Remove(CssBallFlagFile);
        _cssBallFailures = 0;
        _cssBallRetryAt = 0;
        Logger.LogInformation("[SM2DIAG] css_ball_switch on={On}", on);
        return true;
    }

    private string CssBallMenuLabel() => !CssBallWanted ? "CS2 (tuned)" : CssBallActive ? "CS:S original" : $"CS:S original - {_cssBallStatus}";

    private string CssBallToleranceLabel() =>
        _cssBallSettings.PositionTolerance <= 0.031f ? "exact" : _cssBallSettings.PositionTolerance <= 0.26f ? "normal" : "soft";

    private void OpenCssBallMenu(CCSPlayerController player)
    {
        if (!BallWorkbenchAccess(player)) return;
        var menu = new NumberMenu { Title = $"Ball physics: {CssBallMenuLabel()}", Key = "ball-css", OnBack = OpenBallSimpleMenu };
        void Toggle(string text, Action change) => menu.Add(text, p =>
        {
            if (!BallWorkbenchAccess(p)) return;
            change();
            SaveJsonAtomic(CssBallSettingsFile, _cssBallSettings);
            OpenCssBallMenu(p);
        });
        menu.Add(CssBallWanted ? "Switch to the CS2 ball" : "Switch to the CS:S original ball", p =>
        {
            if (!BallWorkbenchAccess(p)) return;
            var on = !CssBallWanted;
            if (!CssBallSetEnabled(on)) p.PrintToChat(" [SM] Not changed: the switch file could not be written.");
            else AnnounceAll(on ? " \x04[SM]\x01 Ball physics: CS:S original." : " \x04[SM]\x01 Ball physics: CS2.");
            OpenCssBallMenu(p);
        });
        Toggle($"Movement values: {(_cssBallSettings.CssMovement ? "CS:S (accelerate 5, friction 4)" : "CS2 server values")}",
            () => _cssBallSettings.CssMovement = !_cssBallSettings.CssMovement);
        Toggle($"Player at the ball: {CssBallToleranceLabel()}", () =>
        {
            (_cssBallSettings.PositionTolerance, _cssBallSettings.VelocityTolerance) = CssBallToleranceLabel() switch
            {
                "exact" => (0.25f, 2.0f),
                "normal" => (1.0f, 8.0f),
                _ => (0.03f, 0.5f),
            };
            _cssBall?.Setting("posthreshold", _cssBallSettings.PositionTolerance);
            _cssBall?.Setting("velthreshold", _cssBallSettings.VelocityTolerance);
        });
        Toggle($"Smooth ball and player steps: {(_cssBallSettings.SmoothTeleports ? "on" : "off")}",
            () => _cssBallSettings.SmoothTeleports = !_cssBallSettings.SmoothTeleports);
        Toggle($"Knife rhythm from CS:S: {(_cssBallSettings.SyncKnife ? "on" : "off")}",
            () => _cssBallSettings.SyncKnife = !_cssBallSettings.SyncKnife);
        menu.AddInfo("CS:S original: ball, body contact and knife run on the CS:S physics.");
        menu.AddInfo("The ball settings of this menu only apply to the CS2 ball.");
        if (CssBallActive && _cssBallSteps > 0)
            menu.AddInfo($"Helper: {_cssBallStepMsSum / _cssBallSteps:0.00} ms per tick (max {_cssBallStepMsMax:0.0})");
        OpenNumberMenu(player, menu);
    }

    private void OnCssBallCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "root")) return;
        var arg = command.ArgCount >= 2 ? command.GetArg(1).ToLowerInvariant() : "status";
        bool On(int i) => command.ArgCount > i && command.GetArg(i).ToLowerInvariant() is "on" or "1" or "true";
        float Number(int i, float fallback) => command.ArgCount > i
            && float.TryParse(command.GetArg(i), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && float.IsFinite(v) ? v : fallback;
        switch (arg)
        {
            case "on":
            case "off":
                command.ReplyToCommand(CssBallSetEnabled(arg == "on") ? $"[SM] CS:S ball: {arg}" : "[SM] CS:S ball: the switch file could not be written");
                return;
            case "tolerance":
                _cssBallSettings.PositionTolerance = Math.Clamp(Number(2, 0.25f), 0.0f, 8.0f);
                _cssBallSettings.VelocityTolerance = Math.Clamp(Number(3, 2.0f), 0.0f, 100.0f);
                _cssBall?.Setting("posthreshold", _cssBallSettings.PositionTolerance);
                _cssBall?.Setting("velthreshold", _cssBallSettings.VelocityTolerance);
                break;
            case "movement": _cssBallSettings.CssMovement = On(2); break;
            case "smooth": _cssBallSettings.SmoothTeleports = On(2); break;
            case "knife": _cssBallSettings.SyncKnife = On(2); break;
            case "probe": CssBallProbe(command); return;
            case "log":
                // css_sm2cssball log <seconds>: every tick of the ball into cssball_drive.csv
                try
                {
                    _cssBallDriveLog ??= new StreamWriter(ConfigPath("cssball_drive.csv"), append: true) { AutoFlush = true };
                    _cssBallDriveLogUntil = Server.TickCount + (int)(Math.Clamp(Number(2, 5), 0.1f, 120.0f) / Server.TickInterval);
                    command.ReplyToCommand("[SM] CS:S ball: logging to cssball_drive.csv");
                }
                catch (Exception ex) { command.ReplyToCommand("[SM] CS:S ball: no log file: " + ex.Message); }
                return;
            case "place":
            case "drive":
            {
                // css_sm2cssball place <slot> x y z [yaw]           put a player somewhere
                // css_sm2cssball drive <slot> yaw speed seconds [buttons] [pitch]   run him in a line (8 forward, +1 knife, +2048 stab)
                var who = (int)Number(2, -1);
                if (Utilities.GetPlayerFromSlot(who) is not { IsValid: true } target || target.PlayerPawn.Value is not { IsValid: true } targetPawn)
                { command.ReplyToCommand("[SM] no such player slot"); return; }
                if (arg == "place")
                {
                    targetPawn.Teleport(new Vector(Number(3, 0), Number(4, 0), Number(5, CssBallHeightOffset)), new QAngle(0.0f, Number(6, 0), 0.0f), new Vector(0.0f, 0.0f, 0.0f));
                    command.ReplyToCommand($"[SM] placed {target.PlayerName}");
                    return;
                }
                _cssBallDrives[who] = new CssBallDrive(Number(3, 0), Number(7, 0), Number(4, 250), Server.TickCount + (int)(Number(5, 3) / Server.TickInterval), (int)Number(6, 8));
                command.ReplyToCommand($"[SM] driving {target.PlayerName}");
                return;
            }
            case "push":
                // css_sm2cssball push fx fy fz: one push through the ball's centre (Source impulse units)
                if (CssBallActive && _cssBallWritten is { } at)
                    _cssBall!.Push(Number(2, 0), Number(3, 0), Number(4, 0), at.Origin.X, at.Origin.Y, at.Origin.Z);
                command.ReplyToCommand(CssBallActive ? "[SM] CS:S ball: pushed" : "[SM] CS:S ball is not running");
                return;
            case "status": break;
            default:
                command.ReplyToCommand("[SM] css_sm2cssball on|off|status|probe|tolerance <units> <speed>|movement on|off|smooth on|off|knife on|off|push fx fy fz|place <slot> x y z [yaw]|drive <slot> yaw speed seconds [buttons] [pitch]");
                return;
        }
        if (arg != "status") SaveJsonAtomic(CssBallSettingsFile, _cssBallSettings);
        var steps = Math.Max(1, _cssBallSteps);
        command.ReplyToCommand($"[SM] CS:S ball: switch={(CssBallWanted ? "on" : "off")} state={(CssBallActive ? "running" : _cssBallStatus)} map={_currentMapName} failures={_cssBallFailures}");
        command.ReplyToCommand($"[SM] helper: steps={_cssBallSteps} avg={_cssBallStepMsSum / steps:0.000} ms max={_cssBallStepMsMax:0.00} ms players={_cssBallPlayers.Count} playerMoves={_cssBallPlayerMoves} kicks={_cssBallKicks}");
        command.ReplyToCommand($"[SM] settings: tolerance={_cssBallSettings.PositionTolerance:0.###}/{_cssBallSettings.VelocityTolerance:0.###} movement={(_cssBallSettings.CssMovement ? "css" : "cs2")} smooth={_cssBallSettings.SmoothTeleports} knife={_cssBallSettings.SyncKnife} dir={_cssBallSettings.Directory}");
        if (_cssBallWritten is { } w)
            command.ReplyToCommand($"[SM] ball: origin={FormatVector(C(w.Origin))} velocity={FormatVector(C(w.Velocity))} spin={FormatVector(C(_cssBallSpin))} asleep={_cssBallAsleep} frozen={_cssBallFrozen}");
    }

    private void CssBallDriveLog(int tick, CssBallHost.StepResult result)
    {
        if (_cssBallDriveSeen.Count == 0)
        {
            // the ball rolls on after the last scripted player: two more seconds of it, then the file closes
            if (_cssBallDrives.Count == 0 && tick > _cssBallDriveLogUntil) { _cssBallDriveLog?.Dispose(); _cssBallDriveLog = null; return; }
        }
        else _cssBallDriveLogUntil = tick + 128;
        try
        {
            _cssBallDriveLog ??= new StreamWriter(ConfigPath("cssball_drive.csv"), append: true) { AutoFlush = true };
            string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            var ball = $"{F(result.X)},{F(result.Y)},{F(result.Z)},{F(result.Vx)},{F(result.Vy)},{F(result.Vz)},{(result.Asleep ? 1 : 0)}";
            if (_cssBallDriveSeen.Count == 0) _cssBallDriveLog.WriteLine($"{tick},-1,,,,,,,,,,,,,,,,,{ball}");
            foreach (var seen in _cssBallDriveSeen)
            {
                var o = result.Players.FirstOrDefault(p => p.Slot == seen.Slot);
                _cssBallDriveLog.WriteLine($"{tick},{seen.Slot},{F(seen.Origin.X)},{F(seen.Origin.Y)},{F(seen.Origin.Z)},{F(seen.Velocity.X)},{F(seen.Velocity.Y)},{F(seen.Velocity.Z)},{seen.Flags},{seen.Buttons},"
                    + $"{o.Mask},{F(o.X)},{F(o.Y)},{F(o.Z)},{F(o.Vx)},{F(o.Vy)},{F(o.Vz)},{(o.Touched ? 1 : 0)},{ball}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[SM2DIAG] css_ball_drive_log_failed");
            _cssBallDrives.Clear();
        }
        _cssBallDriveSeen.Clear();
    }

    // Console check of what the plugin relies on: a held-still ball keeps the speed a teleport gave
    // it (so a cannon shot can be read back), and a teleport counts up the interpolation frame.
    private void CssBallProbe(CommandInfo command)
    {
        if (_ball is not { IsValid: true } ball) { command.ReplyToCommand("[SM] probe: no ball"); return; }
        byte Frame() { try { return Schema.GetRef<byte>(ball.Handle, "CBaseEntity", "m_ubInterpolationFrame"); } catch { return 255; } }
        var origin = N(ball.AbsOrigin!);
        var frameBefore = Frame();
        ball.Teleport(C(origin + new V3(0, 0, 0.5f)), null, new Vector(12.5f, -7.25f, 3.125f));
        var frameAfter = Frame();
        var now = ball.AbsVelocity;
        command.ReplyToCommand($"[SM] probe: active={CssBallActive} frame {frameBefore} -> {frameAfter}; velocity right after the teleport {FormatVector(now)}; origin {FormatVector(ball.AbsOrigin)}");
        Server.NextFrame(() =>
        {
            if (!ball.IsValid) return;
            Logger.LogInformation("[SM2DIAG] css_ball_probe next frame: velocity={Velocity} origin={Origin} frame={Frame}", FormatVector(ball.AbsVelocity), FormatVector(ball.AbsOrigin), Frame());
        });
    }
}

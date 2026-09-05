using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using V3 = System.Numerics.Vector3;

namespace SoccerModMvp;

public sealed partial class SoccerModMvpPlugin
{
    private const string HandlingFile = "soccermod_ball_handling.json";
    private sealed class HandlingSettings { public string Profile { get; set; } = "improved"; }
    private HandlingSettings _handling = new();
    private bool ImprovedHandling => _handling.Profile != "legacy";
    private bool CreativeHandling => _handling.Profile == "creative";
    private sealed class ContactState
    {
        public int Generation;
        public int LastKickTick = -1;
        public readonly Queue<Vector> History = new();
        public double LastWall = -1;
        public int SettleTicks;
        public bool Settled;
        public System.Numerics.Quaternion? PreviousRotation;
        public double RotationTime;
        public V3 MeasuredSpin;
        public bool SpinMeasured;
        public readonly Dictionary<uint, double> Impacts = new();
        public float Curve;
        public double CurveUntil;
    }
    private readonly Dictionary<uint, ContactState> _contacts = new();
    private int _nextPawnImpact;
    private readonly Dictionary<uint, int> _pawnImpacts = new();
    private readonly Dictionary<uint, double> _trapUntil = new();
    private static V3 N(Vector v) => new(v.X, v.Y, v.Z);
    private static Vector C(V3 v) => new(v.X, v.Y, v.Z);
    private ContactState State(CBaseEntity ball)
    {
        var key = ball.EntityHandle.Raw;
        if (!_contacts.TryGetValue(key, out var state)) _contacts[key] = state = new();
        return state;
    }
    private void BallHandlingOnLoad()
    {
        _handling = LoadJsonOrNull<HandlingSettings>(HandlingFile) ?? new();
        if (_handling.Profile is not ("legacy" or "improved" or "creative")) _handling.Profile = "improved";
        AddCommand("css_sm2ball_profile", "Admin: legacy|improved|creative; persisted without changing existing tuning.", (player, command) =>
        {
            if (!RequirePermission(player, command, "ball")) return;
            if (command.ArgCount > 1)
            {
                var profile = command.GetArg(1).ToLowerInvariant();
                if (profile is not ("legacy" or "improved" or "creative")) { command.ReplyToCommand("Usage: css_sm2ball_profile legacy|improved|creative"); return; }
                var settings = new HandlingSettings { Profile = profile };
                if (!SaveJsonAtomic(HandlingFile, settings)) { command.ReplyToCommand("Could not persist profile; unchanged."); return; }
                _handling = settings;
                ResetDerivedMotion();
                ClearHandlingState();
            }
            command.ReplyToCommand($"[SM] Ball handling: {_handling.Profile}. Quick rollback: css_sm2ball_profile legacy");
        });
        AddCommand("css_ball_trap", "Creative profile: arm one cushioned first touch for 0.35 seconds.", (player, command) =>
        {
            if (!CreativeHandling || !IsEligiblePlayer(player) || player!.PlayerPawn.Value is not { IsValid: true } pawn) { command.ReplyToCommand("First touch is available to living players in the creative profile."); return; }
            var key = pawn.EntityHandle.Raw;
            // Repeated binds cannot extend an armed window or bypass recovery.
            if (_trapUntil.TryGetValue(key, out var until) && Server.TickedTime < until + 1) return;
            _trapUntil[key] = Server.TickedTime + 0.35;
        });
        AddCommand("css_sm2ball_runtime", "Admin: actual live model and angular schema telemetry.", (player, command) =>
        {
            if (!RequirePermission(player, command, "ball") || _ball is not { IsValid: true }) return;
            command.ReplyToCommand($"[SM] profile={_handling.Profile} model={ActiveBallModel(_ball)} angularSchema={FormatAngle(_ball.AngVelocity)} observedSpin={State(_ball).MeasuredSpin} spinValid={State(_ball).SpinMeasured} mass=unmeasured nativeImpulse=unacknowledged {BuildGameplayPhysicsProfileSummary()}");
        });
        TrainingCoachOnLoad();
    }
    private static string ActiveBallModel(CPhysicsPropMultiplayer ball) => ball.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName ?? "unknown";
    private void ClearHandlingState() { _contacts.Clear(); _pawnImpacts.Clear(); _trapUntil.Clear(); ClearTrainingCoach(); }
    private void NewBallContact(CPhysicsPropMultiplayer ball)
    {
        if (!ImprovedHandling) return;
        var state = State(ball);
        state.Generation++;
        state.History.Clear();
        state.Settled = false;
        state.SettleTicks = 0;
        state.Curve = 0;
        // Legacy's pending callback is also cancelled if switching profiles.
        if (ball.Index == _ball?.Index) { _wallAssistGeneration++; _recentBallVelocities.Clear(); }
    }
    private bool IsBallGrounded(CPhysicsPropMultiplayer ball, Vector origin)
    {
        if (!ImprovedHandling) return origin.Z <= StadiumPitchPlaneZ + BallCollisionRadius + SettleGroundToleranceZ;
        var end = new Vector(origin.X, origin.Y, origin.Z - BallCollisionRadius - SettleGroundToleranceZ);
        var trace = Trace.TraceEndShape(origin, end, ball, new TraceOptions { InteractsWith = Masks.Solid });
        return trace.DidHit() && trace.Normal.Z >= 0.65f && IsStaticWallSurface(trace);
    }
    private void UpdateSharedBallHandling()
    {
        if (_pausedBallHandle != 0) return;
        var balls = PlayableBalls().ToArray();
        foreach (var target in balls) SampleBallRotation(target.Ball, State(target.Ball));
        if (!ImprovedHandling) return;
        var live = balls.Select(b => b.Ball.EntityHandle.Raw).ToHashSet();
        foreach (var key in _contacts.Keys.Where(k => !live.Contains(k)).ToArray()) _contacts.Remove(key);
        var pawns = Utilities.GetPlayers().Where(IsEligiblePlayer).Select(p => p.PlayerPawn.Value!.EntityHandle.Raw).ToHashSet();
        foreach (var key in _pawnImpacts.Keys.Where(k => !pawns.Contains(k)).ToArray()) _pawnImpacts.Remove(key);
        foreach (var key in _trapUntil.Keys.Where(k => !pawns.Contains(k)).ToArray()) _trapUntil.Remove(key);
        foreach (var target in balls)
        {
            var state = State(target.Ball);
            foreach (var key in state.Impacts.Keys.Where(k => !pawns.Contains(k)).ToArray()) state.Impacts.Remove(key);
            var speed = VectorSpeed(target.Inherited);
            var ground = IsBallGrounded(target.Ball, target.Origin);
            state.Settled = _settleEnabled && ground && speed < _settleSpeedThreshold;
            if (!state.Settled) state.SettleTicks = 0;
            else if (speed >= 0.05f && ++state.SettleTicks >= _settleTicks)
            {
                target.Ball.Teleport(velocity: new Vector()); state.SettleTicks = 0;
            }
            if (!state.Settled) TryApplySharedWallAssist(target.Ball, target.Inherited, Server.TickedTime, state);
            if (CreativeHandling && !ground && Math.Abs(state.Curve) > 0.01f && Server.TickedTime < state.CurveUntil
                && Server.TickedTime - state.LastWall > WallAssistCooldownSeconds)
            {
                target.Ball.Teleport(velocity: C(BallContactMath.CurveStep(N(target.Inherited), state.Curve, Server.TickInterval)));
                state.Curve *= MathF.Exp(-1.5f * Server.TickInterval);
            }
        }
        TrainingCoachOnTick(balls);
    }
    private void ScheduleSharedSeparation(CPhysicsPropMultiplayer ball, ContactState state, int generation, Vector normal, float minimum, int frames)
    {
        if (frames <= 0 || ball.AbsOrigin is not { } point) return;
        var key = ball.EntityHandle.Raw;
        var origin = N(point); var time = Server.TickedTime;
        Server.NextFrame(() =>
        {
            if (!ImprovedHandling || !ball.IsValid || ball.EntityHandle.Raw != key
                || !_contacts.TryGetValue(key, out var active) || !ReferenceEquals(active, state)
                || state.Generation != generation || ball.AbsOrigin is not { } position) return;
            var elapsed = (float)(Server.TickedTime - time);
            if (elapsed <= 0 || elapsed > 0.05f) return;
            var current = (N(position) - origin) / elapsed;
            // A new surface/reversal terminates the old wall's ownership.
            if (V3.Dot(current, N(normal)) < -1 || current.Length() > _kickMaximumBallSpeed) return;
            var trace = Trace.TraceEndShape(position, C(N(position) - N(normal) * (BallCollisionRadius + WallAssistContactProbeExtraDistance)), ball, new TraceOptions { InteractsWith = Masks.Solid });
            if (!trace.DidHit() || !IsStaticWallSurface(trace) || V3.Dot(N(trace.Normal), N(normal)) < 0.9f) return;
            ball.Teleport(velocity: C(BallContactMath.Separate(current, N(normal), minimum)));
            ScheduleSharedSeparation(ball, state, generation, normal, minimum, frames - 1);
        });
    }
    private void TryApplySharedWallAssist(CPhysicsPropMultiplayer ball, Vector current, double now, ContactState state)
    {
        state.History.Enqueue(current);
        while (state.History.Count > WallAssistHistoryTicks)
        {
            state.History.Dequeue();
        }

        if (!_wallAssistEnabled
            || !ball.IsValid
            || now - state.LastWall < WallAssistCooldownSeconds)
        {
            return;
        }

        var approach = current;
        var approachPlanarSpeed = 0.0f;
        foreach (var sample in state.History)
        {
            var speed = MathF.Sqrt(sample.X * sample.X + sample.Y * sample.Y);
            if (speed > approachPlanarSpeed)
            {
                approachPlanarSpeed = speed;
                approach = sample;
            }
        }

        if (approachPlanarSpeed < WallAssistMinimumApproachSpeed)
        {
            return;
        }

        var currentPlanarSpeed = MathF.Sqrt(current.X * current.X + current.Y * current.Y);
        var dot = currentPlanarSpeed > 0.01f
            ? (approach.X * current.X + approach.Y * current.Y) / (approachPlanarSpeed * currentPlanarSpeed)
            : 0.0f;

        // A wall collision may be a clean reversal, a near-zero contact
        // frame, or a glancing hit whose tangential speed makes its overall
        // direction look unchanged.  The common signal is a large speed loss
        // while the ball is physically within one radius of a solid brush.
        var isStrongReversal = currentPlanarSpeed > 0.01f
            && dot <= WallAssistReversalDotThreshold;
        if (!isStrongReversal
            && currentPlanarSpeed / approachPlanarSpeed > WallAssistContactSpeedRatio)
        {
            return;
        }

        if (ball.AbsOrigin is not { } ballOrigin)
        {
            return;
        }

        var approachUnitX = approach.X / approachPlanarSpeed;
        var approachUnitY = approach.Y / approachPlanarSpeed;
        var contactProbeEnd = new Vector(
            ballOrigin.X + approachUnitX * (BallCollisionRadius + WallAssistContactProbeExtraDistance),
            ballOrigin.Y + approachUnitY * (BallCollisionRadius + WallAssistContactProbeExtraDistance),
            ballOrigin.Z);
        // The midfield curb participates in Rubikon's general solid layer but
        // is absent from SolidBrushOnly. Filter the result back down to static
        // map/world classes below so players and movable props cannot qualify.
        var traceOptions = new TraceOptions { InteractsWith = Masks.Solid };
        var contactProbe = Trace.TraceEndShape(
            ballOrigin,
            contactProbeEnd,
            ball,
            traceOptions);

        // Use the actual surface normal, not the ball's full approach vector.
        // On a glancing hit those differ: the approach contains the along-wall
        // component that must remain untouched.
        var wallNormalX = 0.0f;
        var wallNormalY = 0.0f;
        var surfaceSource = "trace";
        if (IsStaticWallSurface(contactProbe) && contactProbe.Fraction < 0.999f)
        {
            var wallNormalPlanarLength = MathF.Sqrt(
                contactProbe.Normal.X * contactProbe.Normal.X
                + contactProbe.Normal.Y * contactProbe.Normal.Y);
            if (wallNormalPlanarLength < 0.70f)
            {
                return;
            }

            wallNormalX = contactProbe.Normal.X / wallNormalPlanarLength;
            wallNormalY = contactProbe.Normal.Y / wallNormalPlanarLength;
        }
        else if (string.Equals(_currentMapName, FoundationMapName, StringComparison.OrdinalIgnoreCase)
            && TryGetFoundationBoundaryNormal(ballOrigin, out wallNormalX, out wallNormalY))
        {
            surfaceSource = "measured_boundary";
        }
        else
        {
            return;
        }

        var incomingNormalSpeed = -(approach.X * wallNormalX + approach.Y * wallNormalY);
        if (incomingNormalSpeed < WallAssistMinimumApproachSpeed)
        {
            return;
        }

        var speedLost = approachPlanarSpeed - currentPlanarSpeed;
        if (speedLost <= 0.0f)
        {
            return;
        }

        // Restore only the component normal to the detected approach. Scaling
        // the whole planar vector would also amplify a glancing wall hit's
        // tangential motion and distort its angle. The CS:S reference wall
        // capture retained about 61 / 334 = 0.18 of the incoming normal speed.
        var currentNormalRebound = current.X * wallNormalX + current.Y * wallNormalY;
        var targetNormalRebound = incomingNormalSpeed * _wallAssistMinimumNormalRetention;
        var addedNormalRebound = Math.Max(0.0f, targetNormalRebound - currentNormalRebound);

        var addedVertical = Math.Min(speedLost * _wallAssistConversionRatio, _wallAssistMaxAddedVertical);
        var boosted = new Vector(
            current.X + wallNormalX * addedNormalRebound,
            current.Y + wallNormalY * addedNormalRebound,
            current.Z + addedVertical);
        ball.Teleport(velocity: boosted);
        // Preserve physical spin: a blind additive "re-spin" cannot set a target.
        state.Curve = 0;
        ScheduleSharedSeparation(ball, state, ++state.Generation,
            new Vector(wallNormalX, wallNormalY, 0), targetNormalRebound, WallAssistSeparationFrames);
        state.LastWall = now;
        state.History.Clear();
        Logger.LogInformation(
            "[SM2DIAG] wall_assist_applied mode={Mode} surface={Surface} approachSpeed={ApproachSpeed:F1} currentSpeed={CurrentSpeed:F1} dot={Dot:F3} wallNormal=({NormalX:F3},{NormalY:F3}) incomingNormal={IncomingNormal:F1} speedLost={SpeedLost:F1} normalBefore={NormalBefore:F1} normalTarget={NormalTarget:F1} addedNormal={AddedNormal:F1} addedVertical={AddedVertical:F1} separationFrames={SeparationFrames} verticalRatio={VerticalRatio:F3} minNormalRetention={MinNormalRetention:F3}",
            isStrongReversal ? "reversal" : "contact_slowdown",
            surfaceSource,
            approachPlanarSpeed,
            currentPlanarSpeed,
            dot,
            wallNormalX,
            wallNormalY,
            incomingNormalSpeed,
            speedLost,
            currentNormalRebound,
            targetNormalRebound,
            addedNormalRebound,
            addedVertical,
            WallAssistSeparationFrames,
            _wallAssistConversionRatio,
            _wallAssistMinimumNormalRetention);
    }

}

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Football mode keeper (owner 2026-09-26): the player who took !gk, inside
// his own box. Right click = catch (slow ball) or parry (fast ball). A caught
// ball is carried in front of him (motion and collision off, placed every
// tick, never past the goal line) for up to 6 s; then right click throws and
// left click punts, both charged. Space while holding A or D = side dive
// (more catch reach for a moment). Leaving the box or the time running out
// drops the ball. No dive/catch animation is possible (engine limit).
public sealed partial class SoccerModMvpPlugin
{
    private sealed class KeeperHold
    {
        public int Slot;
        public uint Pawn;
        public double Since;
        // A new press is needed after the catch: the catching click must not throw.
        public bool Armed;
        public FootballCharge? Charge;
    }

    private KeeperHold? _keeperHold;
    private readonly Dictionary<int, double> _keeperCatchUntil = new();
    private readonly Dictionary<int, double> _keeperCatchNext = new();
    private readonly Dictionary<int, double> _keeperDiveReachUntil = new();
    private readonly Dictionary<int, double> _keeperDiveNext = new();

    private bool KeeperHoldsBall => _keeperHold is not null;

    private void FootballKeeperOnLoad()
    {
        RegisterEventHandler<EventPlayerJump>((@event, _) =>
        {
            if (FootballMode && @event.Userid is { IsValid: true } player) TryKeeperDive(player);
            return HookResult.Continue;
        });
    }

    private bool KeeperActive(CCSPlayerController player, out CCSPlayerPawn pawn)
    {
        pawn = player.PlayerPawn.Value!;
        return FootballMode && pawn is { IsValid: true } && InGoalkeeperBox(player, pawn);
    }

    // True when the keeper logic used the buttons (no field-player charge).
    private bool FootballKeeperOnButtons(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if (_keeperHold is { } hold && hold.Slot == player.Slot)
        {
            if (!hold.Armed) return true;
            if (hold.Charge is { } charge)
            {
                if ((released & charge.Button) != 0) KeeperRelease(player, charge);
                return true;
            }
            var punt = (pressed & PlayerButtons.Attack) != 0;
            var toss = !punt && (pressed & PlayerButtons.Attack2) != 0;
            if ((punt || toss) && player.PlayerPawn.Value is { IsValid: true } pawn)
                hold.Charge = new(pawn.EntityHandle.Raw, toss, punt ? PlayerButtons.Attack : PlayerButtons.Attack2, Server.TickedTime);
            return true;
        }
        if ((pressed & PlayerButtons.Attack2) == 0 || !KeeperActive(player, out _)) return false;
        var now = Server.TickedTime;
        if (!_keeperCatchNext.TryGetValue(player.Slot, out var next) || now >= next)
        {
            _keeperCatchUntil[player.Slot] = now + FootballKeeperRules.CatchWindow;
            _keeperCatchNext[player.Slot] = now + FootballKeeperRules.CatchCooldown;
        }
        return true;
    }

    private void FootballKeeperOnTick()
    {
        var now = Server.TickedTime;
        foreach (var (slot, until) in _keeperCatchUntil.ToArray())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (now > until || player is null || KeeperHoldsBall || !KeeperActive(player, out var pawn))
            {
                _keeperCatchUntil.Remove(slot);
                continue;
            }
            if (TryKeeperCatch(player, pawn, now)) _keeperCatchUntil.Remove(slot);
        }
        if (_keeperHold is { } hold) KeeperCarry(hold, now);
    }

    private bool TryKeeperCatch(CCSPlayerController player, CCSPlayerPawn pawn, double now)
    {
        if (_ball is not { IsValid: true } ball || ball.AbsOrigin is not { } ballOrigin || pawn.AbsOrigin is not { } feet
            || _ballMotionFrozen || _pausedBallHandle != 0 || _matchPhase is MatchPhase.GoalPause or MatchPhase.Paused) return false;
        var eye = new Vector(feet.X, feet.Y, feet.Z + pawn.ViewOffset.Z);
        var toBall = new Vector(ballOrigin.X - eye.X, ballOrigin.Y - eye.Y, ballOrigin.Z - eye.Z);
        var distance = VectorSpeed(toBall);
        var reach = FootballKeeperRules.CatchReach
            + (_keeperDiveReachUntil.TryGetValue(player.Slot, out var diveUntil) && now < diveUntil ? FootballKeeperRules.DiveReachBonus : 0.0f);
        if (distance > reach + BallCollisionRadius || distance < 0.001f) return false;
        var forward = KeeperForward(pawn.EyeAngles, horizontalOnly: false);
        var aim = Dot(forward, toBall) / distance;
        // Very close balls count from any angle (a ball at the keeper's feet).
        if (distance > BallCollisionRadius * 2 && aim < MathF.Cos(FootballKeeperRules.CatchConeDegrees * MathF.PI / 180.0f)) return false;
        var speed = VectorSpeed(_derivedBallVelocity);
        RecordBallTouch(player, ballOrigin);
        if (FootballKeeperRules.Catches(speed))
        {
            BeginKnifeBallContact(ball);
            ball.AcceptInput("DisableMotion");
            ball.AcceptInput("DisableCollision");
            _keeperHold = new KeeperHold { Slot = player.Slot, Pawn = pawn.EntityHandle.Raw, Since = now };
            KeeperCarry(_keeperHold, now);
            Logger.LogInformation("[SM2DIAG] keeper_catch slot={Slot} speed={Speed:F0} distance={Distance:F0}", player.Slot, speed, distance);
            return true;
        }
        // Parry: the ball leaves along the keeper's view, slower.
        var parry = KeeperForward(pawn.EyeAngles, horizontalOnly: false);
        var parrySpeed = FootballKeeperRules.ParrySpeed(speed);
        BeginKnifeBallContact(ball);
        ball.AcceptInput("Wake");
        ball.Teleport(velocity: new Vector(parry.X * parrySpeed, parry.Y * parrySpeed, MathF.Max(parry.Z * parrySpeed, 120.0f)));
        PlayKickSound(ball);
        Logger.LogInformation("[SM2DIAG] keeper_parry slot={Slot} in={In:F0} out={Out:F0}", player.Slot, speed, parrySpeed);
        return true;
    }

    private void KeeperCarry(KeeperHold hold, double now)
    {
        var player = Utilities.GetPlayerFromSlot(hold.Slot);
        if (_ball is not { IsValid: true } ball)
        {
            _keeperHold = null;
            return;
        }
        if (player is null || !IsEligiblePlayer(player) || player.PlayerPawn.Value is not { IsValid: true } pawn
            || pawn.EntityHandle.Raw != hold.Pawn || !KeeperActive(player, out _)
            || now - hold.Since > FootballKeeperRules.HoldSeconds
            || _pausedBallHandle != 0 || _matchPhase is MatchPhase.GoalPause or MatchPhase.Paused or MatchPhase.PeriodBreak)
        {
            KeeperDropBall("keeper_hold_ended");
            return;
        }
        if (!hold.Armed && (player.Buttons & (PlayerButtons.Attack | PlayerButtons.Attack2)) == 0) hold.Armed = true;
        if (hold.Charge is { } charge && (player.Buttons & charge.Button) == 0) KeeperRelease(player, charge);
        else ball.Teleport(position: KeeperHandPoint(player, pawn), velocity: new Vector(0, 0, 0));
    }

    // In front of and below the eyes, kept inside the keeper's box so the
    // carried ball can never reach the goal plane.
    private Vector KeeperHandPoint(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        var feet = pawn.AbsOrigin!;
        var forward = KeeperForward(pawn.EyeAngles, horizontalOnly: true);
        var box = GkBoxFor(player.Team);
        var margin = BallCollisionRadius + 12.0f;
        var x = Math.Clamp(feet.X + forward.X * FootballKeeperRules.HandForward, box.minX + margin, box.maxX - margin);
        var y = Math.Clamp(feet.Y + forward.Y * FootballKeeperRules.HandForward, box.minY + margin, box.maxY - margin);
        var z = MathF.Max(feet.Z + pawn.ViewOffset.Z - FootballKeeperRules.HandDown, StadiumPitchPlaneZ + BallCollisionRadius + 2.0f);
        return new Vector(x, y, z);
    }

    private void KeeperRelease(CCSPlayerController player, FootballCharge charge)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is not { IsValid: true }) { KeeperDropBall("keeper_release_invalid"); return; }
        var fraction = FootballKickRules.ChargeFraction(Server.TickedTime - charge.Started);
        var throwBall = charge.Secondary;
        var power = throwBall ? FootballKeeperRules.ThrowPower(fraction) : FootballKeeperRules.PuntPower(fraction);
        var elevation = (throwBall ? FootballKeeperRules.ThrowElevation(pawn.EyeAngles.X) : FootballKeeperRules.PuntElevation(pawn.EyeAngles.X)) * MathF.PI / 180.0f;
        var yaw = pawn.EyeAngles.Y * MathF.PI / 180.0f;
        var speed = _kickDeltaVelocity * ComputeGameplayMassResponse() * power;
        var velocity = new Vector(MathF.Cos(yaw) * MathF.Cos(elevation) * speed, MathF.Sin(yaw) * MathF.Cos(elevation) * speed, MathF.Sin(elevation) * speed);
        Logger.LogInformation("[SM2DIAG] keeper_{Kind} slot={Slot} charge={Charge:F2} speed={Speed:F0}", throwBall ? "throw" : "punt", player.Slot, fraction, speed);
        KeeperLetGo(player, velocity, $"keeper_{(throwBall ? "throw" : "punt")}");
    }

    // Drop in place (time up, left the box, round/pause): keeps the keeper's drift.
    private void KeeperDropBall(string reason)
    {
        if (_keeperHold is not { } hold) return;
        var player = Utilities.GetPlayerFromSlot(hold.Slot);
        var drift = player?.PlayerPawn.Value is { IsValid: true } pawn ? pawn.AbsVelocity : new Vector(0, 0, 0);
        KeeperLetGo(player, new Vector(drift.X * 0.5f, drift.Y * 0.5f, 0), reason);
    }

    private void KeeperLetGo(CCSPlayerController? player, Vector velocity, string reason)
    {
        var hold = _keeperHold;
        _keeperHold = null;
        if (hold is null || _ball is not { IsValid: true } ball) return;
        ball.AcceptInput("EnableCollision");
        ball.AcceptInput("EnableMotion");
        ball.AcceptInput("Wake");
        BeginKnifeBallContact(ball);
        ball.Teleport(velocity: velocity);
        // A DisableMotion ball leaves slow on the first tick (see ReapplyKickAfterUnfreeze).
        Server.NextFrame(() =>
        {
            if (_keeperHold is null && ball.IsValid) { ball.AcceptInput("Wake"); ball.Teleport(velocity: velocity); }
        });
        if (player is { IsValid: true } && ball.AbsOrigin is { } origin)
        {
            RecordBallTouch(player, origin);
            _lastAcceptedKickTimeBySlot[player.Slot] = Server.TickedTime;
            _lastKickCooldownBySlot[player.Slot] = FootballKickRules.Cooldown * 2;
        }
        if (VectorSpeed(velocity) > 200.0f) PlayKickSound(ball);
        Logger.LogInformation("[SM2DIAG] keeper_let_go reason={Reason} speed={Speed:F0} held={Held:F1}s", reason, VectorSpeed(velocity), Server.TickedTime - hold.Since);
    }

    // Round restart: the ball entity is new, nothing to re-enable.
    private void KeeperForgetBall()
    {
        _keeperHold = null;
        _keeperCatchUntil.Clear();
        _keeperDiveReachUntil.Clear();
    }

    private void TryKeeperDive(CCSPlayerController player)
    {
        if (!KeeperActive(player, out var pawn) || KeeperHoldsBall) return;
        var buttons = player.Buttons;
        var side = FootballKickRules.CurlSide((buttons & PlayerButtons.Moveleft) != 0, (buttons & PlayerButtons.Moveright) != 0);
        var now = Server.TickedTime;
        if (side == 0 || (_keeperDiveNext.TryGetValue(player.Slot, out var next) && now < next)) return;
        _keeperDiveNext[player.Slot] = now + FootballKeeperRules.DiveCooldown;
        _keeperDiveReachUntil[player.Slot] = now + FootballKeeperRules.DiveReachSeconds;
        var yaw = pawn.EyeAngles.Y * MathF.PI / 180.0f;
        // Left of the view is (-sin, cos); side +1 = left (A), -1 = right (D).
        var dirX = -MathF.Sin(yaw) * side;
        var dirY = MathF.Cos(yaw) * side;
        var handle = pawn.EntityHandle.Raw;
        // After the engine's own jump velocity (same reason as the jump assist).
        void Push(int frame)
        {
            if (!pawn.IsValid || pawn.EntityHandle.Raw != handle) return;
            var v = pawn.AbsVelocity;
            pawn.Teleport(velocity: new Vector(dirX * FootballKeeperRules.DiveSpeed, dirY * FootballKeeperRules.DiveSpeed,
                frame == 0 ? FootballKeeperRules.DiveLift : v.Z));
            if (frame < 8) Server.NextFrame(() => Push(frame + 1));
        }
        Server.NextFrame(() => Push(0));
        Logger.LogInformation("[SM2DIAG] keeper_dive slot={Slot} side={Side}", player.Slot, side > 0 ? "left" : "right");
    }

    private static Vector KeeperForward(QAngle angles, bool horizontalOnly)
    {
        var pitch = horizontalOnly ? 0.0f : angles.X * MathF.PI / 180.0f;
        var yaw = angles.Y * MathF.PI / 180.0f;
        return new Vector(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch));
    }
}

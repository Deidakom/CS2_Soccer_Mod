using System;
using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-08-30 user request: in CS:S, a fast ball hitting a player was a real
// Source-physics momentum exchange, not something SoMoE coded on purpose -
// it pushed the PLAYER back proportional to the ball's speed, and if the
// ball landed ON a player it bounced off with real energy instead of just
// dying, which the player could then knife again for a compounded
// "powershot". Our ball's native body-vs-body collision was already
// established this session as too weak to matter on its own (that is
// exactly why ApplyPlayerBallPush exists for the walking-into-it
// direction) - this is the opposite direction: a FAST ball hitting a
// player, not a player walking into a slow/resting ball.
//
// The "powershot" half needs no new mechanic at all: kicks already ADD
// their delta onto whatever velocity the ball currently has (see the kick
// comment in the main file), so once the ball genuinely has real velocity
// after bouncing off someone, a follow-up kick compounds automatically.
// The only new thing needed is making that bounce actually happen.
//
// 2026-08-31 calibration against the live CS:S reference server's exact XSL
// ball: native post-contact player speed was 141, 295, 443, 590 and 741 u/s
// for 300, 600, 900, 1200 and 1500 u/s ball launches respectively. That is
// a near-linear 0.50 transfer with no observed cap. The player retained 100
// health, m_takedamage was 0, and no player_hurt event fired: the old “hurt”
// feel was feedback/motion, not gameplay damage.
public sealed partial class SoccerModMvpPlugin
{
    // Ball must be moving at least this fast to count as a real "hit"
    // rather than gently resting against/rolling past a player.
    private const float DefaultBallImpactMinSpeed = 150.0f;
    // Player knockback: fraction of the ball's speed transferred to the
    // player, in the ball's own direction of travel (a ball flying INTO
    // you knocks you the way it was going).
    private const float DefaultBallImpactPlayerPushRatio = 0.5f;
    // CS:S stayed linear through 1500 u/s. Match that model through CS2's
    // configured 3500 u/s ball-speed ceiling instead of crushing every
    // normal hard kick into the old invented 250 u/s cap.
    private const float DefaultBallImpactPlayerPushMax = 1750.0f;
    // Client-only, non-damaging visual hurt feedback. Full strength
    // corresponds to a normal 1500 u/s CS:S impact (~750 u/s player push).
    // Camera shake was removed after live user feedback; physical knockback
    // already communicates the hit and must remain the only motion effect.
    private const float BallImpactFeedbackFullStrengthPush = 750.0f;
    private const int DefaultBallImpactFeedbackMaxVisualDamage = 10;
    // A pawn velocity written during the collision tick can be replaced by
    // CS2's player-movement pass. Re-assert the same TARGET velocity briefly
    // on the following frames. This never adds the impulse repeatedly: each
    // pass only restores velocity that the engine discarded.
    private const int BallImpactKnockbackReapplyFrames = 2;
    // Ball bounce: only fires when the ball has real downward motion
    // (genuinely landing on the player, not just brushing past them
    // horizontally). Reflects a fraction of the incoming speed back
    // upward and dampens the horizontal component, same shape as a wall
    // bounce but supplied entirely by us since there is no native one.
    private const float DefaultBallImpactFallSpeedThreshold = 80.0f;
    private const float DefaultBallImpactBounceRestitution = 0.6f;
    private const float DefaultBallImpactBounceHorizontalRetention = 0.7f;
    private const float DefaultBallImpactBounceMaxVertical = 600.0f;
    private const double BallImpactCooldownSeconds = 0.5;

    private bool _ballImpactEnabled = true;
    private float _ballImpactMinSpeed = DefaultBallImpactMinSpeed;
    private float _ballImpactPlayerPushRatio = DefaultBallImpactPlayerPushRatio;
    private float _ballImpactPlayerPushMax = DefaultBallImpactPlayerPushMax;
    private float _ballImpactFallSpeedThreshold = DefaultBallImpactFallSpeedThreshold;
    private float _ballImpactBounceRestitution = DefaultBallImpactBounceRestitution;
    private float _ballImpactBounceHorizontalRetention = DefaultBallImpactBounceHorizontalRetention;
    private float _ballImpactBounceMaxVertical = DefaultBallImpactBounceMaxVertical;
    private bool _ballImpactFeedbackEnabled = true;
    private int _ballImpactFeedbackMaxVisualDamage = DefaultBallImpactFeedbackMaxVisualDamage;
    private readonly Dictionary<int, double> _lastBallImpactTimeBySlot = new();
    private uint? _ballImpactTrackedEntityIndex;
    private Vector? _previousBallImpactOrigin;
    private Vector? _previousBallImpactVelocity;

    private void BodyImpactOnLoad()
    {
        AddCommand("css_sm2ball_impact", "Admin: toggle ball-vs-player impact (push + bounce) on/off.", OnBallImpactToggleCommand);
        AddCommand("css_sm2ball_impact_push", "Admin: tune player knockback (minSpeed, ratio, max).", OnBallImpactPushCommand);
        AddCommand("css_sm2ball_impact_bounce", "Admin: tune ball bounce off players (fallThreshold, restitution, horizontalRetention, maxVertical).", OnBallImpactBounceCommand);
        AddCommand("css_sm2ball_impact_feedback", "Admin: tune the non-damaging visual impact cue (on|off, maxVisualDamage).", OnBallImpactFeedbackCommand);
    }

    // Called every tick from the main OnTick, alongside ApplyPlayerBallPush
    // (the opposite direction: player walking into a slow ball).
    private void ApplyBallPlayerImpact()
    {
        if (!_ballImpactEnabled || _ball is not { IsValid: true } || _ball.AbsOrigin is not { } origin)
        {
            ResetBodyImpactMotionTracking();
            return;
        }

        if (_ballImpactTrackedEntityIndex != _ball.Index)
        {
            ResetBodyImpactMotionTracking();
            _ballImpactTrackedEntityIndex = _ball.Index;
        }

        // Keep a separate one-tick history for body impacts. At the actual
        // physics contact frame Rubikon has often already reduced a 1400-u/s
        // kick to 150-250 u/s before this listener samples it. That was the
        // measured live failure: every full-speed kick was absent from the
        // impact log, while only the already-spent contact velocity fired.
        // Use the faster of the current and immediately preceding samples as
        // the incoming velocity, and sweep the ball centre across both
        // positions so a fast crossing cannot fall between tick samples.
        var currentVelocity = new Vector(
            _derivedBallVelocity.X,
            _derivedBallVelocity.Y,
            _derivedBallVelocity.Z);
        var previousOrigin = _previousBallImpactOrigin;
        var previousVelocity = _previousBallImpactVelocity;
        _previousBallImpactOrigin = new Vector(origin.X, origin.Y, origin.Z);
        _previousBallImpactVelocity = currentVelocity;

        var ballVelocity = currentVelocity;
        var velocitySample = "current";
        if (previousVelocity is not null && VectorSpeed(previousVelocity) > VectorSpeed(currentVelocity))
        {
            ballVelocity = previousVelocity;
            velocitySample = "previous";
        }

        var ballSpeed = VectorSpeed(ballVelocity);
        if (ballSpeed < _ballImpactMinSpeed)
        {
            return;
        }

        var planarBallSpeed = MathF.Sqrt(
            ballVelocity.X * ballVelocity.X + ballVelocity.Y * ballVelocity.Y);
        if (planarBallSpeed < 0.001f)
        {
            return;
        }

        var segmentStart = previousOrigin ?? origin;
        var segmentX = origin.X - segmentStart.X;
        var segmentY = origin.Y - segmentStart.Y;
        var segmentLengthSquared = segmentX * segmentX + segmentY * segmentY;

        var now = Server.TickedTime;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsEligiblePlayer(player) || player.PlayerPawn.Value is not { IsValid: true } pawn
                || pawn.AbsOrigin is not { } playerOrigin || !IsAlive(pawn))
            {
                continue;
            }

            var closestFraction = segmentLengthSquared > 0.0001f
                ? Math.Clamp(
                    ((playerOrigin.X - segmentStart.X) * segmentX
                        + (playerOrigin.Y - segmentStart.Y) * segmentY) / segmentLengthSquared,
                    0.0f,
                    1.0f)
                : 1.0f;
            var closestX = segmentStart.X + segmentX * closestFraction;
            var closestY = segmentStart.Y + segmentY * closestFraction;
            var closestZ = segmentStart.Z + (origin.Z - segmentStart.Z) * closestFraction;
            if (MathF.Abs(closestZ - playerOrigin.Z) > BallPushHeightGate)
            {
                continue;
            }

            var dx = closestX - playerOrigin.X;
            var dy = closestY - playerOrigin.Y;
            var planarDistance = MathF.Sqrt(dx * dx + dy * dy);
            if (planarDistance > BallPushContactDistance)
            {
                continue;
            }

            if (_lastBallImpactTimeBySlot.TryGetValue(player.Slot, out var lastTime)
                && now - lastTime < BallImpactCooldownSeconds)
            {
                continue;
            }

            // Player knockback, in the ball's own travel direction.
            var pushAmount = Math.Min(ballSpeed * _ballImpactPlayerPushRatio, _ballImpactPlayerPushMax);
            var dirX = ballVelocity.X / planarBallSpeed;
            var dirY = ballVelocity.Y / planarBallSpeed;
            var playerVelocity = pawn.AbsVelocity;
            var targetAlongDirection = playerVelocity.X * dirX + playerVelocity.Y * dirY + pushAmount;
            ApplyBallImpactKnockback(pawn, dirX, dirY, targetAlongDirection);
            ScheduleBallImpactKnockback(
                player.Slot,
                dirX,
                dirY,
                targetAlongDirection,
                BallImpactKnockbackReapplyFrames);
            var feedback = ApplyBallImpactFeedback(player, pawn, pushAmount);

            var bounced = false;
            var bounceVertical = 0.0f;
            if (-ballVelocity.Z > _ballImpactFallSpeedThreshold)
            {
                // Ball is genuinely falling onto the player - bounce it
                // off instead of letting it just die on contact.
                bounceVertical = Math.Min(-ballVelocity.Z * _ballImpactBounceRestitution, _ballImpactBounceMaxVertical);
                var bouncedVelocity = new Vector(
                    ballVelocity.X * _ballImpactBounceHorizontalRetention,
                    ballVelocity.Y * _ballImpactBounceHorizontalRetention,
                    bounceVertical);
                _ball.AcceptInput("Wake");
                _ball.Teleport(velocity: bouncedVelocity);
                bounced = true;
            }

            _lastBallImpactTimeBySlot[player.Slot] = now;
            Logger.LogInformation(
                "[SM2DIAG] ball_player_impact slot={Slot} name={Name} ballSpeed={BallSpeed:F1} velocitySample={VelocitySample} contactDistance={ContactDistance:F1} sweptFraction={SweptFraction:F2} pushAmount={PushAmount:F1} targetAlong={TargetAlong:F1} bounced={Bounced} bounceVertical={BounceVertical:F1} feedback={Feedback} visualDamage={VisualDamage}",
                player.Slot,
                player.PlayerName,
                ballSpeed,
                velocitySample,
                planarDistance,
                closestFraction,
                pushAmount,
                targetAlongDirection,
                bounced,
                bounceVertical,
                feedback.Applied,
                feedback.VisualDamage);

            // Only the first player hit in a tick gets the knockback/bounce
            // - a bounced ball's new velocity next tick will re-evaluate
            // naturally if it hits someone else.
            return;
        }
    }

    private static void ApplyBallImpactKnockback(
        CCSPlayerPawn pawn,
        float dirX,
        float dirY,
        float targetAlongDirection)
    {
        var velocity = pawn.AbsVelocity;
        var currentAlongDirection = velocity.X * dirX + velocity.Y * dirY;
        var missingVelocity = Math.Max(0.0f, targetAlongDirection - currentAlongDirection);
        if (missingVelocity < 0.01f)
        {
            return;
        }

        pawn.Teleport(velocity: new Vector(
            velocity.X + dirX * missingVelocity,
            velocity.Y + dirY * missingVelocity,
            velocity.Z));
    }

    private void ScheduleBallImpactKnockback(
        int slot,
        float dirX,
        float dirY,
        float targetAlongDirection,
        int framesRemaining)
    {
        if (framesRemaining <= 0)
        {
            return;
        }

        Server.NextFrame(() =>
        {
            if (Utilities.GetPlayerFromSlot(slot)?.PlayerPawn.Value is not { IsValid: true } pawn
                || !IsAlive(pawn))
            {
                return;
            }

            ApplyBallImpactKnockback(pawn, dirX, dirY, targetAlongDirection);
            if (framesRemaining > 1)
            {
                ScheduleBallImpactKnockback(
                    slot,
                    dirX,
                    dirY,
                    targetAlongDirection,
                    framesRemaining - 1);
            }
        });
    }

    // Recreates the perceptual “I was hit” cue without touching server-side
    // health or firing a synthetic player_hurt event. Damage is a client HUD
    // user message only. Camera/screen shake is intentionally not sent.
    private (bool Applied, int VisualDamage) ApplyBallImpactFeedback(
        CCSPlayerController player,
        CCSPlayerPawn pawn,
        float pushAmount)
    {
        if (!_ballImpactFeedbackEnabled)
        {
            return (false, 0);
        }

        var strength = Math.Clamp(pushAmount / BallImpactFeedbackFullStrengthPush, 0.0f, 1.0f);
        var visualDamage = Math.Clamp(
            (int)MathF.Round(1.0f + strength * (_ballImpactFeedbackMaxVisualDamage - 1)),
            1,
            _ballImpactFeedbackMaxVisualDamage);
        var applied = false;

        try
        {
            using var damageMessage = UserMessage.FromPartialName("Damage");
            damageMessage.SetInt("amount", visualDamage);
            damageMessage.SetInt("victim_entindex", (int)pawn.Index);
            damageMessage.Recipients.Add(player);
            damageMessage.Send();
            applied = true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[SM2DIAG] ball_impact_damage_feedback_failed slot={Slot}", player.Slot);
        }

        return (applied, visualDamage);
    }

    private void BodyImpactOnPlayerDisconnect(int slot)
    {
        _lastBallImpactTimeBySlot.Remove(slot);
    }

    private void ResetBodyImpactMotionTracking()
    {
        _ballImpactTrackedEntityIndex = null;
        _previousBallImpactOrigin = null;
        _previousBallImpactVelocity = null;
    }

    private void OnBallImpactToggleCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            _ballImpactEnabled = command.GetArg(1).Equals("on", StringComparison.OrdinalIgnoreCase);
            SaveBallSettings("ball_impact_toggle_command");
        }

        command.ReplyToCommand($"[SM] ball-vs-player impact: {(_ballImpactEnabled ? "on" : "off")} (usage: css_sm2ball_impact <on|off>)");
    }

    private void OnBallImpactPushCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 4
            && float.TryParse(command.GetArg(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var minSpeed)
            && float.TryParse(command.GetArg(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio)
            && float.TryParse(command.GetArg(3), NumberStyles.Float, CultureInfo.InvariantCulture, out var max)
            && minSpeed > 0.0f && ratio > 0.0f && max > 0.0f)
        {
            _ballImpactMinSpeed = minSpeed;
            _ballImpactPlayerPushRatio = ratio;
            _ballImpactPlayerPushMax = max;
            SaveBallSettings("ball_impact_push_command");
        }

        command.ReplyToCommand(
            $"[SM] ball impact push: minSpeed={_ballImpactMinSpeed:F0} ratio={_ballImpactPlayerPushRatio:F2} max={_ballImpactPlayerPushMax:F0} "
            + "(usage: css_sm2ball_impact_push <minSpeed> <ratio> <max>)");
    }

    private void OnBallImpactBounceCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 5
            && float.TryParse(command.GetArg(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var fallThreshold)
            && float.TryParse(command.GetArg(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var restitution)
            && float.TryParse(command.GetArg(3), NumberStyles.Float, CultureInfo.InvariantCulture, out var horizontalRetention)
            && float.TryParse(command.GetArg(4), NumberStyles.Float, CultureInfo.InvariantCulture, out var maxVertical)
            && fallThreshold > 0.0f && restitution > 0.0f && horizontalRetention > 0.0f && maxVertical > 0.0f)
        {
            _ballImpactFallSpeedThreshold = fallThreshold;
            _ballImpactBounceRestitution = restitution;
            _ballImpactBounceHorizontalRetention = horizontalRetention;
            _ballImpactBounceMaxVertical = maxVertical;
            SaveBallSettings("ball_impact_bounce_command");
        }

        command.ReplyToCommand(
            $"[SM] ball impact bounce: fallThreshold={_ballImpactFallSpeedThreshold:F0} restitution={_ballImpactBounceRestitution:F2} "
            + $"horizontalRetention={_ballImpactBounceHorizontalRetention:F2} maxVertical={_ballImpactBounceMaxVertical:F0} "
            + "(usage: css_sm2ball_impact_bounce <fallThreshold> <restitution> <horizontalRetention> <maxVertical>)");
    }

    private void OnBallImpactFeedbackCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            var enabledArg = command.GetArg(1);
            if (enabledArg.Equals("on", StringComparison.OrdinalIgnoreCase)
                || enabledArg.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                _ballImpactFeedbackEnabled = enabledArg.Equals("on", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (command.ArgCount >= 3
            && int.TryParse(command.GetArg(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxVisualDamage)
            && maxVisualDamage >= 1 && maxVisualDamage <= 100)
        {
            _ballImpactFeedbackMaxVisualDamage = maxVisualDamage;
        }

        if (command.ArgCount >= 2)
        {
            SaveBallSettings("ball_impact_feedback_command");
        }

        command.ReplyToCommand(
            $"[SM] ball impact visual cue: {(_ballImpactFeedbackEnabled ? "on" : "off")} "
            + $"maxVisualDamage={_ballImpactFeedbackMaxVisualDamage} (no shake, no health loss; usage: css_sm2ball_impact_feedback <on|off> [maxVisualDamage])");
    }
}

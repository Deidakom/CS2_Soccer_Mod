using System;
using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
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
// Brand new, untested mechanic - every number here is invented, not
// measured against a CS:S capture like the wall-hop was. Expect to tune
// this live with the user rather than trust these defaults.
public sealed partial class SoccerModMvpPlugin
{
    // Ball must be moving at least this fast to count as a real "hit"
    // rather than gently resting against/rolling past a player.
    private const float DefaultBallImpactMinSpeed = 150.0f;
    // Player knockback: fraction of the ball's speed transferred to the
    // player, in the ball's own direction of travel (a ball flying INTO
    // you knocks you the way it was going).
    private const float DefaultBallImpactPlayerPushRatio = 0.5f;
    private const float DefaultBallImpactPlayerPushMax = 250.0f;
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
    private readonly Dictionary<int, double> _lastBallImpactTimeBySlot = new();

    private void BodyImpactOnLoad()
    {
        AddCommand("css_sm2ball_impact", "Admin: toggle ball-vs-player impact (push + bounce) on/off.", OnBallImpactToggleCommand);
        AddCommand("css_sm2ball_impact_push", "Admin: tune player knockback (minSpeed, ratio, max).", OnBallImpactPushCommand);
        AddCommand("css_sm2ball_impact_bounce", "Admin: tune ball bounce off players (fallThreshold, restitution, horizontalRetention, maxVertical).", OnBallImpactBounceCommand);
    }

    // Called every tick from the main OnTick, alongside ApplyPlayerBallPush
    // (the opposite direction: player walking into a slow ball).
    private void ApplyBallPlayerImpact()
    {
        if (!_ballImpactEnabled || _ball is not { IsValid: true } || _ball.AbsOrigin is not { } origin)
        {
            return;
        }

        var ballVelocity = _derivedBallVelocity;
        var ballSpeed = VectorSpeed(ballVelocity);
        if (ballSpeed < _ballImpactMinSpeed)
        {
            return;
        }

        var now = Server.TickedTime;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsEligiblePlayer(player) || player.PlayerPawn.Value is not { IsValid: true } pawn
                || pawn.AbsOrigin is not { } playerOrigin || !IsAlive(pawn))
            {
                continue;
            }

            if (MathF.Abs(origin.Z - playerOrigin.Z) > BallPushHeightGate)
            {
                continue;
            }

            var dx = origin.X - playerOrigin.X;
            var dy = origin.Y - playerOrigin.Y;
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
            var dirX = ballVelocity.X / ballSpeed;
            var dirY = ballVelocity.Y / ballSpeed;
            var playerVelocity = pawn.AbsVelocity;
            var knockedVelocity = new Vector(
                playerVelocity.X + dirX * pushAmount,
                playerVelocity.Y + dirY * pushAmount,
                playerVelocity.Z);
            pawn.Teleport(velocity: knockedVelocity);

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
                "[SM2DIAG] ball_player_impact slot={Slot} name={Name} ballSpeed={BallSpeed:F1} pushAmount={PushAmount:F1} bounced={Bounced} bounceVertical={BounceVertical:F1}",
                player.Slot,
                player.PlayerName,
                ballSpeed,
                pushAmount,
                bounced,
                bounceVertical);

            // Only the first player hit in a tick gets the knockback/bounce
            // - a bounced ball's new velocity next tick will re-evaluate
            // naturally if it hits someone else.
            return;
        }
    }

    private void BodyImpactOnPlayerDisconnect(int slot)
    {
        _lastBallImpactTimeBySlot.Remove(slot);
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
}

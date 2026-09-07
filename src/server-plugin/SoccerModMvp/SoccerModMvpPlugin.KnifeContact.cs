using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

public sealed partial class SoccerModMvpPlugin
{
    private sealed record KnifeSwing(uint Pawn, uint Weapon, double Started, QAngle Aim, float Power, string Mode)
    {
        internal bool Retrying;
    }
    private readonly Dictionary<int, KnifeSwing> _knifeSwings = new();
    private sealed record HeldKnifeSwing(uint Pawn, uint Weapon, string Mode, double Next);
    private readonly Dictionary<int, HeldKnifeSwing> _heldKnifeSwings = new();

    private void BeginKnifeSwing(CCSPlayerController player, CCSPlayerPawn? pawn, CBasePlayerWeapon? weapon, float power, string mode)
    {
        _knifeSwings.Remove(player.Slot);
        if (pawn is { IsValid: true } && weapon is { IsValid: true })
        {
            _knifeSwings[player.Slot] = new(pawn.EntityHandle.Raw, weapon.EntityHandle.Raw, Server.TickedTime,
                new QAngle(pawn.EyeAngles.X, pawn.EyeAngles.Y, pawn.EyeAngles.Z), power, mode);
            if (weapon.DesignerName.Contains("knife", StringComparison.OrdinalIgnoreCase))
                _heldKnifeSwings[player.Slot] = new(pawn.EntityHandle.Raw, weapon.EntityHandle.Raw, mode,
                    KnifeSwingRules.NextHeldSwing(Server.TickedTime, _kickCooldownSeconds));
        }
        // Already-valid contact is immediate, not queued for a timer.
        TryApplyPrimaryKnifeKick(player, pawn, weapon, power, mode);
    }

    private void UpdateKnifeSwings()
    {
        // ButtonsChanged only reports the press edge. CS2 continues swinging
        // while Attack is held, so re-arm contact windows independently of
        // whether the previous swing hit, missed, or expired against a wall.
        foreach (var (slot, held) in _heldKnifeSwings.ToArray())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            var pawn = player?.PlayerPawn.Value;
            var weapon = pawn?.WeaponServices?.ActiveWeapon.Value;
            var button = held.Mode == "primary" ? PlayerButtons.Attack : PlayerButtons.Attack2;
            if (!IsEligiblePlayer(player) || pawn is not { IsValid: true }
                || pawn.EntityHandle.Raw != held.Pawn || weapon is not { IsValid: true }
                || weapon.EntityHandle.Raw != held.Weapon || (player!.Buttons & button) == 0
                || _pausedBallHandle != 0 || _matchPhase == MatchPhase.Paused)
            { _heldKnifeSwings.Remove(slot); _knifeSwings.Remove(slot); continue; }
            if (!KnifeSwingRules.HeldSwingDue(Server.TickedTime, held.Next, true)) continue;
            var crouching = IsPlayerCrouching(pawn);
            var power = held.Mode == "primary"
                ? (crouching ? _leftClickCrouchPowerScale : _leftClickPowerScale)
                : (crouching ? _rightClickCrouchPowerScale : _rightClickPowerScale);
            BeginKnifeSwing(player!, pawn, weapon, power, held.Mode);
        }
        foreach (var (slot, swing) in _knifeSwings.ToArray())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            var pawn = player?.PlayerPawn.Value;
            var weapon = pawn?.WeaponServices?.ActiveWeapon.Value;
            if (!KnifeSwingRules.WithinWindow(Server.TickedTime, swing.Started) || !IsEligiblePlayer(player)
                || pawn is not { IsValid: true } || pawn.EntityHandle.Raw != swing.Pawn
                || weapon is not { IsValid: true } || weapon.EntityHandle.Raw != swing.Weapon
                || _pausedBallHandle != 0 || _matchPhase == MatchPhase.Paused
                || !KnifeSwingRules.AimUnchanged(pawn.EyeAngles.X, pawn.EyeAngles.Y, swing.Aim.X, swing.Aim.Y))
            { _knifeSwings.Remove(slot); continue; }
            swing.Retrying = true;
            TryApplyPrimaryKnifeKick(player!, pawn, weapon, swing.Power, swing.Mode, swing.Aim);
        }
    }

    private void CompleteKnifeSwing(CCSPlayerController player, float distance, bool approaching = false)
    {
        if (!_knifeSwings.Remove(player.Slot, out var swing)) return;
        Logger.LogInformation("[SM2DIAG] knife_contact slot={Slot} windowMs={Ms:F1} retried={Retried} earlyBlend={Early:F3}",
            player.Slot, (Server.TickedTime - swing.Started) * 1000, swing.Retrying,
            approaching ? BallContactMath.EarlyContactFraction(MathF.Max(0, distance - BallCollisionRadius), _kickSurfaceReach) : 0);
    }

    private void ApplyPreKickPlayerImpact(CCSPlayerController player, PlayableBall target)
    {
        if (!_ballImpactEnabled) return;
        if (target.IsMatchBall)
            ApplyBallPlayerImpactFor(target.Ball, target.Origin, target.Inherited,
                ref _previousBallImpactOrigin, ref _previousBallImpactVelocity, player.Slot);
        else if (_trainingBalls.TryGetValue(target.Ball.Index, out var training))
            ApplyBallPlayerImpactFor(target.Ball, target.Origin, target.Inherited,
                ref training.PreviousImpactOrigin, ref training.PreviousImpactVelocity, player.Slot);
    }
}

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Football mode (owner 2026-09-26, map-test server only for now):
//  - empty hands: nobody holds a knife, the mouse buttons kick directly;
//  - hold to charge, release to kick (football HUD or the centre box);
//  - left = shot, Ctrl + left = chip, right = ground pass, Ctrl + right =
//    lofted pass; A/D at the release curls, Shift = finesse, holding past
//    full overhits; R = first touch. Keepers: FootballKeeper.cs.
// Off by default; css_sm2football on|off or Admin - Settings.
public sealed partial class SoccerModMvpPlugin
{
    private sealed record FootballCharge(uint Pawn, bool Secondary, PlayerButtons Button, double Started);
    private readonly Dictionary<int, FootballCharge> _footballCharges = new();
    // The released kick's details, read by the kick path while its contact
    // window is open (TryApplyPrimaryKnifeKick).
    private sealed record FootballKick(string Style, float Charge, float Overhit, float Curl, bool Finesse);
    private readonly Dictionary<int, FootballKick> _footballKicks = new();
    private readonly Dictionary<int, double> _footballTrapUntil = new();
    private readonly Dictionary<int, double> _footballTrapNext = new();
    private const double FootballTrapWindow = 0.4;
    private const double FootballTrapCooldown = 0.8;
    private const float FootballTrapRetain = 0.15f;
    private const float FootballTrapReach = 72.0f;

    private bool FootballMode => _menuParity.FootballMode;
    private bool FootballEmptyHands => _menuParity.FootballMode && _menuParity.FootballEmptyHands;

    private void FootballOnLoad()
    {
        AddCommand("css_sm2football", "Admin: football mode on|off [hands on|off].", OnFootballCommand);
        FootballKeeperOnLoad();
        // FootballHudOnLoad runs next to SprintHudOnLoad (UI kit first).
        Server.NextFrame(ApplyFootballDefaultMelee);
    }

    private void OnFootballCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null && !HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0, "admin"))
        {
            command.ReplyToCommand("[SM] Admins only.");
            return;
        }
        var arg = command.ArgCount > 1 ? command.GetArg(1).ToLowerInvariant() : "";
        if (arg == "hands" && command.ArgCount > 2)
            SetFootballMode(_menuParity.FootballMode, command.GetArg(2).Equals("on", StringComparison.OrdinalIgnoreCase));
        else if (arg is "on" or "off")
            SetFootballMode(arg == "on", _menuParity.FootballEmptyHands);
        command.ReplyToCommand($"[SM] football={OnOff(_menuParity.FootballMode)} emptyHands={OnOff(_menuParity.FootballEmptyHands)} marker={OnOff(_menuParity.FootballLandingMarker)}");
    }

    private void SetFootballMode(bool on, bool emptyHands)
    {
        _menuParity.FootballMode = on;
        _menuParity.FootballEmptyHands = emptyHands;
        SaveJsonAtomic(MenuParityFile, _menuParity);
        _footballCharges.Clear();
        _footballKicks.Clear();
        _heldKnifeSwings.Clear();
        _knifeSwings.Clear();
        KeeperDropBall("football_mode_toggle");
        ApplyFootballDefaultMelee();
        foreach (var player in Utilities.GetPlayers()) EnsurePlayerKnife(player, "football_mode_toggle");
        if (!on) { FootballHudHideAll(); FootballMarkerClear(); }
        Logger.LogInformation("[SM2DIAG] football_mode on={On} emptyHands={Hands}", on, emptyHands);
    }

    // The engine hands out its default melee on every spawn; empty hands
    // needs that switched off, and the knife back when the mode is left.
    private void ApplyFootballDefaultMelee()
    {
        var melee = FootballEmptyHands ? "\"\"" : "weapon_knife";
        Server.ExecuteCommand($"mp_ct_default_melee {melee}; mp_t_default_melee {melee}");
    }

    private void FootballOnRoundStart()
    {
        _footballCharges.Clear();
        _footballKicks.Clear();
        _footballTrapUntil.Clear();
        KeeperForgetBall();
        FootballMarkerClear();
        if (FootballEmptyHands) ApplyFootballDefaultMelee();
    }

    // Called from EnsurePlayerKnife: with empty hands the player keeps nothing.
    private bool StripForEmptyHands(CCSPlayerController player, string reason)
    {
        if (ConVar.Find("mp_ct_default_melee")?.StringValue is { Length: > 0 }) ApplyFootballDefaultMelee();
        var weapons = player.PlayerPawn.Value?.WeaponServices?.MyWeapons;
        if (weapons is null || !weapons.Any(h => h.Value is { IsValid: true })) return true;
        player.RemoveWeapons();
        Logger.LogInformation("[SM2DIAG] football_strip reason={Reason} slot={Slot}", reason, player.Slot);
        return true;
    }

    private void FootballOnButtons(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if (FootballKeeperOnButtons(player, pressed, released)) return;
        if ((pressed & PlayerButtons.Reload) != 0) ArmFootballTrap(player);
        if (_footballCharges.TryGetValue(player.Slot, out var charge) && (released & charge.Button) != 0)
        {
            ReleaseFootballCharge(player, charge);
            return;
        }
        if (_footballCharges.ContainsKey(player.Slot)) return;
        var primary = (pressed & PlayerButtons.Attack) != 0;
        var secondary = !primary && (pressed & PlayerButtons.Attack2) != 0;
        if (!primary && !secondary) return;
        var pawn = player.PlayerPawn.Value;
        if (!IsEligiblePlayer(player) || pawn is not { IsValid: true }) return;
        _footballCharges[player.Slot] = new(pawn.EntityHandle.Raw, secondary,
            primary ? PlayerButtons.Attack : PlayerButtons.Attack2, Server.TickedTime);
    }

    // Style, curl and finesse are read at the release: crouch/A/D/Shift may
    // change while charging.
    private FootballKick ReadFootballKick(CCSPlayerController player, CCSPlayerPawn pawn, FootballCharge charge)
    {
        var held = Server.TickedTime - charge.Started;
        var style = FootballKickRules.Style(charge.Secondary, IsPlayerCrouching(pawn));
        var fraction = FootballKickRules.ChargeFraction(held);
        var overhit = FootballKickRules.CanOverhit(style) ? FootballKickRules.Overhit(held) : 0.0f;
        var buttons = player.Buttons;
        var finesse = FootballKickRules.CanOverhit(style) && (buttons & PlayerButtons.Speed) != 0;
        var side = FootballKickRules.CurlSide((buttons & PlayerButtons.Moveleft) != 0, (buttons & PlayerButtons.Moveright) != 0);
        return new(style, fraction, overhit, FootballKickRules.Curl(style, side, fraction, finesse), finesse);
    }

    private void ReleaseFootballCharge(CCSPlayerController player, FootballCharge charge)
    {
        _footballCharges.Remove(player.Slot);
        var pawn = player.PlayerPawn.Value;
        if (!IsEligiblePlayer(player) || pawn is not { IsValid: true } || pawn.EntityHandle.Raw != charge.Pawn) return;
        var kick = ReadFootballKick(player, pawn, charge);
        var power = FootballKickRules.PowerScale(kick.Style, kick.Charge, kick.Finesse);
        _footballKicks[player.Slot] = kick;
        Logger.LogInformation("[SM2DIAG] football_release slot={Slot} style={Style} charge={Charge:F2} overhit={Overhit:F2} curl={Curl:F2} finesse={Finesse} power={Power:F2}",
            player.Slot, kick.Style, kick.Charge, kick.Overhit, kick.Curl, kick.Finesse, power);
        var weapon = pawn.WeaponServices?.ActiveWeapon.Value;
        _knifeSwings[player.Slot] = new(pawn.EntityHandle.Raw, weapon is { IsValid: true } ? weapon.EntityHandle.Raw : 0,
            Server.TickedTime, new QAngle(pawn.EyeAngles.X, pawn.EyeAngles.Y, pawn.EyeAngles.Z), power, kick.Style);
        TryApplyPrimaryKnifeKick(player, pawn, weapon, power, kick.Style);
    }

    // R: the next touch within the window kills the ball's speed relative to
    // the player (keeps 15 %), in any handling profile and at any ball speed.
    private void ArmFootballTrap(CCSPlayerController player)
    {
        var now = Server.TickedTime;
        if (_footballTrapNext.TryGetValue(player.Slot, out var next) && now < next) return;
        _footballTrapUntil[player.Slot] = now + FootballTrapWindow;
        _footballTrapNext[player.Slot] = now + FootballTrapCooldown;
    }

    private void FootballTrapOnTick()
    {
        if (_footballTrapUntil.Count == 0) return;
        var now = Server.TickedTime;
        foreach (var (slot, until) in _footballTrapUntil.ToArray())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            var pawn = player?.PlayerPawn.Value;
            if (now > until || player is null || !IsEligiblePlayer(player) || pawn?.AbsOrigin is not { } origin)
            {
                _footballTrapUntil.Remove(slot);
                continue;
            }
            if (KeeperHoldsBall || _ballMotionFrozen || _pausedBallHandle != 0) continue;
            foreach (var target in PlayableBalls())
            {
                var eyeZ = origin.Z + pawn.ViewOffset.Z;
                var body = new Vector(origin.X, origin.Y, Math.Clamp(target.Origin.Z, origin.Z, eyeZ));
                var toBall = new Vector(target.Origin.X - body.X, target.Origin.Y - body.Y, target.Origin.Z - body.Z);
                if (VectorSpeed(toBall) > FootballTrapReach + BallCollisionRadius) continue;
                var mover = pawn.AbsVelocity;
                var v = target.Inherited;
                var trapped = new Vector(
                    mover.X + (v.X - mover.X) * FootballTrapRetain,
                    mover.Y + (v.Y - mover.Y) * FootballTrapRetain,
                    MathF.Min(v.Z, 0.0f) * FootballTrapRetain);
                BeginKnifeBallContact(target.Ball);
                target.Ball.AcceptInput("Wake");
                target.Ball.Teleport(velocity: trapped);
                if (target.IsMatchBall) RecordBallTouch(player, target.Origin);
                PlayKickSound(target.Ball);
                _footballTrapUntil.Remove(slot);
                Logger.LogInformation("[SM2DIAG] football_trap slot={Slot} in={In:F0} out={Out:F0}", slot, VectorSpeed(v), VectorSpeed(trapped));
                break;
            }
        }
    }

    private void FootballOnTick()
    {
        if (!FootballMode) return;
        FootballTrapOnTick();
        FootballKeeperOnTick();
        FootballMarkerOnTick();
        foreach (var (slot, charge) in _footballCharges.ToArray())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            var pawn = player?.PlayerPawn.Value;
            if (player is null || !IsEligiblePlayer(player) || pawn is not { IsValid: true } || pawn.EntityHandle.Raw != charge.Pawn)
            {
                _footballCharges.Remove(slot);
                continue;
            }
            // A release the change listener missed still ends the charge.
            if ((player.Buttons & charge.Button) == 0) ReleaseFootballCharge(player, charge);
        }
        FootballHudOnTick();
    }

    // Kick path hook: the released kick for this slot, if any.
    private FootballKick? FootballKickFor(CCSPlayerController player, string mode) =>
        FootballKickRules.IsStyle(mode) && _footballKicks.TryGetValue(player.Slot, out var kick) && kick.Style == mode ? kick : null;

    // After an accepted football kick: curl, and forget the release.
    private void AfterFootballKick(CCSPlayerController player, CPhysicsPropMultiplayer ball, FootballKick? kick, double now)
    {
        if (kick is null) return;
        _footballKicks.Remove(player.Slot);
        if (kick.Curl == 0.0f) return;
        var state = State(ball);
        state.Curve = kick.Curl * FootballKickRules.CurlRate;
        state.CurveUntil = now + FootballKickRules.CurlSeconds;
    }
}

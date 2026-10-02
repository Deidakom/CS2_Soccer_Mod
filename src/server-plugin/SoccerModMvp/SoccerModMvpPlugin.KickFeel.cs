using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using V3 = System.Numerics.Vector3;

namespace SoccerModMvp;

// The CS:S knife's reach, as measured on the CS:S server (tools/css-vphysics-host, game.h
// KnifeSwing): a line from the eye along the view, 48 units for the slash and 32 for the stab;
// when the line misses, a box of 32 x 32 x 36 (the "head hull", +-16, +-16, +-18) is swept along
// the same line. The line lies inside the swept box, so the box alone decides. Not a cone: a
// tube of constant width that starts round the eye.
internal static class CssKnifeArea
{
    internal const float SlashRange = 48.0f;
    internal const float StabRange = 32.0f;
    private static readonly V3 HullHalf = new(16.0f, 16.0f, 18.0f);

    // Does the swept box touch a ball (sphere) with its centre at `ball`?
    internal static bool Reaches(V3 eye, V3 forward, V3 ball, float radius, bool stab)
    {
        var range = stab ? StabRange : SlashRange;
        var limit = radius * radius;
        // the distance from the ball to the box is convex along the sweep: a fine walk is exact enough
        for (var t = 0.0f; t <= range; t += 0.5f)
        {
            var c = eye + forward * t;
            var dx = MathF.Max(MathF.Abs(ball.X - c.X) - HullHalf.X, 0.0f);
            var dy = MathF.Max(MathF.Abs(ball.Y - c.Y) - HullHalf.Y, 0.0f);
            var dz = MathF.Max(MathF.Abs(ball.Z - c.Z) - HullHalf.Z, 0.0f);
            if (dx * dx + dy * dy + dz * dz <= limit) return true;
        }
        return false;
    }
}

// 2026-10-01 owner: two things about the moment of the kick.
//
// 1. "Can we not adapt the cone to the CS:S cone?" - dial kickCssHitArea: the click connects
//    where the CS:S knife would have hit the ball (CssKnifeArea) instead of reach + aim cone.
//    Only WHERE a click connects changes; direction, power and the ball's flight stay CS2's.
//
// 2. "Hitting does not feel instant, like a permanent delay even at ping 10" - measured from the
//    log: 45 of 52 of his kicks were applied in the very server tick of the click, so the server
//    is not late. What is left is the road of the click to the server and of the moving ball
//    back to his screen (ping, one server tick, the client's own snapshot delay), which CS:S
//    with cl_interp_ratio 0 has less of. The server cannot shorten that road, but it can do what
//    games do for projectiles: let the kicked ball start that much AHEAD on its path (dial
//    kickLeadMaxMs). For the kicker the ball is then where it would be had it left at his click;
//    everyone else sees a kick that happened a few hundredths earlier.
//
// 3. 2026-10-02, "the ball feels more responsive in the hall than on the big pitch": the journal
//    of that evening (exact timestamps, css_sm2kick_probe) showed something that holds on every
//    map: all 65 of 65 on-time kicks were applied ONE TICK AFTER the knife's own swing
//    (weapon_fire). OnPlayerButtonsChanged only reports a click when the player's NEXT command is
//    run, and at that point his position is still the one from before that command's movement -
//    a click made while running up to the ball at the edge of the reach misses there and connects
//    another tick later. Dial kickOnKnifeFire: a fresh click starts the kick at the knife's swing
//    itself, in the command of the click; the listener, a tick later, finds it done and skips.
//    Clicks the knife does not swing for (still in its refire delay) go the old way.
public sealed partial class SoccerModMvpPlugin
{
    private float _kickCssHitArea;              // 1 = CS:S knife area, 0 = reach + cone
    private float _kickLeadMaxMs;               // most the ball starts ahead; 0 = off (the owner plays with it off)
    // The server's own share of the delay: the click waits for the next tick, the ball's first
    // step is shown a tick later.
    private const float KickLeadServerMs = 16.0f;

    private float _kickOnKnifeFire = 1.0f;      // 1 = a fresh click kicks at the knife's swing, 0 = a tick later (before)
    private const double KickFireConfirmMs = 60.0;   // the listener reports the same click within a tick or two
    private const PlayerButtons KickAttackButtons = PlayerButtons.Attack | PlayerButtons.Attack2;

    // The attack buttons as the listener last reported them (plus a click the swing has started).
    private readonly Dictionary<int, PlayerButtons> _kickButtonsDown = new();
    private readonly Dictionary<int, (long Stamp, PlayerButtons Button)> _kickFireSwings = new();

    private bool _kickLeadDerivedPending;
    private Vector? _kickLeadDerivedVelocity;
    private bool _kickProbe;

    private bool KickCssArea => _kickCssHitArea >= 0.5f;

    private void KickFeelOnLoad()
    {
        AddCommand("css_sm2kick_probe", "Root: log click and knife-fire ticks for kick timing (on|off).", (player, command) =>
        {
            if (!RequirePermission(player, command, "root")) return;
            if (command.ArgCount >= 2) _kickProbe = command.GetArg(1).ToLowerInvariant() is "on" or "1";
            command.ReplyToCommand($"[SM] Kick probe: {(_kickProbe ? "on" : "off")}; hit area: {(KickCssArea ? "CS:S knife" : "reach + cone")}; kick starts ahead by ping + {KickLeadServerMs:0} ms, at most {_kickLeadMaxMs:0} ms; kick at the knife's swing: {(_kickOnKnifeFire >= 0.5f ? "on" : "off (one tick later)")}");
        });
        RegisterEventHandler<EventWeaponFire>((@event, _) =>
        {
            if (@event.Userid is { IsValid: true } shooter) KickOnKnifeFire(shooter, @event.Weapon);
            return HookResult.Continue;
        });
        RegisterListener<Listeners.OnClientDisconnect>(KickFireForget);
    }

    private static double KickProbeMs => System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    // The knife swings (weapon_fire): a fresh click starts its kick here, in the command of the click.
    private void KickOnKnifeFire(CCSPlayerController player, string weaponName)
    {
        var pawn = player.PlayerPawn.Value;
        var seen = player.Buttons;
        if (pawn is { IsValid: true } && pawn.MovementServices is { } movement)
        {
            // held | changed this tick | pressed and released within the tick
            var states = movement.Buttons.ButtonStates;
            if (states.Length >= 3) seen = (PlayerButtons)(states[0] | states[1] | states[2]);
        }
        var known = _kickButtonsDown.GetValueOrDefault(player.Slot);
        // a click the swing started but the listener never reported (shorter than a tick): not held
        if (_kickFireSwings.TryGetValue(player.Slot, out var open)
            && System.Diagnostics.Stopwatch.GetElapsedTime(open.Stamp).TotalMilliseconds > KickFireConfirmMs)
        {
            _kickFireSwings.Remove(player.Slot);
            known &= ~open.Button;
            _kickButtonsDown[player.Slot] = known;
        }
        var fresh = seen & KickAttackButtons & ~known;
        var button = (fresh & PlayerButtons.Attack) != 0 ? PlayerButtons.Attack
            : (fresh & PlayerButtons.Attack2) != 0 ? PlayerButtons.Attack2 : (PlayerButtons)0;
        var weapon = pawn?.WeaponServices?.ActiveWeapon.Value;
        var start = _kickOnKnifeFire >= 0.5f && button != 0
            && _pausedBallHandle == 0 && _matchPhase != MatchPhase.Paused && IsEligiblePlayer(player)
            && weapon is { IsValid: true } && weapon.DesignerName.Contains("knife", StringComparison.OrdinalIgnoreCase);
        if (_kickProbe)
            Logger.LogInformation("[SM2DIAG] kick_probe event=weapon_fire slot={Slot} tick={Tick} ms={Ms:F1} weapon={Weapon} seen={Seen} known={Known} start={Start} ballDistance={Distance} speed={Speed:F0}",
                player.Slot, Server.TickCount, KickProbeMs, weaponName, (ulong)(seen & KickAttackButtons), (ulong)known, start,
                FormatNullable(GetBallDistance(pawn)), pawn?.AbsVelocity is { } v ? MathF.Sqrt(v.X * v.X + v.Y * v.Y) : 0.0f);
        if (!start) return;

        _kickButtonsDown[player.Slot] = known | button;
        _kickFireSwings[player.Slot] = (System.Diagnostics.Stopwatch.GetTimestamp(), button);
        var crouching = IsPlayerCrouching(pawn);
        try
        {
            if (button == PlayerButtons.Attack)
                BeginKnifeSwing(player, pawn, weapon, crouching ? _leftClickCrouchPowerScale : _leftClickPowerScale, "primary");
            else
                BeginKnifeSwing(player, pawn, weapon, crouching ? _rightClickCrouchPowerScale : _rightClickPowerScale, "secondary");
        }
        catch (Exception e)
        {
            // never lose a click over this: the listener takes it, a tick later, as before
            _kickFireSwings.Remove(player.Slot);
            _kickButtonsDown[player.Slot] = known;
            Logger.LogWarning("[SM2DIAG] kick_at_swing_failed slot={Slot} error={Error}", player.Slot, e.Message);
        }
    }

    // OnPlayerButtonsChanged: keeps track of the attack buttons. True: this press already started
    // its kick when the knife swung, a tick ago - nothing left to do for it.
    private bool KickFireOnButtons(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        var down = _kickButtonsDown.GetValueOrDefault(player.Slot);
        _kickButtonsDown[player.Slot] = (down | (pressed & KickAttackButtons)) & ~(released & KickAttackButtons);
        if ((pressed & KickAttackButtons) == 0) return false;
        var done = _kickFireSwings.Remove(player.Slot, out var fired) && (pressed & fired.Button) != 0
            && System.Diagnostics.Stopwatch.GetElapsedTime(fired.Stamp).TotalMilliseconds <= KickFireConfirmMs;
        if (_kickProbe)
            Logger.LogInformation("[SM2DIAG] kick_probe event=press slot={Slot} tick={Tick} ms={Ms:F1} pressed={Pressed} doneAtSwing={Done} ballDistance={Distance}",
                player.Slot, Server.TickCount, KickProbeMs, (ulong)(pressed & KickAttackButtons), done, FormatNullable(GetBallDistance(player.PlayerPawn.Value)));
        return done;
    }

    private void KickFireForget(int slot)
    {
        _kickButtonsDown.Remove(slot);
        _kickFireSwings.Remove(slot);
    }

    // The geometry of a kick for one ball position (the same answer for the kick itself, the
    // lag-compensated positions).
    private bool KickAreaContains(V3 eye, V3 forward, float yaw, V3 ball, float cone, bool stab)
    {
        if (KickCssArea) return CssKnifeArea.Reaches(eye, forward, ball, BallCollisionRadius, stab);
        var toBall = ball - eye;
        var distance = toBall.Length();
        if (!float.IsFinite(distance) || distance <= 0.0001f || distance > _kickSurfaceReach + BallCollisionRadius) return false;
        var aimDot = V3.Dot(forward, toBall) / distance;
        return BallContactMath.KickSphereInCone(aimDot, distance, BallCollisionRadius, cone)
            && BallContactMath.HorizontalKickAim(toBall, yaw, BallCollisionRadius, cone);
    }

    // Where the kicked match ball starts: ahead on its path by the kicker's delay. Null: where
    // it is (off, a training ball, nothing to gain, or something is in the way right away).
    private Vector? KickLeadPosition(CCSPlayerController player, CPhysicsPropMultiplayer ball, Vector velocity, bool matchBall)
    {
        if (!matchBall || _kickLeadMaxMs <= 0.0f || ball.AbsOrigin is not { } from) return null;
        var seconds = MathF.Min(_kickLeadMaxMs, MathF.Max(0.0f, player.Ping) + KickLeadServerMs) / 1000.0f;
        var gravity = -800.0f * _gameplayGravityScale;
        var step = new V3(velocity.X * seconds, velocity.Y * seconds, velocity.Z * seconds + 0.5f * gravity * seconds * seconds);
        var length = step.Length();
        if (!float.IsFinite(length) || length < 2.0f) return null;

        // walls, posts, nets, the floor: stop short of them
        var r = BallCollisionRadius * 0.9f;
        var to = new Vector(from.X + step.X, from.Y + step.Y, from.Z + step.Z);
        var trace = Trace.TraceHullShape(from, to, new Vector(-r, -r, -r), new Vector(r, r, r), ball,
            new TraceOptions { InteractsWith = Masks.SolidBrushOnly });
        var fraction = trace.DidHit() ? MathF.Max(0.0f, trace.Fraction - 2.0f / length) : 1.0f;

        // other players: the ball does not start behind somebody who stands in its way
        var start = N(from);
        foreach (var other in Utilities.GetPlayers())
        {
            if (!other.IsValid || other.Slot == player.Slot || !IsEligiblePlayer(other)
                || other.PlayerPawn.Value is not { IsValid: true } pawn || pawn.AbsOrigin is not { } feet) continue;
            var mins = new V3(feet.X - 16.0f - r, feet.Y - 16.0f - r, feet.Z - r);
            var maxs = new V3(feet.X + 16.0f + r, feet.Y + 16.0f + r, feet.Z + 72.0f + r);
            if (KickLeadEntry(start, step, mins, maxs) is { } entry) fraction = MathF.Min(fraction, MathF.Max(0.0f, entry - 2.0f / length));
        }

        if (fraction * length < 2.0f) return null;
        _kickLeadDerivedPending = true;
        _kickLeadDerivedVelocity = new Vector(velocity.X, velocity.Y, velocity.Z);
        if (_kickProbe)
            Logger.LogInformation("[SM2DIAG] kick_probe event=lead slot={Slot} tick={Tick} ping={Ping} leadMs={Ms:F0} units={Units:F1} of={Full:F1}",
                player.Slot, Server.TickCount, player.Ping, seconds * 1000.0f, fraction * length, length);
        return new Vector(from.X + step.X * fraction, from.Y + step.Y * fraction, from.Z + step.Z * fraction);
    }

    // Where along start + step * t (t in 0..1) the path enters the box; null when it does not.
    private static float? KickLeadEntry(V3 start, V3 step, V3 mins, V3 maxs)
    {
        float enter = 0.0f, exit = 1.0f;
        for (var axis = 0; axis < 3; axis++)
        {
            var s = axis == 0 ? start.X : axis == 1 ? start.Y : start.Z;
            var d = axis == 0 ? step.X : axis == 1 ? step.Y : step.Z;
            var lo = axis == 0 ? mins.X : axis == 1 ? mins.Y : mins.Z;
            var hi = axis == 0 ? maxs.X : axis == 1 ? maxs.Y : maxs.Z;
            if (MathF.Abs(d) < 0.0001f)
            {
                if (s < lo || s > hi) return null;
                continue;
            }
            var a = (lo - s) / d;
            var b = (hi - s) / d;
            if (a > b) (a, b) = (b, a);
            enter = MathF.Max(enter, a);
            exit = MathF.Min(exit, b);
            if (enter > exit) return null;
        }
        return enter;
    }

    // UpdateDerivedMotion: the tick after a kick that started ahead, the ball's speed from its
    // positions is too high by the jump. The kick's own velocity is the true one.
    private void KickLeadFixDerived()
    {
        if (!_kickLeadDerivedPending) return;
        _kickLeadDerivedPending = false;
        if (_kickLeadDerivedVelocity is { } velocity) _derivedBallVelocity = velocity;
    }
}

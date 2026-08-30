using System;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Port of SoMoE-19's afkkicker.sp + the serverlock half of settings.sp
// (2026-08-30 SoMoE reconstruction round). Only armed while a cap has
// started (_capPhase != Idle) AND this feature is explicitly enabled -
// default OFF, because it rewrites sv_password automatically and this
// server already has a user-managed fixed password workflow (see the
// auto-memory notes on password changes this same session). Opt in with
// css_sm2lock on.
public sealed partial class SoccerModMvpPlugin
{
    private const float AfkCheckIntervalSeconds = 100.0f;
    private const float AfkCaptchaWindowSeconds = 20.0f;
    private const int AfkServerlockThreshold = 13;
    private const int AfkCaptchaJunkOptions = 5;

    private sealed class AfkSnapshot
    {
        public Vector Origin = new(0, 0, 0);
        public QAngle EyeAngles = new(0, 0, 0);
        public PlayerButtons Buttons;
        public double NextCheckAt;
    }

    private sealed class AfkCaptchaState
    {
        public int CorrectSlotOption;
        public double ExpiresAt;
    }

    private bool _afkLockEnabled;
    private bool _serverlockActive;
    private string? _serverlockSavedPassword;
    private readonly Dictionary<int, AfkSnapshot> _afkSnapshotBySlot = new();
    private readonly Dictionary<int, AfkCaptchaState> _afkCaptchaBySlot = new();

    private void AfkOnLoad()
    {
        AddCommand("css_sm2lock", "Admin: toggle AFK-kicker + serverlock (on/off) - only arms while a cap is running.", OnAfkLockCommand);
    }

    private void AfkOnPlayerDisconnect(int slot)
    {
        _afkSnapshotBySlot.Remove(slot);
        _afkCaptchaBySlot.Remove(slot);
    }

    // Called once when a cap starts (see CapOnLoad's start path) and once
    // when a match starts - mirrors SoMoE's "armed only during
    // pre-match/cap, reset to default at match start" lifecycle.
    private void AfkArmServerlock()
    {
        if (!_afkLockEnabled)
        {
            return;
        }

        _serverlockActive = true;
        _afkSnapshotBySlot.Clear();
        _afkCaptchaBySlot.Clear();
        Logger.LogInformation("[SM2DIAG] afk_lock_armed");
    }

    private void AfkDisarm(string reason)
    {
        _serverlockActive = false;
        _afkSnapshotBySlot.Clear();
        _afkCaptchaBySlot.Clear();
        if (_serverlockSavedPassword is { } saved)
        {
            ConVar.Find("sv_password")?.SetValue<string>(saved);
            _serverlockSavedPassword = null;
        }
        Logger.LogInformation("[SM2DIAG] afk_lock_disarmed reason={Reason}", reason);
    }

    // Called every tick from the main OnTick.
    private void AfkOnTick()
    {
        if (!_afkLockEnabled || !_serverlockActive)
        {
            return;
        }

        var now = Server.TickedTime;

        // Serverlock: randomize sv_password once player count crosses the
        // threshold; restore once it drops back below (SoMoE re-locks each
        // time the threshold is crossed upward again).
        var humanCount = Utilities.GetPlayers().Count(p => p.IsValid && !p.IsBot);
        var passwordConvar = ConVar.Find("sv_password");
        if (passwordConvar is not null)
        {
            if (humanCount >= AfkServerlockThreshold && _serverlockSavedPassword is null)
            {
                _serverlockSavedPassword = passwordConvar.GetPrimitiveValue<string>();
                passwordConvar.SetValue<string>(RandomPassword());
                Logger.LogInformation("[SM2DIAG] afk_serverlock_password_randomized players={Players}", humanCount);
            }
            else if (humanCount < AfkServerlockThreshold && _serverlockSavedPassword is { } saved)
            {
                passwordConvar.SetValue<string>(saved);
                _serverlockSavedPassword = null;
                Logger.LogInformation("[SM2DIAG] afk_serverlock_password_restored");
            }
        }

        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot || HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0UL, "admin"))
            {
                continue;
            }

            if (_afkCaptchaBySlot.TryGetValue(player.Slot, out var captcha))
            {
                if (now >= captcha.ExpiresAt)
                {
                    KickAfkPlayer(player, "failed to solve the captcha in time");
                }
                continue;
            }

            var pawn = player.PlayerPawn.Value;
            if (pawn is not { IsValid: true } || pawn.AbsOrigin is not { } origin)
            {
                continue;
            }

            if (!_afkSnapshotBySlot.TryGetValue(player.Slot, out var snapshot))
            {
                snapshot = new AfkSnapshot { Origin = origin, EyeAngles = pawn.EyeAngles, Buttons = player.Buttons, NextCheckAt = now + AfkCheckIntervalSeconds };
                _afkSnapshotBySlot[player.Slot] = snapshot;
                continue;
            }

            if (now < snapshot.NextCheckAt)
            {
                continue;
            }

            var unchanged = 0;
            if (Vector3Equal(snapshot.Origin, origin)) unchanged++;
            if (QAngleEqual(snapshot.EyeAngles, pawn.EyeAngles)) unchanged++;
            if (snapshot.Buttons == player.Buttons) unchanged++;

            if (unchanged >= 2)
            {
                OpenAfkCaptcha(player, now);
            }
            else
            {
                snapshot.Origin = origin;
                snapshot.EyeAngles = pawn.EyeAngles;
                snapshot.Buttons = player.Buttons;
                snapshot.NextCheckAt = now + AfkCheckIntervalSeconds;
            }
        }
    }

    private static bool Vector3Equal(Vector a, Vector b) =>
        MathF.Abs(a.X - b.X) < 1.0f && MathF.Abs(a.Y - b.Y) < 1.0f && MathF.Abs(a.Z - b.Z) < 1.0f;

    private static bool QAngleEqual(QAngle a, QAngle b) =>
        MathF.Abs(a.X - b.X) < 1.0f && MathF.Abs(a.Y - b.Y) < 1.0f;

    private void OpenAfkCaptcha(CCSPlayerController player, double now)
    {
        var menu = new NumberMenu { Title = "[AFK Kicker] Are you there?" };
        var correctOption = Random.Shared.Next(1, AfkCaptchaJunkOptions + 2); // +1 for the correct slot, 1-based
        for (var i = 1; i <= AfkCaptchaJunkOptions + 1; i++)
        {
            if (i == correctOption)
            {
                menu.Add("Yes - Don't kick me!", p => OnAfkCaptchaConfirmed(p));
            }
            else
            {
                var junkLabel = RandomJunkLabel();
                menu.Add(junkLabel, p => OnAfkCaptchaConfirmed(p));
            }
        }

        _afkCaptchaBySlot[player.Slot] = new AfkCaptchaState { CorrectSlotOption = correctOption, ExpiresAt = now + AfkCaptchaWindowSeconds };
        OpenNumberMenu(player, menu);
        Logger.LogInformation("[SM2DIAG] afk_captcha_shown slot={Slot}", player.Slot);
    }

    // Any option in the captcha menu resolves it - picking a junk entry is
    // as much "I'm here" as picking the real one, same as the original
    // (the captcha proves presence, not comprehension).
    private void OnAfkCaptchaConfirmed(CCSPlayerController player)
    {
        _afkCaptchaBySlot.Remove(player.Slot);
        _afkSnapshotBySlot.Remove(player.Slot);
        player.PrintToChat(" \x04[SoccerMod]\x01 AFK verification completed! You will not get kicked.");
        Logger.LogInformation("[SM2DIAG] afk_captcha_confirmed slot={Slot}", player.Slot);
    }

    private void KickAfkPlayer(CCSPlayerController player, string reason)
    {
        _afkCaptchaBySlot.Remove(player.Slot);
        _afkSnapshotBySlot.Remove(player.Slot);
        Logger.LogInformation("[SM2DIAG] afk_kick slot={Slot} name={Name} reason={Reason}", player.Slot, player.PlayerName, reason);
        if (player.UserId is { } userId)
        {
            Server.ExecuteCommand($"kickid {userId} \"You were kicked for being AFK or failed to solve the captcha\"");
        }
    }

    private static string RandomJunkLabel()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        Span<char> buffer = stackalloc char[10];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = chars[Random.Shared.Next(chars.Length)];
        }
        return new string(buffer);
    }

    private static string RandomPassword()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        Span<char> buffer = stackalloc char[20];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = chars[Random.Shared.Next(chars.Length)];
        }
        return new string(buffer);
    }

    private void OnAfkLockCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            var enable = command.GetArg(1).Equals("on", StringComparison.OrdinalIgnoreCase);
            _afkLockEnabled = enable;
            if (!enable)
            {
                AfkDisarm("command_off");
            }
            else if (_capPhase != CapPhase.Idle)
            {
                AfkArmServerlock();
            }
            SaveMatchSettings("afk_lock_command");
        }

        command.ReplyToCommand(
            $"[SM] AFK-kicker + serverlock: {(_afkLockEnabled ? "on" : "off")} "
            + "(arms while a cap is running; usage: css_sm2lock <on|off>)");
    }
}

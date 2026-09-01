using System;
using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Port of SoMoE-19's health.sp (2026-08-30 SoMoE reconstruction round).
// Ball-inflicted damage is already blocked elsewhere
// (OnPlayerTakeDamagePre in the main file); this covers the player-vs-
// player part, which nothing in the port touched until now - players could
// knife-kill each other on a server whose only weapon IS a knife.
//
// Two modes, same as the original:
//   - Godmode ON (default, matches SoMoE's default 1): pawn.TakesDamage =
//     false on spawn. Immune to everything, not just other players.
//   - Godmode OFF: pawn.Health is set to a configurable amount on spawn,
//     and refilled (+ VelocityModifier reset to cancel the knife-tag
//     slowdown) on every EventPlayerHurt.
// No "cap knife-fight" bypass: the original's cap.sp has a captain-
// selection knife duel that this port never built, so there is nothing to
// bypass.
public sealed partial class SoccerModMvpPlugin
{
    private const int DefaultHealthAmount = 250;
    private const int MinHealthAmount = 1;
    private const int MaxHealthAmount = 500;

    private bool _healthGodmodeEnabled = true;
    private int _healthAmount = DefaultHealthAmount;

    private void HealthOnLoad()
    {
        RegisterEventHandler<EventPlayerHurt>(OnPlayerHurtHealth);
        AddCommand("css_sm2health", "Admin: godmode on|off, or amount <1-500>.", OnHealthCommand);
    }

    private void HealthOnPlayerSpawn(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is not { IsValid: true })
        {
            return;
        }

        ApplyHealthOnSpawn(pawn);
    }

    private void ApplyHealthOnSpawn(CCSPlayerPawn pawn)
    {
        // NEVER write pawn.TakesDamage here. See the long note on
        // OnPlayerTakeDamagePre in the main file: setting it false in this
        // CS2 build froze player movement, made players unslayable, and
        // uncapped the knife swing rate. Godmode is enforced purely by
        // returning HookResult.Stop from that damage hook instead.
        //
        // Always restore it to true in case a pawn is carrying a stale
        // false from the build that had this bug.
        pawn.TakesDamage = true;

        if (!_healthGodmodeEnabled)
        {
            pawn.Health = _healthAmount;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        }
    }

    private HookResult OnPlayerHurtHealth(EventPlayerHurt @event, GameEventInfo info)
    {
        // The cap fight (Cap.cs, SoMoE cap.sp duel) is the one place damage
        // must stick: no refill while it runs.
        if (_healthGodmodeEnabled || _capFightStarted)
        {
            return HookResult.Continue;
        }

        var victim = @event.Userid;
        var pawn = victim?.PlayerPawn.Value;
        if (pawn is not { IsValid: true })
        {
            return HookResult.Continue;
        }

        pawn.Health = _healthAmount;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        if (@event.Attacker is { IsValid: true })
        {
            // Cancels the knife-tag movement slowdown from being hit, same
            // as the original: a refill-health server shouldn't also
            // punish speed.
            pawn.VelocityModifier = 1.0f;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
        }

        return HookResult.Continue;
    }

    private void OnHealthCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            var arg = command.GetArg(1);
            if (string.Equals(arg, "godmode", StringComparison.OrdinalIgnoreCase) && command.ArgCount >= 3)
            {
                _healthGodmodeEnabled = command.GetArg(2).Equals("on", StringComparison.OrdinalIgnoreCase);
                SaveMatchSettings("health_command");
            }
            else if (string.Equals(arg, "amount", StringComparison.OrdinalIgnoreCase) && command.ArgCount >= 3
                && int.TryParse(command.GetArg(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
            {
                _healthAmount = Math.Clamp(amount, MinHealthAmount, MaxHealthAmount);
                SaveMatchSettings("health_command");
            }
        }

        command.ReplyToCommand(
            $"[SM] health: godmode={(_healthGodmodeEnabled ? "on" : "off")} amount={_healthAmount} "
            + "(usage: css_sm2health godmode <on|off> | css_sm2health amount <1-500>)");
    }
}

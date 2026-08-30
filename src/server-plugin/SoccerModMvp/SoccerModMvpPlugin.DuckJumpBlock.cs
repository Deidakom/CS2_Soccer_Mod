using System;
using System.Globalization;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Port of SoMoE-19's duckjumpblock.sp mode 3 (the original's default)
// (2026-08-30 SoMoE reconstruction round). Original: strips IN_DUCK from
// the usercmd buttons for 0.4s after every jump (or until landing, if
// sooner). CSSharp cannot rewrite usercmd buttons (no writable buffer for
// that), so the equivalent here is suppressing the movement service's
// OWN duck state every tick during the window instead - same outcome
// (can't duck right after a jump), implemented one layer further down the
// pipeline. Small feel difference accepted.
public sealed partial class SoccerModMvpPlugin
{
    private const float DefaultBlockDjbSeconds = 0.4f;

    private bool _blockDjbEnabled = true;
    private float _blockDjbSeconds = DefaultBlockDjbSeconds;
    private readonly Dictionary<int, double> _djbWindowExpiresBySlot = new();

    private void DuckJumpBlockOnLoad()
    {
        RegisterEventHandler<EventPlayerJump>(OnPlayerJumpDjb);
        AddCommand("css_sm2djb", "Admin: duck-jump block on|off, or seconds <0-2>.", OnDjbCommand);
    }

    private HookResult OnPlayerJumpDjb(EventPlayerJump @event, GameEventInfo info)
    {
        if (!_blockDjbEnabled)
        {
            return HookResult.Continue;
        }

        var player = @event.Userid;
        if (player is { IsValid: true })
        {
            _djbWindowExpiresBySlot[player.Slot] = Server.TickedTime + _blockDjbSeconds;
        }

        return HookResult.Continue;
    }

    // Called every tick from the main OnTick.
    private void DuckJumpBlockOnTick()
    {
        if (!_blockDjbEnabled || _djbWindowExpiresBySlot.Count == 0)
        {
            return;
        }

        var now = Server.TickedTime;
        foreach (var slot in _djbWindowExpiresBySlot.Keys.ToArray())
        {
            var pawn = Utilities.GetPlayerFromSlot(slot)?.PlayerPawn.Value;
            var grounded = pawn is { IsValid: true } && pawn.GroundEntity.IsValid;
            if (now >= _djbWindowExpiresBySlot[slot] || grounded)
            {
                _djbWindowExpiresBySlot.Remove(slot);
                continue;
            }

            if (pawn is not { IsValid: true } || pawn.MovementServices is not { } movement)
            {
                continue;
            }

            var humanoidMovement = new CCSPlayer_MovementServices(movement.Handle);
            if (humanoidMovement.DesiresDuck || humanoidMovement.DuckAmount > 0.0f)
            {
                humanoidMovement.DesiresDuck = false;
                humanoidMovement.DuckAmount = 0.0f;
            }
        }
    }

    private void OnDjbCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            var arg = command.GetArg(1);
            if (string.Equals(arg, "on", StringComparison.OrdinalIgnoreCase))
            {
                _blockDjbEnabled = true;
                SaveMatchSettings("djb_command");
            }
            else if (string.Equals(arg, "off", StringComparison.OrdinalIgnoreCase))
            {
                _blockDjbEnabled = false;
                _djbWindowExpiresBySlot.Clear();
                SaveMatchSettings("djb_command");
            }
            else if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                && seconds is >= 0.0f and <= 2.0f)
            {
                _blockDjbSeconds = seconds;
                SaveMatchSettings("djb_command");
            }
        }

        command.ReplyToCommand(
            $"[SM] duck-jump block: {(_blockDjbEnabled ? "on" : "off")} window={_blockDjbSeconds:F2}s "
            + "(usage: css_sm2djb <on|off|seconds>)");
    }
}

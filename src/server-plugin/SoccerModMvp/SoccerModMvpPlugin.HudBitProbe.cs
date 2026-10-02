using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-26 test (map-test server): which HideHUD bit, if any, hides CS2's
// top team counter? Bit 13 (CS:GO mini scoreboard) does nothing; cs2-ui-kit
// only lists 0/2/3/6/7/8/12. css_sm2hud_probe [seconds] walks every other
// bit on all players, one at a time, and names the active bit in chat and
// the centre; css_sm2hud_probe stop ends it and restores the bits.
public sealed partial class SoccerModMvpPlugin
{
    private static readonly int[] HudProbeBits = { 1, 4, 5, 9, 10, 11, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31 };
    private CounterStrikeSharp.API.Modules.Timers.Timer? _hudProbeTimer;
    private int _hudProbeStep = -1;
    private uint _hudProbeBit;

    private void HudBitProbeOnLoad()
    {
        AddCommand("css_sm2hud_probe", "Admin: cycle untested HideHUD bits [seconds] | stop | <bit>.", OnHudProbeCommand);
    }

    private void OnHudProbeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin")) return;
        var arg = command.ArgCount > 1 ? command.GetArg(1).ToLowerInvariant() : "";
        StopHudProbe();
        if (arg == "stop") { command.ReplyToCommand("[SM] HUD probe stopped."); return; }
        if (arg.StartsWith("bit") && int.TryParse(arg[3..], out var single) && single is >= 0 and < 32)
        {
            SetHudProbeBit(1u << single, $"bit {single}");
            command.ReplyToCommand($"[SM] HUD probe: bit {single} on (css_sm2hud_probe stop to clear).");
            return;
        }
        var seconds = float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? Math.Clamp(s, 2f, 30f) : 6f;
        _hudProbeStep = -1;
        AdvanceHudProbe();
        _hudProbeTimer = AddTimer(seconds, AdvanceHudProbe, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        command.ReplyToCommand($"[SM] HUD probe: {HudProbeBits.Length} bits, {seconds:0.#}s each.");
    }

    private void AdvanceHudProbe()
    {
        _hudProbeStep++;
        if (_hudProbeStep >= HudProbeBits.Length)
        {
            StopHudProbe();
            AnnounceAll(" \u0004[HUD TEST]\u0001 done - all bits cleared.");
            return;
        }
        var bit = HudProbeBits[_hudProbeStep];
        SetHudProbeBit(1u << bit, $"bit {bit}");
    }

    private void SetHudProbeBit(uint mask, string label)
    {
        ApplyHudProbeMask(mask);
        _hudProbeBit = mask;
        Logger.LogInformation("[SM2DIAG] hud_probe {Label}", label);
        foreach (var p in Utilities.GetPlayers())
        {
            if (!p.IsValid || p.IsBot) continue;
            p.PrintToChat($" \u0004[HUD TEST]\u0001 now: \u0004{label}\u0001");
            p.PrintToCenter($"HUD TEST: {label}");
        }
    }

    private void ApplyHudProbeMask(uint mask)
    {
        foreach (var p in Utilities.GetPlayers())
        {
            if (!p.IsValid || p.IsBot || p.PlayerPawn.Value is not { IsValid: true } pawn) continue;
            pawn.HideHUD = (pawn.HideHUD & ~_hudProbeBit) | mask;
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_iHideHUD");
        }
    }

    private void StopHudProbe()
    {
        _hudProbeTimer?.Kill();
        _hudProbeTimer = null;
        if (_hudProbeBit != 0) ApplyHudProbeMask(0);
        _hudProbeBit = 0;
        _hudProbeStep = -1;
    }
}

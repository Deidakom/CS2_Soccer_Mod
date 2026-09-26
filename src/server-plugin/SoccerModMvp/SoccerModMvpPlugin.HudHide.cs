using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CS2UIKit;

namespace SoccerModMvp;

// 2026-09-25 owner: hide the combat HUD a soccer mod does not need - health
// (with the bar and team emblem), the weapon selection and the money. Health
// and weapons are the pawn's HideHUD bits (the flags cs2-ui-kit verified);
// money has no bit, so mp_maxmoney 0 hides it (nothing is bought here, B is
// off). Default on; css_sm2hud_hide on|off (persisted).
public sealed partial class SoccerModMvpPlugin
{
    private const uint SoccerHiddenHud = (uint)(HideHud.Weapons | HideHud.Health);

    private void HudHideOnLoad()
    {
        AddCommand("css_sm2hud_hide", "Admin: hide health, weapon selection and money (on|off).", OnHudHideCommand);
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            if (_menuParity.HideCombatHud) Server.NextFrame(() => Server.ExecuteCommand("mp_maxmoney 0"));
            Server.NextFrame(ApplyNativeRoundClock);
            return HookResult.Continue;
        });
        if (_menuParity.HideCombatHud) Server.ExecuteCommand("mp_maxmoney 0");
        ApplyNativeRoundClock();
    }

    // 2026-09-26: HideHUD bit 13 (CS:GO MINISCOREBOARD) turned out to do
    // nothing in CS2 (the owner still saw the top bar; cs2-ui-kit lists only
    // bits 0/2/3/6/7/8/12 as working). The round clock in that bar is hidden
    // with sv_hide_roundtime_until_seconds instead: rounds last 60 minutes,
    // so 99999 hides it for the whole round while the Panorama scoreboard
    // shows the match clock. Set by the plugin because Workshop maps block
    // some cvars in cfg files.
    private void ApplyNativeRoundClock() =>
        Server.ExecuteCommand($"sv_hide_roundtime_until_seconds {(_menuParity.ScoreHudPanorama ? 1 : 0)}");

    private void OnHudHideCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin")) return;
        var arg = command.ArgCount >= 2 ? command.GetArg(1).ToLowerInvariant() : "";
        if (arg is "on" or "off")
        {
            _menuParity.HideCombatHud = arg == "on";
            SaveJsonAtomic(MenuParityFile, _menuParity);
            Server.ExecuteCommand(_menuParity.HideCombatHud ? "mp_maxmoney 0" : "mp_maxmoney 10000");
            if (!_menuParity.HideCombatHud)
                foreach (var p in Utilities.GetPlayers())
                    if (p.IsValid && p.PlayerPawn.Value is { IsValid: true } pawn && (pawn.HideHUD & SoccerHiddenHud) != 0)
                    {
                        pawn.HideHUD &= ~SoccerHiddenHud;
                        Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_iHideHUD");
                    }
        }
        command.ReplyToCommand($"[SM] Combat HUD hidden: {(_menuParity.HideCombatHud ? "on" : "off")} (usage: css_sm2hud_hide <on|off>)");
    }

    // 2026-09-26 owner: the Panorama scoreboard (ScoreHud.cs) sits on top of
    // CS2's own top bar (round timer, team counters), which showed around it.
    // Bit 13 is CS:GO's HIDEHUD_MINISCOREBOARD; CS2 kept CS:GO's other bits
    // (0, 2, 3, 6, 7, 8, 12), so it is set while the scoreboard is in use and
    // cleared again when the text scoreboard is chosen.
    private const uint MiniScoreboardHud = 1u << 13;

    // Every 16 ticks: a new pawn (spawn, round restart) gets the bits again;
    // nothing is written while they are already right.
    private void HudHideOnTick()
    {
        if (Server.TickCount % 16 != 0) return;
        var wanted = (_menuParity.HideCombatHud ? SoccerHiddenHud : 0u) | (_menuParity.ScoreHudPanorama ? MiniScoreboardHud : 0u);
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot || player.PlayerPawn.Value is not { IsValid: true } pawn) continue;
            var current = pawn.HideHUD;
            var next = (current | wanted) & ~(MiniScoreboardHud & ~wanted);
            if (next == current) continue;
            pawn.HideHUD = next;
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_iHideHUD");
        }
    }
}

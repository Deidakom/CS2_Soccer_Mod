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
            return HookResult.Continue;
        });
        if (_menuParity.HideCombatHud) Server.ExecuteCommand("mp_maxmoney 0");
    }

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

    // Every 16 ticks: a new pawn (spawn, round restart) gets the bits again;
    // nothing is written while they are already set.
    private void HudHideOnTick()
    {
        if (!_menuParity.HideCombatHud || Server.TickCount % 16 != 0) return;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot || player.PlayerPawn.Value is not { IsValid: true } pawn) continue;
            if ((pawn.HideHUD & SoccerHiddenHud) == SoccerHiddenHud) continue;
            pawn.HideHUD |= SoccerHiddenHud;
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_iHideHUD");
        }
    }
}

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private void MenuAuditOnLoad()
    {
        AddCommand("css_sm2menu_audit", "Server only: build menu branches for a connected slot, then return to the main menu.", (caller, command) =>
        {
            if (!RequireServerConsole(caller, command)) return;
            if (command.ArgCount != 2 || !int.TryParse(command.GetArg(1), out var slot) || slot is < 0 or > 63
                || Utilities.GetPlayerFromSlot(slot) is not { IsValid: true, IsBot: false } player)
            { command.ReplyToCommand("Usage: css_sm2menu_audit <connected player slot>"); return; }
            var branches = new (string Name, Action<CCSPlayerController> Open)[]
            {
                ("Main", OpenMainMenu), ("Referee", OpenRefereeMenu), ("Referee score", OpenRefereeScoreMenu),
                ("Remove yellow", p => OpenRemoveCardMenu(p, false)), ("Remove red", p => OpenRemoveCardMenu(p, true)),
                ("Misc", OpenMiscSettingsMenu), ("Chat", OpenChatSettingsMenu), ("Rankings", OpenRankingMenu),
                ("Statistics", OpenStatisticsMenu), ("CAP", OpenCapMenu), ("Training props", OpenTrainingPropsMenu),
                ("Training layouts", OpenTrainingLayouts), ("Ball", OpenBallAdminMenu),
                ("Ball live", OpenBallLiveMenu), ("Ball effects", OpenBallEffectsMenu), ("Ball presets", OpenBallPresetsMenu)
            };
            foreach (var branch in branches)
            {
                CloseMenu(slot, "menu_audit");
                branch.Open(player);
                command.ReplyToCommand(_openMenus.TryGetValue(slot, out var menu)
                    ? $"[SM] {branch.Name}: {menu.Options.Count} entries, {menu.Title}"
                    : $"[SM] {branch.Name}: unavailable for this player/state");
            }
            OpenMainMenu(player);
        });
    }
}

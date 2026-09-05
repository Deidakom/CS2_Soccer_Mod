using CounterStrikeSharp.API.Core;
namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private void OpenRecentSoundMenu(CCSPlayerController player)
    {
        if (!SettingsAccess(player)) return;
        var menu = new NumberMenu { Title = "Recent sound events (whole server)", OnBack = OpenSoundSettingsMenu };
        menu.AddInfo("Blocking an event affects every source of that sound.");
        menu.Add("Refresh", OpenRecentSoundMenu);
        foreach (var pair in _recentSoundEvents.OrderByDescending(p => p.Value))
        {
            var hash = pair.Key;
            menu.Add($"{hash}: {(_blockedSoundHashes.Contains(hash) ? "BLOCKED - unblock" : "block")}", p =>
                RunBallMenuCommand(p, $"css_sm2sound_{(_blockedSoundHashes.Contains(hash) ? "unblock" : "block")} {hash}", OpenRecentSoundMenu));
        }
        OpenNumberMenu(player, menu);
    }
}

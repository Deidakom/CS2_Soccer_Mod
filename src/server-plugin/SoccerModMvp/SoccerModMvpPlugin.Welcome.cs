using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace SoccerModMvp;

// 2026-10-18 owner: "a first time message popup with an overview of the !menu commands and the
// personal toggles info". Shown once per Steam account, on the first spawn after the Workshop
// item is confirmed (players without it get the Workshop notice instead and see this on a later
// join). Two short pages in the menu window, the Workshop notice's format.
public partial class SoccerModMvpPlugin
{
    private const string WelcomeSeenFile = "soccermod_welcome_seen.json";
    private HashSet<ulong>? _welcomeSeen;

    private void MaybeShowWelcome(CCSPlayerController player)
    {
        _welcomeSeen ??= (LoadJsonOrNull<List<ulong>>(WelcomeSeenFile) ?? new()).ToHashSet();
        var id = SteamIdOf(player);
        if (player.IsBot || id == 0 || _welcomeSeen.Contains(id) || !WorkshopVerified(player)) return;
        var slot = player.Slot;
        AddTimer(WorkshopNoticeDelaySeconds + 0.5f, () =>
        {
            var p = Utilities.GetPlayerFromSlot(slot);
            if (p is not { IsValid: true, IsBot: false } || SteamIdOf(p) != id || !_welcomeSeen.Add(id)) return;
            SaveJsonAtomic(WelcomeSeenFile, _welcomeSeen.ToList());
            OpenWelcomePage(p);
        });
    }

    private void OpenWelcomePage(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "WELCOME TO SOCCERMOD", Key = "welcome-1", ForceMouse = true };
        menu.Add("Next: your stadium effects", OpenWelcomeEffectsPage);
        menu.Add("Close", p => CloseMenu(p.Slot, "welcome_close"));
        menu.AddInfo("!menu (or F10) opens the menu:");
        menu.AddInfo("Match, Cap, Training, Ranks, Settings.");
        menu.AddInfo("Sprint: hold E. Keeper skin: !gk.");
        menu.AddInfo("All commands: !menu - Help - Commands.");
        OpenNumberMenu(player, menu);
    }

    private void OpenWelcomeEffectsPage(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "YOUR STADIUM EFFECTS", Key = "welcome-2", ForceMouse = true, OnBack = OpenWelcomePage };
        menu.Add("Switch effects on (Stadium)", OpenStadiumSettingsMenu);
        menu.Add("Stadium sounds", OpenPersonalSoundsMenu);
        menu.Add("Close", p => CloseMenu(p.Slot, "welcome_close"));
        menu.AddInfo("They are OFF until you switch them on:");
        menu.AddInfo("crowd, bench & coach, 3D grass,");
        menu.AddInfo("pitch design, pitch border, sounds.");
        OpenNumberMenu(player, menu);
    }
}

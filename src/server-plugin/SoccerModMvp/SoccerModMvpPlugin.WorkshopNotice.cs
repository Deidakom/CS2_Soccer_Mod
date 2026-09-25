using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-25 owner: every player gets a window on join (not on map reloads)
// saying most features need the Workshop item, until it is known they have
// it. The server cannot see Workshop subscriptions (Steam only shows them to
// the signed-in owner), but a mouse click on any of our Panorama windows can
// only reach the server when that window - i.e. the Workshop item - is loaded
// in the player's game (CS2's OnCustomHudClicked). The first such click marks
// the SteamID as verified and the notice never shows again. Players without
// the item cannot see the window at all, so unverified players also get the
// warning in chat.
public sealed partial class SoccerModMvpPlugin
{
    private const string WorkshopVerifiedFileName = "soccermod_workshop_verified.json";
    private const float WorkshopNoticeDelaySeconds = 2.0f;

    private sealed class WorkshopVerifiedStore
    {
        public List<ulong> SteamIds { get; set; } = new();
    }

    private WorkshopVerifiedStore _workshopVerified = new();

    private void WorkshopNoticeOnLoad()
    {
        _workshopVerified = LoadJsonOrNull<WorkshopVerifiedStore>(WorkshopVerifiedFileName) ?? new WorkshopVerifiedStore();
    }

    private bool WorkshopVerified(CCSPlayerController player) => _workshopVerified.SteamIds.Contains(SteamIdOf(player));

    // Called from OnClickMenuClicked: the click itself is the proof.
    private void MarkWorkshopVerified(CCSPlayerController player)
    {
        var id = SteamIdOf(player);
        if (id == 0 || _workshopVerified.SteamIds.Contains(id)) return;
        _workshopVerified.SteamIds.Add(id);
        SaveJsonAtomic(WorkshopVerifiedFileName, _workshopVerified);
        Logger.LogInformation("[SM2DIAG] workshop_verified steamid={SteamId} name={Name}", id, player.PlayerName);
    }

    // Called once per connection from MenuMaybeSendBindReminder.
    private void MaybeShowWorkshopNotice(CCSPlayerController player)
    {
        if (player.IsBot || WorkshopVerified(player)) return;
        player.PrintToChat(" \x07IMPORTANT: without the SoccerMod Workshop item, many features are missing (menu, jerseys, sounds, sprint bar, minimap). Type  \x0B!links\x07, open the link from your console, click Subscribe and restart CS2.");
        var slot = player.Slot;
        AddTimer(WorkshopNoticeDelaySeconds, () =>
        {
            var p = Utilities.GetPlayerFromSlot(slot);
            if (p is { IsValid: true, IsBot: false } && !WorkshopVerified(p)) OpenWorkshopNotice(p);
        });
    }

    private void OpenWorkshopNotice(CCSPlayerController player)
    {
        // Buttons first so they are 1 and 2; the text rows follow (info rows
        // keep their slot). Short lines: the window cuts off long ones.
        var menu = new NumberMenu { Title = "IMPORTANT", Key = "workshop-notice", ForceMouse = true };
        menu.Add("OK", p => CloseMenu(p.Slot, "workshop_notice_ok"));
        menu.Add("Show the Workshop link", p =>
        {
            PrintLinks(p);
            CloseMenu(p.Slot, "workshop_notice_link");
        });
        menu.AddInfo("Without the SoccerMod Workshop item,");
        menu.AddInfo("many features are missing:");
        menu.AddInfo("menu, jerseys, sounds, sprint bar, minimap.");
        menu.AddInfo("Subscribe to it, then restart CS2.");
        OpenNumberMenu(player, menu);
    }
}

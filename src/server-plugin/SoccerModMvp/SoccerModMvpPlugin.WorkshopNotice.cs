using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-25 owner: a window on every join (not on map reloads) telling
// players that most features need the Workshop item. The server cannot see
// Workshop subscriptions, so the player confirms "I have subscribed" once;
// that SteamID never sees the window again. OK only closes it until the
// next join. Shown once per connection, from the first spawn (the same
// once-per-connection bookkeeping as the join hints in Menu.cs).
public sealed partial class SoccerModMvpPlugin
{
    private const string WorkshopConfirmedFileName = "soccermod_workshop_confirmed.json";
    private const float WorkshopNoticeDelaySeconds = 2.0f;

    private sealed class WorkshopConfirmedStore
    {
        public List<ulong> SteamIds { get; set; } = new();
    }

    private WorkshopConfirmedStore _workshopConfirmed = new();

    private void WorkshopNoticeOnLoad()
    {
        _workshopConfirmed = LoadJsonOrNull<WorkshopConfirmedStore>(WorkshopConfirmedFileName) ?? new WorkshopConfirmedStore();
    }

    private bool WorkshopConfirmed(CCSPlayerController player) => _workshopConfirmed.SteamIds.Contains(SteamIdOf(player));

    // Called once per connection from MenuMaybeSendBindReminder.
    private void MaybeShowWorkshopNotice(CCSPlayerController player)
    {
        if (player.IsBot || WorkshopConfirmed(player)) return;
        var slot = player.Slot;
        AddTimer(WorkshopNoticeDelaySeconds, () =>
        {
            var p = Utilities.GetPlayerFromSlot(slot);
            if (p is { IsValid: true, IsBot: false } && !WorkshopConfirmed(p)) OpenWorkshopNotice(p);
        });
    }

    private void OpenWorkshopNotice(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "IMPORTANT - SoccerMod Workshop item", Key = "workshop-notice" };
        menu.AddInfo("Without the SoccerMod Workshop item, many features are missing:");
        menu.AddInfo("menu, jerseys, sounds, sprint bar, minimap.");
        menu.AddInfo("Subscribe to it, then restart CS2.");
        menu.Add("OK", p => CloseMenu(p.Slot, "workshop_notice_ok"));
        menu.Add("Show the Workshop link (console)", p =>
        {
            PrintLinks(p);
            OpenWorkshopNotice(p);
        });
        menu.Add("I have subscribed - don't show again", p =>
        {
            var id = SteamIdOf(p);
            if (id != 0 && !_workshopConfirmed.SteamIds.Contains(id))
            {
                _workshopConfirmed.SteamIds.Add(id);
                SaveJsonAtomic(WorkshopConfirmedFileName, _workshopConfirmed);
            }
            CloseMenu(p.Slot, "workshop_notice_confirmed");
            p.PrintToChat(" \x04[SoccerMod]\x01 Thanks! Restart CS2 once so the Workshop files load.");
            Logger.LogInformation("[SM2DIAG] workshop_notice_confirmed steamid={SteamId} name={Name}", id, p.PlayerName);
        });
        OpenNumberMenu(player, menu);
    }
}

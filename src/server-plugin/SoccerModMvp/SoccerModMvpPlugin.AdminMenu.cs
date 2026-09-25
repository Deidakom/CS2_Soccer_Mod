using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace SoccerModMvp;

// 2026-09-25 owner: !admin opens a SourceMod-style admin menu (sm_admin):
// pick a player, then kick/slay/ban/mute/gag/silence. Every entry runs the
// permission-checked chat command (css_kick, css_ban, css_mute, ...), so the
// menu never grants more than the commands do. The SoccerMod admin menu
// (ball, referee, training, settings) stays one entry below.
public sealed partial class SoccerModMvpPlugin
{
    private static readonly (string Label, string Minutes)[] AdminDurations =
    {
        ("10 minutes", "10"), ("30 minutes", "30"), ("1 hour", "60"), ("1 day", "1440"),
    };

    private void OpenAdminRootMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Admin Menu", Key = "admin-root" };
        menu.Add("Player commands", OpenAdminPlayersMenu);
        menu.Add("Server commands", OpenAdminServerMenu);
        menu.Add("Active punishments", OpenAdminPunishmentsMenu);
        menu.Add("SoccerMod admin", OpenAdminMenu);
        OpenNumberMenu(player, menu);
    }

    private string CommsTags(CCSPlayerController target) =>
        (IsMuted(target) ? " [muted]" : string.Empty) + (IsGagged(target) ? " [gagged]" : string.Empty);

    private void OpenAdminPlayersMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Admin - Player commands", Key = "admin-players", OnBack = OpenAdminRootMenu };
        foreach (var target in Utilities.GetPlayers().Where(t => t.IsValid && t.UserId is not null && t.Slot != player.Slot))
        {
            var userId = target.UserId!.Value;
            var targetName = target.PlayerName;
            menu.Add(targetName + CommsTags(target), p => OpenAdminPlayerActionsMenu(p, userId, targetName));
        }
        if (menu.Options.Count == 0) menu.AddInfo("No other players online.");
        OpenNumberMenu(player, menu);
    }

    private void OpenAdminPlayerActionsMenu(CCSPlayerController player, int userId, string targetName)
    {
        var target = Utilities.GetPlayerFromUserid(userId);
        if (target is not { IsValid: true })
        {
            player.PrintToChat($" \x04[SM]\x01 {targetName} is no longer on the server.");
            OpenAdminPlayersMenu(player);
            return;
        }
        var menu = new NumberMenu { Title = $"Admin - {targetName}{CommsTags(target)}", Key = "admin-player", OnBack = OpenAdminPlayersMenu };
        void Back(CCSPlayerController p) => OpenAdminPlayerActionsMenu(p, userId, targetName);
        menu.Add("Kick", p => RunAdminCommand(p, $"css_kick #{userId}", OpenAdminPlayersMenu));
        menu.Add("Slay", p => RunAdminCommand(p, $"css_slay #{userId}", Back));
        menu.Add("Move to spectator", p => RunAdminCommand(p, $"css_spec #{userId}", Back));
        menu.Add("Ban...", p => OpenAdminDurationMenu(p, "Ban", $"css_ban #{userId}", " banned", userId, targetName, false));
        var muted = IsMuted(target);
        var gagged = IsGagged(target);
        void Comms(string label, string liftLabel, string command, bool active)
        {
            if (active) menu.Add(liftLabel, p => RunAdminCommand(p, $"css_un{command} #{userId}", Back));
            else menu.Add(label, p => OpenAdminDurationMenu(p, char.ToUpperInvariant(liftLabel[2]) + liftLabel[3..], $"css_{command} #{userId}", string.Empty, userId, targetName, true));
        }
        Comms("Mute (voice)...", "Unmute", "mute", muted);
        Comms("Gag (chat)...", "Ungag", "gag", gagged);
        Comms("Silence (voice + chat)...", "Unsilence", "silence", muted && gagged);
        OpenNumberMenu(player, menu);
    }

    private void OpenAdminDurationMenu(CCSPlayerController player, string action, string command, string reason,
        int userId, string targetName, bool offerMapChange)
    {
        void Back(CCSPlayerController p) => OpenAdminPlayerActionsMenu(p, userId, targetName);
        var menu = new NumberMenu { Title = $"{action} - {targetName}", Key = "admin-duration", OnBack = Back };
        // A ban removes the player, so go back to the player list.
        Action<CCSPlayerController> after = action == "Ban" ? OpenAdminPlayersMenu : Back;
        foreach (var (label, minutes) in AdminDurations)
            menu.Add(label, p => RunAdminCommand(p, $"{command} {minutes}{reason}", after));
        if (offerMapChange) menu.Add("Until map change", p => RunAdminCommand(p, $"{command} map", after));
        if (HasFlag(SteamIdOf(player), "root")) menu.Add("Permanent", p => RunAdminCommand(p, $"{command} 0{reason}", after));
        OpenNumberMenu(player, menu);
    }

    private void OpenAdminServerMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Admin - Server commands", Key = "admin-server", OnBack = OpenAdminRootMenu };
        menu.Add("Restart round", p => RunAdminCommand(p, "css_rr", OpenAdminServerMenu));
        menu.Add("Match menu", OpenMatchMenu);
        menu.Add("Reload map", p => p.ExecuteClientCommandFromServer("css_maprr"));
        OpenNumberMenu(player, menu);
    }

    private void OpenAdminPunishmentsMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Admin - Active punishments", Key = "admin-punishments", OnBack = OpenAdminRootMenu };
        var now = DateTime.UtcNow;
        foreach (var entry in _commsStore.Entries.Where(e => CommsRules.Active(now, e.ExpiresAtUtc)).Take(20).ToList())
        {
            var steamId = entry.SteamId64;
            var lift = entry.Kind == CommsRules.Mute ? "css_unmute" : "css_ungag";
            menu.Add($"Lift {entry.Kind}: {entry.Name} [{CommsRules.Remaining(now, entry.ExpiresAtUtc, entry.UntilMapChange)}]",
                p => RunAdminCommand(p, $"{lift} {steamId}", OpenAdminPunishmentsMenu));
        }
        if (menu.Options.Count == 0) menu.AddInfo("No active mutes or gags.");
        if (HasFlag(SteamIdOf(player), "root")) menu.Add("Bans (unban)...", OpenUnbanMenu);
        OpenNumberMenu(player, menu);
    }

    // Runs the chat command as the admin (so its permission check applies),
    // then reopens a menu once the command has taken effect.
    private void RunAdminCommand(CCSPlayerController player, string command, Action<CCSPlayerController> reopen)
    {
        player.ExecuteClientCommandFromServer(command);
        Server.NextFrame(() =>
        {
            if (player.IsValid) reopen(player);
        });
    }
}

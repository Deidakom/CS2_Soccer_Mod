using CounterStrikeSharp.API.Core;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private static readonly string[] EditableAdminFlags = { "soccermod", "admin", "match", "ball", "root" };
    private bool RootMenuAccess(CCSPlayerController player)
    {
        if (HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0, "root")) return true;
        player.PrintToChat(FormatSoccerModMessage("Root access is required.")); return false;
    }
    private void OpenAdminEditor(CCSPlayerController player)
    {
        if (!RootMenuAccess(player)) return;
        var menu = new NumberMenu { Title = "Admins - Online / Offline", OnBack = OpenServerSettingsMenu };
        menu.Add("Add SteamID64", p => BeginChatTextInput(p, "Enter the player's SteamID64.", (actor, value) =>
        {
            if (!RootMenuAccess(actor)) return;
            if (!ulong.TryParse(value, out var id) || id <= 76561197960265728UL || id > 76561202255233023UL)
            { actor.PrintToChat(FormatSoccerModMessage("Enter a valid individual SteamID64.")); OpenAdminEditor(actor); return; }
            OpenAdminFlagEditor(actor, id);
        }, OpenAdminEditor));
        foreach (var entry in _adminStore.Admins.OrderBy(a => a.Name))
            menu.Add($"{entry.Name} ({entry.SteamId64})", p => OpenAdminFlagEditor(p, entry.SteamId64));
        OpenNumberMenu(player, menu);
    }
    private void OpenAdminFlagEditor(CCSPlayerController player, ulong id)
    {
        if (!RootMenuAccess(player)) return;
        var entry = _adminStore.Admins.FirstOrDefault(a => a.SteamId64 == id);
        var menu = new NumberMenu { Title = $"Admin flags: {id}", OnBack = OpenAdminEditor };
        menu.AddInfo("SoccerMod = admin + match; ball/root stay separate.");
        foreach (var flag in EditableAdminFlags)
        {
            var enabled = entry?.Flags.Contains(flag, StringComparer.OrdinalIgnoreCase) == true;
            menu.Add($"{flag}: {OnOff(enabled)}", p => ConfirmAdminFlag(p, id, flag, !enabled));
        }
        OpenNumberMenu(player, menu);
    }
    private void ConfirmAdminFlag(CCSPlayerController player, ulong id, string flag, bool grant)
    {
        if (!RootMenuAccess(player)) return;
        var menu = new NumberMenu { Title = $"{(grant ? "Grant" : "Revoke")} {flag} for {id}?", OnBack = p => OpenAdminFlagEditor(p, id) };
        menu.Add("Cancel", p => OpenAdminFlagEditor(p, id));
        menu.Add("Confirm", actor =>
        {
            if (!RootMenuAccess(actor)) return;
            if (!grant && flag == "root" && id == (actor.AuthorizedSteamID?.SteamId64 ?? 0))
            { actor.PrintToChat(FormatSoccerModMessage("Use server console to revoke your own root access.")); return; }
            var before = System.Text.Json.JsonSerializer.Serialize(_adminStore);
            var target = _adminStore.Admins.FirstOrDefault(a => a.SteamId64 == id);
            if (target is null && grant)
            {
                target = new AdminEntry { SteamId64 = id, Name = $"steamid:{id}" }; _adminStore.Admins.Add(target);
            }
            if (target is not null)
            {
                target.Flags.RemoveAll(f => f.Equals(flag, StringComparison.OrdinalIgnoreCase));
                if (grant) target.Flags.Add(flag);
                if (target.Flags.Count == 0) _adminStore.Admins.Remove(target);
            }
            if (!SaveJsonAtomic(AdminsFileName, _adminStore))
            { _adminStore = System.Text.Json.JsonSerializer.Deserialize<AdminStore>(before)!; actor.PrintToChat(FormatSoccerModMessage("Save failed; access was not changed.")); }
            OpenAdminFlagEditor(actor, id);
        });
        OpenNumberMenu(player, menu);
    }
}

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace SoccerModMvp;

// 2026-09-25 owner: in the in-game cap the captain picks a player AND his
// position (GK / DEF / MID / WING). The player carries it as a clan tag, e.g.
// "[GK]", until the match ends or is stopped. It is kept per SteamID, so a
// player who rejoins while the cap or match is still going gets it back on
// spawn. Same tag style as the KICKOFF website cap (WebCap.cs).
public sealed partial class SoccerModMvpPlugin
{
    internal static readonly string[] CapRoles = { "GK", "DEF", "MID", "WING" };
    private readonly Dictionary<ulong, string> _capRoles = new();
    private readonly Dictionary<ulong, string> _capRoleOriginalClan = new();

    // Pick menu row -> "Pick <name> as:" GK / DEF / MID / WING.
    private void OpenCapRoleMenu(CCSPlayerController picker, int targetSlot, ulong targetId, string targetName)
    {
        var menu = new NumberMenu { Title = $"Pick {targetName} as", Key = "cap-role", OnBack = OpenCapPickMenu };
        foreach (var role in CapRoles)
        {
            var chosen = role;
            menu.Add(chosen, p =>
            {
                if (targetId != 0 && Utilities.GetPlayerFromSlot(targetSlot)?.AuthorizedSteamID?.SteamId64 == targetId)
                    CapPick(p, targetSlot, chosen);
            });
        }
        OpenNumberMenu(picker, menu);
    }

    private void AssignCapRole(CCSPlayerController player, string role)
    {
        var id = SteamIdOf(player);
        if (id == 0) return;
        if (!_capRoleOriginalClan.ContainsKey(id))
            _capRoleOriginalClan[id] = IsWebsiteCapPositionTag(player.Clan) ? string.Empty : player.Clan ?? string.Empty;
        _capRoles[id] = role;
        SetWebsiteCapClanTag(player, $"[{role}]");
    }

    // From OnPlayerSpawn: a rejoined (or respawned) player gets his tag back.
    private void CapRolesOnPlayerSpawn(CCSPlayerController player)
    {
        if (_capRoles.TryGetValue(SteamIdOf(player), out var role)) SetWebsiteCapClanTag(player, $"[{role}]");
    }

    // With every reset of the cap assignments (match end/stop, new or
    // cancelled cap, map start).
    private void ClearCapRoles()
    {
        if (_capRoles.Count == 0) return;
        foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot))
        {
            var id = SteamIdOf(player);
            if (!_capRoles.ContainsKey(id)) continue;
            SetWebsiteCapClanTag(player, _capRoleOriginalClan.GetValueOrDefault(id, string.Empty));
        }
        _capRoles.Clear();
        _capRoleOriginalClan.Clear();
    }
}

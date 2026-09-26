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

    // 2026-09-26 owner: the TAB board shows the position a player was picked
    // for in the cap (above), else the website-cap role, else his permanent
    // position - for players who only play one: !permpos GK|DEF|MID|WING|off
    // or !menu - Settings - Match. A cap pick overrides it until that cap's
    // match is over. The preferred-position toggles are not shown there.
    private string TabBoardPosition(CCSPlayerController player)
    {
        var id = SteamIdOf(player);
        if (id != 0 && _capRoles.TryGetValue(id, out var capRole)) return capRole;
        if (_playerPositions.TryGetValue(player.Slot, out var websiteRole)) return websiteRole;
        return PermanentPosition(player) ?? string.Empty;
    }

    private string? PermanentPosition(CCSPlayerController player) =>
        _menuParity.PermPos.TryGetValue(SteamIdOf(player), out var position) ? position : null;

    private void SetPermanentPosition(CCSPlayerController player, string? position)
    {
        var id = SteamIdOf(player);
        if (id == 0) return;
        if (position is null) _menuParity.PermPos.Remove(id);
        else _menuParity.PermPos[id] = position;
        SaveJsonAtomic(MenuParityFile, _menuParity);
        player.PrintToChat(position is null
            ? " \u0004[SM]\u0001 Permanent position cleared."
            : $" \u0004[SM]\u0001 Permanent position: \u0004{position}\u0001 (a cap pick overrides it until that match ends).");
    }

    private void OnPermPosCommand(CCSPlayerController? player, CounterStrikeSharp.API.Modules.Commands.CommandInfo command)
    {
        if (player is not { IsValid: true }) return;
        var arg = command.ArgCount >= 2 ? command.GetArg(1).ToUpperInvariant() : string.Empty;
        if (CapRoles.Contains(arg)) SetPermanentPosition(player, arg);
        else if (arg is "OFF" or "NONE" or "CLEAR") SetPermanentPosition(player, null);
        else player.PrintToChat($" \u0004[SM]\u0001 Your permanent position: {PermanentPosition(player) ?? "none"}. Usage: !permpos GK, DEF, MID, WING or off");
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

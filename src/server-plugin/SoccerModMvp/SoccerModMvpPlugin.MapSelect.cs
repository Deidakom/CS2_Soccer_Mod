using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-26 owner: "when I reload the map I want a map selection, like in
// the CS:S mod". !menu - Reload Map opens a list for admins: reload the
// current map, or change to another one from soccermod_maps.json (created
// with the two SoccerMod stadiums; add a Workshop id or a plain map name to
// offer more). Players without the admin flag keep the plain reload. Like
// the reload, a change is refused while a match or a cap is running.
public sealed partial class SoccerModMvpPlugin
{
    private const string MapListFileName = "soccermod_maps.json";

    private sealed class MapListEntry
    {
        public string Name { get; set; } = string.Empty;
        // Either a Workshop item (loaded with host_workshop_map) or a map on disk (changelevel).
        public string? Workshop { get; set; }
        public string? Map { get; set; }
    }

    private List<MapListEntry> _mapList = new();

    private void MapSelectOnLoad()
    {
        _mapList = LoadJsonOrNull<List<MapListEntry>>(MapListFileName) ?? new List<MapListEntry>
        {
            new() { Name = "Soccer Stadium (CSSL v8)", Workshop = LegacyStadiumWorkshopId },
            new() { Name = "SoccerMod Stadium (test)", Workshop = "3807839299" },
        };
        _mapList.RemoveAll(m => string.IsNullOrWhiteSpace(m.Workshop) && string.IsNullOrWhiteSpace(m.Map));
        if (!File.Exists(ConfigPath(MapListFileName))) SaveJsonAtomic(MapListFileName, _mapList);
    }

    // Main menu / Admin: "Reload Map".
    private void OpenReloadMapEntry(CCSPlayerController player)
    {
        if (HasFlag(SteamIdOf(player), "admin")) OpenMapSelectMenu(player);
        else player.ExecuteClientCommandFromServer("css_maprr");
    }

    private void OpenMapSelectMenu(CCSPlayerController player)
    {
        var current = Server.MapName;
        var currentWorkshop = MapWorkshopId(current);
        var menu = new NumberMenu { Title = "Reload / change map", Key = "map-select", OnBack = OpenMainMenu };
        menu.Add($"Reload current map ({current})", p => p.ExecuteClientCommandFromServer("css_maprr"));
        foreach (var entry in _mapList)
        {
            var target = entry;
            var isCurrent = target.Workshop is { Length: > 0 } id
                ? id == currentWorkshop
                : string.Equals(target.Map, current, StringComparison.OrdinalIgnoreCase);
            menu.Add($"{(isCurrent ? "★ " : "")}{target.Name}", p => ChangeMap(p, target));
        }
        OpenNumberMenu(player, menu);
    }

    private void ChangeMap(CCSPlayerController player, MapListEntry entry)
    {
        if (!HasFlag(SteamIdOf(player), "admin")) return;
        if (MatchRunning || CapRunning)
        {
            player.PrintToChat(" \u0004[SM]\u0001 Map change is not allowed while a match or cap is running.");
            return;
        }
        AnnounceAll($" \u0004[SM]\u0001 {player.PlayerName} changes the map to \u0004{entry.Name}\u0001...");
        Logger.LogInformation("[SM2DIAG] map_change by={By} name={Name} workshop={Workshop} map={Map}",
            player.PlayerName, entry.Name, entry.Workshop ?? "-", entry.Map ?? "-");
        // Workshop maps need host_workshop_map (changelevel loses the addon context).
        Server.ExecuteCommand(entry.Workshop is { Length: > 0 } id ? $"host_workshop_map {id}" : $"changelevel {entry.Map}");
    }
}

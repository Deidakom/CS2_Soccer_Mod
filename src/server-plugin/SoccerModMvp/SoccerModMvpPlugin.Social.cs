using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Tier 2 social/QoL batch from the CS:S-parity plan: !pos (position
// preferences, shown in the cap pick menu), !lc/!late (connect-order list -
// "quick reconnect keeps your slot" in spirit, though the reconnect-grace
// logic itself isn't built), !help/!commands (a plain public command list).
//
// 2026-09-01: positions are now SoMoE cap.sp's OpenCapPositionMenu 1:1 -
// seven independent Yes/No toggles (Goalkeeper, Left back, Right back,
// Midfielder, Left wing, Right wing, Spec only), persisted per SteamID64
// (SoMoE: cfg/sm_soccermod/soccer_mod_cap_positions.txt), rendered as
// "[GK][LB]..." / "[SPEC ONLY]" in the pick menu. The KICKOFF website cap
// still writes a single role per slot (WebCap.cs) - that override wins
// while it is active.
public sealed partial class SoccerModMvpPlugin
{
    private const string CapPositionsFileName = "soccermod_cap_positions.json";

    private sealed class CapPositionEntry
    {
        public ulong SteamId64 { get; set; }
        public bool Gk { get; set; }
        public bool Lb { get; set; }
        public bool Rb { get; set; }
        public bool Mf { get; set; }
        public bool Lw { get; set; }
        public bool Rw { get; set; }
        public bool SpecOnly { get; set; }
    }

    private sealed class CapPositionStore
    {
        public int Version { get; set; } = 1;
        public List<CapPositionEntry> Entries { get; set; } = new();
    }

    private CapPositionStore _capPositionStore = new();

    // Website-cap role override per slot (WebCap.cs writes/removes it).
    private readonly Dictionary<int, string> _playerPositions = new();
    private readonly List<int> _connectOrder = new();

    private void SocialOnLoad()
    {
        _capPositionStore = LoadJsonOrNull<CapPositionStore>(CapPositionsFileName) ?? new CapPositionStore();
        AddCommand("css_pos", "Set your cap positions (shown in the cap pick menu).", OnPositionsCommand);
        AddCommand("css_lc", "List connected players in join order.", OnConnectOrderCommand);
        AddCommand("css_late", "Alias for css_lc.", OnConnectOrderCommand);
        AddCommand("css_help", "List available SoccerMod commands.", OnHelpCommand);
        AddCommand("css_commands", "Alias for css_help.", OnHelpCommand);
        RegisterListener<Listeners.OnClientPutInServer>(SocialOnClientPutInServer);
    }

    private void SocialOnClientPutInServer(int playerSlot)
    {
        if (!_connectOrder.Contains(playerSlot))
        {
            _connectOrder.Add(playerSlot);
        }
    }

    private CapPositionEntry? FindCapPositions(ulong steamId64) =>
        steamId64 == 0 ? null : _capPositionStore.Entries.FirstOrDefault(e => e.SteamId64 == steamId64);

    private bool HasAnyCapPosition(ulong steamId64) =>
        FindCapPositions(steamId64) is { } e && (e.Gk || e.Lb || e.Rb || e.Mf || e.Lw || e.Rw || e.SpecOnly);

    // cap.sp CapCreatePickMenu label suffix: "[GK][LB]..." or "[SPEC ONLY]".
    private string FormatCapPositions(ulong steamId64)
    {
        if (FindCapPositions(steamId64) is not { } e)
        {
            return string.Empty;
        }

        if (e.SpecOnly)
        {
            return "[SPEC ONLY]";
        }

        var tags = string.Empty;
        if (e.Gk) tags += "[GK]";
        if (e.Lb) tags += "[LB]";
        if (e.Rb) tags += "[RB]";
        if (e.Mf) tags += "[MF]";
        if (e.Lw) tags += "[LW]";
        if (e.Rw) tags += "[RW]";
        return tags;
    }

    // Compact tag for !lc: the website-cap role if one is active, else the
    // player's own toggles joined with "/".
    private string? PlayerPositionTag(int slot)
    {
        if (_playerPositions.TryGetValue(slot, out var websiteRole))
        {
            return websiteRole;
        }

        var steamId = Utilities.GetPlayerFromSlot(slot)?.AuthorizedSteamID?.SteamId64 ?? 0UL;
        if (FindCapPositions(steamId) is not { } e)
        {
            return null;
        }

        if (e.SpecOnly)
        {
            return "SPEC";
        }

        var tags = new List<string>();
        if (e.Gk) tags.Add("GK");
        if (e.Lb) tags.Add("LB");
        if (e.Rb) tags.Add("RB");
        if (e.Mf) tags.Add("MF");
        if (e.Lw) tags.Add("LW");
        if (e.Rw) tags.Add("RW");
        return tags.Count == 0 ? null : string.Join('/', tags);
    }

    private void SaveCapPositions(string reason)
    {
        if (SaveJsonAtomic(CapPositionsFileName, _capPositionStore))
        {
            Logger.LogInformation("[SM2DIAG] cap_positions_saved reason={Reason} count={Count}", reason, _capPositionStore.Entries.Count);
        }
    }

    private void OnPositionsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        OpenCapPositionMenu(player);
    }

    // cap.sp OpenCapPositionMenu: "Soccer Mod - Cap - Positions", seven
    // "<Name>: Yes|No" toggles, back to the main menu.
    private void OpenCapPositionMenu(CCSPlayerController player)
    {
        var steamId = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        if (steamId == 0)
        {
            player.PrintToChat(" \x04[SM]\x01 Unable to identify your SteamID yet - try again in a moment.");
            return;
        }

        var entry = FindCapPositions(steamId);
        if (entry is null)
        {
            entry = new CapPositionEntry { SteamId64 = steamId };
            _capPositionStore.Entries.Add(entry);
        }

        static string YesNo(bool value) => value ? "Yes" : "No";
        var menu = new NumberMenu { Title = "Soccer Mod - Cap - Positions", OnBack = OpenMainMenu };
        menu.Add($"Goalkeeper: {YesNo(entry.Gk)}", p => ToggleCapPosition(p, entry, e => e.Gk = !e.Gk));
        menu.Add($"Left back: {YesNo(entry.Lb)}", p => ToggleCapPosition(p, entry, e => e.Lb = !e.Lb));
        menu.Add($"Right back: {YesNo(entry.Rb)}", p => ToggleCapPosition(p, entry, e => e.Rb = !e.Rb));
        menu.Add($"Midfielder: {YesNo(entry.Mf)}", p => ToggleCapPosition(p, entry, e => e.Mf = !e.Mf));
        menu.Add($"Left wing: {YesNo(entry.Lw)}", p => ToggleCapPosition(p, entry, e => e.Lw = !e.Lw));
        menu.Add($"Right wing: {YesNo(entry.Rw)}", p => ToggleCapPosition(p, entry, e => e.Rw = !e.Rw));
        menu.Add($"Spec only: {YesNo(entry.SpecOnly)}", p => ToggleCapPosition(p, entry, e => e.SpecOnly = !e.SpecOnly));
        OpenNumberMenu(player, menu);
    }

    private void ToggleCapPosition(CCSPlayerController player, CapPositionEntry entry, Action<CapPositionEntry> flip)
    {
        flip(entry);
        SaveCapPositions("position_menu");
        OpenCapPositionMenu(player);
    }

    private void OnConnectOrderCommand(CCSPlayerController? player, CommandInfo command)
    {
        var connected = _connectOrder
            .Select(Utilities.GetPlayerFromSlot)
            .Where(p => p is { IsValid: true })
            .Cast<CCSPlayerController>()
            .ToList();

        if (connected.Count == 0)
        {
            if (player is { IsValid: true }) player.PrintToChat(" \x04[SM]\x01 No players connected.");
            else command.ReplyToCommand("[SM] no players connected");
            return;
        }

        if (player is { IsValid: true }) player.PrintToChat(" \x04[SM]\x01 Connect order:");
        else command.ReplyToCommand("[SM] connect order:");
        for (var i = 0; i < connected.Count; i++)
        {
            var pos = PlayerPositionTag(connected[i].Slot);
            var line = $"{i + 1}. {connected[i].PlayerName}{(pos is null ? "" : $" [{pos}]")}";
            if (player is { IsValid: true }) player.PrintToChat($" \x04[SM]\x01 {line}");
            else command.ReplyToCommand($"[SM] {line}");
        }
    }

    private void OnHelpCommand(CCSPlayerController? player, CommandInfo command)
    {
        // 2026-08-30 user report: selecting Help from !menu didn't show
        // everything typing !help directly does. Root cause: the menu
        // invoked this via ExecuteClientCommandFromServer, which runs it as
        // a console command rather than a chat trigger - ReplyToCommand
        // then routes its output to the player's CONSOLE instead of chat,
        // where they weren't looking. Fixed by giving the real output its
        // own method that always prints to chat, called directly by the
        // menu (PrintHelp) and by this command handler alike, so both paths
        // are guaranteed identical. ReplyToCommand stays only for the
        // player-less (RCON/console) case, which has no chat to print to.
        if (player is { IsValid: true })
        {
            PrintHelp(player);
            return;
        }

        command.ReplyToCommand("[SM] --- SoccerMod commands ---");
        command.ReplyToCommand("[SM] !menu - open the SoccerMod menu");
        command.ReplyToCommand("[SM] !cap - open the cap menu; !pick - cap pick menu");
        command.ReplyToCommand("[SM] !match - match menu (start/stop, pause, settings)");
        command.ReplyToCommand("[SM] !training - training menu (cannon, ball spawn)");
        command.ReplyToCommand("[SM] !rdy - mark ready during a pause");
        command.ReplyToCommand("[SM] !forfeit - vote to forfeit for your team");
        command.ReplyToCommand("[SM] !sprint - burst of speed (or hold your +use key)");
        command.ReplyToCommand("[SM] !tp - toggle your third-person camera");
        command.ReplyToCommand("[SM] !gk - claim or release your team's goalkeeper skin");
        command.ReplyToCommand("[SM] !pos - set your cap positions");
        command.ReplyToCommand("[SM] !spec me - move yourself to spectator");
        command.ReplyToCommand("[SM] !lc / !late - list players by connect order");
        command.ReplyToCommand("[SM] !rank, !prank, !top, !stats - your ranking and stats");
        command.ReplyToCommand("[SM] !rr - restart the round (admin)");
    }

    private void PrintHelp(CCSPlayerController player)
    {
        player.PrintToChat(" \x04[SM]\x01 --- SoccerMod commands ---");
        player.PrintToChat(" \x04[SM]\x01 !menu - open the SoccerMod menu");
        player.PrintToChat(" \x04[SM]\x01 !cap - open the cap menu; !pick - cap pick menu");
        player.PrintToChat(" \x04[SM]\x01 !match - match menu (start/stop, pause, settings)");
        player.PrintToChat(" \x04[SM]\x01 !training - training menu (cannon, ball spawn)");
        player.PrintToChat(" \x04[SM]\x01 !rdy - mark ready during a pause");
        player.PrintToChat(" \x04[SM]\x01 !forfeit - vote to forfeit for your team");
        player.PrintToChat(" \x04[SM]\x01 !sprint - burst of speed (or hold your +use key)");
        player.PrintToChat(" \x04[SM]\x01 !tp - toggle your third-person camera");
        player.PrintToChat(" \x04[SM]\x01 !gk - claim or release your team's goalkeeper skin");
        player.PrintToChat(" \x04[SM]\x01 !pos - set your cap positions");
        player.PrintToChat(" \x04[SM]\x01 !spec me - move yourself to spectator");
        player.PrintToChat(" \x04[SM]\x01 !lc / !late - list players by connect order");
        player.PrintToChat(" \x04[SM]\x01 !rank, !prank, !top, !stats - your ranking and stats");
        player.PrintToChat(" \x04[SM]\x01 !rr - restart the round (admin)");

        // 2026-08-30 user request: the menu keybind instructions shown on
        // first join should be reachable again from !help, since that
        // first-join message is easy to miss or scroll past.
        player.PrintToChat(" \x04[SM]\x01 --- menu keys ---");
        MenuSendBindInstructions(player);
    }
}

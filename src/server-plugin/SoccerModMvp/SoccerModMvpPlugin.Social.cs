using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace SoccerModMvp;

// Tier 2 social/QoL batch from the CS:S-parity plan: !pos (position
// preference, shown in the cap pool list), !lc/!late (connect-order list -
// "quick reconnect keeps your slot" in spirit, though the reconnect-grace
// logic itself isn't built), !help/!commands (a plain public command list).
public sealed partial class SoccerModMvpPlugin
{
    private static readonly string[] PlayerPositions = { "GK", "LB", "RB", "MF", "LW", "Spec" };
    private readonly Dictionary<int, string> _playerPositions = new();
    private readonly List<int> _connectOrder = new();

    private void SocialOnLoad()
    {
        AddCommand("css_pos", "Pick your preferred position (shown next to your name in the cap pool).", OnPositionsCommand);
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

    private string? PlayerPositionTag(int slot) =>
        _playerPositions.TryGetValue(slot, out var pos) ? pos : null;

    private void OnPositionsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        var menu = new NumberMenu { Title = "Soccer Mod - Positions", OnBack = OpenMainMenu };
        foreach (var pos in PlayerPositions)
        {
            var chosen = pos;
            menu.Add(chosen, p =>
            {
                _playerPositions[p.Slot] = chosen;
                p.PrintToChat($" \x04[SM]\x01 Position set to {chosen}.");
            });
        }
        OpenNumberMenu(player, menu);
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
        command.ReplyToCommand("[SM] Caps are organized at kickoff.212-87-212-58.sslip.io");
        command.ReplyToCommand("[SM] !match status - match state; start/stop/pause/unpause/config need admin");
        command.ReplyToCommand("[SM] !rdy - mark ready during a pause");
        command.ReplyToCommand("[SM] !forfeit - vote to forfeit for your team");
        command.ReplyToCommand("[SM] !sprint - burst of speed (or hold your +use key)");
        command.ReplyToCommand("[SM] !pos - set your preferred position");
        command.ReplyToCommand("[SM] !spec me - move yourself to spectator");
        command.ReplyToCommand("[SM] !lc / !late - list players by connect order");
        command.ReplyToCommand("[SM] !rank, !prank, !top, !stats - your ranking and stats");
        command.ReplyToCommand("[SM] !rr - restart the round (admin)");
    }

    private void PrintHelp(CCSPlayerController player)
    {
        player.PrintToChat(" \x04[SM]\x01 --- SoccerMod commands ---");
        player.PrintToChat(" \x04[SM]\x01 !menu - open the SoccerMod menu");
        player.PrintToChat(" \x04[SM]\x01 Caps: kickoff.212-87-212-58.sslip.io");
        player.PrintToChat(" \x04[SM]\x01 !match status - match state; start/stop/pause/unpause/config need admin");
        player.PrintToChat(" \x04[SM]\x01 !rdy - mark ready during a pause");
        player.PrintToChat(" \x04[SM]\x01 !forfeit - vote to forfeit for your team");
        player.PrintToChat(" \x04[SM]\x01 !sprint - burst of speed (or hold your +use key)");
        player.PrintToChat(" \x04[SM]\x01 !pos - set your preferred position");
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

using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace SoccerModMvp;

// 2026-09-24 user request: links cannot be copied out of the in-game chat.
// The console can be selected and copied, so !links prints each URL there on
// its own line and only points at the console from chat. Only the Workshop
// item players actually receive is listed: MultiAddonManager downloads it on
// join, subscribing just pre-downloads it. 3807367334 (a jersey copy) and
// 3807366566 (menu copy) are not served and must not be advertised.
public sealed partial class SoccerModMvpPlugin
{
    private static readonly (string Label, string Url)[] PlayerLinks =
    {
        ("SoccerMod jerseys (Workshop)", "https://steamcommunity.com/sharedfiles/filedetails/?id=3797479770"),
    };

    private void LinksOnLoad()
    {
        AddCommand("css_links", "Prints the SoccerMod Workshop links to your console for copying.", OnLinksCommand);
        AddCommand("css_workshop", "Alias for css_links.", OnLinksCommand);
        AddCommand("css_bind", "Prints the SoccerMod menu key binds to your console for copying.", OnBindCommand);
        AddCommand("css_binds", "Alias for css_bind.", OnBindCommand);
    }

    // 2026-09-24 owner request: !bind puts the bind lines in the console,
    // where they can be copied (chat cannot be selected).
    private void OnBindCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true })
        {
            command.ReplyToCommand($"[SM] {SpectatorMenuKeysCommand}");
            command.ReplyToCommand($"[SM] {MenuBindLine}");
            return;
        }

        PrintBindsToConsole(player);
    }

    internal const string MenuBindLine =
        "bind 1 css_1;bind 2 css_2;bind 3 css_3;bind 4 css_4;bind 5 css_5;bind 6 css_6;bind 7 css_7;bind 8 css_8;bind 9 css_9;bind 0 css_0;bind F10 css_menu";

    private static void PrintBindsToConsole(CCSPlayerController player)
    {
        player.PrintToConsole("---------------- SoccerMod menu binds ----------------");
        player.PrintToConsole("Copy both lines below, paste them into this console once and press Enter:");
        player.PrintToConsole(SpectatorMenuKeysCommand);
        player.PrintToConsole(MenuBindLine);
        player.PrintToConsole("Then 1-7 pick, 8 = back, 9 = next, 0 = close, F10 opens the menu.");
        player.PrintToConsole("-------------------------------------------------------");
        player.PrintToChat(" \x04[SM]\x01 Bind lines are in your console (press ~) - copy and paste them there once.");
    }

    private void OnLinksCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true })
        {
            foreach (var (label, url) in PlayerLinks) command.ReplyToCommand($"[SM] {label}: {url}");
            return;
        }

        PrintLinks(player);
    }

    private static void PrintLinks(CCSPlayerController player)
    {
        player.PrintToConsole("---------------- SoccerMod links ----------------");
        foreach (var (label, url) in PlayerLinks)
        {
            player.PrintToConsole($"{label}:");
            player.PrintToConsole(url);
        }
        player.PrintToConsole("You do not have to subscribe: the server sends the files when you join.");
        player.PrintToConsole("-------------------------------------------------");
        player.PrintToChat(" \x04[SM]\x01 Links are in your console (press ~) - select and copy them there.");
    }
}

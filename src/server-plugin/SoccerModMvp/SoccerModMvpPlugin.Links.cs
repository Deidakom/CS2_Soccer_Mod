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

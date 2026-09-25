using CounterStrikeSharp.API.Core;

namespace SoccerModMvp;

// 2026-09-25 owner: every available command is listed in the menu under
// Help -> Commands, grouped. Picking a line prints it to chat as well, so
// longer descriptions stay readable. Admin groups only show for admins.
public sealed partial class SoccerModMvpPlugin
{
    internal sealed record HelpCommand(string Command, string Text);

    internal static readonly (string Group, string? Flag, HelpCommand[] Commands)[] HelpCommandGroups =
    {
        ("Player", null, new HelpCommand[]
        {
            new("!menu", "Open the SoccerMod menu"),
            new("!calls", "Football calls for your team (V)"),
            new("!links", "Workshop link, printed to your console"),
            new("!binds", "Menu key binds, printed to your console"),
            new("!menumouse on/off", "Click the menu, or keys only"),
            new("!kill", "Respawn if you are stuck"),
            new("!gk", "Become your team's goalkeeper (1 per team)"),
            new("!sprint", "Sprint (or hold your +use key)"),
            new("!sprintbar on/off/always", "Show or hide your sprint bar"),
            new("!sprintset", "Sprint chat messages on/off"),
            new("!knife / !gloves / !skins", "Knife, gloves and knife skin"),
            new("!spec me / !afk / !brb", "Move yourself to spectator"),
            new("!t / !ct", "Join a team (outside a match)"),
            new("!lc", "Players in join order"),
            new("!help", "This command list in chat"),
        }),
        ("Match and cap", null, new HelpCommand[]
        {
            new("!rdy / !unready", "Ready up during a match pause"),
            new("!forfeit", "Vote to forfeit for your team"),
            new("!cap", "Cap menu"),
            new("!capjoin", "Join or leave the cap signup"),
            new("!pick", "Captain's pick menu"),
            new("!pos", "Set your cap positions"),
        }),
        ("Stats and ranking", null, new HelpCommand[]
        {
            new("!stats", "Your personal stats"),
            new("!rank", "Your competitive ranking"),
            new("!prank", "Your all-time public ranking"),
            new("!top / !top50", "Top players"),
            new("!elo", "ELO ranking and player cards"),
        }),
        ("Admin: match", "match", new HelpCommand[]
        {
            new("!match", "Match start/stop/pause/unpause"),
            new("!matchrr", "Restart the match"),
            new("!rr", "Restart the round"),
            new("!maprr", "Reload the map"),
            new("!teamname", "Set a team name"),
            new("!ref", "Referee: cards and score"),
            new("!yellowcard / !redcard", "Give a card"),
            new("!uncard / !uncardall", "Clear cards"),
            new("!training", "Training menu"),
        }),
        ("Admin: players", "admin", new HelpCommand[]
        {
            new("!admin", "Admin menu (root)"),
            new("!spec all / !spec <player>", "Move players to spectator"),
            new("!kick <player>", "Kick a player"),
            new("!slay <player>", "Slay a player"),
            new("!ban <player> [min] [reason]", "Ban (0 = permanent, root)"),
            new("!unban / !banlist", "Lift or list bans"),
            new("!mute / !gag / !silence", "Voice, chat or both"),
            new("!unmute / !ungag / !unsilence", "Lift a mute or gag"),
            new("!commslist", "Active mutes and gags"),
        }),
    };

    private void OpenHelpCommandsMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Help - Commands", Key = "help-commands", OnBack = OpenHelpMenu };
        foreach (var (group, flag, commands) in HelpCommandGroups)
        {
            if (flag is not null && !HasFlag(SteamIdOf(player), flag)) continue;
            var (name, list) = (group, commands);
            menu.Add(name, p => OpenHelpCommandGroup(p, name, list));
        }
        OpenNumberMenu(player, menu);
    }

    private void OpenHelpCommandGroup(CCSPlayerController player, string group, HelpCommand[] commands)
    {
        var menu = new NumberMenu { Title = $"Commands - {group}", Key = "help-commands-" + group, OnBack = OpenHelpCommandsMenu };
        foreach (var command in commands)
        {
            var entry = command;
            menu.Add($"{entry.Command} - {entry.Text}", p => p.PrintToChat($" \x04[SM]\x01 {entry.Command} - {entry.Text}"));
        }
        OpenNumberMenu(player, menu);
    }
}

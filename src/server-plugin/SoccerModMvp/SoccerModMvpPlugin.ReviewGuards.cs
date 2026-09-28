using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace SoccerModMvp;

// Review 2026-09-28: small shared guards.
public sealed partial class SoccerModMvpPlugin
{
    // Flag files are read from disk at most every 2 s instead of every tick
    // (NetPocket, kickoff curtain and roof score checked them per tick).
    private readonly Dictionary<string, (long At, bool On)> _flagFileCache = new();

    private bool FlagFileOn(string file)
    {
        var now = Environment.TickCount64;
        if (_flagFileCache.TryGetValue(file, out var cached) && now - cached.At < 2000) return cached.On;
        var on = File.Exists(ConfigPath(file));
        _flagFileCache[file] = (now, on);
        return on;
    }

    // Public server (Admin - Settings): players only play and change their own
    // settings. The menu hid ranking, statistics and cap positions, but the
    // chat commands still worked - now they answer with a note instead.
    private bool PublicServerBlocks(CCSPlayerController? player) =>
        player is not null && _menuParity.PublicServer && !HasFlag(SteamIdOf(player), "admin");

    private CommandInfo.CommandCallback PublicOnly(CommandInfo.CommandCallback handler) => (player, command) =>
    {
        if (PublicServerBlocks(player))
        {
            command.ReplyToCommand("[SM] Not available on this public server.");
            return;
        }
        handler(player, command);
    };

    private static readonly HashSet<string> PublicServerHiddenHelp = new()
    {
        "!cap", "!capjoin", "!pick", "!pos", "!stats", "!rank", "!prank", "!top / !top50", "!elo",
    };
}

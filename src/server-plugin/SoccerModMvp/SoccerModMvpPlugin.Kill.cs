using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace SoccerModMvp;

// 2026-08-31 user request: a chat-typable !kill for players stuck against
// geometry or wanting a clean respawn. Engine-native "kill"/"suicide" console
// commands already exist but aren't chat-aliased; registering our own
// css_kill gets CounterStrikeSharp's automatic "!kill" chat alias. No
// permission gate - self-service, respawn-on-death (already on) does the
// rest.
public sealed partial class SoccerModMvpPlugin
{
    private void KillOnLoad()
    {
        AddCommand("css_kill", "Respawn yourself.", OnKillCommand);
    }

    private void OnKillCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true })
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        if (player.PlayerPawn.Value is not { IsValid: true } pawn || !IsAlive(pawn))
        {
            command.ReplyToCommand("[SM] you're not alive right now");
            return;
        }

        player.CommitSuicide(false, true);
    }
}

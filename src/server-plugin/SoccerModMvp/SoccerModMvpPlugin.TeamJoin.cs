using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;

// 2026-09-01 user request, GoSpec-inspired but built natively instead of
// installing joahreason/GoSpec. That plugin was rejected for two conflicts:
// its !spec collides with SoccerMod's existing admin css_spec ("!spec me"),
// and free !t/!ct switching would bypass the Kickoff website cap team
// assignments. Hence: no "spec" alias here (self-spectate is !afk / !brb),
// and every command is locked while a match is in any phase other than
// Warmup/Finished.
public sealed partial class SoccerModMvpPlugin
{
    private void TeamJoinOnLoad()
    {
        AddCommand("css_t", "Join the Terrorists (only outside a match).",
            (player, command) => OnTeamJoinCommand(player, command, CsTeam.Terrorist));
        AddCommand("css_ct", "Join the Counter-Terrorists (only outside a match).",
            (player, command) => OnTeamJoinCommand(player, command, CsTeam.CounterTerrorist));
        AddCommand("css_afk", "Move yourself to the spectators (only outside a match).",
            (player, command) => OnTeamJoinCommand(player, command, CsTeam.Spectator));
        AddCommand("css_brb", "Move yourself to the spectators (only outside a match).",
            (player, command) => OnTeamJoinCommand(player, command, CsTeam.Spectator));
    }

    private void OnTeamJoinCommand(CCSPlayerController? player, CommandInfo command, CsTeam targetTeam)
    {
        if (player is not { IsValid: true })
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        if (IsWebsiteCapActive() && targetTeam is CsTeam.Terrorist or CsTeam.CounterTerrorist)
        {
            if (!TryGetWebsiteCapParticipantTeam(player, out var assignedTeam))
            {
                if (player.Team != CsTeam.Spectator)
                {
                    player.ChangeTeam(CsTeam.Spectator);
                }
                command.ReplyToCommand("[SM] a KICKOFF CAP is running; non-cap players must remain spectators");
                return;
            }

            if (assignedTeam != targetTeam)
            {
                player.SwitchTeam(assignedTeam);
                command.ReplyToCommand("[SM] your KICKOFF CAP team assignment is locked");
                return;
            }
        }

        if (_matchPhase is not (MatchPhase.Warmup or MatchPhase.Finished))
        {
            command.ReplyToCommand("[SM] team switching is locked while a match is running");
            return;
        }

        if (_capFightPending || _capFightStarted)
        {
            command.ReplyToCommand("[SM] team switching is locked during a cap fight");
            return;
        }

        if (player.Team == targetTeam)
        {
            command.ReplyToCommand("[SM] you are already on that team");
            return;
        }

        player.ChangeTeam(targetTeam);
        // Not Match.cs's TeamName() - that helper only knows T/CT and would
        // mislabel a spectator move.
        var label = targetTeam switch
        {
            CsTeam.Terrorist => "the Terrorists",
            CsTeam.CounterTerrorist => "the Counter-Terrorists",
            _ => "the spectators",
        };
        command.ReplyToCommand($"[SM] moved to {label}");
    }
}

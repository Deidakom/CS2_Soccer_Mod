using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Diagnostic added 2026-08-30 while chasing "players can't move after
// spawning", introduced somewhere in that day's SoMoE reconstruction
// round. Three separate guesses (duck-jump block, landing-sound mute,
// godmode) each failed to fix it, so this reads the ACTUAL live pawn state
// instead of theorising a fourth time - the same lesson as the menu bug,
// where one diagnostic build settled in a single round trip what repeated
// reasoning had got wrong twice.
//
// Console/RCON only. Safe to leave in permanently: it only reads.
public sealed partial class SoccerModMvpPlugin
{
    private void MoveProbeOnLoad()
    {
        AddCommand("css_sm2_move_probe", "Server only: dump every movement-relevant field of each connected player.", OnMoveProbeCommand);
    }

    private void OnMoveProbeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command))
        {
            return;
        }

        // The first probe run proved the PAWN is healthy while the player
        // still can't move - so the wedge, if server-side at all, must be
        // in the gamerules (freeze period / warmup / restart-pending are
        // all networked to the client and gate its movement input).
        var proxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        if (proxy is { IsValid: true } && proxy.GameRules is { } rules)
        {
            Logger.LogInformation(
                "[SM2DIAG] move_probe_gamerules freezePeriod={FreezePeriod} warmupPeriod={WarmupPeriod} warmupEnd={WarmupEnd:F1} "
                + "gamePhase={GamePhase} gameRestart={GameRestart} restartRoundTime={RestartRoundTime:F1} roundStartTime={RoundStartTime:F1} "
                + "gameStartTime={GameStartTime:F1} intermissionStart={IntermissionStart:F1} intermissionEnd={IntermissionEnd:F1} "
                + "timeUntilNextPhase={TimeUntilNextPhase:F1} roundsPlayedThisPhase={RoundsPlayedThisPhase} now={Now:F1}",
                rules.FreezePeriod,
                rules.WarmupPeriod,
                rules.WarmupPeriodEnd,
                rules.GamePhase,
                rules.GameRestart,
                rules.RestartRoundTime,
                rules.RoundStartTime,
                rules.GameStartTime,
                rules.IntermissionStartTime,
                rules.IntermissionEndTime,
                rules.TimeUntilNextPhaseStarts,
                rules.RoundsPlayedThisPhase,
                Server.CurrentTime);
        }
        else
        {
            Logger.LogWarning("[SM2DIAG] move_probe_gamerules NOT FOUND");
        }

        var count = 0;
        foreach (var target in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot))
        {
            var pawn = target.PlayerPawn.Value;
            if (pawn is not { IsValid: true })
            {
                Logger.LogInformation("[SM2DIAG] move_probe slot={Slot} name={Name} pawn=<invalid>", target.Slot, target.PlayerName);
                count++;
                continue;
            }

            var movement = pawn.MovementServices;
            var humanoid = movement is not null ? new CCSPlayer_MovementServices(movement.Handle) : null;

            Logger.LogInformation(
                "[SM2DIAG] move_probe slot={Slot} name={Name} team={Team} alive={Alive} "
                + "moveType={MoveType} actualMoveType={ActualMoveType} flags={Flags} "
                + "velocityModifier={VelocityModifier:F3} takesDamage={TakesDamage} health={Health} "
                + "maxSpeed={MaxSpeed:F1} ducked={Ducked} ducking={Ducking} desiresDuck={DesiresDuck} "
                + "duckAmount={DuckAmount:F3} duckOverride={DuckOverride} duckUntilOnGround={DuckUntilOnGround} "
                + "stamina={Stamina:F2} fallVelocity={FallVelocity:F1} origin={Origin} velocity={Velocity}",
                target.Slot,
                target.PlayerName,
                target.Team,
                IsAlive(pawn),
                pawn.MoveType,
                pawn.ActualMoveType,
                pawn.Flags,
                pawn.VelocityModifier,
                pawn.TakesDamage,
                pawn.Health,
                movement?.Maxspeed ?? -1.0f,
                humanoid?.Ducked,
                humanoid?.Ducking,
                humanoid?.DesiresDuck,
                humanoid?.DuckAmount ?? -1.0f,
                humanoid?.DuckOverride,
                humanoid?.DuckUntilOnGround,
                humanoid?.Stamina ?? -1.0f,
                humanoid?.FallVelocity ?? -1.0f,
                FormatVector(pawn.AbsOrigin),
                FormatVector(pawn.AbsVelocity));
            count++;
        }

        command.ReplyToCommand($"[SM2DIAG] move_probe logged {count} player(s)");
    }
}

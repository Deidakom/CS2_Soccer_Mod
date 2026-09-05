using System;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Port of SoMoE-19's ranking.sp + stats.sp point table (2026-08-30 SoMoE
// reconstruction round). Storage is JSON (SaveJsonAtomic, same pattern as
// every other store in this plugin), not the original's SQLite - simpler,
// no new native dependency to verify on the Linux host, same field names
// so nothing about the point table itself is lost.
//
// Two pools per the original: "public" (always accumulates) and "match"
// (only while a real match is running with enough players per team -
// StatsMinPlayersPerTeam, default 5, matching SoMoE's EnoughPlayers gate).
//
// Simplification, stated plainly: SoMoE's "Round MVP" (highest points
// gained in the single round just finished, announced every goal) is NOT
// ported - it needs a per-round point-delta snapshot this pass didn't
// build. MOTM (man of the match, at full time) and the halftime Top-3 ARE
// ported, both using cumulative match points, which needs no per-round
// tracking.
public sealed partial class SoccerModMvpPlugin
{
    private const string StatsFileName = "soccermod_stats.json";
    private const int StatsMinPlayersPerTeam = 5;

    private const int PointsGoal = 17;
    private const int PointsAssist = 12;
    private const int PointsOwnGoal = -10;
    private const int PointsHit = 1;
    private const int PointsPass = 5;
    private const int PointsInterception = 3;
    private const int PointsBallLoss = -3;
    private const int PointsSave = 6;
    private const int PointsRoundWon = 10;
    private const int PointsRoundLost = -10;
    private const int PointsMotm = 25;

    private sealed class StatLine
    {
        public int Goals { get; set; }
        public int Assists { get; set; }
        public int OwnGoals { get; set; }
        public int Hits { get; set; }
        public int Passes { get; set; }
        public int Interceptions { get; set; }
        public int BallLosses { get; set; }
        public int Saves { get; set; }
        public int RoundsWon { get; set; }
        public int RoundsLost { get; set; }
        public int Points { get; set; }
        public int Motm { get; set; }
        public int Matches { get; set; }
    }

    private sealed class StatsEntry
    {
        public ulong SteamId64 { get; set; }
        public string Name { get; set; } = string.Empty;
        public StatLine Public { get; set; } = new();
        public StatLine Match { get; set; } = new();
    }

    private sealed class StatsStore
    {
        public int Version { get; set; } = 1;
        public List<StatsEntry> Entries { get; set; } = new();
    }

    private StatsStore _statsStore = new();
    private readonly Dictionary<ulong, StatsEntry> _statsBySteamId = new();
    // Slot of the second-most-recent toucher, for assist attribution
    // (assist = the same-team player who touched it immediately before
    // the scorer, if the scorer's own touch wasn't already a solo run).
    private int _secondLastKickerSlot = -1;
    private CsTeam _secondLastKickerTeam = CsTeam.None;

    private void StatsOnLoad()
    {
        _statsStore = LoadJsonOrNull<StatsStore>(StatsFileName) ?? new StatsStore();
        _statsBySteamId.Clear();
        foreach (var entry in _statsStore.Entries)
            _statsBySteamId.TryAdd(entry.SteamId64, entry);
        AddCommand("css_rank", "Show your match ranking (5 players/team minimum).", OnRankCommand);
        AddCommand("css_prank", "Show your all-time public ranking.", OnPublicRankCommand);
        AddCommand("css_top", "Top players by points.", OnTopCommand);
        AddCommand("css_stats", "Show your personal stats.", OnStatsCommand);
        AddCommand("css_wiperanks", "Server only: wipe all stats.", OnWipeRanksCommand);
    }

    private void SaveStats(string reason)
    {
        if (SaveJsonAtomic(StatsFileName, _statsStore))
        {
            Logger.LogInformation("[SM2DIAG] stats_saved reason={Reason} count={Count}", reason, _statsStore.Entries.Count);
        }
    }

    private StatsEntry GetOrCreateStatsEntry(ulong steamId64, string name)
    {
        if (!_statsBySteamId.TryGetValue(steamId64, out var entry))
        {
            entry = new StatsEntry { SteamId64 = steamId64, Name = name };
            _statsStore.Entries.Add(entry);
            _statsBySteamId.Add(steamId64, entry);
        }
        else
        {
            entry.Name = name;
        }

        return entry;
    }

    private bool MatchStatsWritable()
    {
        if (_matchPhase is not (MatchPhase.Live or MatchPhase.GoalPause))
            return false;
        var terrorists = 0;
        var counterTerrorists = 0;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot)
                continue;
            if (player.Team == CsTeam.Terrorist) terrorists++;
            else if (player.Team == CsTeam.CounterTerrorist) counterTerrorists++;
        }
        return terrorists >= StatsMinPlayersPerTeam && counterTerrorists >= StatsMinPlayersPerTeam;
    }

    private void ResetMatchStats()
    {
        foreach (var entry in _statsStore.Entries)
            entry.Match = new StatLine();
    }

    private void StatsApply(CCSPlayerController player, Action<StatLine> apply)
    {
        var steamId = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        if (steamId == 0 || player.IsBot)
        {
            return;
        }

        var entry = GetOrCreateStatsEntry(steamId, player.PlayerName);
        apply(entry.Public);
        if (MatchStatsWritable())
        {
            apply(entry.Match);
        }
    }

    private void StatsRecordSave(int slot)
    {
        if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } saver)
        {
            StatsApply(saver, s => { s.Saves++; s.Points += PointsSave; });
        }
    }

    // Called from RecordBallTouch, BEFORE _lastKickerSlot/Team are
    // overwritten - previousToucherSlot/Team are the touch right before
    // this one.
    private void StatsOnBallTouch(CCSPlayerController toucher, int previousToucherSlot, CsTeam previousToucherTeam)
    {
        StatsApply(toucher, s => { s.Hits++; s.Points += PointsHit; });

        if (previousToucherSlot < 0 || previousToucherSlot == toucher.Slot)
        {
            return;
        }

        if (previousToucherTeam == toucher.Team)
        {
            // Pass, credited to the PREVIOUS toucher.
            if (Utilities.GetPlayerFromSlot(previousToucherSlot) is { IsValid: true } passer)
            {
                StatsApply(passer, s => { s.Passes++; s.Points += PointsPass; });
            }
        }
        else
        {
            // Interception for the current toucher, ball loss for the
            // previous one.
            StatsApply(toucher, s => { s.Interceptions++; s.Points += PointsInterception; });
            if (Utilities.GetPlayerFromSlot(previousToucherSlot) is { IsValid: true } loser)
            {
                StatsApply(loser, s => { s.BallLosses++; s.Points += PointsBallLoss; });
            }
        }
    }

    // Called from OnGoalScored (Match.cs) BEFORE _lastKickerSlot is reset
    // by the kickoff restart. scorerSlot/ownGoal already resolved there.
    private void StatsOnGoalScored(int scorerSlot, CsTeam scoringTeam, bool ownGoal)
    {
        if (scorerSlot >= 0 && Utilities.GetPlayerFromSlot(scorerSlot) is { IsValid: true } scorer)
        {
            StatsApply(scorer, s =>
            {
                if (ownGoal) { s.OwnGoals++; s.Points += PointsOwnGoal; }
                else { s.Goals++; s.Points += PointsGoal; }
            });

            // Assist: the second-last toucher, same team as the scorer,
            // not the scorer themselves - i.e. the pass that set up the goal.
            if (!ownGoal && _secondLastKickerSlot >= 0 && _secondLastKickerSlot != scorerSlot
                && _secondLastKickerTeam == scoringTeam
                && Utilities.GetPlayerFromSlot(_secondLastKickerSlot) is { IsValid: true } assister)
            {
                StatsApply(assister, s => { s.Assists++; s.Points += PointsAssist; });
            }
        }

        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
            {
                continue;
            }

            var won = player.Team == scoringTeam;
            StatsApply(player, s =>
            {
                if (won) { s.RoundsWon++; s.Points += PointsRoundWon; }
                else { s.RoundsLost++; s.Points += PointsRoundLost; }
            });
        }

        SaveStats("goal_scored");
    }

    // Called from EndPeriod (Match.cs).
    private void StatsAnnounceHalftimeTop3()
    {
        var top3 = _statsStore.Entries
            .Where(e => e.Match.Hits > 0)
            .OrderByDescending(e => e.Match.Points)
            .Take(3)
            .ToList();
        if (top3.Count == 0)
        {
            return;
        }

        AnnounceAll(" \x04[Match]\x01 Halftime top 3:");
        for (var i = 0; i < top3.Count; i++)
        {
            AnnounceAll($" \x04[Match]\x01 {i + 1}. {top3[i].Name} - {top3[i].Match.Points} pts");
        }
    }

    // Called from FinishMatch (Match.cs).
    private void StatsOnMatchFinished()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player.IsValid && player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
            {
                StatsApply(player, s => s.Matches++);
            }
        }

        var motm = _statsStore.Entries.Where(e => e.Match.Hits > 0).OrderByDescending(e => e.Match.Points).FirstOrDefault();
        if (motm is not null)
        {
            motm.Public.Motm++;
            motm.Match.Motm++;
            motm.Public.Points += PointsMotm;
            motm.Match.Points += PointsMotm;
            AnnounceAll($" \x04[Match]\x01 The man of the match was {motm.Name} with {motm.Match.Points} points.");
            AppendMatchLog($"MOTM {motm.Name} points={motm.Match.Points}");
        }

        // Reset the per-match pool for the next match; public stays.
        foreach (var entry in _statsStore.Entries)
        {
            entry.Match = new StatLine();
        }

        SaveStats("match_finished");
    }

    private static string FormatStatLine(string label, StatLine s) =>
        $"[SM] {label}: {s.Points} pts | G:{s.Goals} A:{s.Assists} OG:{s.OwnGoals} Saves:{s.Saves} Hits:{s.Hits} "
        + $"Pass:{s.Passes} Int:{s.Interceptions} Loss:{s.BallLosses} W/L:{s.RoundsWon}/{s.RoundsLost} MOTM:{s.Motm} Matches:{s.Matches}";

    private static void ReplyStats(CCSPlayerController? player, CommandInfo command, string text)
    {
        if (player is { IsValid: true })
        {
            var body = text.StartsWith("[SM] ", StringComparison.Ordinal) ? text[5..] : text;
            player.PrintToChat($" \x04[SM]\x01 {body}");
        }
        else
        {
            command.ReplyToCommand(text);
        }
    }

    private void OnRankCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        var steamId = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        var entry = _statsStore.Entries.FirstOrDefault(e => e.SteamId64 == steamId);
        if (entry is null || entry.Match.Hits == 0)
        {
            ReplyStats(player, command, "[SM] no match stats yet this match");
            return;
        }

        var ranked = _statsStore.Entries.Where(e => e.Match.Hits > 0).OrderByDescending(e => e.Match.Points).ToList();
        var rank = ranked.FindIndex(e => e.SteamId64 == steamId) + 1;
        AnnounceAll($" \x04[Match]\x01 {player.PlayerName} is ranked {rank} with {entry.Match.Points} points this match.");
    }

    private void OnPublicRankCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        var steamId = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        var entry = _statsStore.Entries.FirstOrDefault(e => e.SteamId64 == steamId);
        if (entry is null || entry.Public.Hits == 0)
        {
            ReplyStats(player, command, "[SM] no stats yet");
            return;
        }

        var ranked = _statsStore.Entries.Where(e => e.Public.Hits > 0).OrderByDescending(e => e.Public.Points).ToList();
        var rank = ranked.FindIndex(e => e.SteamId64 == steamId) + 1;
        ReplyStats(player, command, $"[SM] {player.PlayerName} is ranked {rank} of {ranked.Count} all-time with {entry.Public.Points} points.");
    }

    private void OnTopCommand(CCSPlayerController? player, CommandInfo command)
    {
        var top = _statsStore.Entries.Where(e => e.Public.Hits > 0).OrderByDescending(e => e.Public.Points).Take(10).ToList();
        if (top.Count == 0)
        {
            ReplyStats(player, command, "[SM] no stats yet");
            return;
        }

        ReplyStats(player, command, "[SM] Top players (all-time points):");
        for (var i = 0; i < top.Count; i++)
        {
            ReplyStats(player, command, $"[SM] {i + 1}. {top[i].Name} - {top[i].Public.Points} pts");
        }
    }

    private void OnStatsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        var steamId = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        var entry = _statsStore.Entries.FirstOrDefault(e => e.SteamId64 == steamId);
        if (entry is null)
        {
            ReplyStats(player, command, "[SM] no stats yet - touch the ball to start tracking");
            return;
        }

        ReplyStats(player, command, FormatStatLine("all-time", entry.Public));
        ReplyStats(player, command, FormatStatLine("this match", entry.Match));
    }

    private void OnWipeRanksCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command))
        {
            return;
        }

        var count = _statsStore.Entries.Count;
        _statsStore.Entries.Clear();
        _statsBySteamId.Clear();
        SaveStats("wipe_ranks_command");
        command.ReplyToCommand($"[SM2DIAG] wiped {count} stats entries");
    }
}

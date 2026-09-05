using System;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// CS:S point table with public totals, completed competitive history and live dashboards.
public sealed partial class SoccerModMvpPlugin
{
    private bool _competitiveStoreWritable = true;
    private const string CompetitiveStatsFileName = "soccermod_competitive_stats.json";
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
    private const int PointsMvp = 15;

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
        public int Mvp { get; set; }
        public double PossessionSeconds { get; set; }
        public void Add(StatLine other)
        {
            Goals += other.Goals;
            Assists += other.Assists;
            OwnGoals += other.OwnGoals;
            Hits += other.Hits;
            Passes += other.Passes;
            Interceptions += other.Interceptions;
            BallLosses += other.BallLosses;
            Saves += other.Saves;
            RoundsWon += other.RoundsWon;
            RoundsLost += other.RoundsLost;
            Points += other.Points;
            Motm += other.Motm;
            Matches += other.Matches;
            Mvp += other.Mvp;
            PossessionSeconds += other.PossessionSeconds;
        }
        public StatLine Copy() => (StatLine)MemberwiseClone();
        public double Score(int mode) => mode switch
        {
            1 => RoundsWon + RoundsLost > 0 ? (double)Points / (RoundsWon + RoundsLost) : 0,
            2 => Matches > 0 ? (double)Points / Matches : 0,
            _ => Points
        };
    }

    private sealed class StatsEntry
    {
        public ulong SteamId64 { get; set; }
        public string Name { get; set; } = string.Empty;
        public StatLine Public { get; set; } = new();
        public StatLine Match { get; set; } = new();
        public StatLine Competitive { get; set; } = new();
        public DateTime? CreatedUtc { get; set; }
        public DateTime? LastConnectedUtc { get; set; }
        public double PlaySeconds { get; set; }
        public StatsChatPreferences Chat { get; set; } = new();
        [System.Text.Json.Serialization.JsonIgnore] public StatLine Round { get; set; } = new();
        [System.Text.Json.Serialization.JsonIgnore] public StatLine Current { get; set; } = new();
    }

    private sealed class StatsStore
    {
        public int Version { get; set; } = 2;
        public List<StatsEntry> Entries { get; set; } = new();
    }

    private StatsStore _statsStore = new();
    private readonly Dictionary<ulong, StatsEntry> _statsBySteamId = new();
    // Slot of the second-most-recent toucher, for assist attribution
    // (assist = the same-team player who touched it immediately before
    // the scorer, if the scorer's own touch wasn't already a solo run).
    private int _secondLastKickerSlot = -1;
    private CsTeam _secondLastKickerTeam = CsTeam.None;

    private void StatsOnLoad(bool hotReload)
    {
        _statsStore = LoadJsonOrNull<StatsStore>(StatsFileName) ?? new StatsStore();
        var loadedHistory = LoadJsonOrNull<Dictionary<ulong, StatLine>>(CompetitiveStatsFileName);
        _competitiveStoreWritable = loadedHistory is not null || !File.Exists(ConfigPath(CompetitiveStatsFileName));
        var historical = loadedHistory ?? new();
        foreach (var (id, stats) in historical)
        {
            var old = _statsStore.Entries.FirstOrDefault(e => e.SteamId64 == id);
            if (old is null) _statsStore.Entries.Add(old = new StatsEntry { SteamId64 = id, Name = $"steamid:{id}" });
            old.Competitive = stats;
        }
        _statsStore.Entries = _statsStore.Entries.GroupBy(e => e.SteamId64).Select(g => g.First()).ToList();
        _statsBySteamId.Clear();
        foreach (var entry in _statsStore.Entries)
            _statsBySteamId.TryAdd(entry.SteamId64, entry);
        foreach (var entry in _statsStore.Entries)
        {
            entry.Public ??= new(); entry.Competitive ??= new(); entry.Chat ??= new();
            entry.Match = new(); // A reloaded plugin cannot resume the old match state.
        }
        _statsStore.Version = 2;
        RegisterListener<Listeners.OnClientAuthorized>((slot, steam) => StatsConnected(slot, steam.SteamId64));
        RegisterListener<Listeners.OnClientDisconnect>(StatsDisconnected);
        // Cold startup precedes engine globals; authorization events populate new sessions.
        if (hotReload)
            foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot))
                if (p.AuthorizedSteamID is { } steam) StatsConnected(p.Slot, steam.SteamId64);
        AddTimer(60, () => SaveStats("periodic"), CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
        AddCommand("css_top50", "Browse the top 50 rankings.", (p, c) => { if (p is not null) OpenRankingMenu(p); });
        AddCommand("css_rank", "Show your completed competitive ranking (5 players/team minimum).", OnRankCommand);
        AddCommand("css_prank", "Show your all-time public ranking.", OnPublicRankCommand);
        AddCommand("css_top", "Top players by points.", OnTopCommand);
        AddCommand("css_stats", "Show your personal stats.", OnStatsCommand);
        AddCommand("css_wiperanks", "Server only: wipe all stats.", OnWipeRanksCommand);
    }

    private void SaveStats(string reason)
    {
        StatsFlushPlayTime();
        SaveCompetitiveStats();
        if (SaveJsonAtomic(StatsFileName, _statsStore))
        {
            Logger.LogInformation("[SM2DIAG] stats_saved reason={Reason} count={Count}", reason, _statsStore.Entries.Count);
        }
    }

    private bool SaveCompetitiveStats() => _competitiveStoreWritable && SaveJsonAtomic(CompetitiveStatsFileName,
        _statsStore.Entries.ToDictionary(e => e.SteamId64, e => e.Competitive));

    private StatsEntry GetOrCreateStatsEntry(ulong steamId64, string name)
    {
        if (!_statsBySteamId.TryGetValue(steamId64, out var entry))
        {
            entry = new StatsEntry { SteamId64 = steamId64, Name = name, CreatedUtc = DateTime.UtcNow };
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
        {
            entry.Match = new(); entry.Current = new(); entry.Round = new();
        }
        _teamMatchStats.Clear(); _teamRoundStats.Clear();
    }

    private void StatsApply(CCSPlayerController player, Action<StatLine> apply, bool trackTeam = true)
    {
        var steamId = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        if (steamId == 0 || player.IsBot)
        {
            return;
        }

        var entry = GetOrCreateStatsEntry(steamId, player.PlayerName);
        apply(entry.Public);
        apply(entry.Round);
        if (_matchPhase is MatchPhase.Live or MatchPhase.GoalPause)
        {
            apply(entry.Current);
            if (trackTeam) apply(TeamStats(_teamMatchStats, player.Team));
        }
        if (trackTeam) apply(TeamStats(_teamRoundStats, player.Team));
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
            StatsChatEvent(saver, "save", "made a save");
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
            if (Utilities.GetPlayerFromSlot(previousToucherSlot) is { IsValid: true } passer && passer.Team == previousToucherTeam)
            {
                StatsApply(passer, s => { s.Passes++; s.Points += PointsPass; });
                StatsChatEvent(passer, "pass", "completed a pass");
            }
        }
        else
        {
            // Interception for the current toucher, ball loss for the
            // previous one.
            StatsApply(toucher, s => { s.Interceptions++; s.Points += PointsInterception; });
            if (Utilities.GetPlayerFromSlot(previousToucherSlot) is { IsValid: true } loser && loser.Team == previousToucherTeam)
            {
                StatsApply(loser, s => { s.BallLosses++; s.Points += PointsBallLoss; });
                StatsChatEvent(loser, "loss", "lost possession");
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
                && Utilities.GetPlayerFromSlot(_secondLastKickerSlot) is { IsValid: true } assister && assister.Team == scoringTeam)
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
            }, trackTeam: false);
        }
        foreach (var team in new[] { CsTeam.Terrorist, CsTeam.CounterTerrorist })
        {
            void AwardRound(StatLine stats)
            {
                if (team == scoringTeam) { stats.RoundsWon++; stats.Points += PointsRoundWon; }
                else { stats.RoundsLost++; stats.Points += PointsRoundLost; }
            }
            AwardRound(TeamStats(_teamRoundStats, team));
            if (_matchPhase is MatchPhase.Live or MatchPhase.GoalPause) AwardRound(TeamStats(_teamMatchStats, team));
        }

        var roundMvp = _statsStore.Entries.Where(e => e.Round.Hits > 0)
            .OrderByDescending(e => e.Round.Points).ThenBy(e => e.SteamId64).FirstOrDefault();
        if (roundMvp is not null)
        {
            roundMvp.Public.Mvp++; roundMvp.Public.Points += PointsMvp;
            if (_matchPhase is MatchPhase.Live or MatchPhase.GoalPause) { roundMvp.Current.Mvp++; roundMvp.Current.Points += PointsMvp; }
            if (MatchStatsWritable()) { roundMvp.Match.Mvp++; roundMvp.Match.Points += PointsMvp; }
            if (_menuParity.RoundMvp) AnnounceAll($"[SM] Round MVP: {roundMvp.Name} ({roundMvp.Round.Points} points).");
        }
        foreach (var entry in _statsStore.Entries) entry.Round = new();
        _teamRoundStats.Clear();
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
        var result = FinalizeStatsHistory();
        if (result is { } award)
        {
            AnnounceAll($"[Match] The man of the match was {award.Name} with {award.Points} points.");
            AppendMatchLog($"MOTM {award.Name} points={award.Points}");
        }
        SaveStats("match_finished");
    }

    private (string Name, int Points)? FinalizeStatsHistory()
    {
        // FinishMatch has already entered Finished. Do not use MatchStatsWritable here.
        foreach (var entry in _statsStore.Entries.Where(e => e.Current.Hits > 0 || e.Current.RoundsWon + e.Current.RoundsLost > 0))
        {
            if (entry.Current.Matches == 0) entry.Public.Matches++;
            entry.Current.Matches = 1;
        }
        foreach (var entry in _statsStore.Entries.Where(e => e.Match.Hits > 0 || e.Match.RoundsWon + e.Match.RoundsLost > 0))
            entry.Match.Matches = 1;

        var motm = _statsStore.Entries.Where(e => e.Match.Hits > 0).OrderByDescending(e => e.Match.Points).ThenBy(e => e.SteamId64).FirstOrDefault();
        if (motm is not null)
        {
            motm.Public.Motm++;
            motm.Match.Motm++;
            motm.Current.Motm++; motm.Current.Points += PointsMotm;
            motm.Public.Points += PointsMotm;
            motm.Match.Points += PointsMotm;

        }

        (string Name, int Points)? result = motm is null ? null : (motm.Name, motm.Match.Points);
        foreach (var entry in _statsStore.Entries)
        {
            entry.Competitive.Add(entry.Match);
            entry.Match = new();
        }

        return result;
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

    private void OnRankCommand(CCSPlayerController? player, CommandInfo command) => ReplyRank(player, command, true);
    private void OnPublicRankCommand(CCSPlayerController? player, CommandInfo command) => ReplyRank(player, command, false);
    private void ReplyRank(CCSPlayerController? player, CommandInfo command, bool competitive)
    {
        if (player is null) { command.ReplyToCommand("Use css_top from server console."); return; }
        var id = player.AuthorizedSteamID?.SteamId64 ?? 0;
        if (_rankNext.GetValueOrDefault(id) > Server.TickedTime)
        { ReplyStats(player, command, "[SM] Ranking command is cooling down; rankings remain available in the menu."); return; }
        _rankNext[id] = Server.TickedTime + _menuParity.RankCooldown;
        var mode = _menuParity.RankMode;
        StatLine Pool(StatsEntry e) => competitive ? e.Competitive : e.Public;
        var ranked = _statsStore.Entries.Where(e => Pool(e).Hits > 0 && (mode != 1 || Pool(e).RoundsWon + Pool(e).RoundsLost > 0)
            && (mode != 2 || Pool(e).Matches > 0)).OrderByDescending(e => Pool(e).Score(mode)).ThenBy(e => e.SteamId64).ToList();
        var index = ranked.FindIndex(e => e.SteamId64 == id);
        if (index < 0) { ReplyStats(player, command, "[SM] No eligible ranking history yet."); return; }
        ReplyStats(player, command, $"[SM] {(competitive ? "Competitive" : "Public")} rank {index + 1}/{ranked.Count}: {Pool(ranked[index]).Score(mode):0.##} {new[] { "points", "points/round", "points/match" }[mode]}.");
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

        if (MatchRunning) { command.ReplyToCommand("Stop the match before resetting statistics."); return; }
        var count = _statsStore.Entries.Count;
        _statsStore.Entries.Clear();
        _statsBySteamId.Clear();
        SaveStats("wipe_ranks_command");
        command.ReplyToCommand($"[SM2DIAG] wiped {count} stats entries");
    }
}

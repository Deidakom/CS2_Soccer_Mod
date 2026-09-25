using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace SoccerModMvp;

// ELO ranking, after Subear-17's SoMoE plugin (github.com/Subear-17/soccer-mod-elo-ranking).
// Reimplemented in C# from its documented behaviour; the rating rules live in
// EloMath.cs. Differences decided with the server owner (2026-09-24):
// - a match is rated when it reaches full time (no forfeit, no admin stop) with
//   5v5 or 6v6 at kickoff; SoMoE's 25-minute minimum does not fit 2x10 matches;
// - the LOWER-rated captain picks first when captains differ by more than the
//   tiebreak percentage (what the original code does; its README says higher);
// - admin tools use the plugin's own "admin" flag instead of hard-coded owners;
// - players moved by the halftime vote are rated with the team they finish on.
public sealed partial class SoccerModMvpPlugin
{
    private const string EloFileName = "soccermod_elo.json";
    private const int EloRatingLogMax = 500;
    private const int EloNameHistoryMax = 20;
    private const float EloSwapVoteSeconds = 20.0f;

    private sealed class EloNameRecord
    {
        public string Name { get; set; } = string.Empty;
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public string? ChangedBy { get; set; }
    }

    private sealed class EloCareer
    {
        public int GamesRated { get; set; }
        public int Goals { get; set; }
        public int Assists { get; set; }
        public int OwnGoals { get; set; }
        public int Saves { get; set; }
        public int Passes { get; set; }
        public int Interceptions { get; set; }
        public int Hits { get; set; }
    }

    private sealed class EloPlayer
    {
        public ulong SteamId64 { get; set; }
        public double Rating { get; set; } = EloMath.DefaultRating;
        public int Games { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public double PointsTotal { get; set; }
        public string? Nickname { get; set; }
        public EloCareer Career { get; set; } = new();
        public List<EloNameRecord> JoinNames { get; set; } = new();
        public List<EloNameRecord> NicknameHistory { get; set; } = new();
    }

    private sealed class EloSettings
    {
        public double FirstPickGapPercent { get; set; } = 6.0;
        public int SwapGoalGap { get; set; } = 5;
    }

    private sealed class EloRatingChange
    {
        public DateTime Utc { get; set; }
        public ulong SteamId64 { get; set; }
        public double Before { get; set; }
        public double After { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    private sealed class EloStore
    {
        public int Version { get; set; } = 1;
        public EloSettings Settings { get; set; } = new();
        public List<EloPlayer> Players { get; set; } = new();
        public List<EloRatingChange> Log { get; set; } = new();
    }

    private EloStore _eloStore = new();
    // SteamID64 -> engine team at kickoff (after halftime-vote moves, the
    // team they were moved to, expressed as a kickoff side).
    private readonly Dictionary<ulong, CsTeam> _eloRoster = new();
    private bool _eloMatchRated;
    // Captains and their first picks are never offered in the halftime vote.
    private readonly HashSet<ulong> _eloProtected = new();
    private readonly HashSet<CsTeam> _eloFirstPickTaken = new();

    private bool _eloSwapVoteActive;
    private readonly List<(ulong A, ulong B)> _eloSwapOptions = new();
    private readonly Dictionary<ulong, int> _eloSwapVotes = new();
    private Timer? _eloSwapTimer;

    private void EloOnLoad(bool hotReload)
    {
        _eloStore = LoadJsonOrNull<EloStore>(EloFileName) ?? new EloStore();
        AddCommand("css_elo", "Opens the ELO ranking menu.", OnEloCommand);
        AddCommand("css_sm2elo_config", "ELO settings: firstpickgap <percent> | swapgap <goals>.", OnEloConfigCommand);
        RegisterEventHandler<EventPlayerConnectFull>((@event, _) =>
        {
            if (@event.Userid is { IsValid: true, IsBot: false } player) EloRecordJoinName(player);
            return HookResult.Continue;
        });
        // Cold startup precedes engine globals: GetPlayers threw "Global
        // Variables not initialized yet" and the whole plugin failed to load
        // on the 2026-09-25 CS2 update restart. Connect events cover joins.
        if (hotReload)
            foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot)) EloRecordJoinName(player);
    }

    private void SaveElo(string reason)
    {
        if (_eloStore.Log.Count > EloRatingLogMax) _eloStore.Log.RemoveRange(0, _eloStore.Log.Count - EloRatingLogMax);
        if (SaveJsonAtomic(EloFileName, _eloStore))
        {
            Logger.LogInformation("[SM2DIAG] elo_saved reason={Reason} players={Players}", reason, _eloStore.Players.Count);
        }
    }

    private EloPlayer? FindEloPlayer(ulong steamId64) =>
        steamId64 == 0 ? null : _eloStore.Players.FirstOrDefault(p => p.SteamId64 == steamId64);

    private EloPlayer GetOrCreateEloPlayer(ulong steamId64)
    {
        var player = FindEloPlayer(steamId64);
        if (player is null)
        {
            player = new EloPlayer { SteamId64 = steamId64 };
            _eloStore.Players.Add(player);
        }
        return player;
    }

    private double EloRating(ulong steamId64) => FindEloPlayer(steamId64)?.Rating ?? EloMath.DefaultRating;

    private static ulong SteamIdOf(CCSPlayerController? player) => player?.AuthorizedSteamID?.SteamId64 ?? 0UL;

    private void EloRecordJoinName(CCSPlayerController player)
    {
        var id = SteamIdOf(player);
        var name = player.PlayerName?.Trim() ?? string.Empty;
        if (id == 0 || name.Length == 0) return;
        var entry = GetOrCreateEloPlayer(id);
        var now = DateTime.UtcNow;
        var known = entry.JoinNames.FirstOrDefault(n => n.Name == name);
        if (known is null)
        {
            entry.JoinNames.Add(new EloNameRecord { Name = name, FirstSeenUtc = now, LastSeenUtc = now });
            if (entry.JoinNames.Count > EloNameHistoryMax) entry.JoinNames.RemoveAt(0);
        }
        else
        {
            known.LastSeenUtc = now;
        }
        SaveElo("join_name");
    }

    // "(Nickname) LiveName" when an admin set a nickname, else the live or
    // last known join name.
    private string EloDisplayName(ulong steamId64)
    {
        var live = Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && SteamIdOf(p) == steamId64)?.PlayerName;
        var entry = FindEloPlayer(steamId64);
        var name = live
            ?? entry?.JoinNames.OrderByDescending(n => n.LastSeenUtc).FirstOrDefault()?.Name
            ?? _statsBySteamId.GetValueOrDefault(steamId64)?.Name
            ?? steamId64.ToString();
        return string.IsNullOrWhiteSpace(entry?.Nickname) ? name : $"({entry!.Nickname}) {name}";
    }

    // ---------------------------------------------------------------- match

    // Called from StartMatch. The roster at kickoff decides the rated format.
    private void EloOnMatchStart()
    {
        EloCancelSwapVote();
        _eloRoster.Clear();
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot || player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)) continue;
            var id = SteamIdOf(player);
            if (id != 0) _eloRoster[id] = player.Team;
        }
        var ct = _eloRoster.Values.Count(t => t == CsTeam.CounterTerrorist);
        var t = _eloRoster.Values.Count(t => t == CsTeam.Terrorist);
        _eloMatchRated = EloMath.IsRatedRoster(ct, t);
        if (!_matchWasCap) _eloProtected.Clear();
        AnnounceAll(_eloMatchRated
            ? $" \x04[ELO]\x01 Ranked match ({ct}v{t}) - ratings change at full time."
            : $" \x04[ELO]\x01 Unranked match ({ct}v{t}) - only 5v5 and 6v6 are rated.");
        Logger.LogInformation("[SM2DIAG] elo_match_start ct={Ct} t={T} rated={Rated}", ct, t, _eloMatchRated);
    }

    // Called from FinishMatch before the per-match stats are folded away.
    private void EloOnMatchFinished(bool forfeit)
    {
        EloCancelSwapVote();
        var rated = _eloMatchRated;
        _eloMatchRated = false;
        _eloProtected.Clear();
        _eloFirstPickTaken.Clear();
        if (!rated) return;
        if (forfeit || _scoreCt == _scoreT)
        {
            AnnounceAll($" \x04[ELO]\x01 {(forfeit ? "Forfeit" : "Draw")} - ratings unchanged.");
            Logger.LogInformation("[SM2DIAG] elo_match_unrated reason={Reason}", forfeit ? "forfeit" : "draw");
            return;
        }

        // Scores follow the squads across the halftime swap; convert the
        // winning engine team back to a kickoff side.
        var winningTeam = _scoreCt > _scoreT ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
        var winningKickoffSide = _teamsSwapped ? OppositeTeam(winningTeam) : winningTeam;
        var winners = _eloRoster.Where(r => r.Value == winningKickoffSide).Select(r => r.Key).ToList();
        var losers = _eloRoster.Where(r => r.Value != winningKickoffSide).Select(r => r.Key).ToList();
        if (winners.Count == 0 || losers.Count == 0) return;

        var winAverage = winners.Average(EloRating);
        var loseAverage = losers.Average(EloRating);
        EloApplyTeam(winners, winAverage, loseAverage, true);
        EloApplyTeam(losers, loseAverage, winAverage, false);
        SaveElo("match_finished");
        Logger.LogInformation("[SM2DIAG] elo_match_rated winners={Winners} losers={Losers} winAvg={WinAvg:F1} loseAvg={LoseAvg:F1}",
            winners.Count, losers.Count, winAverage, loseAverage);
    }

    private void EloApplyTeam(List<ulong> team, double teamAverage, double opponentAverage, bool won)
    {
        var lines = team.Select(id => _statsBySteamId.GetValueOrDefault(id)?.Match ?? new StatLine()).ToList();
        var deltas = EloMath.TeamDeltas(teamAverage, opponentAverage, won, lines.Select(l => (double)l.Points).ToList());
        for (var i = 0; i < team.Count; i++)
        {
            var entry = GetOrCreateEloPlayer(team[i]);
            var before = entry.Rating;
            entry.Rating = before + deltas[i];
            entry.Games++;
            if (won) entry.Wins++; else entry.Losses++;
            entry.PointsTotal += lines[i].Points;
            var career = entry.Career;
            career.GamesRated++;
            career.Goals += lines[i].Goals;
            career.Assists += lines[i].Assists;
            career.OwnGoals += lines[i].OwnGoals;
            career.Saves += lines[i].Saves;
            career.Passes += lines[i].Passes;
            career.Interceptions += lines[i].Interceptions;
            career.Hits += lines[i].Hits;
            _eloStore.Log.Add(new EloRatingChange { Utc = DateTime.UtcNow, SteamId64 = team[i], Before = before, After = entry.Rating, Reason = won ? "match win" : "match loss" });
            var delta = deltas[i];
            if (Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && SteamIdOf(p) == team[i]) is { } online)
            {
                online.PrintToChat($" \x04[ELO]\x01 {(won ? "Win" : "Loss")}: {before:F0} -> {entry.Rating:F0} ({(delta >= 0 ? "+" : "")}{delta:F0})");
            }
        }
    }

    private static CsTeam OppositeTeam(CsTeam team) =>
        team == CsTeam.Terrorist ? CsTeam.CounterTerrorist : team == CsTeam.CounterTerrorist ? CsTeam.Terrorist : team;

    // An AFK kick during a match voids its rating (SoMoE afkkicker.sp hook).
    private void EloInvalidateMatch(string reason)
    {
        if (!_eloMatchRated) return;
        _eloMatchRated = false;
        AnnounceAll($" \x04[ELO]\x01 This match is no longer rated ({reason}).");
        Logger.LogInformation("[SM2DIAG] elo_match_invalidated reason={Reason}", reason);
    }

    private void EloOnMatchStopped()
    {
        EloCancelSwapVote();
        _eloMatchRated = false;
        _eloProtected.Clear();
        _eloFirstPickTaken.Clear();
    }

    // ------------------------------------------------------------------ cap

    // Called from CapStartFight with the fighters (one captain candidate per
    // side). Returns the team that picks first when ELO decides it, or null
    // when the cap fight should decide.
    private CsTeam? EloDecideFirstPick(List<CCSPlayerController> fighters)
    {
        var ct = fighters.Where(p => p.Team == CsTeam.CounterTerrorist).ToList();
        var t = fighters.Where(p => p.Team == CsTeam.Terrorist).ToList();
        if (ct.Count != 1 || t.Count != 1 || SteamIdOf(ct[0]) == 0 || SteamIdOf(t[0]) == 0) return null;

        var ratingCt = EloRating(SteamIdOf(ct[0]));
        var ratingT = EloRating(SteamIdOf(t[0]));
        var gap = EloMath.GapPercent(ratingCt, ratingT);
        var threshold = _eloStore.Settings.FirstPickGapPercent;
        if (gap <= threshold)
        {
            CapAnnounce($"CT ELO {ratingCt:F0} vs T ELO {ratingT:F0} ({gap:F1}% apart, within {threshold:F1}%) - the cap fight decides the first pick.");
            return null;
        }

        _capCT = ct[0].Slot;
        _capT = t[0].Slot;
        var firstTeam = ratingCt < ratingT ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
        CapAnnounce($"CT ELO {ratingCt:F0} vs T ELO {ratingT:F0} - the {(firstTeam == CsTeam.CounterTerrorist ? "CT" : "T")} cap has the lower ELO and picks first.");
        Logger.LogInformation("[SM2DIAG] elo_first_pick ct={Ct:F0} t={T:F0} gap={Gap:F1} first={First}", ratingCt, ratingT, gap, firstTeam);
        return firstTeam;
    }

    // Called when the draft starts (EndCapFight with a winner).
    private void EloOnDraftStart()
    {
        _eloProtected.Clear();
        _eloFirstPickTaken.Clear();
        foreach (var slot in new[] { _capT, _capCT })
        {
            var id = SteamIdOf(Utilities.GetPlayerFromSlot(slot));
            if (id != 0) _eloProtected.Add(id);
        }
    }

    private void EloOnCapPick(CsTeam team, ulong targetId)
    {
        if (targetId != 0 && _eloFirstPickTaken.Add(team)) _eloProtected.Add(targetId);
    }

    private string EloPickLabel(ulong steamId64) =>
        steamId64 == 0 ? string.Empty : $" ({EloRating(steamId64):F0})";

    // ------------------------------------------------------- halftime vote

    // Called when the first period ends (not before golden goal).
    private void EloOnHalftime(float breakSeconds)
    {
        if (!_eloMatchRated && _eloRoster.Count == 0) return;
        var gap = Math.Abs(_scoreCt - _scoreT);
        if (gap < _eloStore.Settings.SwapGoalGap) return;

        var ct = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && p.Team == CsTeam.CounterTerrorist && SteamIdOf(p) != 0).ToList();
        var t = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && p.Team == CsTeam.Terrorist && SteamIdOf(p) != 0).ToList();
        if (ct.Count == 0 || t.Count == 0) return;

        bool Allowed(int i, int j)
        {
            var a = SteamIdOf(ct[i]);
            var b = SteamIdOf(t[j]);
            return !_eloProtected.Contains(a) && !_eloProtected.Contains(b) && EloPositionsOverlap(a, b);
        }
        var best = EloMath.BestSwaps(ct.Select(p => EloRating(SteamIdOf(p))).ToList(), t.Select(p => EloRating(SteamIdOf(p))).ToList(), Allowed);
        if (best.Count == 0)
        {
            Logger.LogInformation("[SM2DIAG] elo_swap_none gap={Gap}", gap);
            return;
        }

        _eloSwapOptions.Clear();
        _eloSwapVotes.Clear();
        foreach (var option in best) _eloSwapOptions.Add((SteamIdOf(ct[option.A]), SteamIdOf(t[option.B])));
        _eloSwapVoteActive = true;
        var seconds = Math.Clamp(breakSeconds - 2.0f, 5.0f, EloSwapVoteSeconds);
        AnnounceAll($" \x04[ELO]\x01 Score gap is {gap} - players on the pitch vote on a swap to rebalance the teams ({seconds:F0}s).");
        foreach (var player in ct.Concat(t)) EloOpenSwapVote(player);
        _eloSwapTimer = AddTimer(seconds, EloTallySwapVote, TimerFlags.STOP_ON_MAPCHANGE);
        Logger.LogInformation("[SM2DIAG] elo_swap_vote_start gap={Gap} options={Options}", gap, best.Count);
    }

    // Positions from !pos grouped like SoMoE's gk/def/mid/wing flags.
    private bool EloPositionsOverlap(ulong a, ulong b)
    {
        static bool[] Groups(CapPositionEntry? e) => e is null
            ? new bool[4]
            : new[] { e.Gk, e.Lb || e.Rb, e.Mf, e.Lw || e.Rw };
        var ga = Groups(FindCapPositions(a));
        var gb = Groups(FindCapPositions(b));
        return Enumerable.Range(0, 4).Any(i => ga[i] && gb[i]);
    }

    private void EloOpenSwapVote(CCSPlayerController player)
    {
        if (!_eloSwapVoteActive) return;
        var menu = new NumberMenu { Title = "Swap players to rebalance the teams?" };
        for (var i = 0; i < _eloSwapOptions.Count; i++)
        {
            var index = i;
            var (a, b) = _eloSwapOptions[i];
            menu.Add($"Swap {EloDisplayName(a)} (CT) <-> {EloDisplayName(b)} (T)", p => EloCastSwapVote(p, index));
        }
        menu.Add("Keep teams as they are", p => EloCastSwapVote(p, -1));
        OpenNumberMenu(player, menu);
    }

    private void EloCastSwapVote(CCSPlayerController player, int option)
    {
        var id = SteamIdOf(player);
        if (!_eloSwapVoteActive || id == 0 || _eloSwapVotes.ContainsKey(id)) return;
        _eloSwapVotes[id] = option;
        player.PrintToChat(" \x04[ELO]\x01 Vote registered.");
        CloseMenu(player.Slot, "elo_vote");
    }

    private void EloTallySwapVote()
    {
        _eloSwapTimer = null;
        if (!_eloSwapVoteActive) return;
        _eloSwapVoteActive = false;
        var counts = Enumerable.Range(0, _eloSwapOptions.Count).Select(i => _eloSwapVotes.Values.Count(v => v == i)).ToList();
        var keep = _eloSwapVotes.Values.Count(v => v == -1);
        var result = EloMath.TallySwapVote(counts, keep);
        Logger.LogInformation("[SM2DIAG] elo_swap_vote_end votes={Votes} keep={Keep} result={Result}", string.Join(",", counts), keep, result);
        if (result < 0)
        {
            AnnounceAll(" \x04[ELO]\x01 Vote result: teams stay as they are.");
            return;
        }

        var (a, b) = _eloSwapOptions[result];
        var playerA = Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && SteamIdOf(p) == a);
        var playerB = Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && SteamIdOf(p) == b);
        if (playerA is null || playerB is null || playerA.Team == playerB.Team
            || playerA.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)
            || playerB.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            AnnounceAll(" \x04[ELO]\x01 Vote passed, but a player left - teams stay as they are.");
            return;
        }

        var teamA = playerA.Team;
        var teamB = playerB.Team;
        // Keep the cap draft enforcement and the rating roster in step.
        if (_draftAssignments.ContainsKey(a)) _draftAssignments[a] = teamB;
        if (_draftAssignments.ContainsKey(b)) _draftAssignments[b] = teamA;
        if (_eloRoster.ContainsKey(a) && _eloRoster.ContainsKey(b)) (_eloRoster[a], _eloRoster[b]) = (_eloRoster[b], _eloRoster[a]);
        playerA.SwitchTeam(teamB);
        playerB.SwitchTeam(teamA);
        AnnounceAll($" \x04[ELO]\x01 Vote passed - {playerA.PlayerName} and {playerB.PlayerName} swap teams.");
    }

    private void EloCancelSwapVote()
    {
        _eloSwapTimer?.Kill();
        _eloSwapTimer = null;
        _eloSwapVoteActive = false;
        _eloSwapOptions.Clear();
        _eloSwapVotes.Clear();
    }

    // ---------------------------------------------------------------- menus

    private void OnEloCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            var top = _eloStore.Players.Where(p => p.Games > 0).OrderByDescending(p => p.Rating).Take(10).ToList();
            command.ReplyToCommand($"[SM] ELO: {_eloStore.Players.Count(p => p.Games > 0)} rated players, firstpickgap={_eloStore.Settings.FirstPickGapPercent:F1}% swapgap={_eloStore.Settings.SwapGoalGap}");
            for (var i = 0; i < top.Count; i++) command.ReplyToCommand($"[SM] {i + 1}. {EloDisplayName(top[i].SteamId64)} {top[i].Rating:F0} ({top[i].Wins}-{top[i].Losses})");
            return;
        }
        OpenEloMenu(player);
    }

    private void OpenEloMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - ELO Ranking", OnBack = OpenMainMenu };
        menu.Add("Ranked leaderboard (5v5/6v6)", p => OpenEloLeaderboard(p, true));
        menu.Add("Unranked leaderboard (all-time points)", p => OpenEloLeaderboard(p, false));
        menu.Add("My card", p => { var id = SteamIdOf(p); if (id != 0) OpenEloCard(p, id, true, OpenEloMenu); });
        if (HasFlag(SteamIdOf(player), "admin"))
        {
            menu.Add("Admin: rename a player's stat name", p => OpenEloPlayerPicker(p, "Admin - Rename", EloBeginRename));
            menu.Add("Admin: adjust a player's ELO", p => OpenEloPlayerPicker(p, "Admin - Adjust ELO", (q, id) => OpenEloAdjust(q, id)));
        }
        OpenNumberMenu(player, menu);
    }

    private List<ulong> EloRankedOrder() => _eloStore.Players
        .Where(p => p.Games > 0)
        .OrderByDescending(p => p.Rating).ThenBy(p => p.SteamId64)
        .Select(p => p.SteamId64).ToList();

    private List<ulong> EloUnrankedOrder() => _statsStore.Entries
        .Where(e => e.Public.Hits > 0)
        .OrderByDescending(e => e.Public.Points).ThenBy(e => e.SteamId64)
        .Select(e => e.SteamId64).ToList();

    private void OpenEloLeaderboard(CCSPlayerController player, bool ranked)
    {
        var order = ranked ? EloRankedOrder() : EloUnrankedOrder();
        var menu = new NumberMenu { Title = ranked ? "ELO - Ranked leaderboard" : "ELO - Unranked leaderboard", OnBack = OpenEloMenu };
        if (order.Count == 0) menu.AddInfo(ranked ? "No rated players yet" : "No stats yet");
        for (var i = 0; i < order.Count && i < 100; i++)
        {
            var id = order[i];
            string label;
            if (ranked)
            {
                var entry = FindEloPlayer(id)!;
                label = $"#{i + 1} {EloDisplayName(id)} - {entry.Rating:F0} ({entry.Wins}-{entry.Losses})";
            }
            else
            {
                label = $"#{i + 1} {EloDisplayName(id)} - {_statsBySteamId.GetValueOrDefault(id)?.Public.Points ?? 0} pts";
            }
            menu.Add(label, p => OpenEloCard(p, id, ranked, q => OpenEloLeaderboard(q, ranked)));
        }
        OpenNumberMenu(player, menu);
    }

    private readonly record struct EloCardStats(int Games, int Goals, int Assists, int Saves, int Passes, int Interceptions, int Hits);

    private static (double Shoot, double Pass, double Def, double Touch) EloCardValues(EloCardStats s) => (
        (double)s.Goals / s.Games,
        (s.Assists * 3.0 + s.Passes) / s.Games,
        (s.Saves * 5.0 + s.Interceptions) / s.Games,
        (double)s.Hits / s.Games);

    private IEnumerable<(ulong Id, EloCardStats Stats)> EloCardPool(bool ranked) => ranked
        ? _eloStore.Players.Where(p => p.Career.GamesRated > 0).Select(p => (p.SteamId64,
            new EloCardStats(p.Career.GamesRated, p.Career.Goals, p.Career.Assists, p.Career.Saves, p.Career.Passes, p.Career.Interceptions, p.Career.Hits)))
        : _statsStore.Entries.Where(e => e.Public.Hits > 0).Select(e => (e.SteamId64,
            new EloCardStats(Math.Max(1, e.Public.Matches), e.Public.Goals, e.Public.Assists, e.Public.Saves, e.Public.Passes, e.Public.Interceptions, e.Public.Hits)));

    private int[]? EloCardAttributes(ulong steamId64, bool ranked)
    {
        var pool = EloCardPool(ranked).ToList();
        var mine = pool.FirstOrDefault(p => p.Id == steamId64);
        if (mine.Id == 0) return null;
        var my = EloCardValues(mine.Stats);
        var others = pool.Where(p => p.Id != steamId64).Select(p => EloCardValues(p.Stats)).ToList();
        var total = others.Count;
        // Alone on the server: everybody else counts as below.
        int Rank(Func<(double Shoot, double Pass, double Def, double Touch), double> pick) =>
            total == 0 ? 99 : EloMath.PercentileAttribute(others.Count(o => pick(o) < pick(my)), total);
        return new[] { Rank(v => v.Shoot), Rank(v => v.Pass), Rank(v => v.Def), Rank(v => v.Touch) };
    }

    private void OpenEloCard(CCSPlayerController player, ulong steamId64, bool ranked, Action<CCSPlayerController> back)
    {
        var entry = FindEloPlayer(steamId64);
        var order = ranked ? EloRankedOrder() : EloUnrankedOrder();
        var rank = order.IndexOf(steamId64) + 1;
        var tier = EloMath.Tier(rank);
        var winRate = ranked && entry is { Games: > 0 } ? 40 + (int)Math.Round(100.0 * entry.Wins / entry.Games * 0.59) : 50;
        var attributes = EloCardAttributes(steamId64, ranked);
        var title = $"{EloDisplayName(steamId64)} ({(ranked ? "Ranked" : "Unranked")}) [{tier}]";
        var menu = new NumberMenu { Title = title, OnBack = back };
        if (attributes is { } a)
        {
            var overall = (int)Math.Round((a[0] + a[1] + a[2] + a[3] + winRate) / 5.0);
            menu.AddInfo($"OVR {overall} | SHO {a[0]} PAS {a[1]} DEF {a[2]} TCH {a[3]} WR {winRate}");
        }
        else
        {
            menu.AddInfo("(no career data yet)");
        }

        if (ranked)
        {
            var games = entry?.Games ?? 0;
            menu.AddInfo($"ELO {entry?.Rating ?? EloMath.DefaultRating:F0} | rank {(rank > 0 ? $"#{rank}" : "-")} | {entry?.Wins ?? 0}W-{entry?.Losses ?? 0}L");
            menu.AddInfo(games > 0 ? $"Avg points/game {entry!.PointsTotal / games:F1}" : "No rated games yet");
            if (entry?.Career is { GamesRated: > 0 } c)
                menu.AddInfo($"G {c.Goals} A {c.Assists} OG {c.OwnGoals} Sv {c.Saves} Pass {c.Passes} Int {c.Interceptions} Hits {c.Hits}");
            menu.Add("Unranked card", p => OpenEloCard(p, steamId64, false, back));
        }
        else
        {
            var s = _statsBySteamId.GetValueOrDefault(steamId64)?.Public;
            menu.AddInfo($"{s?.Points ?? 0} pts | rank {(rank > 0 ? $"#{rank}" : "-")} | matches {s?.Matches ?? 0}");
            if (s is not null)
                menu.AddInfo($"G {s.Goals} A {s.Assists} OG {s.OwnGoals} Sv {s.Saves} Pass {s.Passes} Int {s.Interceptions} Hits {s.Hits}");
            menu.Add("Ranked card", p => OpenEloCard(p, steamId64, true, back));
        }
        menu.Add("Join-name history", p => OpenEloNames(p, steamId64, false, q => OpenEloCard(q, steamId64, ranked, back)));
        menu.Add("Admin rename history", p => OpenEloNames(p, steamId64, true, q => OpenEloCard(q, steamId64, ranked, back)));
        OpenNumberMenu(player, menu);
    }

    private void OpenEloNames(CCSPlayerController player, ulong steamId64, bool nicknames, Action<CCSPlayerController> back)
    {
        var entry = FindEloPlayer(steamId64);
        var records = (nicknames ? entry?.NicknameHistory : entry?.JoinNames) ?? new List<EloNameRecord>();
        var menu = new NumberMenu { Title = nicknames ? "Admin rename history" : "Join-name history", OnBack = back };
        if (records.Count == 0) menu.AddInfo("Nothing recorded yet");
        foreach (var record in records.OrderByDescending(r => r.LastSeenUtc))
        {
            menu.AddInfo(nicknames
                ? $"{record.Name} - {record.FirstSeenUtc:yyyy-MM-dd} by {record.ChangedBy ?? "?"}"
                : $"{record.Name} - {record.FirstSeenUtc:yyyy-MM-dd} to {record.LastSeenUtc:yyyy-MM-dd}");
        }
        OpenNumberMenu(player, menu);
    }

    // Online players first, then offline players the ELO store knows.
    private void OpenEloPlayerPicker(CCSPlayerController player, string title, Action<CCSPlayerController, ulong> onPick)
    {
        if (!HasFlag(SteamIdOf(player), "admin")) return;
        var online = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && SteamIdOf(p) != 0).Select(SteamIdOf).ToList();
        var offline = _eloStore.Players.Select(p => p.SteamId64).Where(id => !online.Contains(id))
            .OrderByDescending(id => FindEloPlayer(id)!.JoinNames.Select(n => n.LastSeenUtc).DefaultIfEmpty().Max()).ToList();
        var menu = new NumberMenu { Title = $"ELO - {title}", OnBack = OpenEloMenu };
        foreach (var id in online.Concat(offline).Take(100))
        {
            var target = id;
            menu.Add($"{EloDisplayName(id)} ({EloRating(id):F0}){(online.Contains(id) ? "" : " [offline]")}", p => onPick(p, target));
        }
        OpenNumberMenu(player, menu);
    }

    private void EloBeginRename(CCSPlayerController admin, ulong steamId64)
    {
        if (!HasFlag(SteamIdOf(admin), "admin")) return;
        CloseMenu(admin.Slot, "elo_rename");
        BeginChatTextInput(admin, $"Type the new stat name for {EloDisplayName(steamId64)} in chat (\"-\" clears it, cancel to stop).", (p, text) =>
        {
            if (!HasFlag(SteamIdOf(p), "admin")) return;
            var entry = GetOrCreateEloPlayer(steamId64);
            var name = text.Trim();
            entry.Nickname = name == "-" ? null : name[..Math.Min(name.Length, 32)];
            var now = DateTime.UtcNow;
            entry.NicknameHistory.Add(new EloNameRecord { Name = entry.Nickname ?? "(cleared)", FirstSeenUtc = now, LastSeenUtc = now, ChangedBy = p.PlayerName });
            if (entry.NicknameHistory.Count > EloNameHistoryMax) entry.NicknameHistory.RemoveAt(0);
            SaveElo("admin_rename");
            p.PrintToChat($" \x04[ELO]\x01 Stat name is now: {EloDisplayName(steamId64)}");
            Logger.LogInformation("[SM2DIAG] elo_rename by={By} target={Target} name={Name}", p.PlayerName, steamId64, entry.Nickname ?? "(cleared)");
        });
    }

    private void OpenEloAdjust(CCSPlayerController admin, ulong steamId64)
    {
        if (!HasFlag(SteamIdOf(admin), "admin")) return;
        var menu = new NumberMenu
        {
            Title = $"Adjust ELO: {EloDisplayName(steamId64)} ({EloRating(steamId64):F0})",
            Key = "elo_adjust",
            OnBack = p => OpenEloPlayerPicker(p, "Admin - Adjust ELO", (q, id) => OpenEloAdjust(q, id)),
        };
        foreach (var step in new[] { 100, 50, 25, -25, -50, -100 })
        {
            var amount = step;
            menu.Add(amount > 0 ? $"+{amount}" : $"{amount}", p => EloAdjust(p, steamId64, amount));
        }
        OpenNumberMenu(admin, menu);
    }

    private void EloAdjust(CCSPlayerController admin, ulong steamId64, int amount)
    {
        if (!HasFlag(SteamIdOf(admin), "admin")) return;
        var entry = GetOrCreateEloPlayer(steamId64);
        var before = entry.Rating;
        entry.Rating += amount;
        _eloStore.Log.Add(new EloRatingChange { Utc = DateTime.UtcNow, SteamId64 = steamId64, Before = before, After = entry.Rating, Reason = $"admin {admin.PlayerName}" });
        SaveElo("admin_adjust");
        Logger.LogInformation("[SM2DIAG] elo_adjust by={By} target={Target} before={Before:F0} after={After:F0}", admin.PlayerName, steamId64, before, entry.Rating);
        OpenEloAdjust(admin, steamId64);
    }

    private void OnEloConfigCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin")) return;
        var settings = _eloStore.Settings;
        if (command.ArgCount >= 3)
        {
            var key = command.GetArg(1).ToLowerInvariant();
            var raw = command.GetArg(2).Replace(',', '.');
            if (key == "firstpickgap" && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct) && pct is >= 0 and <= 100)
                settings.FirstPickGapPercent = pct;
            else if (key == "swapgap" && int.TryParse(raw, out var goals) && goals is >= 1 and <= 99)
                settings.SwapGoalGap = goals;
            else
            {
                command.ReplyToCommand("[SM] Usage: css_sm2elo_config firstpickgap <0-100> | swapgap <1-99>");
                return;
            }
            SaveElo("config");
        }
        command.ReplyToCommand($"[SM] ELO firstpickgap={settings.FirstPickGapPercent:F1}% swapgap={settings.SwapGoalGap} (usage: css_sm2elo_config firstpickgap <0-100> | swapgap <1-99>)");
    }
}

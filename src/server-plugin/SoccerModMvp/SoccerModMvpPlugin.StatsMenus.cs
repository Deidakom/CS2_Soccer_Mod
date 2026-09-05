using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private sealed class StatsChatPreferences
    {
        public bool Enabled { get; set; }
        public int Audience { get; set; } // 0 self, 1 team, 2 everyone
        public bool Passes { get; set; } = true;
        public bool Saves { get; set; } = true;
        public bool Losses { get; set; } = true;
    }
    private readonly Dictionary<CsTeam, StatLine> _teamMatchStats = new();
    private readonly Dictionary<CsTeam, StatLine> _teamRoundStats = new();
    private readonly Dictionary<ulong, double> _statsChatNext = new();
    private static StatLine TeamStats(Dictionary<CsTeam, StatLine> table, CsTeam team)
    {
        if (!table.TryGetValue(team, out var stats)) table[team] = stats = new();
        return stats;
    }
    private void StatsChatEvent(CCSPlayerController actor, string kind, string description)
    {
        foreach (var recipient in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot))
        {
            var id = recipient.AuthorizedSteamID?.SteamId64 ?? 0;
            if (!_statsBySteamId.TryGetValue(id, out var entry)) continue;
            var pref = entry.Chat;
            if (!pref.Enabled || (pref.Audience == 0 && recipient != actor)
                || (pref.Audience == 1 && recipient.Team != actor.Team)
                || kind == "pass" && !pref.Passes || kind == "save" && !pref.Saves || kind == "loss" && !pref.Losses
                || _statsChatNext.GetValueOrDefault(id) > Server.TickedTime) continue;
            _statsChatNext[id] = Server.TickedTime + 1;
            recipient.PrintToChat(FormatSoccerModMessage($"{actor.PlayerName} {description}."));
        }
    }
    private void OpenRankingMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Ranking", OnBack = OpenMainMenu };
        menu.Add("Competitive Top 50", p => OpenRankingTable(p, true, _menuParity.RankMode));
        menu.Add("My competitive history", p =>
        {
            var id = p.AuthorizedSteamID?.SteamId64 ?? 0;
            if (id != 0) OpenStatsDetail(p, p.PlayerName, GetOrCreateStatsEntry(id, p.PlayerName).Competitive, OpenRankingMenu);
        });
        menu.Add("Last connected", OpenLastConnectedMenu);
        menu.Add("Public Top 50", p => OpenRankingTable(p, false, _menuParity.RankMode));
        menu.Add("This match", p => OpenStatsPlayers(p, false));
        menu.Add("Reset my rankings", OpenPersonalRankReset);
        OpenNumberMenu(player, menu);
    }
    private void OpenRankingTable(CCSPlayerController player, bool competitive, int mode)
    {
        StatLine Pool(StatsEntry e) => competitive ? e.Competitive : e.Public;
        var labels = new[] { "Total points", "Points / round", "Points / match" };
        var ranked = _statsStore.Entries.Where(e => Pool(e).Hits > 0
            && (mode != 1 || Pool(e).RoundsWon + Pool(e).RoundsLost > 0)
            && (mode != 2 || Pool(e).Matches > 0))
            .OrderByDescending(e => Pool(e).Score(mode)).ThenBy(e => e.SteamId64).ToList();
        var menu = new NumberMenu { Title = $"{(competitive ? "Competitive" : "Public")} Top 50", OnBack = OpenRankingMenu };
        menu.Add($"Mode: {labels[mode]}", p => OpenRankingTable(p, competitive, (mode + 1) % 3));
        var ownRank = ranked.FindIndex(e => e.SteamId64 == (player.AuthorizedSteamID?.SteamId64 ?? 0));
        menu.AddInfo(ownRank >= 0 ? $"Your rank: {ownRank + 1} / {ranked.Count}" : "No eligible record in this view");
        if (competitive) menu.AddInfo("Completed eligible matches since v1.4");
        for (var i = 0; i < Math.Min(50, ranked.Count); i++)
        {
            var entry = ranked[i];
            menu.Add($"{i + 1}. {entry.Name}: {Pool(entry).Score(mode):0.##}", p =>
                OpenStatsDetail(p, entry.Name, Pool(entry), actor => OpenRankingTable(actor, competitive, mode)));
        }
        OpenNumberMenu(player, menu);
    }
    private void OpenPersonalRankReset(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Reset MY rankings", OnBack = OpenRankingMenu };
        menu.AddInfo("Only your selected history is erased.");
        menu.Add("Public history...", p => ConfirmPersonalRankReset(p, false));
        menu.Add("Competitive history...", p => ConfirmPersonalRankReset(p, true));
        OpenNumberMenu(player, menu);
    }
    private void ConfirmPersonalRankReset(CCSPlayerController player, bool competitive)
    {
        var menu = new NumberMenu { Title = $"Erase my {(competitive ? "competitive" : "public")} history?", OnBack = OpenPersonalRankReset };
        menu.Add("Cancel", OpenPersonalRankReset);
        menu.Add("Confirm reset", actor =>
        {
            if (MatchRunning) { actor.PrintToChat(FormatSoccerModMessage("Reset is unavailable during a match.")); return; }
            var id = actor.AuthorizedSteamID?.SteamId64 ?? 0;
            if (!_statsBySteamId.TryGetValue(id, out var entry)) return;
            var before = competitive ? entry.Competitive : entry.Public;
            if (competitive) entry.Competitive = new(); else entry.Public = new();
            if (!(competitive ? SaveCompetitiveStats() : SaveJsonAtomic(StatsFileName, _statsStore)))
            {
                if (competitive) entry.Competitive = before; else entry.Public = before;
                actor.PrintToChat(FormatSoccerModMessage("Could not save reset; history retained."));
            }
            else actor.PrintToChat(FormatSoccerModMessage("Your selected ranking history was reset."));
            OpenRankingMenu(actor);
        });
        OpenNumberMenu(player, menu);
    }
    private void OpenStatisticsMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Statistics", OnBack = OpenMainMenu };
        menu.Add("Team CT", p => OpenStatsDetail(p, "CT this match", TeamStats(_teamMatchStats, CsTeam.CounterTerrorist), OpenStatisticsMenu));
        menu.Add("Team T", p => OpenStatsDetail(p, "T this match", TeamStats(_teamMatchStats, CsTeam.Terrorist), OpenStatisticsMenu));
        menu.Add("Player", p => OpenStatsPlayers(p, false));
        menu.Add("Current Round", p => OpenStatsPlayers(p, true));
        menu.Add("Current Match", p => OpenStatsDetail(p, "Current match", SumStats(_statsStore.Entries.Select(e => e.Current)), OpenStatisticsMenu));
        menu.Add("My public history", p =>
        {
            var id = p.AuthorizedSteamID?.SteamId64 ?? 0;
            if (id != 0) OpenStatsDetail(p, p.PlayerName, GetOrCreateStatsEntry(id, p.PlayerName).Public, OpenStatisticsMenu);
        });
        menu.Add("Chat Info", OpenStatsChatMenu);
        OpenNumberMenu(player, menu);
    }
    private static StatLine SumStats(IEnumerable<StatLine> lines)
    {
        var total = new StatLine(); foreach (var line in lines) total.Add(line); return total;
    }
    private void OpenStatsPlayers(CCSPlayerController player, bool round)
    {
        var menu = new NumberMenu { Title = round ? "Current round" : "Current match players", OnBack = OpenStatisticsMenu };
        menu.Add("Refresh", p => OpenStatsPlayers(p, round));
        if (round)
        {
            menu.Add("Team CT", p => OpenStatsDetail(p, "CT this round", TeamStats(_teamRoundStats, CsTeam.CounterTerrorist), a => OpenStatsPlayers(a, true)));
            menu.Add("Team T", p => OpenStatsDetail(p, "T this round", TeamStats(_teamRoundStats, CsTeam.Terrorist), a => OpenStatsPlayers(a, true)));
        }
        foreach (var entry in _statsStore.Entries.Where(e => (round ? e.Round : e.Current).Hits > 0).OrderBy(e => e.Name))
            menu.Add(entry.Name, p => OpenStatsDetail(p, entry.Name, round ? entry.Round : entry.Current, a => OpenStatsPlayers(a, round)));
        OpenNumberMenu(player, menu);
    }
    private void OpenStatsDetail(CCSPlayerController player, string title, StatLine stats, Action<CCSPlayerController> back)
    {
        var menu = new NumberMenu { Title = title, OnBack = back };
        menu.AddInfo($"Points: {stats.Points}");
        menu.AddInfo($"Possession: {stats.PossessionSeconds:0.0}s");
        menu.AddInfo($"Goals: {stats.Goals} | Assists: {stats.Assists}");
        menu.AddInfo($"Own goals: {stats.OwnGoals} | Saves: {stats.Saves}");
        menu.AddInfo($"Hits: {stats.Hits} | Passes: {stats.Passes}");
        menu.AddInfo($"Interceptions: {stats.Interceptions} | Losses: {stats.BallLosses}");
        menu.AddInfo($"Rounds won / lost: {stats.RoundsWon} / {stats.RoundsLost}");
        menu.AddInfo($"Matches: {stats.Matches} | MVP: {stats.Mvp} | MOTM: {stats.Motm}");
        OpenNumberMenu(player, menu);
    }
    private void OpenStatsChatMenu(CCSPlayerController player)
    {
        var id = player.AuthorizedSteamID?.SteamId64 ?? 0; if (id == 0) return;
        var entry = GetOrCreateStatsEntry(id, player.PlayerName);
        void Change(CCSPlayerController actor, Action<StatsChatPreferences> edit)
        {
            var before = System.Text.Json.JsonSerializer.Serialize(entry.Chat);
            edit(entry.Chat);
            if (!SaveJsonAtomic(StatsFileName, _statsStore)) entry.Chat = System.Text.Json.JsonSerializer.Deserialize<StatsChatPreferences>(before)!;
            OpenStatsChatMenu(actor);
        }
        var menu = new NumberMenu { Title = "Statistics - Chat Info", OnBack = OpenStatisticsMenu };
        menu.Add($"Extended chat: {OnOff(entry.Chat.Enabled)}", p => Change(p, s => s.Enabled = !s.Enabled));
        menu.Add($"Mode: {new[] { "My events", "My team", "Everyone" }[Math.Clamp(entry.Chat.Audience, 0, 2)]}", p => Change(p, s => s.Audience = (s.Audience + 1) % 3));
        menu.Add($"Passes: {OnOff(entry.Chat.Passes)}", p => Change(p, s => s.Passes = !s.Passes));
        menu.Add($"Saves: {OnOff(entry.Chat.Saves)}", p => Change(p, s => s.Saves = !s.Saves));
        menu.Add($"Ball losses: {OnOff(entry.Chat.Losses)}", p => Change(p, s => s.Losses = !s.Losses));
        OpenNumberMenu(player, menu);
    }
}

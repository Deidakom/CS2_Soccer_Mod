using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Port of SoMoE-19's referee.sp (2026-08-30 SoMoE reconstruction round).
// Yellow/red cards, persisted by SteamID64, enforced to spectator on every
// spawn while carded. A second yellow converts to red, same as the
// original. Score add/remove is folded in here too (was the referee
// menu's own submenu in SoMoE) - reuses the same _scoreCt/_scoreT +
// UpdateTeamScoreboard the goal flow already uses, so a ref-awarded goal
// looks identical to a real one on the scoreboard.
public sealed partial class SoccerModMvpPlugin
{
    private const string RefereeCardsFileName = "soccermod_referee_cards.json";

    private sealed class RefereeCardEntry
    {
        public ulong SteamId64 { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool Yellow { get; set; }
        public bool Red { get; set; }
    }

    private sealed class RefereeCardStore
    {
        public int Version { get; set; } = 1;
        public List<RefereeCardEntry> Cards { get; set; } = new();
    }

    private RefereeCardStore _refereeCardStore = new();

    private void RefereeOnLoad()
    {
        _refereeCardStore = LoadJsonOrNull<RefereeCardStore>(RefereeCardsFileName) ?? new RefereeCardStore();
        AddCommand("css_ref", "Match admin: open the referee menu (cards, score).", OnRefCommand);
        AddCommand("css_yellowcard", "Match admin: give a player a yellow card.", OnYellowCardCommand);
        AddCommand("css_redcard", "Match admin: give a player a red card.", OnRedCardCommand);
        AddCommand("css_uncard", "Match admin: clear a player's cards.", OnUncardCommand);
        AddCommand("css_uncardall", "Match admin: clear every card.", OnUncardAllCommand);
        AddCommand("css_refscore", "Match admin: add/remove a goal for ct|t.", OnRefScoreCommand);
    }

    private RefereeCardEntry? FindCard(ulong steamId64) =>
        _refereeCardStore.Cards.FirstOrDefault(c => c.SteamId64 == steamId64);

    private void SaveRefereeCards(string reason)
    {
        if (SaveJsonAtomic(RefereeCardsFileName, _refereeCardStore))
        {
            Logger.LogInformation("[SM2DIAG] referee_cards_saved reason={Reason} count={Count}", reason, _refereeCardStore.Cards.Count);
        }
    }

    // Called from the existing OnPlayerSpawn hook in the main file.
    private void RefereeEnforceOnSpawn(CCSPlayerController player)
    {
        var steamId = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        if (steamId == 0 || FindCard(steamId) is not { Red: true })
        {
            return;
        }

        player.ChangeTeam(CsTeam.Spectator);
        player.PrintToChat(" \x04[Referee]\x01 You have been put to spectator because you have a red card.");
    }

    private void GiveYellowCard(CCSPlayerController referee, CCSPlayerController target)
    {
        var steamId = target.AuthorizedSteamID?.SteamId64 ?? 0UL;
        if (steamId == 0)
        {
            return;
        }

        var card = FindCard(steamId);
        if (card is null)
        {
            card = new RefereeCardEntry { SteamId64 = steamId, Name = target.PlayerName };
            _refereeCardStore.Cards.Add(card);
        }

        if (card.Yellow)
        {
            card.Yellow = false;
            card.Red = true;
            target.ChangeTeam(CsTeam.Spectator);
            AnnounceAll($" \x04[Referee]\x01 {referee.PlayerName} has given a second yellow card to {target.PlayerName}.");
            AppendMatchLog($"Yellow-Red Card target={target.PlayerName} by={referee.PlayerName}");
        }
        else
        {
            card.Yellow = true;
            AnnounceAll($" \x04[Referee]\x01 {referee.PlayerName} has given a yellow card to {target.PlayerName}.");
            AppendMatchLog($"Yellow Card target={target.PlayerName} by={referee.PlayerName}");
        }

        SaveRefereeCards("yellow_card");
    }

    private void GiveRedCard(CCSPlayerController referee, CCSPlayerController target)
    {
        var steamId = target.AuthorizedSteamID?.SteamId64 ?? 0UL;
        if (steamId == 0)
        {
            return;
        }

        var card = FindCard(steamId);
        if (card is null)
        {
            card = new RefereeCardEntry { SteamId64 = steamId, Name = target.PlayerName };
            _refereeCardStore.Cards.Add(card);
        }

        card.Yellow = false;
        card.Red = true;
        target.ChangeTeam(CsTeam.Spectator);
        AnnounceAll($" \x04[Referee]\x01 {referee.PlayerName} has given a red card to {target.PlayerName}.");
        AppendMatchLog($"Red Card target={target.PlayerName} by={referee.PlayerName}");
        SaveRefereeCards("red_card");
    }

    private void OnRefCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        OpenRefereeMenu(player);
    }

    private void OpenRefereeMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Admin - Referee", OnBack = OpenAdminMenu };
        menu.Add("Yellow Card", OpenYellowCardPlayerMenu);
        menu.Add("Red Card", OpenRedCardPlayerMenu);
        menu.Add("Remove all cards", p =>
        {
            var count = _refereeCardStore.Cards.Count;
            _refereeCardStore.Cards.Clear();
            SaveRefereeCards("remove_all_menu");
            AnnounceAll($" \x04[Referee]\x01 {p.PlayerName} has removed all cards ({count}).");
        });
        menu.Add("Add goal CT", p => { _scoreCt++; UpdateTeamScoreboard(); AnnounceAll($" \x04[Referee]\x01 {p.PlayerName} has added a goal to counter-terrorists."); AppendMatchLog($"Referee add goal CT by={p.PlayerName}"); });
        menu.Add("Add goal T", p => { _scoreT++; UpdateTeamScoreboard(); AnnounceAll($" \x04[Referee]\x01 {p.PlayerName} has added a goal to terrorists."); AppendMatchLog($"Referee add goal T by={p.PlayerName}"); });
        menu.Add("Remove goal CT", p => { _scoreCt = Math.Max(0, _scoreCt - 1); UpdateTeamScoreboard(); AnnounceAll($" \x04[Referee]\x01 {p.PlayerName} has removed a goal from counter-terrorists."); AppendMatchLog($"Referee remove goal CT by={p.PlayerName}"); });
        menu.Add("Remove goal T", p => { _scoreT = Math.Max(0, _scoreT - 1); UpdateTeamScoreboard(); AnnounceAll($" \x04[Referee]\x01 {p.PlayerName} has removed a goal from terrorists."); AppendMatchLog($"Referee remove goal T by={p.PlayerName}"); });
        OpenNumberMenu(player, menu);
    }

    private void OpenYellowCardPlayerMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Referee - Yellow Card", OnBack = OpenRefereeMenu };
        foreach (var target in Utilities.GetPlayers().Where(t => t.IsValid && t.UserId is not null))
        {
            var steamId = target.AuthorizedSteamID?.SteamId64 ?? 0UL;
            var suffix = FindCard(steamId) is { Yellow: true } ? " (Yellow)" : "";
            var userId = target.UserId!.Value;
            menu.Add(target.PlayerName + suffix, p => p.ExecuteClientCommandFromServer($"css_yellowcard #{userId}"));
        }
        OpenNumberMenu(player, menu);
    }

    private void OpenRedCardPlayerMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Referee - Red Card", OnBack = OpenRefereeMenu };
        foreach (var target in Utilities.GetPlayers().Where(t => t.IsValid && t.UserId is not null))
        {
            var userId = target.UserId!.Value;
            menu.Add(target.PlayerName, p => p.ExecuteClientCommandFromServer($"css_redcard #{userId}"));
        }
        OpenNumberMenu(player, menu);
    }

    private void OnYellowCardCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        var target = ResolveTarget(command, 1, out var error);
        if (target is null)
        {
            command.ReplyToCommand($"[SM] {error}");
            return;
        }

        if (player is null)
        {
            // RCON: log-only attribution (no referee identity to announce as).
            GiveYellowCard(target, target);
            return;
        }

        GiveYellowCard(player, target);
    }

    private void OnRedCardCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        var target = ResolveTarget(command, 1, out var error);
        if (target is null)
        {
            command.ReplyToCommand($"[SM] {error}");
            return;
        }

        GiveRedCard(player ?? target, target);
    }

    private void OnUncardCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        var target = ResolveTarget(command, 1, out var error);
        if (target is null)
        {
            command.ReplyToCommand($"[SM] {error}");
            return;
        }

        var steamId = target.AuthorizedSteamID?.SteamId64 ?? 0UL;
        if (_refereeCardStore.Cards.RemoveAll(c => c.SteamId64 == steamId) > 0)
        {
            SaveRefereeCards("uncard_command");
            AnnounceAll($" \x04[Referee]\x01 {(player?.PlayerName ?? "Console")} has removed the card(s) from {target.PlayerName}.");
        }
    }

    private void OnUncardAllCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        var count = _refereeCardStore.Cards.Count;
        _refereeCardStore.Cards.Clear();
        SaveRefereeCards("uncard_all_command");
        command.ReplyToCommand($"[SM] removed {count} card(s)");
    }

    private void OnRefScoreCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        if (command.ArgCount < 3)
        {
            command.ReplyToCommand("[SM] usage: css_refscore <add|remove> <ct|t>");
            return;
        }

        var op = command.GetArg(1).ToLowerInvariant();
        var team = command.GetArg(2).ToLowerInvariant();
        var delta = op == "add" ? 1 : op == "remove" ? -1 : 0;
        if (delta == 0 || team is not ("ct" or "t"))
        {
            command.ReplyToCommand("[SM] usage: css_refscore <add|remove> <ct|t>");
            return;
        }

        if (team == "ct")
        {
            _scoreCt = Math.Max(0, _scoreCt + delta);
        }
        else
        {
            _scoreT = Math.Max(0, _scoreT + delta);
        }

        UpdateTeamScoreboard();
        AppendMatchLog($"Referee score {op} {team} by={player?.PlayerName ?? "RCON"}");
        command.ReplyToCommand($"[SM] score: {_teamNameCt} {_scoreCt} - {_scoreT} {_teamNameT}");
    }
}

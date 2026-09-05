using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

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
        _refereeCardStore = LoadJsonOrNull<RefereeCardStore>(RefereeCardsFileName) ?? new();
        AddCommand("css_ref", "Match admin: referee cards and score.", (p, c) => { if (p is not null) OpenRefereeMenu(p); });
        AddCommand("css_yellowcard", "Match admin: give a yellow card.", (p, c) => CardCommand(p, c, false));
        AddCommand("css_redcard", "Match admin: give a red card.", (p, c) => CardCommand(p, c, true));
        AddCommand("css_uncard", "Match admin: clear a player's cards.", (p, c) =>
        {
            if (!RequirePermission(p, c, "match")) return;
            var target = ResolveTarget(c, 1, out var error);
            if (target is null) { c.ReplyToCommand(error); return; }
            RemoveRefereeCard(p, target.AuthorizedSteamID?.SteamId64 ?? 0, null);
        });
        AddCommand("css_uncardall", "Match admin: clear all cards.", (p, c) =>
        {
            if (RequirePermission(p, c, "match")) RemoveAllRefereeCards(p);
        });
        AddCommand("css_refscore", "Match admin: add|remove ct|t, or reset.", OnRefScoreCommand);
        RegisterEventHandler<EventPlayerTeam>((e, info) =>
        {
            if (e.Userid is { IsValid: true } player)
            {
                var id = player.AuthorizedSteamID?.SteamId64 ?? 0;
                Server.NextFrame(() => { if (player.IsValid && player.AuthorizedSteamID?.SteamId64 == id) EnforceRedCard(player); });
            }
            return HookResult.Continue;
        });
    }
    private RefereeCardEntry? FindCard(ulong id) => _refereeCardStore.Cards.FirstOrDefault(c => c.SteamId64 == id);
    private bool HasRedCard(CCSPlayerController player) => player.IsValid && FindCard(player.AuthorizedSteamID?.SteamId64 ?? 0) is { Red: true };
    private bool RefereeAccess(CCSPlayerController? player)
    {
        if (player is null || player.IsValid && HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0, "match")) return true;
        if (player.IsValid) player.PrintToChat(FormatSoccerModMessage("You do not have referee permission."));
        return false;
    }
    private bool EnforceRedCard(CCSPlayerController player)
    {
        if (!HasRedCard(player)) return false;
        if (player.Team != CsTeam.Spectator) player.ChangeTeam(CsTeam.Spectator);
        return true;
    }
    private void RefereeEnforceOnSpawn(CCSPlayerController player)
    {
        if (EnforceRedCard(player)) player.PrintToChat(FormatSoccerModMessage("You must spectate while you have a red card."));
    }
    private bool MutateRefereeCards(Action mutation)
    {
        var before = System.Text.Json.JsonSerializer.Serialize(_refereeCardStore);
        mutation();
        if (SaveJsonAtomic(RefereeCardsFileName, _refereeCardStore)) return true;
        _refereeCardStore = System.Text.Json.JsonSerializer.Deserialize<RefereeCardStore>(before)!;
        return false;
    }
    private static bool ApplyCard(RefereeCardEntry card, bool red)
    {
        if (card.Red) return false;
        card.Red = red || card.Yellow;
        card.Yellow = !card.Red;
        return true;
    }
    private void CardCommand(CCSPlayerController? referee, CommandInfo command, bool red)
    {
        if (!RequirePermission(referee, command, "match")) return;
        var target = ResolveTarget(command, 1, out var error);
        if (target is null) { command.ReplyToCommand(error); return; }
        GiveRefereeCard(referee, target, red);
    }
    private void GiveRefereeCard(CCSPlayerController? referee, CCSPlayerController target, bool red)
    {
        if (!RefereeAccess(referee) || !target.IsValid || target.IsBot) return;
        var id = target.AuthorizedSteamID?.SteamId64 ?? 0;
        if (id == 0 || FindCard(id) is { Red: true }) return;
        var secondYellow = !red && FindCard(id) is { Yellow: true };
        if (!MutateRefereeCards(() =>
        {
            var card = FindCard(id);
            if (card is null) _refereeCardStore.Cards.Add(card = new() { SteamId64 = id });
            card.Name = target.PlayerName; ApplyCard(card, red);
        })) { referee?.PrintToChat(FormatSoccerModMessage("Could not save the card; unchanged.")); return; }
        EnforceRedCard(target);
        var kind = red ? "red card" : secondYellow ? "second yellow card" : "yellow card";
        AnnounceAll($"[Referee] {referee?.PlayerName ?? "Console"} has given {target.PlayerName} a {kind}.");
        AppendMatchLog($"Card {kind} target={target.PlayerName} by={referee?.PlayerName ?? "Console"}");
    }
    private void RemoveRefereeCard(CCSPlayerController? referee, ulong id, bool? red)
    {
        if (!RefereeAccess(referee) || FindCard(id) is not { } card || red is true && !card.Red || red is false && !card.Yellow) return;
        var name = card.Name;
        if (!MutateRefereeCards(() => _refereeCardStore.Cards.RemoveAll(c => c.SteamId64 == id))) return;
        AnnounceAll($"[Referee] {referee?.PlayerName ?? "Console"} removed {(red is null ? "the cards" : red.Value ? "the red card" : "the yellow card")} from {name}.");
        AppendMatchLog($"Card removed steamid={id} by={referee?.PlayerName ?? "Console"}");
    }
    private void RemoveAllRefereeCards(CCSPlayerController? referee)
    {
        if (!RefereeAccess(referee) || !MutateRefereeCards(() => _refereeCardStore.Cards.Clear())) return;
        AnnounceAll($"[Referee] {referee?.PlayerName ?? "Console"} removed all cards.");
        AppendMatchLog($"Cards cleared by={referee?.PlayerName ?? "Console"}");
    }
    private void OpenRefereeMenu(CCSPlayerController player)
    {
        if (!RefereeAccess(player)) return;
        var menu = new NumberMenu { Title = "Soccer Mod - Admin - Referee", OnBack = OpenAdminMenu };
        menu.Add("Yellow Card", p => OpenGiveCardMenu(p, false));
        menu.Add("Red Card", p => OpenGiveCardMenu(p, true));
        menu.Add("Remove yellow card", p => OpenRemoveCardMenu(p, false));
        menu.Add("Remove red card", p => OpenRemoveCardMenu(p, true));
        menu.Add("Remove all cards", p => { if (RefereeAccess(p)) { RemoveAllRefereeCards(p); OpenRefereeMenu(p); } });
        menu.Add("Score", OpenRefereeScoreMenu);
        OpenNumberMenu(player, menu);
    }
    private void OpenGiveCardMenu(CCSPlayerController player, bool red)
    {
        if (!RefereeAccess(player)) return;
        var menu = new NumberMenu { Title = red ? "Referee - Red Card" : "Referee - Yellow Card", OnBack = OpenRefereeMenu };
        foreach (var target in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !HasRedCard(p)))
        {
            var id = target.AuthorizedSteamID?.SteamId64 ?? 0; if (id == 0) continue;
            menu.Add(target.PlayerName + (FindCard(id) is { Yellow: true } ? " (Yellow)" : ""), actor =>
            {
                if (!RefereeAccess(actor)) return;
                var current = Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && p.AuthorizedSteamID?.SteamId64 == id);
                if (current is not null) GiveRefereeCard(actor, current, red);
                OpenGiveCardMenu(actor, red);
            });
        }
        OpenNumberMenu(player, menu);
    }
    private void OpenRemoveCardMenu(CCSPlayerController player, bool red)
    {
        if (!RefereeAccess(player)) return;
        var menu = new NumberMenu { Title = red ? "Referee - Remove red card" : "Referee - Remove yellow card", OnBack = OpenRefereeMenu };
        foreach (var card in _refereeCardStore.Cards.Where(c => red ? c.Red : c.Yellow))
        {
            var id = card.SteamId64;
            menu.Add($"{card.Name} ({id})", actor => { RemoveRefereeCard(actor, id, red); OpenRemoveCardMenu(actor, red); });
        }
        OpenNumberMenu(player, menu);
    }
    private void OpenRefereeScoreMenu(CCSPlayerController player)
    {
        if (!RefereeAccess(player)) return;
        var menu = new NumberMenu { Title = $"Referee - Score: {_scoreCt} - {_scoreT}", OnBack = OpenRefereeMenu };
        foreach (var team in new[] { "ct", "t" })
            foreach (var delta in new[] { 1, -1 })
                menu.Add($"{(delta > 0 ? "Add" : "Remove")} goal {team.ToUpperInvariant()}", actor =>
                { ChangeRefereeScore(actor, team, delta); OpenRefereeScoreMenu(actor); });
        menu.Add("Reset score", actor =>
        {
            if (!RefereeAccess(actor)) return;
            var confirm = new NumberMenu { Title = "Reset both scores to zero?", OnBack = OpenRefereeScoreMenu };
            confirm.Add("Confirm reset", p => { ChangeRefereeScore(p, "reset", 0); OpenRefereeScoreMenu(p); });
            OpenNumberMenu(actor, confirm);
        });
        OpenNumberMenu(player, menu);
    }
    private void ChangeRefereeScore(CCSPlayerController? referee, string team, int delta)
    {
        if (!RefereeAccess(referee)) return;
        if (team == "reset") _scoreCt = _scoreT = 0;
        else if (team == "ct") _scoreCt = Math.Max(0, _scoreCt + delta);
        else if (team == "t") _scoreT = Math.Max(0, _scoreT + delta);
        else return;
        UpdateTeamScoreboard(); UpdateHostname();
        AnnounceAll($"[Referee] {referee?.PlayerName ?? "Console"} updated the score: {_teamNameCt} {_scoreCt} - {_scoreT} {_teamNameT}.");
        AppendMatchLog($"Referee score team={team} delta={delta} score={_scoreCt}-{_scoreT} by={referee?.PlayerName ?? "Console"}");
    }
    private void OnRefScoreCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match")) return;
        if (command.ArgCount == 2 && command.GetArg(1) == "reset") { ChangeRefereeScore(player, "reset", 0); return; }
        var op = command.ArgCount > 1 ? command.GetArg(1).ToLowerInvariant() : "";
        var team = command.ArgCount > 2 ? command.GetArg(2).ToLowerInvariant() : "";
        if (op is not ("add" or "remove") || team is not ("ct" or "t"))
        { command.ReplyToCommand("Usage: css_refscore <add|remove> <ct|t>, or css_refscore reset"); return; }
        ChangeRefereeScore(player, team, op == "add" ? 1 : -1);
        command.ReplyToCommand($"[SM] score: {_scoreCt} - {_scoreT}");
    }
}

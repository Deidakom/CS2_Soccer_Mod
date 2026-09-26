using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CS2UIKit;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-26 owner: a soccer TAB board in the style of the match scoreboard,
// always (not only in a match): per team position, name, captain, goals,
// assists and saves, plus score and clock. CS2's own scoreboard cannot be
// switched off by a server, so this board is shown while the scoreboard key
// (IN_SCORE, bit 33 in hl2sdk-cs2 in_buttons.h) is held and is opaque and
// big enough to cover it. Layout: soccermod_tabboard.xml in the Workshop
// item. Admin: css_sm2tabboard on|off.
public sealed partial class SoccerModMvpPlugin
{
    internal const string TabBoardLayout = "panorama/layout/custom_game/soccermod_tabboard.xml";
    private const ulong ScoreboardButton = 1UL << 33;
    private const int TabBoardRows = 8;
    private Panel? _tabBoardPanel;
    private readonly HashSet<int> _tabBoardOpen = new();
    private readonly Dictionary<int, Dictionary<string, string>> _tabBoardSent = new();
    private double _nextTabBoardDraw;
    private bool _tabBoardSeenScoreKey;

    // After ClickMenuOnLoad: the UI kit must be initialised first.
    private void TabBoardOnLoad()
    {
        _tabBoardPanel = new Panel(TabBoardLayout, new PanelOptions { Root = "tb", ShownClass = "shown", CaptureInput = false });
        AddCommand("css_permpos", "Your permanent position on the TAB board: GK, DEF, MID, WING or off.", OnPermPosCommand);
        AddCommand("css_sm2tabboard", "Admin: SoccerMod TAB board over CS2's scoreboard (on|off).", OnTabBoardCommand);
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            // A round restart rebuilds the HUD entity without our texts.
            Server.NextFrame(() => { _tabBoardSent.Clear(); _tabBoardOpen.Clear(); });
            return HookResult.Continue;
        });
        RegisterListener<Listeners.OnClientDisconnect>(slot => { _tabBoardSent.Remove(slot); _tabBoardOpen.Remove(slot); });
    }

    private void OnTabBoardCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin")) return;
        var arg = command.ArgCount >= 2 ? command.GetArg(1).ToLowerInvariant() : "";
        if (arg is "on" or "off")
        {
            _menuParity.TabBoard = arg == "on";
            SaveJsonAtomic(MenuParityFile, _menuParity);
            if (!_menuParity.TabBoard) _tabBoardPanel?.HideAll();
            _tabBoardOpen.Clear();
            _tabBoardSent.Clear();
        }
        command.ReplyToCommand($"[SM] TAB board: {(_menuParity.TabBoard ? "on" : "off")} (usage: css_sm2tabboard <on|off>)");
    }

    // Every tick: open/close follows the key at once; texts are redrawn at
    // most 4x a second and only what changed is sent.
    private void TabBoardOnTick()
    {
        if (_tabBoardPanel is not { } panel || !_menuParity.TabBoard) return;
        var now = (double)Server.TickedTime;
        var redraw = now >= _nextTabBoardDraw;
        if (redraw) _nextTabBoardDraw = now + 0.25;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot) continue;
            if (((ulong)player.Buttons & ScoreboardButton) == 0)
            {
                if (_tabBoardOpen.Remove(player.Slot)) panel.Hide(player);
                continue;
            }
            if (!_tabBoardSeenScoreKey)
            {
                _tabBoardSeenScoreKey = true;
                Logger.LogInformation("[SM2DIAG] tabboard scoreboard key seen slot={Slot}", player.Slot);
            }
            var opened = false;
            if (!panel.IsOpen(player))
            {
                panel.Show(player);
                if (!panel.IsOpen(player)) continue; // layout entity not there yet
                _tabBoardSent.Remove(player.Slot);
                opened = true;
            }
            _tabBoardOpen.Add(player.Slot);
            if (redraw || opened) DrawTabBoard(player, panel);
        }
    }

    private void DrawTabBoard(CCSPlayerController viewer, Panel panel)
    {
        var (clock, period, _, _) = ScoreHudState(Server.TickedTime);
        if (!MatchRunning) { clock = "--:--"; period = "NO MATCH RUNNING"; }
        TabText(viewer, panel, "tb_red", MatchRuleMath.ScoreHudTeamName(_teamNameT, "RED"));
        TabText(viewer, panel, "tb_blue", MatchRuleMath.ScoreHudTeamName(_teamNameCt, "BLUE"));
        TabText(viewer, panel, "tb_score_red", _scoreT.ToString());
        TabText(viewer, panel, "tb_score_blue", _scoreCt.ToString());
        TabText(viewer, panel, "tb_clock", clock);
        TabText(viewer, panel, "tb_period", period);

        var players = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot).ToList();
        DrawTabBoardTeam(viewer, panel, "t", CsTeam.Terrorist, _capT, players);
        DrawTabBoardTeam(viewer, panel, "c", CsTeam.CounterTerrorist, _capCT, players);
        var spectators = players.Where(p => p.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)).Select(p => p.PlayerName).ToList();
        TabText(viewer, panel, "tb_spec", spectators.Count == 0 ? "Spectators: -" : "Spectators: " + string.Join(", ", spectators));
    }

    private void DrawTabBoardTeam(CCSPlayerController viewer, Panel panel, string prefix, CsTeam team, int captainSlot, List<CCSPlayerController> players)
    {
        TabText(viewer, panel, $"tb_{prefix}h_pos", "POS");
        TabText(viewer, panel, $"tb_{prefix}h_name", "PLAYER");
        TabText(viewer, panel, $"tb_{prefix}h_cap", "");
        TabText(viewer, panel, $"tb_{prefix}h_g", "G");
        TabText(viewer, panel, $"tb_{prefix}h_a", "A");
        TabText(viewer, panel, $"tb_{prefix}h_s", "SV");
        // Captain first, then the goalkeeper, then by name.
        var members = players.Where(p => p.Team == team)
            .OrderByDescending(p => p.Slot == captainSlot)
            .ThenByDescending(p => IsGkSlot(p.Slot, team))
            .ThenBy(p => p.PlayerName, StringComparer.OrdinalIgnoreCase)
            .Take(TabBoardRows).ToList();
        for (var i = 0; i < TabBoardRows; i++)
        {
            var row = $"tb_{prefix}{i}";
            if (i >= members.Count)
            {
                if (TabRemember(viewer.Slot, "#" + row, "empty")) panel.SetVariant(viewer, row, "tr-", "empty");
                continue;
            }
            var p = members[i];
            if (TabRemember(viewer.Slot, "#" + row, "used")) panel.SetVariant(viewer, row, "tr-", "used");
            var stats = SteamIdOf(p) is var id && id != 0 && _statsBySteamId.TryGetValue(id, out var entry) ? entry.Current : null;
            var position = TabBoardPosition(p);
            TabText(viewer, panel, row + "_pos", position);
            TabText(viewer, panel, row + "_name", p.PlayerName);
            TabText(viewer, panel, row + "_cap", p.Slot == captainSlot ? "C" : "");
            TabText(viewer, panel, row + "_g", (stats?.Goals ?? 0).ToString());
            TabText(viewer, panel, row + "_a", (stats?.Assists ?? 0).ToString());
            TabText(viewer, panel, row + "_s", (stats?.Saves ?? 0).ToString());
        }
    }

    private bool TabRemember(int slot, string key, string value)
    {
        if (!_tabBoardSent.TryGetValue(slot, out var sent)) _tabBoardSent[slot] = sent = new Dictionary<string, string>();
        if (sent.TryGetValue(key, out var old) && old == value) return false;
        sent[key] = value;
        return true;
    }

    private void TabText(CCSPlayerController viewer, Panel panel, string id, string text)
    {
        if (TabRemember(viewer.Slot, id, text)) panel.SetText(viewer, id, text);
    }
}

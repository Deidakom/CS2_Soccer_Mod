using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CS2UIKit;

namespace SoccerModMvp;

// 2026-09-25 owner: the grey centre-text score box and the plain "MATCH
// START" box become a match HUD like the sprint bar: a TV-style scoreboard at
// the top (team, score, clock, period, status pill) and short event banners
// (match start, goal, half-time, golden goal, full time). Layout:
// soccermod_scorebug.xml in the Workshop item. The old centre text stays as
// the fallback: css_sm2score_hud text.
public sealed partial class SoccerModMvpPlugin
{
    internal const string ScoreHudLayout = "panorama/layout/custom_game/soccermod_scorebug.xml";
    private const double ScoreHudBannerSeconds = 3.5;
    private const double ScoreHudFinalHoldSeconds = 8.0;
    private Panel? _scoreHudPanel;
    private readonly Dictionary<int, Dictionary<string, string>> _scoreHudSent = new();
    private readonly HashSet<int> _scoreHudShown = new();
    private double _nextScoreHudDraw;
    private double _scoreHudHoldUntil;
    private int _scoreHudBannerSerial;

    private bool ScoreHudPanorama => _menuParity.ScoreHudPanorama && _scoreHudPanel is not null;

    // After ClickMenuOnLoad: the UI kit must be initialised first.
    private void ScoreHudOnLoad()
    {
        _scoreHudPanel = new Panel(ScoreHudLayout, new PanelOptions { Root = "sm_hud", ShownClass = "shown", CaptureInput = false });
        AddCommand("css_sm2score_hud", "Admin: match HUD style (panorama|text).", OnScoreHudCommand);
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            // A round restart rebuilds the HUD entity without our classes.
            Server.NextFrame(ResetScoreHud);
            return HookResult.Continue;
        });
        RegisterListener<Listeners.OnClientDisconnect>(slot => { _scoreHudSent.Remove(slot); _scoreHudShown.Remove(slot); });
    }

    private void OnScoreHudCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin")) return;
        var arg = command.ArgCount >= 2 ? command.GetArg(1).ToLowerInvariant() : "";
        if (arg is "panorama" or "text")
        {
            _menuParity.ScoreHudPanorama = arg == "panorama";
            SaveJsonAtomic(MenuParityFile, _menuParity);
            if (!_menuParity.ScoreHudPanorama) _scoreHudPanel?.HideAll();
            ApplyNativeRoundClock();
            ResetScoreHud();
        }
        command.ReplyToCommand($"[SM] Match HUD: {(_menuParity.ScoreHudPanorama ? "panorama" : "text")} (usage: css_sm2score_hud <panorama|text>)");
    }

    private void ResetScoreHud()
    {
        _scoreHudSent.Clear();
        _scoreHudShown.Clear();
    }

    private (string Clock, string Period, string Status, string StatusClass) ScoreHudState(double now)
    {
        var remaining = _kickoffClockWaitingForBall ? _pausedRemainingSeconds : _periodEndsAtServerTime - now;
        var period = _inGoldenGoal ? "GOLDEN GOAL"
            : _matchPeriods == 2 ? (_matchPeriod == 1 ? "1ST HALF" : "2ND HALF")
            : $"PERIOD {_matchPeriod}/{_matchPeriods}";
        return _matchPhase switch
        {
            MatchPhase.Countdown => (MatchRuleMath.ScoreHudClock(_pausedRemainingSeconds > 0 ? _pausedRemainingSeconds : remaining), period, "KICKOFF", "kickoff"),
            MatchPhase.Live when _kickoffClockWaitingForBall => (MatchRuleMath.ScoreHudClock(remaining), period, "KICKOFF - CLOCK STARTS ON FIRST TOUCH", "kickoff"),
            MatchPhase.Live => (MatchRuleMath.ScoreHudClock(remaining), period, _stoppageActive ? "STOPPAGE TIME" : "LIVE", "live"),
            MatchPhase.GoalPause => (MatchRuleMath.ScoreHudClock(_pausedRemainingSeconds > 0 ? _pausedRemainingSeconds : remaining), period, "GOAL", "goal"),
            MatchPhase.PeriodBreak => (MatchRuleMath.ScoreHudClock(_phaseTransitionAtServerTime - now), _inGoldenGoal ? "GOLDEN GOAL" : "BREAK", _inGoldenGoal ? "GOLDEN GOAL NEXT" : "HALF-TIME", "break"),
            MatchPhase.Paused => (MatchRuleMath.ScoreHudClock(_pausedRemainingSeconds), period, "PAUSED", "paused"),
            _ => ("00:00", "FULL TIME", "FULL TIME", "final"),
        };
    }

    // Called every tick from OnTick; draws at most 4x a second and only
    // sends what changed per player.
    private void ScoreHudOnTick()
    {
        if (!ScoreHudPanorama) return;
        var now = (double)Server.TickedTime;
        if (now < _nextScoreHudDraw) return;
        _nextScoreHudDraw = now + 0.25;
        var visible = _menuParity.MatchInfo && (MatchRunning || now < _scoreHudHoldUntil);
        var (clock, period, status, statusClass) = ScoreHudState(now);
        var panel = _scoreHudPanel!;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot) continue;
            if (!visible)
            {
                if (_scoreHudShown.Remove(player.Slot)) panel.Hide(player);
                continue;
            }
            if (!panel.IsOpen(player))
            {
                panel.Show(player);
                if (!panel.IsOpen(player)) continue; // layout entity not there yet
                _scoreHudSent.Remove(player.Slot);
            }
            _scoreHudShown.Add(player.Slot);
            SendScoreHudText(player, "sm_team_red", MatchRuleMath.ScoreHudTeamName(_teamNameT, "RED"));
            SendScoreHudText(player, "sm_team_blue", MatchRuleMath.ScoreHudTeamName(_teamNameCt, "BLUE"));
            SendScoreHudText(player, "sm_score_red", _scoreT.ToString());
            SendScoreHudText(player, "sm_score_blue", _scoreCt.ToString());
            SendScoreHudText(player, "sm_clock", clock);
            SendScoreHudText(player, "sm_period", period);
            SendScoreHudText(player, "sm_status", status);
            if (Remember(player.Slot, "#status", statusClass)) panel.SetVariant(player, "sm_status", "st-", statusClass);
            var layout = ScoreHudCompact(player) ? "compact" : "full";
            if (Remember(player.Slot, "#layout", layout)) panel.SetVariant(player, "sm_hud", "ly-", layout);
        }
    }

    // 2026-09-26 owner: a second, compact scoreboard (score + clock, no team
    // names), chosen per player in !menu - Settings - Match.
    private bool ScoreHudCompact(CCSPlayerController player) =>
        _menuParity.ScoreHudCompact.TryGetValue(SteamIdOf(player), out var compact) ? compact : true; // compact is the default (owner 2026-09-26)

    private void SetScoreHudCompact(CCSPlayerController player, bool compact)
    {
        var id = SteamIdOf(player);
        if (id == 0) return;
        _menuParity.ScoreHudCompact[id] = compact;
        SaveJsonAtomic(MenuParityFile, _menuParity);
        _nextScoreHudDraw = 0;
    }

    private bool Remember(int slot, string key, string value)
    {
        if (!_scoreHudSent.TryGetValue(slot, out var sent)) _scoreHudSent[slot] = sent = new Dictionary<string, string>();
        if (sent.TryGetValue(key, out var old) && old == value) return false;
        sent[key] = value;
        return true;
    }

    private void SendScoreHudText(CCSPlayerController player, string id, string text)
    {
        if (Remember(player.Slot, id, text)) _scoreHudPanel!.SetText(player, id, text);
    }

    // A banner for everyone: main line, sub line, style (start, goal-red,
    // goal-blue, break, final). A newer banner replaces an older one.
    private void ShowScoreBanner(string main, string sub, string style, bool holdHud = false)
    {
        if (!ScoreHudPanorama) return;
        var now = (double)Server.TickedTime;
        if (holdHud) _scoreHudHoldUntil = now + ScoreHudFinalHoldSeconds;
        _nextScoreHudDraw = 0;
        ScoreHudOnTick(); // make sure the HUD is open before the banner
        var serial = ++_scoreHudBannerSerial;
        var panel = _scoreHudPanel!;
        foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && panel.IsOpen(p)))
        {
            panel.SetText(player, "sm_banner_main", main);
            panel.SetText(player, "sm_banner_sub", sub);
            panel.SetVariant(player, "sm_banner", "bn-", style);
            panel.SetClass(player, "sm_banner", "shown", true);
        }
        AddTimer((float)ScoreHudBannerSeconds, () =>
        {
            if (serial != _scoreHudBannerSerial || _scoreHudPanel is null) return;
            foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && _scoreHudPanel.IsOpen(p)))
                _scoreHudPanel.SetClass(player, "sm_banner", "shown", false);
        });
    }
}

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace SoccerModMvp;

// 2026-09-27 owner: every player chooses in !menu - Settings - Visuals
// between the SoccerMod scoreboard at the top (ScoreHud.cs) and CS2's own top
// bar, and between the SoccerMod TAB board (TabBoard.cs) and CS2's own
// scoreboard. Default: CS2 standard for both (owner, same day). For the CS2 top bar the round
// clock shows the match time during a match: the round time is re-stamped
// from the match clock 4x a second (mp_ignore_round_win_conditions 1 keeps
// the round from ending at 0:00) and sv_hide_roundtime_until_seconds is 0
// while a match runs. Players with the SoccerMod bar do not see it anyway -
// the bar covers CS2's.
public sealed partial class SoccerModMvpPlugin
{
    private const string HudPrefsFile = "soccermod_hud_prefs.json";

    private sealed class HudPrefs
    {
        public Dictionary<ulong, bool> CsTopBar { get; set; } = new();
        public Dictionary<ulong, bool> CsTabBoard { get; set; } = new();
    }

    private HudPrefs _hudPrefs = new();
    private CCSGameRulesProxy? _nativeClockRulesProxy;
    private int _nativeClockHideApplied = -1;

    private void HudChoiceOnLoad() => _hudPrefs = LoadJsonOrNull<HudPrefs>(HudPrefsFile) ?? new HudPrefs();

    private bool OwnTopBar(CCSPlayerController player) =>
        _hudPrefs.CsTopBar.TryGetValue(SteamIdOf(player), out var cs) && !cs;

    private bool OwnTabBoard(CCSPlayerController player) =>
        _hudPrefs.CsTabBoard.TryGetValue(SteamIdOf(player), out var cs) && !cs;

    private void SetOwnTopBar(CCSPlayerController player, bool own)
    {
        var id = SteamIdOf(player);
        if (id == 0) return;
        _hudPrefs.CsTopBar[id] = !own;
        SaveJsonAtomic(HudPrefsFile, _hudPrefs);
        _nextScoreHudDraw = 0;
    }

    private void SetOwnTabBoard(CCSPlayerController player, bool own)
    {
        var id = SteamIdOf(player);
        if (id == 0) return;
        _hudPrefs.CsTabBoard[id] = !own;
        SaveJsonAtomic(HudPrefsFile, _hudPrefs);
    }

    // Seconds the match clock shows right now (same cases as ScoreHudState).
    private double MatchClockSeconds(double now)
    {
        var remaining = _kickoffClockWaitingForBall ? _pausedRemainingSeconds : _periodEndsAtServerTime - now;
        return _matchPhase switch
        {
            MatchPhase.Countdown => _pausedRemainingSeconds > 0 ? _pausedRemainingSeconds : remaining,
            MatchPhase.Live => remaining,
            MatchPhase.GoalPause => _pausedRemainingSeconds > 0 ? _pausedRemainingSeconds : remaining,
            MatchPhase.PeriodBreak => _phaseTransitionAtServerTime - now,
            MatchPhase.Paused => _pausedRemainingSeconds,
            _ => 0,
        };
    }

    // From ScoreHudOnTick (4x a second).
    private void NativeMatchClockTick(double now)
    {
        var matchOn = _menuParity.ScoreHudPanorama && MatchRunning;
        // Outside a match CS2's clock stays hidden (the 60-minute round).
        var hide = _menuParity.ScoreHudPanorama && !matchOn ? 1 : 0;
        if (hide != _nativeClockHideApplied)
        {
            _nativeClockHideApplied = hide;
            Server.ExecuteCommand($"sv_hide_roundtime_until_seconds {hide}");
        }
        if (!matchOn) return;

        if (_nativeClockRulesProxy is not { IsValid: true })
            _nativeClockRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault(p => p.IsValid);
        if (_nativeClockRulesProxy?.GameRules is not { } rules) return;
        // A pending mp_restartgame owns the round timing until it has run.
        if (rules.RestartRoundTime >= Server.CurrentTime) return;
        var seconds = (int)Math.Ceiling(Math.Max(0.0, MatchClockSeconds(now)));
        rules.RoundTime = seconds;
        rules.RoundStartTime = Server.CurrentTime;
        Utilities.SetStateChanged(_nativeClockRulesProxy, "CCSGameRulesProxy", "m_pGameRules");
    }
}

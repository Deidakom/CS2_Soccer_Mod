using System.Globalization;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Match MVP: periods, pause/unpause, kickoff/goal reset, map reload/restart.
// State machine and goal-crossing detection are a C# port of the already
// validated src/ball-lab/core/match.js and goal.js (segment interpolation +
// lock-until-verified-reset, so a fast ball can't tunnel through the plane
// between ticks and a single goal can't double-count while the kickoff
// restart is in flight). SoMoE period defaults (globals.sp): 2 periods,
// 900s each, 60s half-time break - deliberately simplified from SoMoE
// itself: no stoppage time, no golden goal, no readycheck, no forfeit (all
// explicitly deferred per the MVP plan).
public sealed partial class SoccerModMvpPlugin
{
    private enum MatchPhase
    {
        Warmup,
        Countdown,
        Live,
        GoalPause,
        PeriodBreak,
        Paused,
        Finished,
    }

    // Goal geometry. First live test (2026-08-30) found the real net
    // backstop (a solid func_brush) at y=+-1460.47 - but that is where the
    // ball's SURFACE stops, not its centre: css_sm2ball_status reports the
    // ball's centre (AbsOrigin), and a sphere of BallCollisionRadius (18.8)
    // can only push its centre to within one radius of a solid surface, i.e.
    // ~1441.7. A goal plane AT 1460 could therefore never be reached and
    // never fired. Moved well inside that limit with margin. Which physical
    // end belongs to which team is a guess pending live confirmation -
    // css_sm2goal_swap flips it in one command without a redeploy.
    private const float GoalPlaneY = 1400.0f;
    private const float GoalCenterX = 0.0f;
    private float _goalHalfWidthX = 200.0f;
    private float _goalApertureMinZ = -32.0f;
    private float _goalApertureMaxZ = 120.0f;
    private bool _ctDefendsNegativeY = true;

    private const int DefaultMatchPeriods = 2;
    private const float DefaultPeriodLengthSeconds = 900.0f;
    private const float DefaultBreakLengthSeconds = 60.0f;
    private const float GoalPauseSeconds = 4.0f;
    private const float KickoffCountdownSeconds = 3.0f;

    private MatchPhase _matchPhase = MatchPhase.Warmup;
    private int _scoreCt;
    private int _scoreT;
    private int _matchPeriod = 1;
    private int _matchPeriods = DefaultMatchPeriods;
    private float _periodLengthSeconds = DefaultPeriodLengthSeconds;
    private float _breakLengthSeconds = DefaultBreakLengthSeconds;
    private bool _teamsSwapped;
    private bool _goalLocked;
    private double _periodEndsAtServerTime;
    private double _pausedRemainingSeconds;
    private double _phaseTransitionAtServerTime;
    private double _nextScoreboardUpdateTime;
    private int _lastKickerSlot = -1;
    private CsTeam _lastKickerTeam = CsTeam.None;

    // CS:S-parity plan Tier 1 additions.
    private bool _goldenGoalEnabled = true;
    private bool _inGoldenGoal;
    private const float GoldenGoalLengthSeconds = 300.0f;
    private string _teamNameCt = "Counter-Terrorists";
    private string _teamNameT = "Terrorists";
    private readonly HashSet<int> _readyPlayers = new();
    private readonly HashSet<int> _forfeitVotes = new();
    private CsTeam _forfeitVoteTeam = CsTeam.None;
    private const string MatchLogFileName = "soccermod_last_match.txt";

    // 2026-08-30 user request, SoMoE-flavoured "punishment" for conceding a
    // goal. Guaranteed-working half: CommitSuicide is a real CSSharp API.
    // Respawn-on-death is already on (mp_respawn_on_death_t/ct 1) and
    // mp_autokick 0 means no suicide-kick, so this is pure flavour on top
    // of the existing goal flow - it does not touch detection/scoring/the
    // kickoff restart.
    private bool _goalPunishEnabled = true;
    // Native CS2 round-win banner/music on every goal, via the REAL public
    // wrapper CCSGameRules.TerminateRound(delay, RoundEndReason) - found by
    // reflection this session (CCSGameRulesProxy.GameRules is a genuine
    // property returning CCSGameRules; TerminateRound is declared directly
    // on CCSGameRules, confirmed against this exact 1.0.373 build). This is
    // NOT a raw VirtualFunctions/memory-pointer call - deliberately avoided
    // that route after this session's CheckTransmit crash.
    // Defaults OFF until verified in-game with css_sm2goal_test + the user
    // watching for the banner, correct team credit, and exactly one round
    // restart. Requires mp_maxrounds 0 / mp_halftime 0 in
    // gamemode_casual_server.cfg first, or CS2's own match flow (halftime
    // swap, match end at maxrounds) will start fighting our match logic
    // once real round wins start happening.
    private bool _goalRoundWinEnabled;

    private string TeamName(CsTeam team) => team == CsTeam.CounterTerrorist ? _teamNameCt : _teamNameT;

    // Kickoff wall/possession (SoMoE "kickoffwall.sp"): after a kickoff, the
    // non-kicking team is held back in their own half until the kicking
    // team's own player touches the ball first (or a timeout elapses, so a
    // team that refuses to approach can't soft-lock the match forever).
    // Implemented as a soft rubber-band (teleport back on crossing) rather
    // than spawned wall geometry - functionally the same restriction.
    private const float KickoffWallTimeoutSeconds = 10.0f;
    private bool _kickoffRestrictionActive;
    private CsTeam _kickoffTeam = CsTeam.None;
    private double _kickoffRestrictionExpiresAt;
    private readonly Dictionary<int, int> _goalsBySlot = new();

    // 2026-08-30 user request: removed for now, not deleted - single gate
    // here so every call site (goal/kickoff/match-start) stays untouched;
    // nothing ever arms, so EnforceKickoffWall/ClearKickoffRestrictionOnTouch
    // become no-ops automatically. css_sm2kickoffwall <on|off> to bring it
    // back without a code change.
    private bool _kickoffWallEnabled;

    private void StartKickoffRestriction(CsTeam kickoffTeam)
    {
        if (!_kickoffWallEnabled)
        {
            return;
        }

        _kickoffTeam = kickoffTeam;
        _kickoffRestrictionActive = true;
        _kickoffRestrictionExpiresAt = Server.TickedTime + KickoffWallTimeoutSeconds;
        Logger.LogInformation("[SM2DIAG] kickoff_wall_start team={Team}", kickoffTeam);
    }

    // Called from the kick and push touch paths - the FIRST touch by the
    // kicking team's own player lifts the restriction immediately.
    private void ClearKickoffRestrictionOnTouch(CsTeam toucherTeam)
    {
        if (_kickoffRestrictionActive && toucherTeam == _kickoffTeam)
        {
            _kickoffRestrictionActive = false;
            Logger.LogInformation("[SM2DIAG] kickoff_wall_cleared reason=kicking_team_touch");
        }
    }

    private void EnforceKickoffWall()
    {
        if (!_kickoffRestrictionActive)
        {
            return;
        }

        if (Server.TickedTime >= _kickoffRestrictionExpiresAt)
        {
            _kickoffRestrictionActive = false;
            Logger.LogInformation("[SM2DIAG] kickoff_wall_cleared reason=timeout");
            return;
        }

        var blockedTeam = _kickoffTeam == CsTeam.Terrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
        var blockedTeamDefendsNegativeY = blockedTeam == CsTeam.CounterTerrorist ? _ctDefendsNegativeY : !_ctDefendsNegativeY;

        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.Team != blockedTeam)
            {
                continue;
            }

            var pawn = player.PlayerPawn.Value;
            if (pawn is not { IsValid: true } || !IsAlive(pawn) || pawn.AbsOrigin is not { } origin)
            {
                continue;
            }

            var crossedIntoOpponentHalf = blockedTeamDefendsNegativeY ? origin.Y > 0.0f : origin.Y < 0.0f;
            if (!crossedIntoOpponentHalf)
            {
                continue;
            }

            var clampedY = blockedTeamDefendsNegativeY ? MathF.Min(origin.Y, -10.0f) : MathF.Max(origin.Y, 10.0f);
            pawn.Teleport(
                position: new Vector(origin.X, clampedY, origin.Z),
                angles: pawn.AbsRotation,
                velocity: new Vector(0.0f, 0.0f, 0.0f));
        }
    }

    private void MatchOnLoad()
    {
        AddCommand("css_match", "Admin (match): start|stop|pause|unpause|status.", OnMatchCommand);
        AddCommand("css_rr", "Admin (match): restart the round without touching the match clock/score.", OnRoundRestartCommand);
        AddCommand("css_matchrr", "Admin (match): stop then start a fresh match.", OnMatchRestartCommand);
        AddCommand("css_maprr", "Admin: reload the workshop map (host_workshop_map, keeps addon context).", OnMapReloadCommand);
        AddCommand("css_sm2goal_calib", "Admin (match): set goal aperture half-width and max height.", OnGoalCalibCommand);
        AddCommand("css_sm2goal_swap", "Admin (match): flip which end is CT's goal vs T's goal.", OnGoalSwapCommand);
        AddCommand("css_sm2goal_test", "Server only: teleport the ball through a goal to test detection (no double-count check needs 2 runs).", OnGoalTestCommand);
        AddCommand("css_sm2goal_punish", "Admin (match): toggle killing the conceding team on each goal (on/off).", OnGoalPunishCommand);
        AddCommand("css_sm2goal_roundwin", "Admin (match): toggle a native CS2 round-win on each goal (on/off). EXPERIMENTAL.", OnGoalRoundWinCommand);
        AddCommand("css_sm2kickoffwall", "Admin (match): toggle the post-kickoff possession wall (on/off). Currently off by default.", OnKickoffWallCommand);
        AddCommand("css_sm2match_config", "Admin (match): set periods/periodLength/breakLength/goldenGoal.", OnMatchConfigCommand);
        AddCommand("css_teamname", "Admin (match): set the CT or T display name.", OnTeamNameCommand);
        AddCommand("css_rdy", "Mark yourself ready during a match pause; auto-resumes once everyone is.", OnReadyCommand);
        AddCommand("css_forfeit", "Vote to forfeit the match for your team.", OnForfeitCommand);
    }

    private void MatchOnRoundStart()
    {
        // The round-start ball rebuild (EnsureBallFoundation, already wired
        // into OnRoundStart) is the "verified reset" that clears the goal
        // lock - exactly the goal.js semantics this is ported from.
        _goalLocked = false;
        // mp_restartgame zeroes CS2's own team scores, and we restart on
        // every kickoff - so the real scoreboard has to be re-stamped after
        // each one or it silently falls back to 0-0 mid-match.
        Server.NextFrame(UpdateTeamScoreboard);
    }

    // Writes our match score into CS2's own team entities so the Tab
    // scoreboard / HUD show it. Without this the plugin's score only ever
    // existed in chat and center-text.
    private void UpdateTeamScoreboard()
    {
        var stamped = 0;
        foreach (var team in Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager"))
        {
            if (!team.IsValid)
            {
                continue;
            }

            stamped++;

            if (team.TeamNum == (byte)CsTeam.CounterTerrorist)
            {
                team.Score = _scoreCt;
                Utilities.SetStateChanged(team, "CCSTeam", "m_iScore");
            }
            else if (team.TeamNum == (byte)CsTeam.Terrorist)
            {
                team.Score = _scoreT;
                Utilities.SetStateChanged(team, "CCSTeam", "m_iScore");
            }
        }

        Logger.LogInformation(
            "[SM2DIAG] team_scoreboard_stamped teams={Teams} scoreCt={ScoreCt} scoreT={ScoreT}",
            stamped,
            _scoreCt,
            _scoreT);
    }

    // Called every tick from the main OnTick, after UpdateDerivedMotion.
    private void MatchOnTick()
    {
        var now = (double)Server.TickedTime;
        switch (_matchPhase)
        {
            case MatchPhase.Countdown:
                if (now >= _phaseTransitionAtServerTime)
                {
                    _matchPhase = MatchPhase.Live;
                    _periodEndsAtServerTime = now + _pausedRemainingSeconds;
                    AnnounceAll($" \x04[Match]\x01 Period {_matchPeriod}/{_matchPeriods} is LIVE!");
                    UpdateHostname();
                }
                break;

            case MatchPhase.Live:
                if (now >= _periodEndsAtServerTime)
                {
                    EndPeriod();
                }
                else if (now >= _nextScoreboardUpdateTime)
                {
                    _nextScoreboardUpdateTime = now + 1.0;
                    UpdateScoreboardDisplay(now);
                }
                EnforceKickoffWall();
                break;

            case MatchPhase.GoalPause:
                if (now >= _phaseTransitionAtServerTime)
                {
                    Server.ExecuteCommand("mp_restartgame 1");
                    _matchPhase = MatchPhase.Live;
                }
                break;

            case MatchPhase.PeriodBreak:
                if (now >= _phaseTransitionAtServerTime)
                {
                    StartNextPeriodOrFinish();
                }
                break;

            case MatchPhase.Warmup:
            case MatchPhase.Paused:
            case MatchPhase.Finished:
                break;
        }
    }

    // Called from UpdateDerivedMotion with the ball's position one tick ago
    // and right now, so a fast ball can't skip past the goal plane between
    // samples without the crossing being caught (segment interpolation,
    // same as goal.js).
    // Returns true if a goal fired (and therefore a kickoff reset happened
    // INSIDE this call, invalidating the caller's in-flight ball-motion
    // state - see the comment at the UpdateDerivedMotion call site).
    private bool MatchCheckGoalCrossing(Vector previous, Vector current)
    {
        if (_matchPhase != MatchPhase.Live || _goalLocked)
        {
            return false;
        }

        return TryGoalPlane(previous, current, GoalPlaneY)
            || TryGoalPlane(previous, current, -GoalPlaneY);
    }

    private bool TryGoalPlane(Vector previous, Vector current, float planeY)
    {
        var previousSide = previous.Y - planeY;
        var currentSide = current.Y - planeY;
        if (previousSide == 0.0f || Math.Sign(previousSide) == Math.Sign(currentSide))
        {
            return false;
        }

        var deltaY = current.Y - previous.Y;
        if (MathF.Abs(deltaY) < 0.0001f)
        {
            return false;
        }

        var t = Math.Clamp((planeY - previous.Y) / deltaY, 0.0f, 1.0f);
        var crossX = previous.X + (current.X - previous.X) * t;
        var crossZ = previous.Z + (current.Z - previous.Z) * t;

        if (MathF.Abs(crossX - GoalCenterX) > _goalHalfWidthX
            || crossZ < _goalApertureMinZ
            || crossZ > _goalApertureMaxZ)
        {
            return false;
        }

        // planeY > 0 is the goal at the +Y end. _ctDefendsNegativeY tells us
        // which physical end belongs to which team; the ball entering a
        // team's own goal scores for the OTHER team.
        var enteredPositiveEnd = planeY > 0;
        var enteredCtGoal = enteredPositiveEnd != _ctDefendsNegativeY;
        var scoringTeam = enteredCtGoal ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
        OnGoalScored(scoringTeam, crossX, crossZ, planeY);
        return true;
    }

    private void OnGoalScored(CsTeam scoringTeam, float x, float z, float planeY)
    {
        _goalLocked = true;

        var ownGoal = _lastKickerTeam != CsTeam.None && _lastKickerTeam != scoringTeam;
        if (scoringTeam == CsTeam.CounterTerrorist)
        {
            _scoreCt++;
        }
        else
        {
            _scoreT++;
        }

        if (!ownGoal && _lastKickerSlot >= 0)
        {
            _goalsBySlot[_lastKickerSlot] = _goalsBySlot.GetValueOrDefault(_lastKickerSlot) + 1;
        }

        StatsOnGoalScored(_lastKickerSlot, scoringTeam, ownGoal);

        var concedingTeam = scoringTeam == CsTeam.CounterTerrorist ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
        StartKickoffRestriction(concedingTeam);

        var scorerName = _lastKickerSlot >= 0
            ? Utilities.GetPlayerFromSlot(_lastKickerSlot) is { IsValid: true } scorer ? scorer.PlayerName : "unknown"
            : "unknown";

        var message = ownGoal
            ? $" \x04[Match]\x01 OWN GOAL by {scorerName}! {TeamName(scoringTeam)} score."
            : $" \x04[Match]\x01 GOAL by {scorerName} ({TeamName(scoringTeam)})!";
        AnnounceAll(message);
        AnnounceAll($" \x04[Match]\x01 {_teamNameCt} {_scoreCt} - {_scoreT} {_teamNameT}");
        AppendMatchLog($"GOAL {TeamName(scoringTeam)} scorer={scorerName} ownGoal={ownGoal} score={_scoreCt}-{_scoreT}");
        UpdateHostname();
        UpdateTeamScoreboard();

        Logger.LogInformation(
            "[SM2DIAG] goal_scored team={Team} ownGoal={OwnGoal} x={X:F1} z={Z:F1} planeY={PlaneY:F0} scoreCt={ScoreCt} scoreT={ScoreT}",
            scoringTeam,
            ownGoal,
            x,
            z,
            planeY,
            _scoreCt,
            _scoreT);
        Logger.LogInformation("[SM2DIAG] goal_locked reason=goal_scored");

        if (_goalPunishEnabled)
        {
            PunishConcedingTeam(concedingTeam);
        }

        if (_inGoldenGoal)
        {
            // Sudden death - first goal ends it immediately, no restart-and-
            // resume like a normal in-match goal.
            FinishMatch();
            return;
        }

        if (_goalRoundWinEnabled && TryNativeRoundWin(scoringTeam, GoalPauseSeconds))
        {
            // The native round-end already schedules its own restart after
            // the delay, which fires EventRoundStart -> MatchOnRoundStart
            // (clears _goalLocked, re-stamps the scoreboard) exactly like
            // every other kickoff. Do NOT also set GoalPause/mp_restartgame
            // below - that would restart the round twice for one goal.
            return;
        }

        _matchPhase = MatchPhase.GoalPause;
        _phaseTransitionAtServerTime = Server.TickedTime + GoalPauseSeconds;
        // Deliberately NOT resetting the ball here: the kickoff restart a
        // few seconds later rebuilds it at centre anyway, so doing it twice
        // just made the ball visibly jump twice for one goal.
    }

    // Kills every alive, valid player on the conceding team. Own goals
    // still punish correctly since concedingTeam is derived from which
    // goal the ball entered, not from the last toucher.
    private void PunishConcedingTeam(CsTeam concedingTeam)
    {
        var killed = 0;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.Team != concedingTeam)
            {
                continue;
            }

            var pawn = player.PlayerPawn.Value;
            if (pawn is not { IsValid: true } || !IsAlive(pawn))
            {
                continue;
            }

            pawn.CommitSuicide(false, true);
            killed++;
        }

        Logger.LogInformation("[SM2DIAG] goal_punish team={Team} killed={Killed}", concedingTeam, killed);
    }

    // Returns true if the native round-win call was made. False (with a
    // log line, never a throw) if the gamerules entity/proxy couldn't be
    // found - the caller falls back to the normal GoalPause flow so a
    // missing entity never silently eats a goal restart.
    private bool TryNativeRoundWin(CsTeam scoringTeam, float delaySeconds)
    {
        var proxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        if (proxy is not { IsValid: true } || proxy.GameRules is not { } gameRules)
        {
            Logger.LogWarning("[SM2DIAG] goal_roundwin_failed reason=gamerules_not_found");
            return false;
        }

        var reason = scoringTeam == CsTeam.CounterTerrorist ? RoundEndReason.CTsWin : RoundEndReason.TerroristsWin;
        gameRules.TerminateRound(delaySeconds, reason);
        Logger.LogInformation("[SM2DIAG] goal_roundwin team={Team} reason={Reason} delay={Delay:F1}", scoringTeam, reason, delaySeconds);
        return true;
    }

    private void EndPeriod()
    {
        if (_matchPeriod >= _matchPeriods)
        {
            if (_goldenGoalEnabled && !_inGoldenGoal && _scoreCt == _scoreT)
            {
                StartGoldenGoal();
                return;
            }

            FinishMatch();
            return;
        }

        _matchPhase = MatchPhase.PeriodBreak;
        _phaseTransitionAtServerTime = Server.TickedTime + _breakLengthSeconds;
        FreezeAllPlayers(true);
        AnnounceAll($" \x04[Match]\x01 End of period {_matchPeriod}/{_matchPeriods}. {_teamNameCt} {_scoreCt} - {_scoreT} {_teamNameT}. Half-time: {_breakLengthSeconds:F0}s.");
        StatsAnnounceHalftimeTop3();
        Logger.LogInformation("[SM2DIAG] match_period_end period={Period} scoreCt={ScoreCt} scoreT={ScoreT}", _matchPeriod, _scoreCt, _scoreT);
    }

    private void StartGoldenGoal()
    {
        _inGoldenGoal = true;
        _matchPhase = MatchPhase.PeriodBreak;
        _phaseTransitionAtServerTime = Server.TickedTime + _breakLengthSeconds;
        FreezeAllPlayers(true);
        AnnounceAll($" \x04[Match]\x01 Full time: {_scoreCt}-{_scoreT} draw. GOLDEN GOAL - first goal wins! Starting in {_breakLengthSeconds:F0}s.");
        Logger.LogInformation("[SM2DIAG] golden_goal_start scoreCt={ScoreCt} scoreT={ScoreT}", _scoreCt, _scoreT);
    }

    private void StartNextPeriodOrFinish()
    {
        if (_inGoldenGoal)
        {
            FreezeAllPlayers(false);
            Server.ExecuteCommand("mp_restartgame 1");
            _matchPhase = MatchPhase.Countdown;
            _pausedRemainingSeconds = GoldenGoalLengthSeconds;
            _phaseTransitionAtServerTime = Server.TickedTime + KickoffCountdownSeconds;
            AnnounceAll($" \x04[Match]\x01 Golden goal kicks off in {KickoffCountdownSeconds:F0}s - first goal wins!");
            Logger.LogInformation("[SM2DIAG] golden_goal_kickoff");
            StartKickoffRestriction(CsTeam.CounterTerrorist);
            return;
        }

        // Swap ends at half-time so both teams play one half attacking each
        // goal - SwitchTeam (not ChangeTeam) avoids a kill/respawn screen,
        // and the restart right after re-spawns everyone cleanly anyway.
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
            {
                continue;
            }

            player.SwitchTeam(player.Team == CsTeam.Terrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist);
        }
        _teamsSwapped = !_teamsSwapped;

        _matchPeriod++;
        FreezeAllPlayers(false);
        Server.ExecuteCommand("mp_restartgame 1");
        _matchPhase = MatchPhase.Countdown;
        _pausedRemainingSeconds = _periodLengthSeconds;
        _phaseTransitionAtServerTime = Server.TickedTime + KickoffCountdownSeconds;
        AnnounceAll($" \x04[Match]\x01 Teams swapped ends. Period {_matchPeriod}/{_matchPeriods} kicks off in {KickoffCountdownSeconds:F0}s.");
        Logger.LogInformation("[SM2DIAG] match_period_start period={Period}", _matchPeriod);
        StartKickoffRestriction(CsTeam.CounterTerrorist);
    }

    private void FinishMatch()
    {
        _matchPhase = MatchPhase.Finished;
        var winner = _scoreCt == _scoreT ? "Draw" : (_scoreCt > _scoreT ? $"{_teamNameCt} win" : $"{_teamNameT} win");
        AnnounceAll($" \x04[Match]\x01 FULL TIME - {_teamNameCt} {_scoreCt} - {_scoreT} {_teamNameT}. {winner}!");
        Logger.LogInformation("[SM2DIAG] match_finished scoreCt={ScoreCt} scoreT={ScoreT} winner={Winner}", _scoreCt, _scoreT, winner);
        AppendMatchLog($"FULL TIME {_teamNameCt} {_scoreCt} - {_scoreT} {_teamNameT} ({winner})");
        if (_goalsBySlot.Count > 0)
        {
            var topSlot = _goalsBySlot.OrderByDescending(kv => kv.Value).First();
            var mvpName = Utilities.GetPlayerFromSlot(topSlot.Key) is { IsValid: true } mvp ? mvp.PlayerName : "unknown";
            AnnounceAll($" \x04[Match]\x01 MVP: {mvpName} ({topSlot.Value} goal{(topSlot.Value == 1 ? "" : "s")})");
            AppendMatchLog($"MVP {mvpName} goals={topSlot.Value}");
        }
        UpdateHostname();
        _inGoldenGoal = false;
        StatsOnMatchFinished();
        FreezeAllPlayers(false);
        Server.NextFrame(() => _matchPhase = MatchPhase.Warmup);
    }

    // SoMoE updated the server's hostname with live match status
    // (soccer_mod.sp "gamestatus") - mirrors that with the info we track.
    private void UpdateHostname()
    {
        var status = _matchPhase switch
        {
            MatchPhase.Live => _inGoldenGoal ? "GOLDEN GOAL" : "LIVE",
            MatchPhase.Countdown => "KICKOFF",
            MatchPhase.GoalPause => "GOAL!",
            MatchPhase.Paused => "PAUSED",
            MatchPhase.Finished => "FULL TIME",
            MatchPhase.PeriodBreak => _inGoldenGoal ? "GOLDEN GOAL BREAK" : "HALF-TIME",
            _ => "WARMUP",
        };
        Server.ExecuteCommand($"hostname \"SoccerMod | {_teamNameCt} {_scoreCt} - {_scoreT} {_teamNameT} | {status}\"");
    }

    // SoMoE's soccer_mod_last_match.txt equivalent: overwritten fresh at
    // match start, appended per goal, closed with the final line.
    private void AppendMatchLog(string line)
    {
        try
        {
            File.AppendAllText(
                ConfigPath(MatchLogFileName),
                $"[{DateTime.UtcNow:u}] {line}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[SM2DIAG] match_log_write_failed");
        }
    }

    private void UpdateScoreboardDisplay(double now)
    {
        var remaining = Math.Max(0.0, _periodEndsAtServerTime - now);
        var minutes = (int)(remaining / 60.0);
        var seconds = (int)(remaining % 60.0);
        var periodLabel = _inGoldenGoal ? "golden goal" : $"period {_matchPeriod}/{_matchPeriods}";
        var text = $"{_teamNameCt} {_scoreCt} - {_scoreT} {_teamNameT}\n{minutes}:{seconds:D2}  ({periodLabel})";
        foreach (var player in Utilities.GetPlayers())
        {
            // Both writers target the same centre-screen HUD region; without
            // this the score ticker clobbers an open !menu panel every
            // second (root cause of the menu "disappearing" during a live
            // match).
            if (player.IsValid && !_openMenus.ContainsKey(player.Slot))
            {
                player.PrintToCenter(text);
            }
        }
    }

    private void FreezeAllPlayers(bool freeze)
    {
        foreach (var player in Utilities.GetPlayers())
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn is not { IsValid: true } || !IsAlive(pawn))
            {
                continue;
            }

            pawn.MoveType = freeze ? MoveType_t.MOVETYPE_OBSOLETE : MoveType_t.MOVETYPE_WALK;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
        }
    }

    private void AnnounceAll(string message)
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player.IsValid)
            {
                player.PrintToChat(message);
            }
        }
    }

    private void OnMatchCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        var sub = command.ArgCount >= 2 ? command.GetArg(1).ToLowerInvariant() : "status";
        switch (sub)
        {
            case "start":
                StartMatch();
                command.ReplyToCommand("[SM] match starting");
                break;

            case "stop":
                _matchPhase = MatchPhase.Warmup;
                FreezeAllPlayers(false);
                AnnounceAll(" \x04[Match]\x01 Match stopped.");
                Logger.LogInformation("[SM2DIAG] match_stopped by={By}", player?.PlayerName ?? "RCON");
                break;

            case "pause":
                if (_matchPhase == MatchPhase.Live)
                {
                    _pausedRemainingSeconds = _periodEndsAtServerTime - Server.TickedTime;
                    _matchPhase = MatchPhase.Paused;
                    _readyPlayers.Clear();
                    FreezeAllPlayers(true);
                    AnnounceAll(" \x04[Match]\x01 Match paused. Type !rdy when you're ready to continue.");
                    UpdateHostname();
                }
                else
                {
                    command.ReplyToCommand("[SM] match is not live");
                }
                break;

            case "unpause":
                if (_matchPhase == MatchPhase.Paused)
                {
                    ResumeFromPause("admin_override");
                }
                else
                {
                    command.ReplyToCommand("[SM] match is not paused");
                }
                break;

            case "status":
            default:
                command.ReplyToCommand(
                    $"[SM] phase={_matchPhase} period={_matchPeriod}/{_matchPeriods} score={_teamNameCt} {_scoreCt} - {_scoreT} {_teamNameT} swapped={_teamsSwapped} goldenGoal={_inGoldenGoal}");
                break;
        }
    }

    private void ResumeFromPause(string reason)
    {
        FreezeAllPlayers(false);
        _matchPhase = MatchPhase.Countdown;
        _phaseTransitionAtServerTime = Server.TickedTime + KickoffCountdownSeconds;
        _readyPlayers.Clear();
        AnnounceAll($" \x04[Match]\x01 Resuming in {KickoffCountdownSeconds:F0}s.");
        UpdateHostname();
        Logger.LogInformation("[SM2DIAG] match_resumed reason={Reason}", reason);
    }

    private void OnReadyCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            return;
        }

        if (_matchPhase != MatchPhase.Paused)
        {
            command.ReplyToCommand("[SM] match is not paused");
            return;
        }

        if (!_readyPlayers.Add(player.Slot))
        {
            return;
        }

        var activePlayers = Utilities.GetPlayers()
            .Where(p => p.IsValid && p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
            .ToList();
        AnnounceAll($" \x04[Match]\x01 {player.PlayerName} is ready ({_readyPlayers.Count}/{activePlayers.Count}).");
        if (activePlayers.Count > 0 && activePlayers.All(p => _readyPlayers.Contains(p.Slot)))
        {
            ResumeFromPause("all_ready");
        }
    }

    private void OnForfeitCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            return;
        }

        if (_matchPhase is MatchPhase.Warmup or MatchPhase.Finished)
        {
            command.ReplyToCommand("[SM] no match is running");
            return;
        }

        if (player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            command.ReplyToCommand("[SM] you must be on a team to vote forfeit");
            return;
        }

        if (_forfeitVoteTeam != CsTeam.None && _forfeitVoteTeam != player.Team)
        {
            command.ReplyToCommand("[SM] the other team already has a forfeit vote in progress");
            return;
        }

        _forfeitVoteTeam = player.Team;
        if (!_forfeitVotes.Add(player.Slot))
        {
            command.ReplyToCommand("[SM] you already voted to forfeit");
            return;
        }

        var teamPlayers = Utilities.GetPlayers().Where(p => p.IsValid && p.Team == player.Team).ToList();
        var needed = teamPlayers.Count / 2 + 1;
        AnnounceAll($" \x04[Match]\x01 {player.PlayerName} voted to forfeit for {TeamName(player.Team)} ({_forfeitVotes.Count}/{needed} needed).");
        if (_forfeitVotes.Count < needed)
        {
            return;
        }

        var winningTeam = player.Team == CsTeam.Terrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
        AnnounceAll($" \x04[Match]\x01 {TeamName(player.Team)} forfeited. {TeamName(winningTeam)} win!");
        Logger.LogInformation("[SM2DIAG] match_forfeited team={Team}", player.Team);
        AppendMatchLog($"FORFEIT by {TeamName(player.Team)} - {TeamName(winningTeam)} win");
        _forfeitVotes.Clear();
        _forfeitVoteTeam = CsTeam.None;
        FinishMatch();
    }

    private void OnMatchConfigCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        if (command.ArgCount < 3)
        {
            command.ReplyToCommand(
                $"[SM] periods={_matchPeriods} periodLength={_periodLengthSeconds:F0}s breakLength={_breakLengthSeconds:F0}s goldenGoal={_goldenGoalEnabled}; usage: css_sm2match_config periods|periodlength|breaklength|goldengoal <value>");
            return;
        }

        var key = command.GetArg(1).ToLowerInvariant();
        var valueArg = command.GetArg(2);
        switch (key)
        {
            case "periods":
                if (int.TryParse(valueArg, out var periods) && periods is >= 1 and <= 8)
                {
                    _matchPeriods = periods;
                }
                break;
            case "periodlength":
                if (float.TryParse(valueArg, NumberStyles.Float, CultureInfo.InvariantCulture, out var periodLength) && periodLength > 0)
                {
                    _periodLengthSeconds = periodLength;
                }
                break;
            case "breaklength":
                if (float.TryParse(valueArg, NumberStyles.Float, CultureInfo.InvariantCulture, out var breakLength) && breakLength >= 0)
                {
                    _breakLengthSeconds = breakLength;
                }
                break;
            case "goldengoal":
                _goldenGoalEnabled = valueArg.Equals("on", StringComparison.OrdinalIgnoreCase);
                break;
            default:
                command.ReplyToCommand("[SM] unknown key; use periods|periodlength|breaklength|goldengoal");
                return;
        }

        SaveMatchSettings("match_config_command");
        command.ReplyToCommand(
            $"[SM] periods={_matchPeriods} periodLength={_periodLengthSeconds:F0}s breakLength={_breakLengthSeconds:F0}s goldenGoal={_goldenGoalEnabled}");
    }

    private void OnTeamNameCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        if (command.ArgCount < 3)
        {
            command.ReplyToCommand($"[SM] usage: css_teamname <ct|t> <name...> (current: CT='{_teamNameCt}' T='{_teamNameT}')");
            return;
        }

        var side = command.GetArg(1).ToLowerInvariant();
        var name = string.Join(' ', Enumerable.Range(2, command.ArgCount - 2).Select(command.GetArg));
        if (side == "ct")
        {
            _teamNameCt = name;
        }
        else if (side == "t")
        {
            _teamNameT = name;
        }
        else
        {
            command.ReplyToCommand("[SM] usage: css_teamname <ct|t> <name...>");
            return;
        }

        SaveMatchSettings("teamname_command");
        UpdateHostname();
        command.ReplyToCommand($"[SM] CT='{_teamNameCt}' T='{_teamNameT}'");
    }

    private void StartMatch()
    {
        _scoreCt = 0;
        _scoreT = 0;
        _matchPeriod = 1;
        _teamsSwapped = false;
        _goalLocked = false;
        _inGoldenGoal = false;
        _forfeitVotes.Clear();
        _forfeitVoteTeam = CsTeam.None;
        _goalsBySlot.Clear();
        AfkDisarm("match_start");
        FreezeAllPlayers(false);
        UpdateTeamScoreboard();
        Server.ExecuteCommand("mp_restartgame 1");
        StartKickoffRestriction(CsTeam.CounterTerrorist);
        _matchPhase = MatchPhase.Countdown;
        _pausedRemainingSeconds = _periodLengthSeconds;
        _phaseTransitionAtServerTime = Server.TickedTime + KickoffCountdownSeconds;
        AnnounceAll($" \x04[Match]\x01 Match starting! Period 1/{_matchPeriods} kicks off in {KickoffCountdownSeconds:F0}s.");
        Logger.LogInformation("[SM2DIAG] match_started periods={Periods} periodLength={PeriodLength}", _matchPeriods, _periodLengthSeconds);
        try
        {
            File.WriteAllText(ConfigPath(MatchLogFileName), $"[{DateTime.UtcNow:u}] MATCH START {_teamNameCt} vs {_teamNameT}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[SM2DIAG] match_log_write_failed");
        }
        UpdateHostname();
    }

    private void OnRoundRestartCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        Server.ExecuteCommand("mp_restartgame 1");
        command.ReplyToCommand("[SM] round restarted");
    }

    private void OnMatchRestartCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        StartMatch();
        command.ReplyToCommand("[SM] match restarted");
    }

    private void OnMapReloadCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        // changelevel loses the Workshop addon context on this map - re-issuing
        // the same host_workshop_map command is what keeps it (documented, hard
        // rule from the ball-foundation work). If this ever stops working after
        // a CS2 update, the systemd service restart remains the safety net.
        command.ReplyToCommand("[SM] reloading workshop map, this takes a few seconds...");
        Logger.LogInformation("[SM2DIAG] map_reload_requested by={By}", player?.PlayerName ?? "RCON");
        Server.ExecuteCommand("host_workshop_map 3361075564");
    }

    private void OnGoalCalibCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        if (command.ArgCount != 3
            || !float.TryParse(command.GetArg(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var halfWidth)
            || !float.TryParse(command.GetArg(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var maxZ))
        {
            command.ReplyToCommand($"[SM] usage: css_sm2goal_calib <halfWidth> <maxHeight> (current: {_goalHalfWidthX:F0}, {_goalApertureMaxZ:F0})");
            return;
        }

        _goalHalfWidthX = Math.Clamp(halfWidth, 20.0f, 500.0f);
        _goalApertureMaxZ = Math.Clamp(maxZ, 0.0f, 400.0f);
        SaveMatchSettings("goal_calib_command");
        command.ReplyToCommand($"[SM] goal aperture: halfWidth={_goalHalfWidthX:F0} maxHeight={_goalApertureMaxZ:F0}");
    }

    private void OnGoalSwapCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        _ctDefendsNegativeY = !_ctDefendsNegativeY;
        SaveMatchSettings("goal_swap_command");
        command.ReplyToCommand($"[SM] CT now defends {(_ctDefendsNegativeY ? "negative" : "positive")} Y");
    }

    private void OnGoalPunishCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            _goalPunishEnabled = command.GetArg(1).Equals("on", StringComparison.OrdinalIgnoreCase);
            SaveMatchSettings("goal_punish_command");
        }

        command.ReplyToCommand($"[SM] goal punish (kill conceding team): {(_goalPunishEnabled ? "on" : "off")} (usage: css_sm2goal_punish <on|off>)");
    }

    private void OnGoalRoundWinCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            _goalRoundWinEnabled = command.GetArg(1).Equals("on", StringComparison.OrdinalIgnoreCase);
            SaveMatchSettings("goal_roundwin_command");
        }

        command.ReplyToCommand(
            $"[SM] native round-win on goal: {(_goalRoundWinEnabled ? "on" : "off")} - EXPERIMENTAL, verify with css_sm2goal_test "
            + "and mp_maxrounds 0 / mp_halftime 0 set first (usage: css_sm2goal_roundwin <on|off>)");
    }

    private void OnKickoffWallCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            _kickoffWallEnabled = command.GetArg(1).Equals("on", StringComparison.OrdinalIgnoreCase);
            if (!_kickoffWallEnabled)
            {
                _kickoffRestrictionActive = false;
            }
            SaveMatchSettings("kickoffwall_command");
        }

        command.ReplyToCommand($"[SM] kickoff wall: {(_kickoffWallEnabled ? "on" : "off")} (usage: css_sm2kickoffwall <on|off>)");
    }

    private void OnGoalTestCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command)
            || !BindBall("goal_test_command")
            || _ball is not { IsValid: true })
        {
            return;
        }

        var toward = command.ArgCount >= 2 && command.GetArg(1).Equals("ct", StringComparison.OrdinalIgnoreCase)
            ? -1.0f
            : 1.0f;
        var planeY = toward * GoalPlaneY;
        var startY = planeY - toward * 150.0f;
        _ball.Teleport(
            position: new Vector(GoalCenterX, startY, BallResetZ),
            velocity: new Vector(0.0f, toward * 800.0f, 0.0f));
        ResetDerivedMotion();
        command.ReplyToCommand($"[SM2DIAG] goal test ball launched toward y={planeY:F0}");
    }
}

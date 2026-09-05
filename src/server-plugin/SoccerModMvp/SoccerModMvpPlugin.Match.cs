using System.Globalization;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Match MVP: periods, pause/unpause, kickoff/goal reset, map reload/restart.
// State machine and goal-crossing detection are a C# port of the already
// validated src/ball-lab/core/match.js and goal.js (segment interpolation +
// lock-until-verified-reset, so a fast ball can't tunnel through the plane
// between ticks and a single goal can't double-count while the kickoff
// restart is in flight). KICKOFF defaults: 2 periods, 600s each and a 60s
// half-time break. A completed website vote can select 450s, 600s or 900s
// through the existing periodlength command before the lineup is imported.
// The match flow remains deliberately simplified from SoMoE
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
    // 2026-09-01 goal-line fix (user report: a shot passing NEXT to the goal,
    // not even touching the post, counted as a goal). Two never-measured
    // guesses were responsible: _goalHalfWidthX=200 is wider than the real
    // posts (the same day's crossbar measurement found NO frame geometry at
    // x=+-195, so the posts stand inside +-200), and the detection plane was
    // simply "60u in front of the backstop" with no notion of a goal LINE.
    // Now the goal line has its own persisted Y (_goalLineY), the ball's
    // CENTRE must travel _goalDepthRequired past it before it counts
    // (default one ball radius = the whole ball over the line, football
    // rules), only crossings INTO the goal count, and css_sm2goal_measure
    // traces the real posts and line so the numbers come from the map.
    // The effective plane is capped below the backstop's centre limit
    // (~1441.7, see above) so it can always physically be reached.
    private const float DefaultGoalLineY = 1400.0f;
    private const float NetBackstopY = 1460.47f;
    private const float GoalCenterX = 0.0f;
    private float _goalLineY = DefaultGoalLineY;
    private float _goalDepthRequired = BallCollisionRadius;
    private float GoalPlaneY => MathF.Min(_goalLineY + _goalDepthRequired, NetBackstopY - BallCollisionRadius - 5.0f);
    private float _goalHalfWidthX = 200.0f;
    private float _goalApertureMinZ = -32.0f;
    // 2026-09-01: was 120 (an unmeasured guess) - user reported shots that
    // visibly pass above the crossbar still counting. css_sm2goal_measure
    // traced straight down at the goal line and found real solid geometry
    // at 102u above the pitch at both goal mouths (dead centre - the side
    // traces near the posts found nothing, a separate, not-yet-reported
    // question about _goalHalfWidthX left alone for now). 100 keeps a
    // small margin under the measured crossbar height.
    private float _goalApertureMaxZ = 100.0f;
    private bool _ctDefendsNegativeY = true;

    private const int DefaultMatchPeriods = 2;
    private const float DefaultPeriodLengthSeconds = 600.0f;
    private const float DefaultBreakLengthSeconds = 60.0f;
    private const float GoalPauseSeconds = 4.0f;
    private const float KickoffCountdownSeconds = 3.0f;
    private const float KickoffBallActivePlanarSpeed = 5.0f;

    private MatchPhase _matchPhase = MatchPhase.Warmup;
    private int _scoreCt;
    private int _scoreT;
    private int _matchPeriod = 1;
    private int _matchPeriods = DefaultMatchPeriods;
    private float _periodLengthSeconds = DefaultPeriodLengthSeconds;
    private float _activePeriodLengthSeconds = DefaultPeriodLengthSeconds;
    private string _matchLengthSource = "default";
    private float _breakLengthSeconds = DefaultBreakLengthSeconds;
    private bool _teamsSwapped;
    private bool _goalLocked;
    // 2026-09-01: the goal punish kills the conceding team IMMEDIATELY at
    // goal time (user request - deaths are the visible reset signal, CS:S
    // style). mp_respawn_on_death_t/ct run at 1 on this server, which used
    // to swallow the kill with an instant auto-respawn; both cvars are
    // therefore forced to 0 for the GoalPause window and restored right
    // before the kickoff restart. This flag tracks the suppression so
    // every exit path (goal pause end, round start, golden goal) can
    // restore idempotently and a stuck 0 can never break !kill respawns.
    private bool _goalRespawnSuppressed;
    private double _periodEndsAtServerTime;
    private double _pausedRemainingSeconds;
    private double _phaseTransitionAtServerTime;
    private double _nextScoreboardUpdateTime;
    private bool _kickoffClockWaitingForBall;
    private bool _kickoffBallActivityObserved;
    private bool _countdownRequiresBallActivation;
    private bool _nativeGoalRestartPending;
    private int _lastKickerSlot = -1;
    private CsTeam _lastKickerTeam = CsTeam.None;

    // CS:S-parity plan Tier 1 additions.
    private bool _goldenGoalEnabled = true;
    private bool _inGoldenGoal;
    private const float GoldenGoalLengthSeconds = 300.0f;
    private string _teamNameCt = "Counter-Terrorists";
    private string _teamNameT = "Terrorists";
    // SoMoE match.sp "[Perm]" vs "[Match]" team names: the permanent pair is
    // what gets persisted; a match-only name lives in _teamNameCt/T until
    // the match stops or finishes, then the permanent one comes back.
    private string _permanentTeamNameCt = "Counter-Terrorists";
    private string _permanentTeamNameT = "Terrorists";
    private readonly HashSet<ulong> _readyPlayers = new();
    private readonly HashSet<int> _forfeitVotes = new();
    private CsTeam _forfeitVoteTeam = CsTeam.None;
    private const string MatchLogFileName = "soccermod_last_match.txt";
    // In-memory mirror of the match log file for the SoMoE "Match Log"
    // menu (newest first).
    private const int MatchLogMaxLines = 40;
    private readonly List<string> _matchLogLines = new();

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
    // team touches the ball first. The wall persists until ball activity;
    // waiting or a round restart must never release it.
    // Implemented as a soft rubber-band (teleport back on crossing) rather
    // than spawned wall geometry - functionally the same restriction.
    private bool _kickoffRestrictionActive;
    private CsTeam _kickoffTeam = CsTeam.None;
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
        DrawKickoffOutline();
        Logger.LogInformation("[SM2DIAG] kickoff_wall_start team={Team}", kickoffTeam);
    }

    // These are accepted contacts, not attempted kicks. Opponent kick/push
    // eligibility remains governed by IsKickoffTouchAllowed.
    private void ClearKickoffRestrictionOnTouch(CsTeam toucherTeam)
    {
        if (toucherTeam is CsTeam.CounterTerrorist or CsTeam.Terrorist)
            CompleteKickoffRestriction("player_touch");
    }

    private void CompleteKickoffRestriction(string reason)
    {
        if (!_kickoffRestrictionActive) return;
        _kickoffRestrictionActive = false;
        ClearKickoffOutline();
        Logger.LogInformation("[SM2DIAG] kickoff_wall_cleared reason={Reason}", reason);
    }

    private void EnforceKickoffWall()
    {
        if (!_kickoffRestrictionActive)
        {
            ClearKickoffOutline();
            return;
        }

        if (_menuParity.KickoffOutline) { EnforceOutlinedKickoff(); return; }

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
        AddCommand("css_maprr", "Reload the workshop map (host_workshop_map, keeps addon context). Open to everyone.", OnMapReloadCommand);
        AddCommand("css_sm2goal_calib", "Admin (match): set goal aperture half-width and max height.", OnGoalCalibCommand);
        AddCommand("css_sm2goal_measure", "Server only: trace the real crossbar/frame height at both goal mouths (fixes calibration by measurement, not guesswork).", OnGoalMeasureCommand);
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
        // Defensive: if any goal path missed its own restore, the restart
        // that just fired is the moment everyone respawns anyway.
        RestoreGoalRespawnCvars();
        // mp_restartgame zeroes CS2's own team scores, and we restart on
        // every kickoff - so the real scoreboard has to be re-stamped after
        // each one or it silently falls back to 0-0 mid-match.
        Server.NextFrame(UpdateTeamScoreboard);
        // Round cleanup deletes beam entities but the kickoff itself survives.
        // Draw rechecks the current restriction, so a touch before this callback
        // cannot accidentally resurrect the wall.
        Server.NextFrame(DrawKickoffOutline);
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
                    EnterLiveAfterCountdown(now);
                }
                break;

            case MatchPhase.Live:
                if (_kickoffClockWaitingForBall)
                {
                    if (_kickoffBallActivityObserved || IsKickoffBallMoving())
                    {
                        ActivateKickoffClock("ball_motion_or_touch");
                    }
                    if (_kickoffClockWaitingForBall)
                    {
                        if (now >= _nextScoreboardUpdateTime)
                        {
                            _nextScoreboardUpdateTime = now + 1.0;
                            UpdateScoreboardDisplay(now);
                        }
                        EnforceKickoffWall();
                        break;
                    }
                }
                if (now >= _periodEndsAtServerTime)
                {
                    if (ShouldEndPeriod()) { EndPeriod(); break; }
                }
                else if (_ball is { IsValid: true } movingBall && movingBall.AbsOrigin is { } movingOrigin)
                {
                    _stoppagePreviousY = movingOrigin.Y - CreateBallResetOrigin().Y;
                }
                if (now >= _nextScoreboardUpdateTime)
                {
                    _nextScoreboardUpdateTime = now + 1.0;
                    UpdateScoreboardDisplay(now);
                }
                EnforceKickoffWall();
                break;

            case MatchPhase.GoalPause:
                if (now >= _phaseTransitionAtServerTime)
                {
                    // The conceding team died the moment the goal was scored
                    // (see OnGoalScored) and respawn-on-death has been
                    // suppressed since then so the deaths stay visible for
                    // the whole pause. Restore respawn right before the one
                    // authoritative kickoff restart brings everyone back.
                    RestoreGoalRespawnCvars();
                    if (!_nativeGoalRestartPending)
                    {
                        Server.ExecuteCommand("mp_restartgame 1");
                    }
                    _nativeGoalRestartPending = false;
                    _matchPhase = MatchPhase.Countdown;
                    _countdownRequiresBallActivation = true;
                    _kickoffBallActivityObserved = false;
                    _phaseTransitionAtServerTime = now + KickoffCountdownSeconds;
                    AnnounceAll($" \x04[Match]\x01 Kickoff in {KickoffCountdownSeconds:F0}s. The clock waits for the ball.");
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

    private void EnterLiveAfterCountdown(double now)
    {
        ReleasePausedBall(true);
        _matchPhase = MatchPhase.Live;
        if (_countdownRequiresBallActivation)
        {
            var activityObserved = _kickoffBallActivityObserved;
            BeginKickoffClockWait("countdown_complete");
            if (activityObserved || IsKickoffBallMoving())
            {
                ActivateKickoffClock("activity_during_countdown");
            }
        }
        else
        {
            _periodEndsAtServerTime = now + _pausedRemainingSeconds;
            _kickoffClockWaitingForBall = false;
            AnnounceAll($" \x04[Match]\x01 Period {_matchPeriod}/{_matchPeriods} is LIVE!");
            UpdateHostname();
        }
        _countdownRequiresBallActivation = false;
    }

    private void BeginKickoffClockWait(string reason)
    {
        _kickoffClockWaitingForBall = true;
        _kickoffBallActivityObserved = false;
        _periodEndsAtServerTime = 0.0;
        _nextScoreboardUpdateTime = 0.0;
        AnnounceAll(" \x04[Match]\x01 Kickoff is live. Match clock starts when the ball moves or is touched.");
        UpdateHostname();
        Logger.LogInformation(
            "[SM2DIAG] kickoff_clock_waiting reason={Reason} remaining={Remaining:F2}",
            reason,
            _pausedRemainingSeconds);
    }

    private bool IsKickoffBallMoving()
    {
        var planarSpeed = MathF.Sqrt(
            _derivedBallVelocity.X * _derivedBallVelocity.X
            + _derivedBallVelocity.Y * _derivedBallVelocity.Y);
        return planarSpeed >= KickoffBallActivePlanarSpeed || _ball?.TouchedByPlayer == true;
    }

    private void MatchOnBallActivity(string reason)
    {
        if (_matchPhase == MatchPhase.Countdown && _countdownRequiresBallActivation)
        {
            _kickoffBallActivityObserved = true;
            return;
        }
        if (_matchPhase == MatchPhase.Live && _kickoffClockWaitingForBall)
        {
            _kickoffBallActivityObserved = true;
            ActivateKickoffClock(reason);
        }
    }

    private void ActivateKickoffClock(string reason)
    {
        if (_matchPhase != MatchPhase.Live || !_kickoffClockWaitingForBall)
        {
            return;
        }

        _kickoffClockWaitingForBall = false;
        CompleteKickoffRestriction("ball_activity");
        _periodEndsAtServerTime = Server.TickedTime + _pausedRemainingSeconds;
        _nextScoreboardUpdateTime = 0.0;
        AnnounceAll($" \x04[Match]\x01 Ball active - period {_matchPeriod}/{_matchPeriods} clock started.");
        UpdateHostname();
        Logger.LogInformation(
            "[SM2DIAG] kickoff_clock_started reason={Reason} remaining={Remaining:F2}",
            reason,
            _pausedRemainingSeconds);
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
        // 2026-09-01 user request: goal detection should work even with no
        // match formally started ("!match start" was previously required -
        // scoring during Warmup silently did nothing, which read as the
        // conceding-team-death feature being broken when it was really just
        // never being reached). Warmup goals get the lightweight effect
        // only (HandleWarmupGoal, called from OnGoalScored) - no periods,
        // no kickoff restart, no persisted score. Every OTHER non-Live
        // phase (GoalPause, Countdown, PeriodBreak, Paused, Finished) stays
        // excluded: those are mid-transition windows for a real match and a
        // goal firing there would double up with logic already in flight.
        if (_matchPhase is not (MatchPhase.Live or MatchPhase.Warmup) || _goalLocked)
        {
            return false;
        }

        // Training menu "Disable Goals" (SoMoE training.sp control_goals):
        // only ever honoured outside a real match.
        if (_trainingGoalsDisabled && _matchPhase == MatchPhase.Warmup)
        {
            return false;
        }

        return TryGoalPlane(previous, current, GoalPlaneY)
            || TryGoalPlane(previous, current, -GoalPlaneY);
    }

    private bool TryGoalPlane(Vector previous, Vector current, float planeY)
    {
        // Only a crossing INTO the goal counts - from the pitch side of the
        // plane to the net side. A ball coming back out (net rebound, or a
        // wide ball that rolled in behind the post and out through the
        // mouth) must never fire; the old any-direction sign test could.
        var enteringPositiveEnd = planeY > 0.0f && previous.Y < planeY && current.Y >= planeY;
        var enteringNegativeEnd = planeY < 0.0f && previous.Y > planeY && current.Y <= planeY;
        if (!enteringPositiveEnd && !enteringNegativeEnd)
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

        var wide = MathF.Abs(crossX - GoalCenterX) > _goalHalfWidthX;
        var high = crossZ > _goalApertureMaxZ;
        var low = crossZ < _goalApertureMinZ;
        if (wide || high || low)
        {
            // Makes the "shot next to the goal" case provable from the
            // journal: the ball DID cross the line plane, outside the frame.
            Logger.LogInformation(
                "[SM2DIAG] goal_rejected reason={Reason} x={X:F1} z={Z:F1} planeY={PlaneY:F1} halfWidth={HalfWidth:F0} maxZ={MaxZ:F0}",
                wide ? "wide" : high ? "high" : "low",
                crossX,
                crossZ,
                planeY,
                _goalHalfWidthX,
                _goalApertureMaxZ);
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
        if (_matchPhase == MatchPhase.Warmup)
        {
            foreach (var entry in _statsStore.Entries) entry.Round = new();
            _teamRoundStats.Clear();
            HandleWarmupGoal(scoringTeam, x, z, planeY);
            return;
        }

        _goalLocked = true;
        _pausedRemainingSeconds = _kickoffClockWaitingForBall
            ? _pausedRemainingSeconds
            : Math.Max(0.0, _periodEndsAtServerTime - Server.TickedTime);

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

        // 2026-09-01 user request: the conceding team must die VISIBLY the
        // moment the goal is scored, and stay dead until the kickoff restart
        // respawns everyone - the deaths ARE the reset signal (CS:S SoMoE
        // feel). The old flow deferred the kill to the END of the 4s
        // GoalPause (right before mp_restartgame) and the server runs
        // mp_respawn_on_death_t/ct 1, so the kill was instantly swallowed by
        // an auto-respawn - players never saw anyone die. Suppress
        // respawn-on-death for the pause window, kill NOW, and restore the
        // cvars right before the restart (plus defensive restores at round
        // start / golden goal, so a missed path can never leave respawn
        // permanently broken for !kill).
        if (_goalPunishEnabled)
        {
            // ConVar.Find + SetValue is SYNCHRONOUS. Server.ExecuteCommand
            // goes through the engine command buffer and can apply AFTER
            // the kills below are processed, letting respawn-on-death
            // swallow the deaths in the same tick (part of the 2026-09-01
            // "still not dying" report).
            SetRespawnOnDeathCvars(false);
            _goalRespawnSuppressed = true;
            try
            {
                PunishConcedingTeam(concedingTeam);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SM2DIAG] goal_punish_failed team={Team}", concedingTeam);
            }
        }

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

        if (_inGoldenGoal)
        {
            // Sudden death - first goal ends it immediately, no restart-and-
            // resume like a normal in-match goal. The punish already
            // happened above; restore respawn right away since no kickoff
            // restart follows to do it for us.
            RestoreGoalRespawnCvars();
            FinishMatch();
            return;
        }

        if (_stoppageActive || (_menuParity.HalfwayStoppage && !_kickoffClockWaitingForBall && Server.TickedTime >= _periodEndsAtServerTime))
        { RestoreGoalRespawnCvars(); EndPeriod(); return; }

        BeginCelebration();
        if (_goalRoundWinEnabled && TryNativeRoundWin(scoringTeam, GoalPauseSeconds))
        {
            // The native round-end already schedules its own restart after
            // the delay, which fires EventRoundStart -> MatchOnRoundStart
            // (clears _goalLocked, re-stamps the scoreboard) exactly like
            // every other kickoff. Do NOT also set GoalPause/mp_restartgame
            // below - that would restart the round twice for one goal.
            _nativeGoalRestartPending = true;
            _matchPhase = MatchPhase.GoalPause;
            _phaseTransitionAtServerTime = Server.TickedTime + GoalPauseSeconds;
            return;
        }

        _matchPhase = MatchPhase.GoalPause;
        _phaseTransitionAtServerTime = Server.TickedTime + GoalPauseSeconds;
        // Deliberately NOT resetting the ball here: the kickoff restart a
        // few seconds later rebuilds it at centre anyway, so doing it twice
        // just made the ball visibly jump twice for one goal.
    }

    // 2026-09-01: the lightweight goal effect for when no match is running
    // (see MatchCheckGoalCrossing). Deliberately skips everything that is
    // real-match bookkeeping - _scoreCt/_scoreT, stats, the hostname/
    // scoreboard stamp, the kickoff wall - per explicit user request
    // ("nur der Effekt"). What it DOES do: announce the goal, kill the
    // conceding team the same way a real match does, then after a brief
    // pause bring them back and reset the ball. There is no kickoff
    // restart to do any of that for us here, so this path does it all
    // itself instead of just setting a phase and waiting.
    private void HandleWarmupGoal(CsTeam scoringTeam, float x, float z, float planeY)
    {
        _goalLocked = true;

        var ownGoal = _lastKickerTeam != CsTeam.None && _lastKickerTeam != scoringTeam;
        var concedingTeam = scoringTeam == CsTeam.CounterTerrorist ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
        var scorerName = _lastKickerSlot >= 0
            ? Utilities.GetPlayerFromSlot(_lastKickerSlot) is { IsValid: true } scorer ? scorer.PlayerName : "unknown"
            : "unknown";

        var message = ownGoal
            ? $" \x04[Match]\x01 OWN GOAL by {scorerName}!"
            : $" \x04[Match]\x01 GOAL by {scorerName} ({TeamName(scoringTeam)})!";
        AnnounceAll(message);
        BeginCelebration();

        Logger.LogInformation(
            "[SM2DIAG] goal_scored_warmup team={Team} ownGoal={OwnGoal} x={X:F1} z={Z:F1} planeY={PlaneY:F0}",
            scoringTeam,
            ownGoal,
            x,
            z,
            planeY);

        if (_goalPunishEnabled)
        {
            SetRespawnOnDeathCvars(false);
            _goalRespawnSuppressed = true;
            try
            {
                PunishConcedingTeam(concedingTeam);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[SM2DIAG] goal_punish_failed team={Team}", concedingTeam);
            }
        }

        AddTimer(GoalPauseSeconds, () =>
        {
            RestoreGoalRespawnCvars();
            // mp_respawn_on_death being back on only affects FUTURE deaths -
            // players killed above are still dead right now and need an
            // explicit respawn, since (unlike a real match goal) nothing
            // here calls mp_restartgame to do it for us.
            foreach (var player in Utilities.GetPlayers())
            {
                if (player.IsValid
                    && player.Team == concedingTeam
                    && player.PlayerPawn.Value is { IsValid: true } pawn
                    && !IsAlive(pawn))
                {
                    player.Respawn();
                }
            }

            // 2026-09-02: own goals still reset to centre (the scoring
            // team didn't earn field position), but a normal warmup goal
            // now leaves the ball where it landed - explicit user request.
            if (ownGoal)
            {
                ForceBallFullStop("warmup_goal_reset");
            }
            else
            {
                Logger.LogInformation("[SM2DIAG] warmup_goal ball_left_in_place");
            }

            _goalLocked = false;
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    // Idempotent: only touches the cvars if a goal actually suppressed
    // them. Called from the goal-pause exit, round start (defensive) and
    // the golden-goal finish path.
    private void RestoreGoalRespawnCvars()
    {
        if (!_goalRespawnSuppressed)
        {
            return;
        }

        _goalRespawnSuppressed = false;
        SetRespawnOnDeathCvars(true);
        Logger.LogInformation("[SM2DIAG] goal_respawn_restored");
    }

    private static void SetRespawnOnDeathCvars(bool enabled)
    {
        ConVar.Find("mp_respawn_on_death_t")?.SetValue(enabled);
        ConVar.Find("mp_respawn_on_death_ct")?.SetValue(enabled);
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

            try
            {
                // CONTROLLER CommitSuicide, NOT pawn.CommitSuicide: the
                // 2026-09-01 live logs proved the pawn variant is a silent
                // no-op in this build (goal_punish killed=1 logged while
                // the player demonstrably stayed alive), whereas !kill
                // (Kill.cs) uses the controller variant and reliably kills.
                player.CommitSuicide(false, true);
                killed++;

                // Never fly blind on this again: verify one frame later
                // whether the player is actually dead and say so in the
                // journal, so any future regression is provable from logs
                // alone instead of needing a live repro session.
                var verifySlot = player.Slot;
                var verifyName = player.PlayerName;
                Server.NextFrame(() =>
                {
                    var verifyPawn = Utilities.GetPlayerFromSlot(verifySlot)?.PlayerPawn.Value;
                    Logger.LogInformation(
                        "[SM2DIAG] goal_punish_verify slot={Slot} name={Name} aliveAfter={AliveAfter}",
                        verifySlot,
                        verifyName,
                        verifyPawn is { IsValid: true } && IsAlive(verifyPawn));
                });
            }
            catch (Exception ex)
            {
                // One player's CommitSuicide throwing must not skip the
                // rest of the team - see the try/catch around this call's
                // own call site for the full story.
                Logger.LogError(ex, "[SM2DIAG] goal_punish_player_failed slot={Slot} name={Name}", player.Slot, player.PlayerName);
            }
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
        _stoppageActive = false;
        _kickoffRestrictionActive = false; ClearKickoffOutline();
        AppendMatchLog($"PERIOD {_matchPeriod} ended score={_scoreCt}-{_scoreT}");
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
        FreezeBallForPause();
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
        FreezeBallForPause();
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
            _countdownRequiresBallActivation = true;
            _kickoffBallActivityObserved = false;
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

        foreach (var id in _draftAssignments.Keys.ToArray())
            _draftAssignments[id] = _draftAssignments[id] == CsTeam.Terrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
        var ctStats = TeamStats(_teamMatchStats, CsTeam.CounterTerrorist);
        _teamMatchStats[CsTeam.CounterTerrorist] = TeamStats(_teamMatchStats, CsTeam.Terrorist);
        _teamMatchStats[CsTeam.Terrorist] = ctStats;
        _teamRoundStats.Clear();
        foreach (var entry in _statsStore.Entries) entry.Round = new();
        _matchPeriod++;
        FreezeAllPlayers(false);
        Server.ExecuteCommand("mp_restartgame 1");
        _matchPhase = MatchPhase.Countdown;
        _pausedRemainingSeconds = _activePeriodLengthSeconds;
        _countdownRequiresBallActivation = true;
        _kickoffBallActivityObserved = false;
        _phaseTransitionAtServerTime = Server.TickedTime + KickoffCountdownSeconds;
        AnnounceAll($" \x04[Match]\x01 Teams swapped ends. Period {_matchPeriod}/{_matchPeriods} kicks off in {KickoffCountdownSeconds:F0}s.");
        Logger.LogInformation("[SM2DIAG] match_period_start period={Period}", _matchPeriod);
        StartKickoffRestriction(CsTeam.CounterTerrorist);
    }

    private void FinishMatch(CsTeam? forfeitWinner = null)
    {
        ReleasePausedBall(false);
        EndCelebration();
        _draftAssignments.Clear(); _matchWasCap = false; _capDraftCompleted = false;
        _kickoffRestrictionActive = false;
        ClearKickoffOutline();
        _matchPhase = MatchPhase.Finished;
        var winner = forfeitWinner is { } awarded ? $"{TeamName(awarded)} win by forfeit" : _scoreCt == _scoreT ? "Draw" : (_scoreCt > _scoreT ? $"{_teamNameCt} win" : $"{_teamNameT} win");
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
        // A website-created cap owns the temporary team/position assignments
        // only for the duration of this match.  Clear them immediately at
        // full time so players return to normal team selection without a map
        // reload; the website status poll will close the corresponding cap.
        ClearWebsiteCapState("match_finished");
        RestoreMatchOnlyTeamNames();
        StatsOnMatchFinished();
        FreezeAllPlayers(false);
        Server.NextFrame(() => _matchPhase = MatchPhase.Warmup);
    }

    // SoMoE updated the server's hostname with live match status
    // (soccer_mod.sp "gamestatus") - mirrors that with the info we track.
    private void UpdateHostname()
    {
        if (!_menuParity.HostnameInfo) return;
        var status = _matchPhase switch
        {
            MatchPhase.Live => _kickoffClockWaitingForBall
                ? "KICKOFF"
                : _inGoldenGoal ? "GOLDEN GOAL" : "LIVE",
            MatchPhase.Countdown => "KICKOFF",
            MatchPhase.GoalPause => "GOAL!",
            MatchPhase.Paused => "PAUSED",
            MatchPhase.Finished => "FULL TIME",
            MatchPhase.PeriodBreak => _inGoldenGoal ? "GOLDEN GOAL BREAK" : "HALF-TIME",
            // SoMoE HostName_Change_Status("Specced"/"Capfight"/"Picking")
            // from the cap flow (Cap.cs), shown until a match starts.
            _ => _capHostnameStatus ?? "WARMUP",
        };
        Server.ExecuteCommand($"hostname \"KA Soccer Mod - Public Server | {_teamNameCt} {_scoreCt} - {_scoreT} {_teamNameT} | {status}\"");
    }

    // SoMoE's soccer_mod_last_match.txt equivalent: overwritten fresh at
    // match start, appended per goal, closed with the final line.
    private void AppendMatchLog(string line)
    {
        if (!LogActive || (!_menuParity.MatchLogGoals && line.StartsWith("GOAL "))
            || (!_menuParity.MatchLogCards && line.StartsWith("Card", StringComparison.OrdinalIgnoreCase))
            || (!_menuParity.LogPauses && (line.StartsWith("PAUSE ") || line.StartsWith("RESUME ")))
            || (!_menuParity.LogPeriods && (line.StartsWith("PERIOD ") || line.StartsWith("STOPPAGE ")))) return;
        _matchLogLines.Insert(0, $"{DateTime.Now:HH:mm} {line}");
        if (_matchLogLines.Count > MatchLogMaxLines)
        {
            _matchLogLines.RemoveRange(MatchLogMaxLines, _matchLogLines.Count - MatchLogMaxLines);
        }

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

    // SoMoE match.sp NameReset on MatchStop: a "[Match]" (match-only) team
    // name only lasts for that match.
    private void RestoreMatchOnlyTeamNames()
    {
        _teamNameCt = _permanentTeamNameCt;
        _teamNameT = _permanentTeamNameT;
    }

    private void SetTeamName(CsTeam team, string name, bool permanent, CCSPlayerController? actor)
    {
        if (!RequirePublicControl(actor, true)) return;
        name = name.Trim();
        if (name.Length == 0)
        {
            return;
        }

        if (team == CsTeam.CounterTerrorist)
        {
            _teamNameCt = name;
            if (permanent)
            {
                _permanentTeamNameCt = name;
            }
        }
        else
        {
            _teamNameT = name;
            if (permanent)
            {
                _permanentTeamNameT = name;
            }
        }

        var sideLabel = team == CsTeam.CounterTerrorist ? "CTs" : "Terrorists";
        var actorName = actor?.PlayerName ?? "RCON";
        if (permanent)
        {
            SaveMatchSettings("teamname_menu");
            AnnounceAll($" \x04[SM]\x01 {actorName} has set the name of the {sideLabel} to {name}");
        }
        else
        {
            AnnounceAll($" \x04[SM]\x01 {actorName} has set the name of the {sideLabel} for this match to {name}");
        }

        UpdateHostname();
        UpdateTeamScoreboard();
    }

    // Shared by the menu (SoMoE "Start / Stop" toggle) and css_match stop.
    private void StopMatch(string by)
    {
        ReleasePausedBall(false);
        EndCelebration();
        _draftAssignments.Clear(); _matchWasCap = false; _capDraftCompleted = false;
        _kickoffRestrictionActive = false;
        ClearKickoffOutline();
        AppendMatchLog($"STOP by={by}");
        _stoppageActive = false;
        _matchPhase = MatchPhase.Warmup;
        _kickoffClockWaitingForBall = false;
        _countdownRequiresBallActivation = false;
        _goalLocked = false;
        _readyPlayers.Clear();
        _forfeitVotes.Clear();
        _forfeitVoteTeam = CsTeam.None;
        // Stop is also the explicit end signal for a web cap.  This
        // restores original clan tags and removes imported team
        // assignments immediately, without requiring a map reload.
        ClearWebsiteCapState("match_stopped");
        RestoreMatchOnlyTeamNames();
        FreezeAllPlayers(false);
        UpdateHostname();
        AnnounceAll($" \x04[Match]\x01 {by} has stopped the match");
        Logger.LogInformation("[SM2DIAG] match_stopped by={By}", by);
    }

    // Returns false (with the SoMoE reason) when there is nothing to pause.
    private bool PauseMatch(out string failure)
    {
        EndCelebration();
        if (_matchPhase == MatchPhase.Paused)
        {
            failure = "Match already paused";
            return false;
        }

        if (_matchPhase != MatchPhase.Live)
        {
            failure = "No match started";
            return false;
        }

        _pausedRemainingSeconds = _kickoffClockWaitingForBall
            ? _pausedRemainingSeconds
            : _periodEndsAtServerTime - Server.TickedTime;
        _countdownRequiresBallActivation = _kickoffClockWaitingForBall;
        _kickoffClockWaitingForBall = false;
        _matchPhase = MatchPhase.Paused;
        FreezeBallForPause();
        _readyPlayers.Clear();
        FreezeAllPlayers(true);
        BeginReadyCheck();
        AppendMatchLog("PAUSE match paused");
        AnnounceAll(" \x04[Match]\x01 Match paused. Type !rdy when you're ready to continue.");
        UpdateHostname();
        failure = string.Empty;
        return true;
    }

    private void UpdateScoreboardDisplay(double now)
    {
        var remaining = _kickoffClockWaitingForBall
            ? Math.Max(0.0, _pausedRemainingSeconds)
            : Math.Max(0.0, _periodEndsAtServerTime - now);
        var minutes = (int)(remaining / 60.0);
        var seconds = (int)(remaining % 60.0);
        var periodLabel = _inGoldenGoal ? "golden goal" : $"period {_matchPeriod}/{_matchPeriods}";
        var kickoffLabel = _kickoffClockWaitingForBall ? " - WAITING FOR BALL" : string.Empty;
        var text = !_menuParity.MatchInfo ? "" : $"{_teamNameCt} {_scoreCt} - {_scoreT} {_teamNameT}\n{minutes}:{seconds:D2}  ({periodLabel}{kickoffLabel}){(_stoppageActive ? " STOPPAGE" : "")}";
        foreach (var player in Utilities.GetPlayers())
        {
            // Both writers target the same centre-screen HUD region; without
            // this the score ticker clobbers an open !menu panel every
            // second (root cause of the menu "disappearing" during a live
            // match).
            if (player.IsValid && !_openMenus.ContainsKey(player.Slot))
            {
                if (text.Length > 0) player.PrintToCenter(text);
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
        var body = System.Text.RegularExpressions.Regex.Replace(message, @"[\x01-\x10]", "").TrimStart();
        foreach (var prefix in new[] { "[SM]", "[Match]", "[Soccer Mod]" })
            if (body.StartsWith(prefix, StringComparison.Ordinal)) { body = body[prefix.Length..].TrimStart(); break; }
        message = FormatSoccerModMessage(body);
        foreach (var player in Utilities.GetPlayers())
        {
            if (player.IsValid)
            {
                player.PrintToChat(message);
            }
        }
    }

    // 2026-09-01: no permission gate any more - SoMoE publicmode 2 parity
    // (the live CS:S server's setting), the same rule the Match menu in
    // !menu follows for everyone. css_rr stays admin-only as sm_rr was.
    private void OnMatchCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (command.ArgCount >= 2 && command.GetArg(1).ToLowerInvariant() != "status" && !RequirePublicControl(player)) return;
        if (player is { IsValid: true } && command.ArgCount < 2)
        {
            OpenMatchMenu(player);
            return;
        }

        var sub = command.ArgCount >= 2 ? command.GetArg(1).ToLowerInvariant() : "status";
        switch (sub)
        {
            case "start":
                var requestedLength = command.ArgCount >= 3 ? command.GetArg(2).ToLowerInvariant() : "default";
                if (requestedLength == "cap")
                {
                    if (!TryGetWebsiteCapReference(out var capHalfSeconds))
                    {
                        command.ReplyToCommand("[SM] no active KICKOFF cap reference is available");
                        break;
                    }
                    StartMatch(capHalfSeconds, "cap_reference");
                }
                else if (requestedLength == "default")
                {
                    StartMatch(_periodLengthSeconds, "default");
                }
                else
                {
                    command.ReplyToCommand("[SM] usage: css_match start <cap|default>");
                    break;
                }
                command.ReplyToCommand(
                    $"[SM] match starting ({FormatHalfMinutes(_activePeriodLengthSeconds)} min/half, {_matchLengthSource})");
                break;

            case "stop":
                if (_matchPhase is MatchPhase.Warmup or MatchPhase.Finished)
                {
                    command.ReplyToCommand("[SM] No match started");
                    break;
                }
                StopMatch(player?.PlayerName ?? "RCON");
                break;

            case "pause":
                if (!PauseMatch(out var pauseFailure))
                {
                    command.ReplyToCommand($"[SM] {pauseFailure}");
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
        foreach (var (slot, menu) in _openMenus.ToArray())
            if (menu.Title == "Match - Ready Check") CloseMenu(slot, "resume");
        AppendMatchLog($"RESUME {reason}");
        FreezeAllPlayers(false);
        _matchPhase = MatchPhase.Countdown;
        _kickoffBallActivityObserved = false;
        _phaseTransitionAtServerTime = Server.TickedTime + KickoffCountdownSeconds;
        _readyPlayers.Clear();
        AnnounceAll($" \x04[Match]\x01 Resuming in {KickoffCountdownSeconds:F0}s.");
        UpdateHostname();
        Logger.LogInformation("[SM2DIAG] match_resumed reason={Reason}", reason);
    }

    private void OnReadyCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null) SetPlayerReady(player, true);
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

        if (!_menuParity.ForfeitEnabled || (_menuParity.ForfeitCapOnly && !_matchWasCap)
            || (!_menuParity.ForfeitPublic && !HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0, "admin")))
        { command.ReplyToCommand("[SM] Forfeit voting is unavailable with the current settings."); return; }
        var deficit = player.Team == CsTeam.CounterTerrorist ? _scoreT - _scoreCt : _scoreCt - _scoreT;
        if (_menuParity.ForfeitGoalDifference > 0 && deficit < _menuParity.ForfeitGoalDifference)
        { command.ReplyToCommand($"[SM] Your team must be at least {_menuParity.ForfeitGoalDifference} goals behind."); return; }
        // Discard votes from disconnected or switched players before counting.
        var eligibleVoters = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && p.Team == player.Team).Select(p => p.Slot).ToHashSet();
        if (_forfeitVoteTeam != CsTeam.None && _forfeitVoteTeam != player.Team)
        {
            command.ReplyToCommand("[SM] the other team already has a forfeit vote in progress");
            return;
        }

        _forfeitVotes.IntersectWith(eligibleVoters);
        _forfeitVoteTeam = player.Team;
        if (!_forfeitVotes.Add(player.Slot))
        {
            command.ReplyToCommand("[SM] you already voted to forfeit");
            return;
        }

        var teamPlayers = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && p.Team == player.Team).ToList();
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
        FinishMatch(winningTeam);
        if (_menuParity.ForfeitAutoSpec)
            foreach (var voter in teamPlayers.Where(p => p.IsValid)) voter.ChangeTeam(CsTeam.Spectator);
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
        if (side is not ("ct" or "t"))
        {
            command.ReplyToCommand("[SM] usage: css_teamname <ct|t> <name...>");
            return;
        }

        SetTeamName(side == "ct" ? CsTeam.CounterTerrorist : CsTeam.Terrorist, name, permanent: true, player);
        command.ReplyToCommand($"[SM] CT='{_teamNameCt}' T='{_teamNameT}'");
    }

    private void StartMatch(float halfSeconds, string lengthSource)
    {
        ReleasePausedBall(false);
        EndCelebration();
        _stoppageActive = false;
        _readyPlayers.Clear(); _readyRoster.Clear();
        _matchWasCap = _capDraftCompleted || IsWebsiteCapActive();
        _capDraftCompleted = false;
        _capPicksLeft = 0;
        if (!_matchWasCap || IsWebsiteCapActive()) _draftAssignments.Clear();
        _capRosterCaptured = false; _capEligible.Clear(); _preCapJoin.Clear();
        ResetMatchStats();
        _activePeriodLengthSeconds = halfSeconds;
        _matchLengthSource = lengthSource;
        _scoreCt = 0;
        _scoreT = 0;
        _matchPeriod = 1;
        _teamsSwapped = false;
        _goalLocked = false;
        RestoreGoalRespawnCvars();
        if (_matchPhase == MatchPhase.Countdown && _countdownRequiresBallActivation)
        {
            // Discard touches/motion from the pre-restart ball. Only activity
            // on the freshly spawned kickoff ball may start the clock.
            _kickoffBallActivityObserved = false;
        }
        _inGoldenGoal = false;
        _nativeGoalRestartPending = false;
        _kickoffClockWaitingForBall = false;
        _kickoffBallActivityObserved = false;
        _countdownRequiresBallActivation = true;
        _forfeitVotes.Clear();
        _forfeitVoteTeam = CsTeam.None;
        _goalsBySlot.Clear();
        _capHostnameStatus = null;
        if (_capFightPending || _capFightStarted)
        {
            EndCapFight(null, "match_start");
        }
        TrainingOnMatchStart();
        AfkDisarm("match_start");
        FreezeAllPlayers(false);
        UpdateTeamScoreboard();
        Server.ExecuteCommand("mp_restartgame 1");
        StartKickoffRestriction(CsTeam.CounterTerrorist);
        _matchPhase = MatchPhase.Countdown;
        _pausedRemainingSeconds = _activePeriodLengthSeconds;
        _phaseTransitionAtServerTime = Server.TickedTime + KickoffCountdownSeconds;
        AnnounceAll($" \x04[Match]\x01 Match starting! Period 1/{_matchPeriods} kicks off in {KickoffCountdownSeconds:F0}s.");
        Logger.LogInformation(
            "[SM2DIAG] match_started periods={Periods} periodLength={PeriodLength} lengthSource={LengthSource}",
            _matchPeriods,
            _activePeriodLengthSeconds,
            _matchLengthSource);
        _matchLogLines.Clear();
        if (LogActive)
        {
            try { File.WriteAllText(ConfigPath(MatchLogFileName), string.Empty); }
            catch (Exception ex) { Logger.LogWarning(ex, "[SM2DIAG] match_log_write_failed"); }
            AppendMatchLog($"MATCH START {_teamNameCt} vs {_teamNameT}");
        }
        if (_menuParity.InfoPeriod) AnnounceAll($"[SM] Periods: {_matchPeriods} x {_activePeriodLengthSeconds / 60:0.##} minutes.");
        if (_menuParity.InfoBreak) AnnounceAll($"[SM] Break: {_breakLengthSeconds:0} seconds.");
        if (_menuParity.InfoGolden) AnnounceAll($"[SM] Golden goal: {OnOff(_goldenGoalEnabled)}.");
        if (_menuParity.InfoForfeit) AnnounceAll($"[SM] Forfeit: {OnOff(_menuParity.ForfeitEnabled)}.");
        if (_menuParity.InfoForfeitSettings) AnnounceAll($"[SM] Forfeit deficit: {_menuParity.ForfeitGoalDifference}; CAP only: {OnOff(_menuParity.ForfeitCapOnly)}.");
        if (_menuParity.InfoLog) AnnounceAll($"[SM] Match log: {OnOff(LogActive)}.");
        UpdateHostname();
    }

    private void OnRoundRestartCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        // 2026-09-01 user report: the match clock kept running through a
        // manual !rr even though nobody had touched the fresh kickoff ball
        // yet. A goal already re-arms this correctly (OnGoalScored sets
        // GoalPause -> Countdown -> BeginKickoffClockWait); !rr just fired
        // mp_restartgame directly and never entered that sequence at all.
        // Mirror it here so both paths behave identically - only matters
        // when a period clock is actually running.
        if (_matchPhase == MatchPhase.Live)
        {
            _pausedRemainingSeconds = _kickoffClockWaitingForBall
                ? _pausedRemainingSeconds
                : Math.Max(0.0, _periodEndsAtServerTime - Server.TickedTime);
            _matchPhase = MatchPhase.Countdown;
            _countdownRequiresBallActivation = true;
            _kickoffBallActivityObserved = false;
            _phaseTransitionAtServerTime = Server.TickedTime + KickoffCountdownSeconds;
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

        StartMatch(_periodLengthSeconds, "default");
        command.ReplyToCommand("[SM] match restarted");
    }

    private void OnMapReloadCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePublicControl(player, true)) return;
        // 2026-09-01 user decision: open to EVERYONE, deliberately without
        // any cooldown or player-count guard ("Komplett ohne Schutz").

        // changelevel loses the Workshop addon context on this map - re-issuing
        // the same host_workshop_map command is what keeps it (documented, hard
        // rule from the ball-foundation work). If this ever stops working after
        // a CS2 update, the systemd service restart remains the safety net.
        command.ReplyToCommand("[SM] reloading workshop map, this takes a few seconds...");
        Logger.LogInformation("[SM2DIAG] map_reload_requested by={By}", player?.PlayerName ?? "RCON");
        Server.ExecuteCommand("host_workshop_map 3361075564");
    }

    // 2026-09-01 user report: shots that visually pass ABOVE the goal
    // frame/crossbar still count as a goal - the aperture code itself is
    // already correct (crossZ > _goalApertureMaxZ rejects it), so this is a
    // calibration problem, not a logic bug. _goalApertureMaxZ=120 was never
    // actually measured against the map's real crossbar geometry (unlike
    // GoalPlaneY/StadiumPitchPlaneZ, which were). Trace straight down at
    // both goal mouths, at three points across the width, to find the real
    // frame height instead of guessing a replacement number.
    private void OnGoalMeasureCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command))
        {
            return;
        }

        var kneeZ = StadiumPitchPlaneZ + 50.0f;
        foreach (var sign in new[] { 1.0f, -1.0f })
        {
            var lineY = sign * _goalLineY;

            // Crossbar: straight down at three points across the (current)
            // width, as before.
            foreach (var offsetX in new[] { -_goalHalfWidthX + 5.0f, 0.0f, _goalHalfWidthX - 5.0f })
            {
                var start = new Vector(GoalCenterX + offsetX, lineY, StadiumPitchPlaneZ + 400.0f);
                var end = new Vector(GoalCenterX + offsetX, lineY, StadiumPitchPlaneZ);
                var trace = Trace.TraceEndShape(start, end, null, new TraceOptions { InteractsWith = Masks.Solid });
                var hitAboveGround = trace.DidHit() ? trace.EndPos.Z - StadiumPitchPlaneZ : -1.0f;
                Logger.LogInformation(
                    "[SM2DIAG] goal_measure planeY={PlaneY:F0} offsetX={OffsetX:F0} hit={Hit} hitZ={HitZ:F1} heightAboveGround={HeightAboveGround:F1}",
                    lineY,
                    offsetX,
                    trace.DidHit(),
                    trace.DidHit() ? trace.EndPos.Z : 0.0f,
                    hitAboveGround);
            }

            // Posts: from the centre line outward along +-X at knee height,
            // at the goal line and a little behind it. The first solid hit
            // is the INNER face of the post -> the real half-width.
            foreach (var depth in new[] { 0.0f, 20.0f, 40.0f })
            {
                var y = lineY + sign * depth;
                foreach (var dir in new[] { 1.0f, -1.0f })
                {
                    var start = new Vector(GoalCenterX, y, kneeZ);
                    var end = new Vector(GoalCenterX + dir * 600.0f, y, kneeZ);
                    var trace = Trace.TraceEndShape(start, end, null, new TraceOptions { InteractsWith = Masks.Solid });
                    Logger.LogInformation(
                        "[SM2DIAG] goal_measure_post end={End} y={Y:F0} dir={Dir} hit={Hit} postInnerX={PostX:F1}",
                        sign > 0 ? "positive" : "negative",
                        y,
                        dir > 0 ? "+x" : "-x",
                        trace.DidHit(),
                        trace.DidHit() ? MathF.Abs(trace.EndPos.X) : -1.0f);
                }
            }

            // Goal line / net depth: from midfield toward this end along Y
            // at knee height (x = 0 hits the net pocket back wall, x beyond
            // the posts hits whatever stands beside the goal), and from the
            // line itself further in to find the backstop.
            foreach (var x in new[] { 0.0f, 100.0f, -100.0f, 250.0f, -250.0f })
            {
                var start = new Vector(x, 0.0f, kneeZ);
                var end = new Vector(x, sign * 1600.0f, kneeZ);
                var trace = Trace.TraceEndShape(start, end, null, new TraceOptions { InteractsWith = Masks.Solid });
                Logger.LogInformation(
                    "[SM2DIAG] goal_measure_depth end={End} x={X:F0} hit={Hit} hitY={HitY:F1}",
                    sign > 0 ? "positive" : "negative",
                    x,
                    trace.DidHit(),
                    trace.DidHit() ? trace.EndPos.Y : 0.0f);
            }
        }

        command.ReplyToCommand(
            $"[SM2DIAG] goal geometry measured - journal: goal_measure (crossbar), goal_measure_post (inner post X), goal_measure_depth (line/backstop Y). "
            + $"Current: lineY={_goalLineY:F0} depth={_goalDepthRequired:F1} plane={GoalPlaneY:F1} halfWidth={_goalHalfWidthX:F0} maxZ={_goalApertureMaxZ:F0}");
    }

    private void OnGoalCalibCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        if (command.ArgCount < 3
            || !float.TryParse(command.GetArg(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var halfWidth)
            || !float.TryParse(command.GetArg(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var maxZ))
        {
            command.ReplyToCommand(
                $"[SM] usage: css_sm2goal_calib <halfWidth> <maxHeight> [lineY] [depth] "
                + $"(current: halfWidth={_goalHalfWidthX:F0} maxHeight={_goalApertureMaxZ:F0} lineY={_goalLineY:F0} depth={_goalDepthRequired:F1} -> plane={GoalPlaneY:F1})");
            return;
        }

        _goalHalfWidthX = Math.Clamp(halfWidth, 20.0f, 500.0f);
        _goalApertureMaxZ = Math.Clamp(maxZ, 0.0f, 400.0f);
        if (command.ArgCount >= 4
            && float.TryParse(command.GetArg(3), NumberStyles.Float, CultureInfo.InvariantCulture, out var lineY))
        {
            _goalLineY = Math.Clamp(lineY, 1000.0f, 1500.0f);
        }
        if (command.ArgCount >= 5
            && float.TryParse(command.GetArg(4), NumberStyles.Float, CultureInfo.InvariantCulture, out var depth))
        {
            _goalDepthRequired = Math.Clamp(depth, 0.0f, 60.0f);
        }
        SaveMatchSettings("goal_calib_command");
        command.ReplyToCommand(
            $"[SM] goal: halfWidth={_goalHalfWidthX:F0} maxHeight={_goalApertureMaxZ:F0} lineY={_goalLineY:F0} depth={_goalDepthRequired:F1} -> detection plane={GoalPlaneY:F1}");
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
                ClearKickoffOutline();
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
        UnfreezeBallForPlay("goal_test");
        _ball.Teleport(
            position: new Vector(GoalCenterX, startY, BallResetZ),
            velocity: new Vector(0.0f, toward * 800.0f, 0.0f));
        ResetDerivedMotion();
        command.ReplyToCommand($"[SM2DIAG] goal test ball launched toward y={planeY:F0}");
    }
}

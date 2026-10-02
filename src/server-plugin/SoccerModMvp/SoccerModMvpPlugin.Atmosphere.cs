using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-29 owner: "Arena Vision" - a stadium director that reads the game
// and conducts the stadium (plan: Phase 0 on the test server first). Phase 0
// runs it silently: every moment (shot, near miss, post, save, the kind of
// goal, kickoff, match start, half time, full time) moves the hype value
// (AtmosphereRules.cs) and is logged as "atmo_moment", so the detection can
// be checked in real games before any instrument (crowd, lights, boards,
// pyro) listens to it. Flag file soccermod_atmo.enabled (27018 only).
// Admin/RCON: css_sm2atmo status | moment <kind> [t|ct] | hype <0-100> | lab ...
public sealed partial class SoccerModMvpPlugin
{
    private const string AtmoFlagFile = "soccermod_atmo.enabled";
    private const double AtmoUpdateSeconds = 0.25;

    private float _atmoHype = AtmosphereRules.HypeStart;
    private double _atmoLastUpdate;
    private readonly List<string> _atmoRecent = new();

    // 2026-09-29 owner: all stadium effects only on the v8 stadium (soccer_cssl_stadium_v8).
    private bool AtmoOn => FlagFileOn(AtmoFlagFile) && (string.Equals(_currentMapName, FoundationMapName, StringComparison.OrdinalIgnoreCase) || OnArena || OnHall) && AtmoSet.Director;

    private void AtmosphereOnLoad(bool hotReload)
    {
        AddCommand("css_sm2atmo", "Admin: stadium director status | moment <kind> [t|ct] | hype <0-100> | lab <n>|list|clear", OnAtmoCommand);
        RegisterListener<Listeners.OnMapStart>(_ => AtmoDirectorMapStart());
        AtmoLabOnLoad(hotReload);
        AtmoBannersOnLoad();
        AtmoCrowdOnLoad();
        BrandBoardsOnLoad();   // brand boards, before the LED boards (BrandBoards.cs)
        AtmoBoardsOnLoad();
        AtmoRingOnLoad();
        AtmoDugoutsOnLoad();
        AtmoAnnouncerOnLoad();
        AtmoSpiderOnLoad();
        AtmoCrowdSoundOnLoad();
        if (hotReload) AddTimer(0.5f, AtmoHotReload);
    }

    // Hot reload: OnMapStart does not fire, so remove what the old plugin instance spawned
    // (everything is named sm2_atmo*) and run every module's map start (review 2026-09-29).
    private void AtmoHotReload()
    {
        var removed = 0;
        foreach (var cls in new[] { "prop_dynamic", "info_particle_system", "point_worldtext", "beam" })
            foreach (var e in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(cls))
                if (e.IsValid && e.Entity?.Name is { } name && name.StartsWith("sm2_atmo", StringComparison.Ordinal)) { e.Remove(); removed++; }
        AtmoDirectorMapStart(); AtmoBannersMapStart(); AtmoCrowdMapStart(); AtmoBoardsMapStart(); AtmoRingMapStart(); AtmoDugoutsMapStart();
        AtmoAnnouncerMapStart(); AtmoSpiderMapStart(); AtmoCrowdSoundMapStart();
        Logger.LogInformation("[SM2DIAG] atmo_hot_reload removed={Removed}", removed);
    }

    // From the main OnTick; the director itself only needs a few updates a second.
    private void AtmoOnTick()
    {
        AtmoSpiderOnTick();
        if (AtmoOn) AtmoDugoutsOnTick();
        var now = (double)Server.TickedTime;
        var dt = now - _atmoLastUpdate;
        // Server time can start again at a map change: re-anchor instead of stalling (review 2026-09-29).
        if (dt < 0) { _atmoLastUpdate = now; return; }
        if (dt < AtmoUpdateSeconds) return;
        _atmoLastUpdate = now;
        if (dt > 1.0) dt = AtmoUpdateSeconds; // map change / hibernation
        AtmoLabOnTick();
        if (!AtmoOn) return;
        var pressure = _ball is { IsValid: true } ball && ball.AbsOrigin is { } origin
            ? AtmosphereRules.Pressure(ToPitchLocal(origin).Y)
            : 0.0f;
        var secondsLeft = _matchPhase == MatchPhase.Live ? _periodEndsAtServerTime - now : -1.0;
        var closeLate = secondsLeft is >= 0.0 and < 120.0 && Math.Abs(_scoreT - _scoreCt) <= 1;
        _atmoHype = AtmosphereRules.Step(_atmoHype, AtmosphereRules.BaseLevel(pressure, closeLate), (float)dt);
        if (secondsLeft >= 0) AtmoAnnounceClock(secondsLeft);
    }

    private void AtmoFire(AtmoMoment moment, string detail = "")
    {
        if (!AtmoOn) return;
        var before = _atmoHype;
        _atmoHype = AtmosphereRules.Add(_atmoHype, moment);
        AtmoRingMoment(moment);
        AtmoAnnounceMoment(moment);
        AtmoCrowdSoundMoment(moment);
        var clock = TimeSpan.FromSeconds(Server.CurrentTime);
        _atmoRecent.Insert(0, $"{clock:mm\\:ss} {moment} {detail}".TrimEnd());
        if (_atmoRecent.Count > 8) _atmoRecent.RemoveAt(_atmoRecent.Count - 1);
        Logger.LogInformation("[SM2DIAG] atmo_moment kind={Kind} hype={Before:F0}->{After:F0} mood={Mood} phase={Phase} {Detail}",
            moment, before, _atmoHype, AtmosphereRules.Mood(_atmoHype), _matchPhase, detail);
    }

    // ---- hooks (one line each in the existing code) ------------------------------------------

    // OnGoalScored (Match.cs), before the score is counted.
    private void AtmoGoal(CsTeam scoringTeam, float x, float z, float planeY)
    {
        if (!AtmoOn) return;
        var ownGoal = _lastKickerTeam != CsTeam.None && _lastKickerTeam != scoringTeam;
        var scoringAfter = (scoringTeam == CsTeam.CounterTerrorist ? _scoreCt : _scoreT) + 1;
        var other = scoringTeam == CsTeam.CounterTerrorist ? _scoreT : _scoreCt;
        var scorerAfter = ownGoal || _lastKickerSlot < 0 ? 0 : _goalsBySlot.GetValueOrDefault(_lastKickerSlot) + 1;
        var secondsLeft = _matchPhase == MatchPhase.Live ? _periodEndsAtServerTime - Server.TickedTime : -1.0;
        var kind = AtmosphereRules.ClassifyGoal(scoringAfter, other, ownGoal, scorerAfter, secondsLeft);
        var scorer = _lastKickerSlot >= 0 && Utilities.GetPlayerFromSlot(_lastKickerSlot) is { IsValid: true } p ? p.PlayerName : "?";
        // 2026-10-01 owner: an own goal is celebrated too - by the team that gets it (no personal celebration).
        AtmoGoalShow(scoringTeam, planeY > 0 ? 1 : -1, kind == AtmoMoment.OwnGoal ? -1 : _lastKickerSlot);
        AtmoFire(kind, $"team={scoringTeam} scorer=\"{scorer}\" x={x:F0} z={z:F0} speed={_atmoLastShotSpeed:F0}");
        AtmoAnnounceGoal(scoringTeam, kind, _atmoLastShotSpeed);
    }

    private float _atmoLastShotSpeed;

    // After kick_accepted (SoccerModMvpPlugin.cs): only real shots count.
    private void AtmoKick(CCSPlayerController player, float finalSpeed)
    {
        _atmoLastShotSpeed = finalSpeed;
        if (finalSpeed >= AtmosphereRules.ShotMinSpeed) AtmoFire(AtmoMoment.Shot, $"slot={player.Slot} speed={finalSpeed:F0}");
    }

    private void AtmoNearMiss(float x, float z) => AtmoFire(AtmoMoment.NearMiss, $"x={x:F0} z={z:F0}");

    private void AtmoPost(bool crossbar, Vector origin)
    {
        AtmoPostSparks(origin);
        AtmoFire(AtmoMoment.Post, $"part={(crossbar ? "crossbar" : "post")} at={FormatVector(origin)}");
    }

    private void AtmoSave(CCSPlayerController saver) => AtmoFire(AtmoMoment.Save, $"slot={saver.Slot} team={saver.Team}");

    private void AtmoKickoff() => AtmoFire(AtmoMoment.Kickoff, $"score={_scoreT}:{_scoreCt}");

    private void AtmoMatchPhase(AtmoMoment moment)
    {
        AtmoFire(moment, $"score={_scoreT}:{_scoreCt}");
        AtmoShowPhase(moment);
    }

    // ---- admin ------------------------------------------------------------------------------------

    private void OnAtmoCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin")) return;
        var arg = command.ArgCount >= 2 ? command.GetArg(1).ToLowerInvariant() : "status";
        switch (arg)
        {
            case "moment" when command.ArgCount >= 3 && Enum.TryParse<AtmoMoment>(command.GetArg(2), true, out var moment):
                AtmoFire(moment, "source=command");
                AtmoShowPhase(moment);
                break;
            case "hype" when command.ArgCount >= 3 && float.TryParse(command.GetArg(2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hype):
                _atmoHype = Math.Clamp(hype, AtmosphereRules.HypeMin, AtmosphereRules.HypeMax);
                break;
            case "crowdsound" when command.ArgCount >= 3:
                // test: css_sm2atmo crowdsound Applause
                AtmoCrowdSound(command.GetArg(2));
                break;
            case "say" when command.ArgCount >= 3:
                // test: css_sm2atmo say GoalRed
                AtmoAnnounce(command.GetArg(2), true);
                break;
            case "spiderhit":
                command.ReplyToCommand("[SM] " + AtmoSpiderHitTest());
                break;
            case "lab":
                OnAtmoLabCommand(player, command);
                return;
            case "status":
                break;
            default:
                command.ReplyToCommand("[SM] usage: css_sm2atmo status | moment <Shot|NearMiss|Post|Save|Goal|Equaliser|Lead|LastMinute|HatTrick|OwnGoal|Kickoff|MatchStart|HalfTime|FullTime> | hype <0-100> | lab <n>|list|clear");
                return;
        }
        command.ReplyToCommand($"[SM] Stadium director: flag={AtmoOn} hype={_atmoHype:F0} mood={AtmosphereRules.Mood(_atmoHype)} phase={_matchPhase} lab={_atmoLabEntities.Count(e => e.IsValid)} entities");
        foreach (var line in _atmoRecent.Take(6)) command.ReplyToCommand($"[SM]   {line}");
    }

    // Map start (also run after a plugin hot reload, AtmoHotReload).
    private void AtmoDirectorMapStart()
    {
        _atmoHype = AtmosphereRules.HypeStart;
        _atmoRecent.Clear();
        _atmoLastUpdate = 0;
    }
}

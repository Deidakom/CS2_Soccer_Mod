using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-29 "Arena Vision" stadium announcer (v8 only, admin toggle
// "Announcer"). Owner: generated voice, English; picked "George A" (Kokoro-82M,
// Apache-2.0, bm_george, faster + stadium PA processing; lines in
// soundevents/soccermod_atmo.vsndevts). Heard by everyone with "Stadium"
// sounds on. Goals: "GOAL for the Reds/Blues!", then what kind of goal it was
// (equaliser, lead, late, hat-trick, a rocket). Also saves, posts, near misses
// (with cooldowns so he never chatters), welcome at a started match, kick-off,
// second half, one minute to go, half time, full time.
public sealed partial class SoccerModMvpPlugin
{
    private const string AtmoSoundEventsFile = "soundevents/soccermod_atmo.vsndevts";
    private const double AtmoAnnounceGap = 2.5, AtmoAnnounceChanceCooldown = 10.0;
    private const float AtmoAnnounceRocketSpeed = 2200f;

    private double _atmoAnnounceLast = -100, _atmoAnnounceChanceLast = -100;
    private bool _atmoAnnounceKickoffPending, _atmoAnnounceSecondHalfPending, _atmoAnnounceOneMinuteDone;

    private void AtmoAnnouncerOnLoad()
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            if (File.Exists(ConfigPath(AtmoFlagFile))) manifest.AddResource(AtmoSoundEventsFile);
        });
        RegisterListener<Listeners.OnMapStart>(_ => AtmoAnnouncerMapStart());
    }

    private void AtmoAnnounce(string line, bool force = false)
    {
        if (!AtmoOn || !AtmoSet.Announcer) return;
        // from the world entity: the goal lines come after the round restart rebuilt the ball
        CBaseEntity? source = Utilities.GetEntityFromIndex<CBaseEntity>(0);
        if (source is not { IsValid: true }) source = _ball;
        if (source is not { IsValid: true }) return;
        var now = (double)Server.TickedTime;
        if (!force && now - _atmoAnnounceLast < AtmoAnnounceGap) return;
        _atmoAnnounceLast = now;
        source.EmitSound("SoccerMod.Announcer." + line, StadiumRecipients(SoccerSound.Announcer));
        Logger.LogInformation("[SM2DIAG] atmo_announce line={Line}", line);
    }

    // From AtmoGoal (not for own goals' team line).
    private void AtmoAnnounceGoal(CsTeam team, AtmoMoment kind, float shotSpeed)
    {
        if (!AtmoSet.Announcer) return;
        if (kind == AtmoMoment.OwnGoal) { AtmoLater(1.0, () => AtmoAnnounce("Owngoal", true)); return; }
        AtmoLater(1.0, () => AtmoAnnounce(team == CsTeamRed ? "GoalRed" : "GoalBlue", true));
        var second = kind switch
        {
            AtmoMoment.HatTrick => "Hattrick",
            AtmoMoment.LastMinute => "Late",
            AtmoMoment.Equaliser => "Equaliser",
            AtmoMoment.Lead => "Lead",
            _ => shotSpeed >= AtmoAnnounceRocketSpeed ? "Strike" : null,
        };
        if (second is not null) AtmoLater(3.6, () => AtmoAnnounce(second, true));
    }

    // From AtmoFire: every other moment.
    private void AtmoAnnounceMoment(AtmoMoment moment)
    {
        if (!AtmoSet.Announcer) return;
        var now = (double)Server.TickedTime;
        switch (moment)
        {
            case AtmoMoment.MatchStart:
                _atmoAnnounceKickoffPending = true;
                _atmoAnnounceSecondHalfPending = false;
                _atmoAnnounceOneMinuteDone = false;
                AtmoAnnounce("Welcome", true);
                break;
            case AtmoMoment.Kickoff when _atmoAnnounceKickoffPending || _atmoAnnounceSecondHalfPending:
                var line = _atmoAnnounceSecondHalfPending ? "Secondhalf" : "Kickoff";
                _atmoAnnounceKickoffPending = _atmoAnnounceSecondHalfPending = false;
                AtmoLater(0.8, () => AtmoAnnounce(line, true));
                break;
            case AtmoMoment.HalfTime:
                _atmoAnnounceSecondHalfPending = true;
                _atmoAnnounceOneMinuteDone = false;
                AtmoLater(1.0, () => AtmoAnnounce("Halftime", true));
                break;
            case AtmoMoment.FullTime:
                AtmoLater(1.5, () => AtmoAnnounce("Fulltime", true));
                break;
            case AtmoMoment.Save or AtmoMoment.Post or AtmoMoment.NearMiss when now - _atmoAnnounceChanceLast >= AtmoAnnounceChanceCooldown:
                _atmoAnnounceChanceLast = now;
                AtmoAnnounce(moment switch { AtmoMoment.Save => "Save", AtmoMoment.Post => "Post", _ => "Wide" });
                break;
        }
    }

    // From AtmoOnTick (4 Hz): "one minute to go" once per half.
    private void AtmoAnnounceClock(double secondsLeft)
    {
        if (_atmoAnnounceOneMinuteDone || secondsLeft is < 55.0 or > 60.0) return;
        _atmoAnnounceOneMinuteDone = true;
        AtmoAnnounce("Oneminute", true);
    }

    // Map start (also run after a plugin hot reload, AtmoHotReload).
    private void AtmoAnnouncerMapStart()
    {
        _atmoAnnounceLast = _atmoAnnounceChanceLast = -100;
        _atmoAnnounceKickoffPending = _atmoAnnounceSecondHalfPending = _atmoAnnounceOneMinuteDone = false;
    }
}

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-29 "Arena Vision" Stadium Sound 2.0 (v8 only, admin toggle "Crowd
// sounds"): the synthesised crowd (tools/atmo/synth-crowd.mjs, owner-approved
// v3) - a murmur bed that gets louder with the director's hype, "ooh" at near
// misses and the post, applause at saves and at half/full time, a roar at
// goals, whistles at own goals and chants when the stadium is hot. Owner: with
// these the old boo (StadiumBallWide) is switched off - see AtmoCrowdSoundOn.
// Played from the world entity so a round restart never cuts them off.
public sealed partial class SoccerModMvpPlugin
{
    private double _atmoChantLast = -100, _atmoOohLast = -100, _atmoApplauseLast = -100;

    // Read by StadiumBallWide (StadiumSounds.cs): the crowd's "ooh" replaces the boo.
    private bool AtmoCrowdSoundOn => AtmoOn && !OnHall && AtmoSet.CrowdSound;   // no stadium sounds in the indoor hall

    private void AtmoCrowdSoundOnLoad()
    {
        RegisterListener<Listeners.OnMapStart>(_ => AtmoCrowdSoundMapStart());
    }

    private void AtmoCrowdSound(string name)
    {
        if (!AtmoCrowdSoundOn) return;
        CBaseEntity? source = Utilities.GetEntityFromIndex<CBaseEntity>(0);
        if (source is not { IsValid: true }) source = _ball;
        if (source is not { IsValid: true }) return;
        // own switch per kind (Sounds - Stadium & Effects)
        var category = name.StartsWith("Murmur", StringComparison.Ordinal) ? SoccerSound.CrowdMurmur : name == "Chant" ? SoccerSound.CrowdChants : SoccerSound.CrowdReactions;
        source.EmitSound("SoccerMod.Crowd." + name, StadiumRecipients(category));
    }

    // 2026-09-30 owner: after switching the crowd on he only heard part of it - the bed went out
    // every 27 s to whoever had it on at that moment, so a player who switched it on (or joined)
    // waited up to 27 s. Now every player has his own loop: it starts the moment he can hear it
    // and repeats every AtmoBedSeconds for him.
    private const double AtmoBedSeconds = 27.0;
    private readonly Dictionary<ulong, double> _atmoBedLast = new();

    private bool AtmoCrowdBedHears(CCSPlayerController p) =>
        p.IsValid && !p.IsBot && SoundOn(p, SoccerSound.Stadium) && SoundOn(p, SoccerSound.CrowdMurmur);

    private void AtmoCrowdBed()
    {
        var now = (double)Server.TickedTime;
        foreach (var p in Utilities.GetPlayers())
        {
            var id = SteamIdOf(p);
            if (id == 0) continue;
            if (!AtmoCrowdSoundOn || !AtmoCrowdBedHears(p)) { _atmoBedLast.Remove(id); continue; }
            if (_atmoBedLast.TryGetValue(id, out var last) && now - last < AtmoBedSeconds && now >= last) continue;
            AtmoCrowdBedFor(p);
        }
    }

    // Starts the bed for one player right now (menu switch, join, his loop).
    private void AtmoCrowdBedFor(CCSPlayerController p)
    {
        if (!AtmoCrowdSoundOn || !AtmoCrowdBedHears(p)) return;
        CBaseEntity? source = Utilities.GetEntityFromIndex<CBaseEntity>(0);
        if (source is not { IsValid: true }) source = _ball;
        if (source is not { IsValid: true }) return;
        var one = new RecipientFilter();
        one.Add(p);
        source.EmitSound("SoccerMod.Crowd." + (_atmoHype >= 55f ? "MurmurLoud" : "Murmur"), one);
        _atmoBedLast[SteamIdOf(p)] = Server.TickedTime;
    }

    private void AtmoCrowdChantCheck()
    {
        var now = (double)Server.TickedTime;
        if (!AtmoCrowdSoundOn || _atmoHype < 55f || now - _atmoChantLast < 35.0 || Random.Shared.NextDouble() > 0.4) return;
        _atmoChantLast = now;
        AtmoCrowdSound("Chant");
    }

    // From AtmoFire: every moment the director sees.
    private void AtmoCrowdSoundMoment(AtmoMoment moment)
    {
        if (!AtmoCrowdSoundOn) return;
        var now = (double)Server.TickedTime;
        switch (moment)
        {
            case AtmoMoment.NearMiss or AtmoMoment.Post when now - _atmoOohLast >= 4.0:
                _atmoOohLast = now;
                AtmoCrowdSound("Ooh");
                break;
            case AtmoMoment.Save when now - _atmoApplauseLast >= 5.0:
                _atmoApplauseLast = now;
                AtmoLater(0.3, () => AtmoCrowdSound("Applause"));
                break;
            case AtmoMoment.Goal or AtmoMoment.Equaliser or AtmoMoment.Lead or AtmoMoment.LastMinute or AtmoMoment.HatTrick:
                AtmoLater(0.2, () => AtmoCrowdSound("Roar"));
                break;
            case AtmoMoment.OwnGoal:
                // 2026-10-01 owner: the other team's fans cheer, then the unlucky side whistles.
                AtmoLater(0.2, () => AtmoCrowdSound("Roar"));
                AtmoLater(1.6, () => AtmoCrowdSound("Whistle"));
                break;
            case AtmoMoment.HalfTime or AtmoMoment.FullTime:
                AtmoLater(0.6, () => AtmoCrowdSound("Applause"));
                break;
        }
    }

    // Map start (also run after a plugin hot reload, AtmoHotReload).
    private void AtmoCrowdSoundMapStart()
    {
        _atmoChantLast = _atmoOohLast = _atmoApplauseLast = -100;
        _atmoBedLast.Clear();
        AddTimer(2.0f, AtmoCrowdBed, TimerFlags.STOP_ON_MAPCHANGE);
        // 2026-09-29 owner: the bed is his stadium recording (Downloads/vishiv-crowd-cheering-in-
        // stadium-435357: trimmed, compressed, 6.5 kHz low-pass for distance), 28.5 s with 1.5 s
        // fades - restarting every 27 s (per player, AtmoCrowdBed) crossfades it into a seamless loop.
        AddTimer(1.0f, AtmoCrowdBed, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);   // per-player loops
        AddTimer(10.0f, AtmoCrowdChantCheck, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }
}

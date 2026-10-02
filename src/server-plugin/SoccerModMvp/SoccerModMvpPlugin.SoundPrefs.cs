using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;

// 2026-09-25 owner: every player can switch our custom sounds on and off one
// by one in !menu -> Settings -> Sounds. Saved per SteamID (default on). All
// our EmitSound calls take their recipients from SoundRecipients.
internal enum SoccerSound
{
    Radio,
    Kick,
    GoalNet,
    Posts,
    Sprint,
    Stadium,          // master switch for everything in "Stadium & Effects" (kept: older mutes)
    RefereeWhistle,   // kick-off and goal whistles
    CrowdMurmur,      // the background crowd bed (Arena Vision)
    CrowdReactions,   // ooh, applause, roar, fans whistling, goal cheer, air horn
    CrowdChants,
    Announcer,
}

public sealed partial class SoccerModMvpPlugin
{
    private static readonly (SoccerSound Sound, string Label)[] SoccerSoundLabels =
    {
        (SoccerSound.Radio, "Soccer radio (V menu calls)"),
        (SoccerSound.Kick, "Ball kick (off = CS2 knife hit)"),
        (SoccerSound.GoalNet, "Goal net"),
        (SoccerSound.Posts, "Post and crossbar hits"),
        (SoccerSound.Sprint, "Sprint breathing"),
    };

    // 2026-09-29 owner: "Menu - Settings - Sound - Stadium & Effects", every stadium sound on its
    // own. The master (Stadium) must be on as well, so earlier "stadium off" choices still hold.
    private static readonly (SoccerSound Sound, string Label)[] StadiumSoundLabels =
    {
        (SoccerSound.RefereeWhistle, "Referee whistles (kick-off, goal)"),
        (SoccerSound.CrowdMurmur, "Crowd background"),
        (SoccerSound.CrowdReactions, "Crowd reactions (ooh, applause, roar, horn)"),
        (SoccerSound.CrowdChants, "Chants"),
        (SoccerSound.Announcer, "Stadium announcer"),
    };

    // 2026-10-02 owner: no stadium sounds in the indoor hall - nobody is sent one there (HallLayout.cs).
    private RecipientFilter StadiumRecipients(SoccerSound category) =>
        SoundRecipients(category, p => !OnHall && SoundOn(p, SoccerSound.Stadium));

    private void OpenStadiumSoundsMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Sounds - Stadium & Effects", Key = "stadium-sounds", OnBack = OpenPersonalSoundsMenu };
        // 2026-09-30 owner: switching the stadium sounds on gave only part of them (the crowd
        // categories are opt-in). "All" now means master + every category: not all on -> all on,
        // all on -> master off (everything silent, category choices kept).
        var allOn = SoundOn(player, SoccerSound.Stadium) && StadiumSoundLabels.All(s => SoundOn(player, s.Sound));
        var someOn = SoundOn(player, SoccerSound.Stadium) && StadiumSoundLabels.Any(s => SoundOn(player, s.Sound));
        menu.Add($"All stadium sounds: {(allOn ? "On" : someOn ? "Some" : "Off")}", p =>
        {
            var turnOn = !(SoundOn(p, SoccerSound.Stadium) && StadiumSoundLabels.All(s => SoundOn(p, s.Sound)));
            SetSoundOn(p, SoccerSound.Stadium, turnOn);
            if (turnOn) foreach (var (sound, _) in StadiumSoundLabels) SetSoundOn(p, sound, true);
            SaveJsonAtomic(MenuParityFile, _menuParity);
            if (turnOn) AtmoCrowdBedFor(p);
            OpenStadiumSoundsMenu(p);
        });
        foreach (var (sound, label) in StadiumSoundLabels)
        {
            var entry = sound;
            menu.Add($"{label}: {(SoundOn(player, entry) ? "On" : "Off")}", p =>
            {
                SetSoundOn(p, entry, !SoundOn(p, entry));
                SaveJsonAtomic(MenuParityFile, _menuParity);
                if (entry == SoccerSound.CrowdMurmur) AtmoCrowdBedFor(p);
                OpenStadiumSoundsMenu(p);
            });
        }
        OpenNumberMenu(player, menu);
    }

    private List<ulong> MutedSoundList(SoccerSound sound)
    {
        var key = sound.ToString();
        if (!_menuParity.MutedSounds.TryGetValue(key, out var list)) _menuParity.MutedSounds[key] = list = new List<ulong>();
        return list;
    }

    // 2026-10-02 owner (his own Sounds page: whistles on, the rest off - "is this config default
    // for everyone? if not do it"): default for everyone = master + referee whistles on; crowd
    // background, reactions, chants and announcer off, as from 2026-09-29. Those four are opt-in:
    // a player is on only when he switched them on (list "On_<name>", stored beside the muted
    // lists); the rest stay opt-out. (2026-10-01 they were on by default for one day.)
    private static bool SoundDefaultOff(SoccerSound sound) =>
        sound is SoccerSound.CrowdMurmur or SoccerSound.CrowdReactions or SoccerSound.CrowdChants or SoccerSound.Announcer;

    // Who switched one of the four off during the day they were on by default stays off, also
    // when he had opted in before; the muted lists of the four are not read any more.
    private void MigrateCrowdSoundChoices()
    {
        var changed = false;
        foreach (var sound in new[] { SoccerSound.CrowdMurmur, SoccerSound.CrowdReactions, SoccerSound.CrowdChants, SoccerSound.Announcer })
        {
            var muted = MutedSoundList(sound);
            if (muted.Count == 0) continue;
            var optedIn = OptedInSoundList(sound);
            foreach (var id in muted) optedIn.Remove(id);
            muted.Clear();
            changed = true;
        }
        if (changed) SaveJsonAtomic(MenuParityFile, _menuParity);
    }

    private List<ulong> OptedInSoundList(SoccerSound sound)
    {
        var key = "On_" + sound;
        if (!_menuParity.MutedSounds.TryGetValue(key, out var list)) _menuParity.MutedSounds[key] = list = new List<ulong>();
        return list;
    }

    private bool SoundOn(CCSPlayerController player, SoccerSound sound) => SoundDefaultOff(sound)
        ? OptedInSoundList(sound).Contains(SteamIdOf(player))
        : !MutedSoundList(sound).Contains(SteamIdOf(player));

    private RecipientFilter SoundRecipients(SoccerSound sound, Func<CCSPlayerController, bool>? include = null, bool ignoreMute = false)
    {
        var filter = new RecipientFilter();
        foreach (var player in Utilities.GetPlayers())
        {
            if (player.IsValid && !player.IsBot && (include?.Invoke(player) ?? true) && (ignoreMute || SoundOn(player, sound)))
                filter.Add(player);
        }
        return filter;
    }

    private void SetSoundOn(CCSPlayerController player, SoccerSound sound, bool on)
    {
        var id = SteamIdOf(player);
        if (id == 0) return;
        if (SoundDefaultOff(sound))
        {
            var optedIn = OptedInSoundList(sound);
            if (on && !optedIn.Contains(id)) optedIn.Add(id);
            else if (!on) optedIn.Remove(id);
            return;
        }
        var muted = MutedSoundList(sound);
        if (on) muted.Remove(id);
        else if (!muted.Contains(id)) muted.Add(id);
    }

    // The first version (a few hours, 2026-09-25) had two groups; keep what
    // players chose there.
    private void MigrateSoundGroups()
    {
        if (_menuParity.MutedRadioSounds.Count == 0 && _menuParity.MutedEffectSounds.Count == 0) return;
        foreach (var id in _menuParity.MutedRadioSounds)
            if (!MutedSoundList(SoccerSound.Radio).Contains(id)) MutedSoundList(SoccerSound.Radio).Add(id);
        foreach (var id in _menuParity.MutedEffectSounds)
            foreach (var sound in new[] { SoccerSound.Kick, SoccerSound.GoalNet, SoccerSound.Posts, SoccerSound.Sprint })
                if (!MutedSoundList(sound).Contains(id)) MutedSoundList(sound).Add(id);
        _menuParity.MutedRadioSounds.Clear();
        _menuParity.MutedEffectSounds.Clear();
        SaveJsonAtomic(MenuParityFile, _menuParity);
    }

    private void OpenPersonalSoundsMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Settings - Sounds", Key = "personal-sounds", OnBack = OpenClientSettingsMenu };
        foreach (var (sound, label) in SoccerSoundLabels)
        {
            var entry = sound;
            menu.Add($"{label}: {(SoundOn(player, entry) ? "On" : "Off")}", p =>
            {
                SetSoundOn(p, entry, !SoundOn(p, entry));
                SaveJsonAtomic(MenuParityFile, _menuParity);
                OpenPersonalSoundsMenu(p);
            });
        }
        if (!OnHall) menu.Add($"Stadium & Effects: {(SoundOn(player, SoccerSound.Stadium) ? "On" : "Off")} ›", OpenStadiumSoundsMenu);   // not offered in the indoor hall
        menu.Add("All on", p => SetAllSounds(p, true));
        menu.Add("All off", p => SetAllSounds(p, false));
        OpenNumberMenu(player, menu);
    }

    private void SetAllSounds(CCSPlayerController player, bool on)
    {
        foreach (var (sound, _) in SoccerSoundLabels.Concat(StadiumSoundLabels)) SetSoundOn(player, sound, on);
        SetSoundOn(player, SoccerSound.Stadium, on);
        if (on) AtmoCrowdBedFor(player);
        SaveJsonAtomic(MenuParityFile, _menuParity);
        OpenPersonalSoundsMenu(player);
    }

    // SoMoE parity (modules/sprint.sp): the CS:S suit_sprint.wav breathing
    // when a sprint starts, for the sprinter only.
    internal const string SprintSoundEvent = "SoccerMod.Sprint.Start";

    private void PlaySprintSound(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        if (!player.IsValid || player.IsBot || !pawn.IsValid || !SoundOn(player, SoccerSound.Sprint)) return;
        var self = new RecipientFilter();
        self.Add(player);
        pawn.EmitSound(SprintSoundEvent, self);
    }
}

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
    Stadium,
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
        (SoccerSound.Stadium, "Stadium (whistles and crowd)"),
    };

    private List<ulong> MutedSoundList(SoccerSound sound)
    {
        var key = sound.ToString();
        if (!_menuParity.MutedSounds.TryGetValue(key, out var list)) _menuParity.MutedSounds[key] = list = new List<ulong>();
        return list;
    }

    private bool SoundOn(CCSPlayerController player, SoccerSound sound) => !MutedSoundList(sound).Contains(SteamIdOf(player));

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
        menu.Add("All on", p => SetAllSounds(p, true));
        menu.Add("All off", p => SetAllSounds(p, false));
        OpenNumberMenu(player, menu);
    }

    private void SetAllSounds(CCSPlayerController player, bool on)
    {
        foreach (var (sound, _) in SoccerSoundLabels) SetSoundOn(player, sound, on);
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

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;

// 2026-09-25 owner: every player can mute our custom sounds by group in
// !menu -> Settings. Radio = the V menu calls; Effects = goal net, post and
// crossbar hits, the ball kick. Saved per SteamID (default on). All our
// EmitSound calls take their recipients from SoundRecipients.
internal enum SoccerSoundGroup
{
    Radio,
    Effects,
}

public sealed partial class SoccerModMvpPlugin
{
    private List<ulong> MutedSoundList(SoccerSoundGroup group) =>
        group == SoccerSoundGroup.Radio ? _menuParity.MutedRadioSounds : _menuParity.MutedEffectSounds;

    private bool SoundGroupOn(CCSPlayerController player, SoccerSoundGroup group) =>
        !MutedSoundList(group).Contains(SteamIdOf(player));

    private RecipientFilter SoundRecipients(SoccerSoundGroup group, Func<CCSPlayerController, bool>? include = null)
    {
        var filter = new RecipientFilter();
        foreach (var player in Utilities.GetPlayers())
        {
            if (player.IsValid && !player.IsBot && (include?.Invoke(player) ?? true) && SoundGroupOn(player, group))
                filter.Add(player);
        }
        return filter;
    }

    private void ToggleSoundGroup(CCSPlayerController player, SoccerSoundGroup group)
    {
        var id = SteamIdOf(player);
        if (id == 0) return;
        var muted = MutedSoundList(group);
        var nowOn = muted.Contains(id);
        if (nowOn) muted.Remove(id);
        else muted.Add(id);
        SaveJsonAtomic(MenuParityFile, _menuParity);
        var name = group == SoccerSoundGroup.Radio ? "Soccer radio sounds" : "Effect sounds";
        player.PrintToChat($" \x04[SM]\x01 {name}: {(nowOn ? "\x04on" : "\x07off")}");
    }

    private static string SoundGroupLabel(SoccerSoundGroup group) => group == SoccerSoundGroup.Radio
        ? "Soccer radio sounds (V menu calls)"
        : "Effect sounds (goal net, posts, ball kick, sprint)";

    // SoMoE parity (modules/sprint.sp): the CS:S suit_sprint.wav breathing
    // when a sprint starts, for the sprinter only.
    internal const string SprintSoundEvent = "SoccerMod.Sprint.Start";

    private void PlaySprintSound(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        if (!player.IsValid || player.IsBot || !pawn.IsValid || !SoundGroupOn(player, SoccerSoundGroup.Effects)) return;
        var self = new RecipientFilter();
        self.Add(player);
        pawn.EmitSound(SprintSoundEvent, self);
    }
}

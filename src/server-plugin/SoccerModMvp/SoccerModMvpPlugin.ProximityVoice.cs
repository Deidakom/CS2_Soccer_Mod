using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-29 owner: proximity voice chat on its own key, the normal voice key
// stays team voice. CS2 has one voice key, so the second key is a bind that
// tells the server "this is proximity" while it holds +voicerecord:
//   alias +prox "css_prox 1; +voicerecord"; alias -prox "-voicerecord; css_prox 0"; bind v +prox
// While a player talks on it, everyone within the range hears them (both
// teams) and nobody further away does - per listener via SetListenOverride,
// refreshed ~10 times a second. The server can only switch a voice on or off,
// not make it quieter with distance. Test flag soccermod_proximity_voice.enabled;
// admin switch and range in Admin - Settings; the bind line in !menu - Settings.
public sealed partial class SoccerModMvpPlugin
{
    private const string ProxFlagFile = "soccermod_proximity_voice.enabled";
    private const string ProxSettingsFile = "soccermod_proximity_voice.json";
    private const string ProxBindLine = "alias +prox \"css_prox 1; +voicerecord\"; alias -prox \"-voicerecord; css_prox 0\"; bind v +prox";

    private sealed class ProxSettings
    {
        public bool Enabled { get; set; } = true;
        public float Range { get; set; } = 900f;
    }

    private ProxSettings? _proxSettings;
    private ProxSettings ProxSet => _proxSettings ??= LoadJsonOrNull<ProxSettings>(ProxSettingsFile) ?? new ProxSettings();
    private readonly HashSet<int> _proxTalking = new();
    private readonly HashSet<(int Listener, int Speaker)> _proxOverrides = new();
    private int _proxTick;

    private bool ProxAvailable => File.Exists(ConfigPath(ProxFlagFile)) && ProxSet.Enabled;

    private void ProximityVoiceOnLoad()
    {
        AddCommand("css_prox", "Proximity voice key (used by the +prox bind from !menu - Settings).", OnProxCommand);
        RegisterListener<Listeners.OnClientDisconnect>(slot =>
        {
            _proxTalking.Remove(slot);
            // Put every pair with the leaving player back to normal team voice before forgetting it,
            // so whoever takes the slot next is not left muted or heard across teams (review 2026-09-29).
            ProxResetPairs(p => p.Listener == slot || p.Speaker == slot);
        });
        RegisterListener<Listeners.OnMapEnd>(() =>
        {
            _proxTalking.Clear();
            ProxResetPairs(_ => true);
        });
    }

    private void OnProxCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true }) return;
        var on = command.ArgCount >= 2 && command.GetArg(1) == "1";
        if (!ProxAvailable) on = false;
        if (on) _proxTalking.Add(player.Slot); else _proxTalking.Remove(player.Slot);
        ProxApply();
    }

    // From the main OnTick.
    private void ProximityVoiceOnTick()
    {
        if (++_proxTick < 6) return;
        _proxTick = 0;
        if (_proxTalking.Count > 0 || _proxOverrides.Count > 0) ProxApply();
    }

    private void ProxApply()
    {
        var players = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot).ToList();
        var range = ProxSet.Range;
        var wanted = new Dictionary<(int, int), ListenOverride>();
        foreach (var speaker in players.Where(p => _proxTalking.Contains(p.Slot)))
        {
            var from = speaker.PlayerPawn.Value?.AbsOrigin ?? speaker.Pawn.Value?.AbsOrigin;
            foreach (var listener in players)
            {
                if (listener.Slot == speaker.Slot) continue;
                var to = listener.PlayerPawn.Value?.AbsOrigin ?? listener.Pawn.Value?.AbsOrigin;
                var near = from is not null && to is not null
                    && MathF.Sqrt((from.X - to.X) * (from.X - to.X) + (from.Y - to.Y) * (from.Y - to.Y) + (from.Z - to.Z) * (from.Z - to.Z)) <= range;
                wanted[(listener.Slot, speaker.Slot)] = near ? ListenOverride.Hear : ListenOverride.Mute;
            }
        }
        // Back to normal (team voice) for pairs no longer in proximity mode.
        foreach (var pair in _proxOverrides.ToList())
        {
            if (wanted.ContainsKey(pair)) continue;
            if (Utilities.GetPlayerFromSlot(pair.Listener) is { IsValid: true } l && Utilities.GetPlayerFromSlot(pair.Speaker) is { IsValid: true } s)
                l.SetListenOverride(s, ListenOverride.Default);
            _proxOverrides.Remove(pair);
        }
        foreach (var ((listenerSlot, speakerSlot), mode) in wanted)
        {
            if (Utilities.GetPlayerFromSlot(listenerSlot) is not { IsValid: true } l || Utilities.GetPlayerFromSlot(speakerSlot) is not { IsValid: true } s) continue;
            if (l.GetListenOverride(s) != mode) l.SetListenOverride(s, mode);
            _proxOverrides.Add((listenerSlot, speakerSlot));
        }
    }

    private void ProxResetPairs(Func<(int Listener, int Speaker), bool> which)
    {
        foreach (var pair in _proxOverrides.Where(which).ToList())
        {
            if (Utilities.GetPlayerFromSlot(pair.Listener) is { IsValid: true } l && Utilities.GetPlayerFromSlot(pair.Speaker) is { IsValid: true } s)
                l.SetListenOverride(s, ListenOverride.Default);
            _proxOverrides.Remove(pair);
        }
    }

    // !menu - Settings: how to set up the key (one-time console bind).
    private void AddProximityVoiceEntry(NumberMenu menu)
    {
        if (!ProxAvailable) return;
        menu.Add("Proximity voice key: how to", p =>
        {
            p.PrintToChat(" [SM] Proximity voice: hold V to talk to everyone near you (both teams). One-time setup - paste this in your console:");
            p.PrintToChat($" {ProxBindLine}");
            p.PrintToConsole(ProxBindLine);
        });
    }

    // Admin - Settings (next to the stadium effects).
    private void AddProximityAdminEntry(NumberMenu menu)
    {
        if (!File.Exists(ConfigPath(ProxFlagFile))) return;
        menu.Add($"Proximity voice (test): {(ProxSet.Enabled ? "on" : "off")}, range {ProxSet.Range:F0}", p =>
        {
            if (!SettingsAccess(p)) return;
            // on 900 -> on 1400 -> on 600 -> off -> on 900
            if (!ProxSet.Enabled) { ProxSet.Enabled = true; ProxSet.Range = 900f; }
            else if (ProxSet.Range < 1000f && ProxSet.Range > 700f) ProxSet.Range = 1400f;
            else if (ProxSet.Range >= 1000f) ProxSet.Range = 600f;
            else ProxSet.Enabled = false;
            if (!ProxSet.Enabled) { _proxTalking.Clear(); ProxApply(); }
            SaveJsonAtomic(ProxSettingsFile, ProxSet);
            Logger.LogInformation("[SM2DIAG] proximity_voice_setting enabled={On} range={Range}", ProxSet.Enabled, ProxSet.Range);
            OpenServerSettingsMenu(p);
        });
    }
}

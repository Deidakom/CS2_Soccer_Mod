using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-25 owner: SourceMod-style communication punishments next to the
// existing kick/ban (Admin.cs). Mute = no voice (VoiceFlags.Muted), gag = no
// chat, silence = both. Saved per SteamID in soccermod_comms.json with an
// expiry, so they survive reconnects, map changes and plugin reloads. "Until
// map change" entries are dropped at the next map start.
public static class CommsRules
{
    public const string Mute = "mute";
    public const string Gag = "gag";

    // Minutes as typed: > 0 = timed, 0 = permanent, "map" = until map change.
    public static bool TryParseDuration(string text, out double minutes, out bool untilMapChange)
    {
        untilMapChange = text.Equals("map", StringComparison.OrdinalIgnoreCase);
        minutes = 0;
        return untilMapChange || double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out minutes) && minutes >= 0;
    }

    public static DateTime? ExpiryFor(DateTime now, double minutes) => minutes > 0 ? now.AddMinutes(minutes) : null;

    public static bool Active(DateTime now, DateTime? expires) => expires is null || expires > now;

    public static bool IsPermanent(double minutes, bool untilMapChange) => !untilMapChange && minutes <= 0;

    // Chat commands (!menu, /admin) still work while gagged.
    public static bool IsChatCommand(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith('!') || trimmed.StartsWith('/');
    }

    public static string Remaining(DateTime now, DateTime? expires, bool untilMapChange)
    {
        if (untilMapChange) return "until map change";
        if (expires is not { } end) return "permanent";
        var minutes = Math.Max(1.0, Math.Ceiling((end - now).TotalMinutes));
        return minutes >= 120 ? $"{Math.Ceiling(minutes / 60):F0} h" : $"{minutes:F0} min";
    }

    public static string DurationText(double minutes, bool untilMapChange)
    {
        if (untilMapChange) return "until map change";
        if (minutes <= 0) return "permanently";
        if (minutes >= 1440 && minutes % 1440 == 0) return minutes == 1440 ? "for 1 day" : $"for {minutes / 1440:F0} days";
        if (minutes >= 60 && minutes % 60 == 0) return minutes == 60 ? "for 1 hour" : $"for {minutes / 60:F0} hours";
        return $"for {minutes:0.#} min";
    }
}

public sealed partial class SoccerModMvpPlugin
{
    private const string CommsFileName = "soccermod_comms.json";
    private CommsStore _commsStore = new();
    private double _nextCommsSweep;

    private sealed class CommsEntry
    {
        public ulong SteamId64 { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = CommsRules.Mute;
        public string Reason { get; set; } = string.Empty;
        public ulong AddedBy { get; set; }
        public DateTime AddedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public bool UntilMapChange { get; set; }
    }

    private sealed class CommsStore
    {
        public int Version { get; set; } = 1;
        public List<CommsEntry> Entries { get; set; } = new();
    }

    // Registered right after AdminOnLoad so the gag listener runs before
    // the other say/say_team listeners.
    private void CommsOnLoad()
    {
        _commsStore = LoadJsonOrNull<CommsStore>(CommsFileName) ?? new CommsStore();
        AddCommandListener("say", OnGaggedSay, HookMode.Pre);
        AddCommandListener("say_team", OnGaggedSay, HookMode.Pre);
        foreach (var (command, kinds, add) in new[]
                 {
                     ("css_mute", new[] { CommsRules.Mute }, true), ("css_unmute", new[] { CommsRules.Mute }, false),
                     ("css_gag", new[] { CommsRules.Gag }, true), ("css_ungag", new[] { CommsRules.Gag }, false),
                     ("css_silence", new[] { CommsRules.Mute, CommsRules.Gag }, true),
                     ("css_unsilence", new[] { CommsRules.Mute, CommsRules.Gag }, false),
                 })
        {
            var help = add
                ? $"Admin only: {command[4..]} a player. {command} <#userid|name|steamid64> [minutes, 0 = permanent, map = until map change] [reason]"
                : $"Admin only: lift a {kinds[^1]}. {command} <#userid|name|steamid64>";
            AddCommand(command, help, (player, info) => OnCommsCommand(player, info, command, kinds, add));
        }
        AddCommand("css_commslist", "Admin only: list active mutes and gags.", OnCommsListCommand);
        RegisterListener<Listeners.OnMapStart>(_ => DropMapChangeComms());
        RegisterEventHandler<EventPlayerSpawn>((@event, _) =>
        {
            if (@event.Userid is { IsValid: true } player) ApplyVoiceMute(player);
            return HookResult.Continue;
        });
        Logger.LogInformation("[SM2DIAG] comms_loaded entries={Count}", _commsStore.Entries.Count);
    }

    private void SaveComms(string reason)
    {
        if (SaveJsonAtomic(CommsFileName, _commsStore))
            Logger.LogInformation("[SM2DIAG] comms_saved reason={Reason} count={Count}", reason, _commsStore.Entries.Count);
    }

    private CommsEntry? ActiveComms(ulong steamId64, string kind)
    {
        if (steamId64 == 0) return null;
        var now = DateTime.UtcNow;
        return _commsStore.Entries.FirstOrDefault(e => e.SteamId64 == steamId64 && e.Kind == kind && CommsRules.Active(now, e.ExpiresAtUtc));
    }

    private bool IsMuted(CCSPlayerController player) => ActiveComms(SteamIdOf(player), CommsRules.Mute) is not null;
    private bool IsGagged(CCSPlayerController player) => ActiveComms(SteamIdOf(player), CommsRules.Gag) is not null;

    private void ApplyVoiceMute(CCSPlayerController player)
    {
        if (!player.IsValid || player.IsBot) return;
        var muted = IsMuted(player);
        var flags = player.VoiceFlags;
        var wanted = muted ? flags | VoiceFlags.Muted : flags & ~VoiceFlags.Muted;
        if (wanted != flags) player.VoiceFlags = wanted;
    }

    // Once a second: drop expired entries and lift their voice mute.
    private void CommsOnTick()
    {
        var now = Server.TickedTime;
        if (now < _nextCommsSweep) return;
        _nextCommsSweep = now + 1.0;
        var utc = DateTime.UtcNow;
        var expired = _commsStore.Entries.Where(e => !CommsRules.Active(utc, e.ExpiresAtUtc)).ToList();
        if (expired.Count > 0)
        {
            foreach (var entry in expired)
            {
                _commsStore.Entries.Remove(entry);
                var online = Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && SteamIdOf(p) == entry.SteamId64);
                online?.PrintToChat($" \x04[SM]\x01 Your {entry.Kind} has expired.");
            }
            SaveComms("expired");
        }
        foreach (var player in Utilities.GetPlayers()) ApplyVoiceMute(player);
    }

    private void DropMapChangeComms()
    {
        if (_commsStore.Entries.RemoveAll(e => e.UntilMapChange) > 0) SaveComms("map_change");
    }

    private HookResult OnGaggedSay(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true } || info.ArgCount < 2) return HookResult.Continue;
        var gag = ActiveComms(SteamIdOf(player), CommsRules.Gag);
        if (gag is null || CommsRules.IsChatCommand(info.GetArg(1))) return HookResult.Continue;
        player.PrintToChat($" \x04[SM]\x01 You are gagged ({CommsRules.Remaining(DateTime.UtcNow, gag.ExpiresAtUtc, gag.UntilMapChange)}).");
        return HookResult.Handled;
    }

    private void OnCommsCommand(CCSPlayerController? player, CommandInfo command, string name, string[] kinds, bool add)
    {
        if (!RequirePermission(player, command, "admin")) return;
        if (command.ArgCount < 2)
        {
            command.ReplyToCommand(add
                ? $"[SM] usage: {name} <#userid|name|steamid64> [minutes, 0 = permanent, map = until map change] [reason]"
                : $"[SM] usage: {name} <#userid|name|steamid64>");
            return;
        }

        ulong steamId64;
        string targetName;
        var target = ResolveTarget(command, 1, out var resolveError);
        if (target is not null)
        {
            steamId64 = SteamIdOf(target);
            targetName = target.PlayerName;
            if (steamId64 == 0)
            {
                command.ReplyToCommand("[SM] target is not yet Steam-authorized, try again in a moment");
                return;
            }
        }
        else if (ulong.TryParse(command.GetArg(1), out steamId64) && steamId64 > 76561197960265728UL)
        {
            targetName = _commsStore.Entries.FirstOrDefault(e => e.SteamId64 == steamId64)?.Name ?? $"steamid:{steamId64}";
        }
        else
        {
            command.ReplyToCommand($"[SM] {resolveError}");
            return;
        }

        var by = player?.PlayerName ?? "Console";
        var what = kinds.Length == 2 ? "silenced" : kinds[0] == CommsRules.Mute ? "muted" : "gagged";
        if (!add)
        {
            var removed = _commsStore.Entries.RemoveAll(e => e.SteamId64 == steamId64 && kinds.Contains(e.Kind));
            if (removed == 0)
            {
                command.ReplyToCommand($"[SM] {targetName} is not {what}");
                return;
            }
            SaveComms(name);
            if (target is not null) ApplyVoiceMute(target);
            AnnounceAll($"[SM] {by} un{what} {targetName}.");
            Logger.LogInformation("[SM2DIAG] comms lift kind={Kind} target={Target} steamid={SteamId} by={By}", what, targetName, steamId64, by);
            return;
        }

        var minutes = 0.0;
        var untilMapChange = false;
        var reasonStart = 2;
        if (command.ArgCount >= 3 && CommsRules.TryParseDuration(command.GetArg(2), out minutes, out untilMapChange))
            reasonStart = 3;
        // Same rights matrix as bans: permanent is root-only.
        if (CommsRules.IsPermanent(minutes, untilMapChange) && player is not null && !HasFlag(SteamIdOf(player), "root"))
        {
            command.ReplyToCommand($"[SM] permanent {what} is root-only - give a duration, e.g. {name} <target> 30");
            return;
        }
        var reason = command.ArgCount > reasonStart
            ? string.Join(' ', Enumerable.Range(reasonStart, command.ArgCount - reasonStart).Select(command.GetArg))
            : string.Empty;
        var now = DateTime.UtcNow;
        _commsStore.Entries.RemoveAll(e => e.SteamId64 == steamId64 && kinds.Contains(e.Kind));
        foreach (var kind in kinds)
        {
            _commsStore.Entries.Add(new CommsEntry
            {
                SteamId64 = steamId64, Name = targetName, Kind = kind, Reason = reason,
                AddedBy = player is null ? 0UL : SteamIdOf(player), AddedAtUtc = now,
                ExpiresAtUtc = untilMapChange ? null : CommsRules.ExpiryFor(now, minutes), UntilMapChange = untilMapChange,
            });
        }
        SaveComms(name);
        if (target is not null) ApplyVoiceMute(target);
        var duration = CommsRules.DurationText(minutes, untilMapChange);
        AnnounceAll($"[SM] {by} {what} {targetName} {duration}{(reason.Length > 0 ? $" ({reason})" : string.Empty)}.");
        Logger.LogInformation("[SM2DIAG] comms add kind={Kind} target={Target} steamid={SteamId} duration={Duration} by={By}", what, targetName, steamId64, duration, by);
    }

    private void OnCommsListCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin")) return;
        var now = DateTime.UtcNow;
        var active = _commsStore.Entries.Where(e => CommsRules.Active(now, e.ExpiresAtUtc)).ToList();
        if (active.Count == 0)
        {
            ReplyAdminList(player, command, "[SM] no active mutes or gags");
            return;
        }
        foreach (var entry in active)
            ReplyAdminList(player, command, $"[SM] {entry.SteamId64} ({entry.Name}) {entry.Kind} {CommsRules.Remaining(now, entry.ExpiresAtUtc, entry.UntilMapChange)}{(entry.Reason.Length > 0 ? $" reason={entry.Reason}" : string.Empty)}");
    }
}

using System;
using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.UserMessages;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-01 user reports: (1) the ball makes a "weird" sound while being
// rolled by body contact, (2) the player landing sound is too loud. Both are
// Source 2 "SOS" (sound operating system) events, broadcast to clients as
// usermessage id 208 (CMsgSosStartSoundEvent) - confirmed via HookUserMessage
// existing in this CSSharp build (1.0.373 symbol scan: HookUserMessage,
// UnhookUserMessage, ReadUInt, ReadInt all present). The old claim in
// LandingSound.cs ("no sound/user-message listener exists") predates this and
// was wrong.
//
// The only field name confirmed from public CSSharp examples is
// "soundevent_hash" (a stable per-sound-event-name hash). No entity-index
// field name for the sound's source is confirmed anywhere reachable - rather
// than guess one and risk silently gating on a field that never reads (same
// class of mistake as the AntiCampRadius direction guess), this blocks by
// HASH ONLY: css_sm2sound_log surfaces the real hash for whatever sound is
// currently annoying, css_sm2sound_block persists it. Simpler and more
// robust than contact/entity gating, at the cost of blocking that exact
// sound everywhere it plays (acceptable for a landing thud or a rolling
// scrape sound, which are unlikely to double as a sound the user wants to
// keep elsewhere - to be confirmed live).
public sealed partial class SoccerModMvpPlugin
{
    private const int SosStartSoundEventMessageId = 208;
    private const double SoundLogIntervalSeconds = 0.2;

    private bool _soundLogEnabled;
    private double _nextSoundLogTime;
    private HashSet<uint> _blockedSoundHashes = new();

    // Best-effort only, for diagnostic completeness - never gated on.
    private static readonly string[] SoundDiagnosticEntityFieldCandidates =
    {
        "source_entity_index", "entity_index", "entindex", "source_entindex",
    };

    private void SoundBlockOnLoad()
    {
        HookUserMessage(SosStartSoundEventMessageId, OnSosStartSoundEvent, HookMode.Pre);
        AddCommand("css_sm2sound_log", "Admin: toggle diagnostic logging of sound events (on|off) - watch console for [SM2DIAG] sound_event.", OnSoundLogCommand);
        AddCommand("css_sm2sound_block", "Admin: block a sound event by its soundevent_hash (get the hash from css_sm2sound_log).", OnSoundBlockCommand);
        AddCommand("css_sm2sound_unblock", "Admin: remove a soundevent_hash from the block list.", OnSoundUnblockCommand);
        AddCommand("css_sm2sound_blocklist", "Admin: list currently blocked soundevent_hash values.", OnSoundBlocklistCommand);
    }

    private HookResult OnSosStartSoundEvent(UserMessage um)
    {
        uint hash = 0;
        try
        {
            hash = um.ReadUInt("soundevent_hash");
        }
        catch
        {
            // Field name unconfirmed by local docs - diagnostic log below
            // will show hash=0 if this ever actually fails, making the
            // problem visible instead of silently no-op'ing forever.
        }

        if (_soundLogEnabled)
        {
            var now = Server.TickedTime;
            if (now >= _nextSoundLogTime)
            {
                _nextSoundLogTime = now + SoundLogIntervalSeconds;
                var entityInfo = "n/a";
                foreach (var field in SoundDiagnosticEntityFieldCandidates)
                {
                    try
                    {
                        entityInfo = $"{field}={um.ReadInt(field).ToString(CultureInfo.InvariantCulture)}";
                        break;
                    }
                    catch
                    {
                        // try next candidate
                    }
                }

                Logger.LogInformation(
                    "[SM2DIAG] sound_event hash={Hash} {EntityInfo} ballIndex={BallIndex} blocked={Blocked}",
                    hash,
                    entityInfo,
                    _ball is { IsValid: true } ? _ball.Index.ToString(CultureInfo.InvariantCulture) : "none",
                    hash != 0 && _blockedSoundHashes.Contains(hash));
            }
        }

        if (hash != 0 && _blockedSoundHashes.Contains(hash))
        {
            return HookResult.Stop;
        }

        return HookResult.Continue;
    }

    private void OnSoundLogCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            _soundLogEnabled = command.GetArg(1).Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        command.ReplyToCommand(
            $"[SM] sound event diagnostic log: {(_soundLogEnabled ? "on" : "off")} "
            + "(trigger the sound in-game, then check the server console/journal for [SM2DIAG] sound_event lines; usage: css_sm2sound_log <on|off>)");
    }

    private void OnSoundBlockCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        if (command.ArgCount < 2
            || !uint.TryParse(command.GetArg(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hash))
        {
            command.ReplyToCommand("[SM] usage: css_sm2sound_block <hash> (get the hash from css_sm2sound_log)");
            return;
        }

        _blockedSoundHashes.Add(hash);
        SaveBallSettings("sound_block_command");
        command.ReplyToCommand($"[SM] blocked soundevent_hash {hash} ({_blockedSoundHashes.Count} total blocked)");
    }

    private void OnSoundUnblockCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        if (command.ArgCount < 2
            || !uint.TryParse(command.GetArg(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hash))
        {
            command.ReplyToCommand("[SM] usage: css_sm2sound_unblock <hash>");
            return;
        }

        var removed = _blockedSoundHashes.Remove(hash);
        if (removed)
        {
            SaveBallSettings("sound_unblock_command");
        }

        command.ReplyToCommand(removed ? $"[SM] unblocked {hash}" : $"[SM] {hash} was not blocked");
    }

    private void OnSoundBlocklistCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        command.ReplyToCommand(_blockedSoundHashes.Count == 0
            ? "[SM] no sound hashes blocked"
            : $"[SM] blocked hashes: {string.Join(", ", _blockedSoundHashes)}");
    }
}

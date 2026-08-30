using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Removes the map's secret sky-path (2026-08-29 user report): a hidden
// teleport up to a roof catwalk (z=1065), reachable two ways in the
// decompiled entity lump (maps/soccer_cssl_stadium_v8/entities/default_ents):
//   1. Wall buttons "button_tele_t"/"button_tele_ct" -> enable
//      "trig_tele_t"/"trig_tele_ct" (trigger_teleport to the roof).
//   2. Spawn-room 3-button sequences "button_ct_1/2/3" and "button_t_1/2/3"
//      -> enable "tele_player_t"/"tele_player_ct" (also trigger_teleport to
//      the roof).
// Exact-name match, same as the existing ct_killer/terro_killer neutralizer
// (NeutralizeLegacyMapKillTriggers in the main file) - that pattern already
// proved the map's real runtime targetnames do NOT carry the "[PR#]" prefix
// visible in the raw decompiled entity dump, despite that prefix showing up
// there.
//
// 2026-08-30 user report: the map's own physical roof scoreboard (the 0:0
// digits) and its wall score buttons next to the door are invisible from
// most of the pitch, only appearing when you stand close.
//
// The two halves get OPPOSITE treatment, clarified by the user after a
// first pass removed both:
//   - Wall buttons: removed outright (RemoveMapScoreboardButtons). The
//     plugin owns the score; nobody should be able to poke it by hand.
//     This supersedes the older "do NOT touch" note that used to live here.
//   - Roof scoreboard digits: KEPT and left exactly as the map shipped
//     them. The user wants that scoreboard readable from anywhere on the
//     pitch, but neither runtime lever worked - see the notes further
//     down (fade was already off; a PVS override crash-looped the server).
// func_door is still left alone (not part of that request).
public sealed partial class SoccerModMvpPlugin
{
    private static readonly string[] SkyPathButtonNames =
    {
        "button_tele_t", "button_tele_ct",
        "button_t_1", "button_t_2", "button_t_3",
        "button_ct_1", "button_ct_2", "button_ct_3",
    };

    private static readonly string[] SkyPathTeleportNames =
    {
        "trig_tele_t", "trig_tele_ct",
        "tele_player_t", "tele_player_ct",
    };

    private const float SkyPathPlatformMinBottomZ = 500.0f;
    private const float SkyPathPlatformMinSpan = 2000.0f;
    private const float SkyPathPlatformMaxThickness = 200.0f;

    private bool _skyPathNeutralized;

    // The wall score buttons, seen in the decompiled dump with a "[PR#]"
    // prefix that (per the header note above) does not appear on the
    // runtime entity - same convention as SkyPathButtonNames.
    // The first live run removed 6 of these but NOT "reset" (the user
    // reported it still on the wall), so that one's runtime name differs
    // from the dump - hence the suffix match below and the
    // css_sm2_button_probe command to read the real names off the server.
    private static readonly string[] MapScoreboardButtonNames =
    {
        "t_plus", "t_minus", "ct_plus", "ct_minus",
        "reset", "button_goal_ct", "button_goal_t",
    };

    // The physical roof scoreboard is 28 seven-segment func_brush digits,
    // all sharing this name prefix in the dump (Counter_digit_a..g,
    // _a0..g0, _a_axis..g_axis, _a0_axis..g0_axis). Nothing else on this
    // map uses the prefix, so a StartsWith match is safe. These are KEPT
    // and otherwise untouched; the prefix is still used by the probe.
    private const string MapScoreboardDigitNamePrefix = "Counter_digit_";

    private bool _mapScoreboardRemoved;
    private bool _mapScoreboardCullFixed;

    private void OnHighGeometryCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command))
        {
            return;
        }

        var minTopZ = 300.0f;
        if (command.ArgCount >= 2
            && float.TryParse(command.GetArg(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            minTopZ = parsed;
        }

        LogHighGeometry(minTopZ);
        command.ReplyToCommand($"[SM2DIAG] logged brush geometry with topZ >= {minTopZ:F0}");
    }

    // Diagnostic: the buttons+teleports that GRANT access to the sky path
    // are killed above, but the path's own physical geometry is separate
    // and still collidable - the user reported the ball stopping in mid-air
    // over the pitch. Brush entities carry their geometry in a model, not in
    // their origin, so this reports collision bounds to identify them.
    private void LogHighGeometry(float minTopZ)
    {
        foreach (var designerName in new[] { "func_brush", "func_wall", "func_illusionary", "func_clip_vphysics" })
        {
            foreach (var brush in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(designerName))
            {
                if (!brush.IsValid || brush.Collision is not { } collision)
                {
                    continue;
                }

                var origin = brush.AbsOrigin;
                var topZ = (origin?.Z ?? 0.0f) + collision.Maxs.Z;
                if (topZ < minTopZ)
                {
                    continue;
                }

                Logger.LogInformation(
                    "[SM2DIAG] high_geometry class={Class} name={Name} index={Index} origin={Origin} mins={Mins} maxs={Maxs} topZ={TopZ:F1}",
                    designerName,
                    brush.Entity?.Name ?? "<none>",
                    brush.Index,
                    FormatVector(origin),
                    FormatVector(collision.Mins),
                    FormatVector(collision.Maxs),
                    topZ);
            }
        }
    }

    private void NeutralizeSkyPath(string reason)
    {
        var neutralized = 0;

        foreach (var button in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_button"))
        {
            if (button.IsValid
                && button.Entity?.Name is { } buttonName
                && Array.IndexOf(SkyPathButtonNames, buttonName) >= 0)
            {
                button.AcceptInput("Disable");
                button.AcceptInput("Kill");
                neutralized++;
            }
        }

        foreach (var teleport in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("trigger_teleport"))
        {
            if (teleport.IsValid
                && teleport.Entity?.Name is { } teleportName
                && Array.IndexOf(SkyPathTeleportNames, teleportName) >= 0)
            {
                teleport.AcceptInput("Disable");
                teleport.AcceptInput("Kill");
                neutralized++;
            }
        }

        // The sky path's own walking surface, which killing the buttons and
        // teleports above does NOT remove: an unnamed func_brush slab
        // spanning the whole map high above the pitch (measured on this map:
        // origin z=928, bounds x -1620..1621, y -2004..2005, only 65 units
        // thick, top at z=961 - with the roof teleport destinations at
        // z=1065 landing straight onto it). It is invisible from the pitch
        // and was stopping any ball shot high over the centre circle dead in
        // mid-air. Matched by shape, not by name, since it has none:
        // unnamed, pitch-spanning, thin, and far above any real play.
        foreach (var brush in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_brush"))
        {
            if (!brush.IsValid
                || !string.IsNullOrEmpty(brush.Entity?.Name)
                || brush.Collision is not { } collision
                || brush.AbsOrigin is not { } origin)
            {
                continue;
            }

            var bottomZ = origin.Z + collision.Mins.Z;
            var sizeX = collision.Maxs.X - collision.Mins.X;
            var sizeY = collision.Maxs.Y - collision.Mins.Y;
            var thickness = collision.Maxs.Z - collision.Mins.Z;
            if (bottomZ < SkyPathPlatformMinBottomZ
                || sizeX < SkyPathPlatformMinSpan
                || sizeY < SkyPathPlatformMinSpan
                || thickness > SkyPathPlatformMaxThickness)
            {
                continue;
            }

            Logger.LogInformation(
                "[SM2DIAG] sky_path_platform_removed index={Index} origin={Origin} spanX={SpanX:F0} spanY={SpanY:F0} bottomZ={BottomZ:F0}",
                brush.Index,
                FormatVector(origin),
                sizeX,
                sizeY,
                bottomZ);
            brush.AcceptInput("Disable");
            brush.AcceptInput("Kill");
            neutralized++;
        }

        if (neutralized > 0)
        {
            _skyPathNeutralized = true;
            Logger.LogInformation(
                "[SM2DIAG] sky_path_neutralized reason={Reason} count={Count}",
                reason,
                neutralized);
        }
    }

    private static bool IsMapScoreboardButtonName(string name)
    {
        if (Array.IndexOf(MapScoreboardButtonNames, name) >= 0)
        {
            return true;
        }

        // The "reset" button survived an exact-name pass, so its runtime
        // name carries something the dump didn't show. Match any name that
        // ends in "reset" (e.g. "score_reset", "[PR#]reset") without
        // matching unrelated entities that merely contain the word.
        return name.EndsWith("reset", StringComparison.OrdinalIgnoreCase);
    }

    // Removes the map's wall score buttons (2026-08-30 user report - see
    // header comment). Same Disable-then-Kill shape as NeutralizeSkyPath.
    // The roof scoreboard digits are deliberately NOT touched here; they
    // deliberately left alone.
    private void RemoveMapScoreboardButtons(string reason)
    {
        var removed = 0;

        foreach (var button in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_button"))
        {
            if (!button.IsValid || button.Entity?.Name is not { } buttonName
                || !IsMapScoreboardButtonName(buttonName))
            {
                continue;
            }

            Logger.LogInformation(
                "[SM2DIAG] map_scoreboard_removed class=func_button name={Name} index={Index} origin={Origin}",
                buttonName,
                button.Index,
                FormatVector(button.AbsOrigin));
            button.AcceptInput("Disable");
            button.AcceptInput("Kill");
            removed++;
        }

        if (removed > 0)
        {
            _mapScoreboardRemoved = true;
            Logger.LogInformation(
                "[SM2DIAG] map_scoreboard_neutralized reason={Reason} count={Count}",
                reason,
                removed);
        }
    }

    // The roof scoreboard digits are only visible from close up.
    //
    // MEASURED 2026-08-30, and it rules out the obvious cause: every one of
    // these 56 brushes already had fadeMin=0, fadeMax=0, fadeScale=0,
    // allowFadeInView=False, renderMode=kRenderNormal at runtime. A
    // fadeMaxDist of 0 already means "never fade", so distance fade was
    // NEVER what hid them and writing those fields is a no-op.
    //
    // Second attempt (2026-08-30): CBaseModelEntity.ObjectCulling, a
    // networked byte the client uses to decide whether to cull the object.
    // This is a plain schema write, the same kind as the fade fields above,
    // which ran safely - NOT the CheckTransmit call that crashed the
    // server. Before-values are logged so this stays falsifiable: if the
    // digits already read 0 here, this lever is dead too and the answer is
    // genuinely map-side.
    private void FixMapScoreboardVisibility(string reason)
    {
        var touched = 0;

        foreach (var digit in Utilities.FindAllEntitiesByDesignerName<CBaseModelEntity>("func_brush"))
        {
            if (!digit.IsValid || digit.Entity?.Name is not { } digitName
                || !digitName.StartsWith(MapScoreboardDigitNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (touched == 0)
            {
                Logger.LogInformation(
                    "[SM2DIAG] map_scoreboard_cull_before name={Name} objectCulling={Culling} effects={Effects} fadeMin={FadeMin:F1} fadeMax={FadeMax:F1} fadeScale={FadeScale:F3}",
                    digitName,
                    digit.ObjectCulling,
                    digit.Effects,
                    digit.FadeMinDist,
                    digit.FadeMaxDist,
                    digit.FadeScale);
            }

            digit.ObjectCulling = 0;
            Utilities.SetStateChanged(digit, "CBaseModelEntity", "m_nObjectCulling");
            touched++;
        }

        if (touched > 0)
        {
            _mapScoreboardCullFixed = true;
            Logger.LogInformation(
                "[SM2DIAG] map_scoreboard_cull_cleared reason={Reason} count={Count}",
                reason,
                touched);
        }
    }

    // DO NOT re-add a CheckTransmit force-transmit for these entities.
    //
    // Tried 2026-08-30 and it hard-crashed the server (SIGSEGV, status
    // 139, crash-loop) the instant a player connected - CheckTransmit only
    // runs with a client present, which is why an empty server looked
    // fine. Calling TransmitEntities.Add() on these func_brush entities
    // segfaults inside the engine; an entity the engine has already
    // PVS-excluded for that client evidently has no valid networking slot
    // to be forced into. The IsValid checks did not help.
    //
    // The digits are therefore left exactly as the map shipped them. If
    // the roof scoreboard's distance visibility is revisited, do it from
    // the map side (recompiled visibility / a bigger brush), not by
    // fighting PVS at runtime from the plugin.

    // Probe: dump every func_button on the map (to find the "reset"
    // button's real runtime name) and the current fade state of the roof
    // scoreboard digits. Console/RCON only, same gate as the sky-path probe.
    private void OnButtonProbeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command))
        {
            return;
        }

        var buttons = 0;
        foreach (var button in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_button"))
        {
            if (!button.IsValid)
            {
                continue;
            }

            Logger.LogInformation(
                "[SM2DIAG] button_probe name={Name} index={Index} origin={Origin}",
                button.Entity?.Name ?? "<none>",
                button.Index,
                FormatVector(button.AbsOrigin));
            buttons++;
        }

        var digits = 0;
        foreach (var digit in Utilities.FindAllEntitiesByDesignerName<CBaseModelEntity>("func_brush"))
        {
            if (!digit.IsValid || digit.Entity?.Name is not { } digitName
                || !digitName.StartsWith(MapScoreboardDigitNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Logger.LogInformation(
                "[SM2DIAG] digit_probe name={Name} index={Index} origin={Origin} fadeMin={FadeMin:F1} fadeMax={FadeMax:F1} fadeScale={FadeScale:F3} allowFadeInView={AllowFade} renderMode={RenderMode}",
                digitName,
                digit.Index,
                FormatVector(digit.AbsOrigin),
                digit.FadeMinDist,
                digit.FadeMaxDist,
                digit.FadeScale,
                digit.AllowFadeInView,
                digit.RenderMode);
            digits++;
        }

        command.ReplyToCommand($"[SM2DIAG] logged {buttons} func_button and {digits} scoreboard digit entities");
    }
}

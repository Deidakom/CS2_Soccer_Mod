using System;
using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Port of SoMoE-19's gkareas.sp save-detection (2026-08-30 SoMoE
// reconstruction round). Deliberately NOT a port of its box-SETUP UI: the
// original needed an admin to stand at a 6-yard-box corner and derive both
// boxes from that, because SoMoE had no programmatic goal geometry. This
// port already HAS exact, per-map-calibrated goal geometry
// (GoalPlaneY/GoalCenterX/_goalHalfWidthX, tuned via css_sm2goal_calib/
// _swap) - deriving the two boxes from that directly is strictly more
// robust than a manual corner-click and needs no per-map setup step at
// all. The save ARM/CREDIT state machine itself is a straight port.
public sealed partial class SoccerModMvpPlugin
{
    private const float DefaultGkAreaHalfWidth = 250.0f;
    private const float DefaultGkAreaDepth = 220.0f;
    private const float DefaultGkAreaHeight = 150.0f;

    private float _gkAreaHalfWidth = DefaultGkAreaHalfWidth;
    private float _gkAreaDepth = DefaultGkAreaDepth;
    private float _gkAreaHeight = DefaultGkAreaHeight;
    private bool _gkAreasEnabled = true;

    // Save state - module-level, not per-player: only one save can be "in
    // progress" at a time across the whole match, exactly like the
    // original (statsSaver is a single global in stats.sp).
    private int _gkArmedSaverSlot = -1;
    private CsTeam _gkArmedSaverTeam = CsTeam.None;
    private readonly Dictionary<int, int> _gkSavesBySlot = new();

    private void GkAreasOnLoad()
    {
        AddCommand("css_sm2gk_area", "Admin (match): tune the GK save-detection box (halfWidth, depth, height) or on|off.", OnGkAreaCommand);
    }

    // Shared entry point for every ball-touch site (primary kick, wall-pop
    // kick, body push) - added 2026-08-30 alongside GK saves and the stats
    // engine, both of which need the same "who touched it, who touched it
    // before that" bookkeeping the kickoff-wall clear already needed.
    // Replaces the old 3-line "_lastKickerSlot = ...; _lastKickerTeam =
    // ...; ClearKickoffRestrictionOnTouch(...)" block that used to be
    // duplicated at each call site.
    private void RecordBallTouch(CCSPlayerController player, Vector ballOrigin)
    {
        var previousToucherSlot = _lastKickerSlot;
        var previousToucherTeam = _lastKickerTeam;

        GkAreasOnBallTouch(player, ballOrigin, previousToucherTeam);
        StatsOnBallTouch(player, previousToucherSlot, previousToucherTeam);

        _secondLastKickerSlot = previousToucherSlot;
        _secondLastKickerTeam = previousToucherTeam;
        _lastKickerSlot = player.Slot;
        _lastKickerTeam = player.Team;
        ClearKickoffRestrictionOnTouch(player.Team);
    }

    // Returns the box for the team that is DEFENDING at the given goal
    // plane sign (+1 = the +Y goal, -1 = the -Y goal), matching the same
    // enteredCtGoal convention TryGoalPlane uses.
    private (float minX, float maxX, float minY, float maxY, float minZ, float maxZ) GkBoxFor(CsTeam defendingTeam)
    {
        var defendsPositiveEnd = defendingTeam == CsTeam.CounterTerrorist ? !_ctDefendsNegativeY : _ctDefendsNegativeY;
        var goalY = defendsPositiveEnd ? GoalPlaneY : -GoalPlaneY;
        var minY = MathF.Min(goalY, goalY + (defendsPositiveEnd ? -_gkAreaDepth : _gkAreaDepth));
        var maxY = MathF.Max(goalY, goalY + (defendsPositiveEnd ? -_gkAreaDepth : _gkAreaDepth));
        return (
            GoalCenterX - _gkAreaHalfWidth,
            GoalCenterX + _gkAreaHalfWidth,
            minY,
            maxY,
            BallResetZ - BallCollisionRadius,
            BallResetZ - BallCollisionRadius + _gkAreaHeight);
    }

    private static bool InsideBox(Vector point, (float minX, float maxX, float minY, float maxY, float minZ, float maxZ) box) =>
        point.X >= box.minX && point.X <= box.maxX
        && point.Y >= box.minY && point.Y <= box.maxY
        && point.Z >= box.minZ && point.Z <= box.maxZ;

    // Called from the shared RecordBallTouch helper, BEFORE
    // _lastKickerSlot/_lastKickerTeam are overwritten with this new touch,
    // so previousToucherTeam is genuinely the team that touched it before
    // this one.
    private void GkAreasOnBallTouch(CCSPlayerController toucher, Vector ballOrigin, CsTeam previousToucherTeam)
    {
        if (!_gkAreasEnabled)
        {
            return;
        }

        // 1. Credit check, using the state armed BEFORE this touch.
        if (_gkArmedSaverSlot >= 0)
        {
            var saverBox = GkBoxFor(_gkArmedSaverTeam);
            if (!InsideBox(ballOrigin, saverBox))
            {
                CreditSave(_gkArmedSaverSlot);
                _gkArmedSaverSlot = -1;
                _gkArmedSaverTeam = CsTeam.None;
            }
        }

        // 2. Arm check for THIS touch: toucher's own box contains the
        // ball, and the touch immediately before this one was the enemy.
        if (previousToucherTeam != CsTeam.None && previousToucherTeam != toucher.Team)
        {
            var ownBox = GkBoxFor(toucher.Team);
            if (InsideBox(ballOrigin, ownBox))
            {
                _gkArmedSaverSlot = toucher.Slot;
                _gkArmedSaverTeam = toucher.Team;
                Logger.LogInformation("[SM2DIAG] gk_save_armed slot={Slot} name={Name}", toucher.Slot, toucher.PlayerName);
            }
        }
    }

    private void CreditSave(int slot)
    {
        _gkSavesBySlot[slot] = _gkSavesBySlot.GetValueOrDefault(slot) + 1;
        StatsRecordSave(slot);
        if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } saver)
        {
            AnnounceAll($" \x04[Match]\x01 {saver.PlayerName} has made a save.");
            Logger.LogInformation("[SM2DIAG] gk_save_credited slot={Slot} name={Name} totalSaves={Total}", slot, saver.PlayerName, _gkSavesBySlot[slot]);
        }
    }

    private void OnGkAreaCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match"))
        {
            return;
        }

        if (command.ArgCount >= 2 && (command.GetArg(1).Equals("on", StringComparison.OrdinalIgnoreCase) || command.GetArg(1).Equals("off", StringComparison.OrdinalIgnoreCase)))
        {
            _gkAreasEnabled = command.GetArg(1).Equals("on", StringComparison.OrdinalIgnoreCase);
            SaveMatchSettings("gk_area_command");
        }
        else if (command.ArgCount >= 4
            && float.TryParse(command.GetArg(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var halfWidth)
            && float.TryParse(command.GetArg(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var depth)
            && float.TryParse(command.GetArg(3), NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
        {
            _gkAreaHalfWidth = Math.Clamp(halfWidth, 20.0f, 800.0f);
            _gkAreaDepth = Math.Clamp(depth, 20.0f, 800.0f);
            _gkAreaHeight = Math.Clamp(height, 20.0f, 500.0f);
            SaveMatchSettings("gk_area_command");
        }

        command.ReplyToCommand(
            $"[SM] GK area: enabled={_gkAreasEnabled} halfWidth={_gkAreaHalfWidth:F0} depth={_gkAreaDepth:F0} height={_gkAreaHeight:F0} "
            + "(usage: css_sm2gk_area <on|off> | css_sm2gk_area <halfWidth> <depth> <height>)");
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Small, self-contained JSON persistence helpers shared by the admin store
// (SoccerModMvpPlugin.Admin.cs) and, from Phase 2 onward, the tunable ball
// settings store. Deliberately not CSSharp's BasePluginConfig/IPluginConfig:
// that mechanism auto-loads from configs/plugins/... but has no supported
// write-back API, and the whole point here is persisting values the admin
// tunes live in-game. Everything lives under ModuleDirectory so it survives
// a DLL-only redeploy (push-ball-build.sh never touches the plugin folder's
// other files).
public sealed partial class SoccerModMvpPlugin
{
    private static readonly JsonSerializerOptions ConfigJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private string ConfigPath(string fileName) => Path.Combine(ModuleDirectory, fileName);

    // Loads and deserializes fileName, or returns null if the file is
    // missing, empty, or fails to parse (logged, never throws outward).
    private T? LoadJsonOrNull<T>(string fileName) where T : class
    {
        var path = ConfigPath(fileName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(text, ConfigJsonOptions);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[SM2DIAG] config_load_failed file={File}", fileName);
            return null;
        }
    }

    // Atomic write: serialize to a temp file in the same directory, then
    // rename over the target. Avoids a half-written file if the process is
    // killed mid-save (map change, service restart) while an admin/ball
    // command is saving.
    private bool SaveJsonAtomic<T>(string fileName, T value)
    {
        var path = ConfigPath(fileName);
        var tempPath = path + ".tmp";
        try
        {
            Directory.CreateDirectory(ModuleDirectory);
            var text = JsonSerializer.Serialize(value, ConfigJsonOptions);
            File.WriteAllText(tempPath, text);
            File.Move(tempPath, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[SM2DIAG] config_save_failed file={File}", fileName);
            return false;
        }
    }

    // --- Ball tuning persistence (Phase 2) -----------------------------
    // The admin ball-physics panel (css_sm2ball_power/_physics/_wallassist/
    // _model/_kickmode/_collision) mutates plain runtime fields that used to
    // be lost on every map change/restart. This snapshots them to
    // soccermod_settings.json on every accepted change and reloads them
    // before the first EnsureBallFoundation call, so a tuned value survives.
    private const string BallSettingsFileName = "soccermod_settings.json";

    private sealed class BallSettingsStore
    {
        public int Version { get; set; } = 2;
        public float KickDeltaVelocity { get; set; }
        public float KickMaximumBallSpeed { get; set; }
        public float KickOverheadBonusMax { get; set; }
        public float SoftPassStartRatio { get; set; }
        public float SoftPassFullRatio { get; set; }
        public float SoftPassMinPowerScale { get; set; }
        public float SoftPitchStartDegrees { get; set; }
        public float SoftPitchFullDegrees { get; set; }
        public float SoftPitchMinPowerScale { get; set; }
        public float RightClickPowerScale { get; set; }
        public float LeftClickPowerScale { get; set; }
        public float LeftClickCrouchPowerScale { get; set; }
        public float RightClickCrouchPowerScale { get; set; }
        public float? BallSpinFactor { get; set; }
        public float KickElevationSensitivity { get; set; }
        public float BallPushTransferRatio { get; set; }
        public float BallPushMaxSpeed { get; set; }
        public string KickMode { get; set; } = "velocity";
        public string ModelKey { get; set; } = "large1850";
        public float MassScale { get; set; }
        public float Friction { get; set; }
        public float Elasticity { get; set; }
        public float GravityScale { get; set; }
        public int CollisionGroup { get; set; }
        public bool WallAssistEnabled { get; set; }
        public float WallAssistConversionRatio { get; set; }
        public float WallAssistMaxAddedVertical { get; set; }
        public float? WallAssistMinimumNormalRetention { get; set; }
        public bool SettleEnabled { get; set; } = true;
        public float SettleSpeedThreshold { get; set; }
        public int SettleTicks { get; set; }
        public bool MuteLandingEnabled { get; set; } = true;
        public string? MenuRenderMode { get; set; }
        // Retained for migration from v1 settings files.
        public bool? MenuUsePlainCenterText { get; set; }
        public float MenuRedrawPlainSeconds { get; set; }
        public float MenuRedrawHtmlSeconds { get; set; }
        public bool BallImpactEnabled { get; set; } = true;
        public float BallImpactMinSpeed { get; set; }
        public float BallImpactPlayerPushRatio { get; set; }
        public float BallImpactPlayerPushMax { get; set; }
        public float BallImpactFallSpeedThreshold { get; set; }
        public float BallImpactBounceRestitution { get; set; }
        public float BallImpactBounceHorizontalRetention { get; set; }
        public float BallImpactBounceMaxVertical { get; set; }
        public bool? BallImpactFeedbackEnabled { get; set; }
        public int? BallImpactFeedbackMaxVisualDamage { get; set; }
        public float? KickAirborneDeltaScale { get; set; }
        public HashSet<uint>? BlockedSoundHashes { get; set; }
        // null = never configured (keep compiled default); "" = explicitly off.
        public string? KickSoundName { get; set; }
        public float? BallResetX { get; set; }
        public float? BallResetY { get; set; }
        public float? KickSurfaceReach { get; set; }
        public float? CurveStrength { get; set; }
        public float? CurveDuration { get; set; }
        public float? TrapWindow { get; set; }
        public float? TrapRetention { get; set; }
        public float? WallPopChance { get; set; }
        public float? WallPopVertical { get; set; }
        public float? WallPopLateral { get; set; }

        public float? KickAimConeDegrees { get; set; }
        public float? KickCooldownSeconds { get; set; }
    }

    private void BallSettingsOnLoad()
    {
        var stored = LoadJsonOrNull<BallSettingsStore>(BallSettingsFileName);
        if (stored is null)
        {
            // Nothing saved yet - keep the compiled-in defaults and write
            // them out once, so the file exists from the first run onward.
            SaveBallSettings("initial_defaults");
            return;
        }

        if (stored.KickSurfaceReach is >= 16 and <= 160) _kickSurfaceReach = stored.KickSurfaceReach.Value;
        if (stored.KickAimConeDegrees is >= 10 and <= 90) _kickAimConeDegrees = stored.KickAimConeDegrees.Value;
        if (stored.KickCooldownSeconds is >= .05f and <= 2) _kickCooldownSeconds = stored.KickCooldownSeconds.Value;
        if (stored.CurveStrength is >= 0f and <= 2f) _curveStrength = stored.CurveStrength.Value;
        if (stored.CurveDuration is >= 0f and <= 3f) _curveDuration = stored.CurveDuration.Value;
        if (stored.TrapWindow is >= 0.1f and <= 1f) _trapWindow = stored.TrapWindow.Value;
        if (stored.TrapRetention is >= 0f and <= 1f) _trapRetention = stored.TrapRetention.Value;
        if (stored.WallPopChance is >= 0f and <= 1f) _wallPopChance = stored.WallPopChance.Value;
        if (stored.WallPopVertical is >= 0f and <= 2000f) _wallPopVertical = stored.WallPopVertical.Value;
        if (stored.WallPopLateral is >= 0f and <= 1000f) _wallPopLateral = stored.WallPopLateral.Value;
        _kickDeltaVelocity = stored.KickDeltaVelocity > 0 ? stored.KickDeltaVelocity : _kickDeltaVelocity;
        _kickMaximumBallSpeed = stored.KickMaximumBallSpeed > 0 ? stored.KickMaximumBallSpeed : _kickMaximumBallSpeed;
        _kickOverheadBonusMax = (stored.KickOverheadBonusMax > 0 || (stored.Version >= 3 && stored.KickOverheadBonusMax == 0)) ? stored.KickOverheadBonusMax : _kickOverheadBonusMax;
        if ((stored.SoftPassStartRatio > 0 || (stored.Version >= 3 && stored.SoftPassStartRatio == 0))) _softPassStartRatio = stored.SoftPassStartRatio;
        if (stored.SoftPassFullRatio > 0) _softPassFullRatio = stored.SoftPassFullRatio;
        if (stored.SoftPassMinPowerScale > 0) _softPassMinPowerScale = stored.SoftPassMinPowerScale;
        if ((stored.SoftPitchStartDegrees > 0 || (stored.Version >= 3 && stored.SoftPitchStartDegrees == 0))) _softPitchStartDegrees = stored.SoftPitchStartDegrees;
        if (stored.SoftPitchFullDegrees > 0) _softPitchFullDegrees = stored.SoftPitchFullDegrees;
        if (stored.SoftPitchMinPowerScale > 0) _softPitchMinPowerScale = stored.SoftPitchMinPowerScale;
        if (stored.RightClickPowerScale > 0) _rightClickPowerScale = stored.RightClickPowerScale;
        if (stored.LeftClickPowerScale > 0) _leftClickPowerScale = stored.LeftClickPowerScale;
        if (stored.LeftClickCrouchPowerScale > 0) _leftClickCrouchPowerScale = stored.LeftClickCrouchPowerScale;
        if (stored.RightClickCrouchPowerScale > 0) _rightClickCrouchPowerScale = stored.RightClickCrouchPowerScale;
        // Range-checked nullable, not the ">0" pattern above - 0 is a
        // legitimate "spin off" value (the user's requested fallback), not
        // "unset".
        if (stored.BallSpinFactor is { } spinFactor && spinFactor is >= 0.0f and <= 2.0f) _ballSpinFactor = spinFactor;
        if (stored.KickElevationSensitivity > 0) _kickElevationSensitivity = stored.KickElevationSensitivity;
        if ((stored.BallPushTransferRatio > 0 || (stored.Version >= 3 && stored.BallPushTransferRatio == 0))) _ballPushTransferRatio = stored.BallPushTransferRatio;
        if ((stored.BallPushMaxSpeed > 0 || (stored.Version >= 3 && stored.BallPushMaxSpeed == 0))) _ballPushMaxSpeed = stored.BallPushMaxSpeed;
        _kickMode = string.Equals(stored.KickMode, "thruster", StringComparison.OrdinalIgnoreCase)
            ? KickMode.Thruster
            : KickMode.Velocity;
        if (stored.ModelKey is not null && BallPhysicsModelCandidates.TryGetValue(stored.ModelKey, out var modelPath))
        {
            _ballPhysicsModelKey = stored.ModelKey;
            _ballPhysicsModel = modelPath;
        }
        if (stored.MassScale > 0) _gameplayMassScale = stored.MassScale;
        if (stored.Friction >= 0) _gameplayFriction = stored.Friction;
        if (stored.Elasticity >= 0) _gameplayElasticity = stored.Elasticity;
        if (stored.GravityScale > 0) _gameplayGravityScale = stored.GravityScale;
        _ballCollisionGroup = stored.CollisionGroup;
        _wallAssistEnabled = stored.WallAssistEnabled;
        if ((stored.WallAssistConversionRatio > 0 || (stored.Version >= 3 && stored.WallAssistConversionRatio == 0))) _wallAssistConversionRatio = stored.WallAssistConversionRatio;
        if ((stored.WallAssistMaxAddedVertical > 0 || (stored.Version >= 3 && stored.WallAssistMaxAddedVertical == 0))) _wallAssistMaxAddedVertical = stored.WallAssistMaxAddedVertical;
        if (stored.WallAssistMinimumNormalRetention is { } normalRetention && normalRetention >= 0)
        {
            _wallAssistMinimumNormalRetention = normalRetention;
        }
        _settleEnabled = stored.SettleEnabled;
        if ((stored.SettleSpeedThreshold > 0 || (stored.Version >= 3 && stored.SettleSpeedThreshold == 0))) _settleSpeedThreshold = stored.SettleSpeedThreshold;
        if (stored.SettleTicks > 0) _settleTicks = stored.SettleTicks;
        _muteLandingEnabled = stored.MuteLandingEnabled;
        if (!string.IsNullOrWhiteSpace(stored.MenuRenderMode)
            && Enum.TryParse<MenuRenderMode>(stored.MenuRenderMode, ignoreCase: true, out var menuRenderMode))
        {
            _menuRenderMode = menuRenderMode;
        }
        else
        {
            _menuRenderMode = stored.MenuUsePlainCenterText == false
                ? MenuRenderMode.Html
                : MenuRenderMode.Plain;
        }
        if (stored.MenuRedrawPlainSeconds > 0) _menuRedrawPlainSeconds = stored.MenuRedrawPlainSeconds;
        if (stored.MenuRedrawHtmlSeconds >= 0) _menuRedrawHtmlSeconds = stored.MenuRedrawHtmlSeconds;
        _ballImpactEnabled = stored.BallImpactEnabled;
        if (stored.BallImpactMinSpeed > 0) _ballImpactMinSpeed = stored.BallImpactMinSpeed;
        if ((stored.BallImpactPlayerPushRatio > 0 || (stored.Version >= 3 && stored.BallImpactPlayerPushRatio == 0))) _ballImpactPlayerPushRatio = stored.BallImpactPlayerPushRatio;
        if (stored.BallImpactPlayerPushMax > 0) _ballImpactPlayerPushMax = stored.BallImpactPlayerPushMax;
        if (stored.BallImpactFallSpeedThreshold > 0) _ballImpactFallSpeedThreshold = stored.BallImpactFallSpeedThreshold;
        if ((stored.BallImpactBounceRestitution > 0 || (stored.Version >= 3 && stored.BallImpactBounceRestitution == 0))) _ballImpactBounceRestitution = stored.BallImpactBounceRestitution;
        if ((stored.BallImpactBounceHorizontalRetention > 0 || (stored.Version >= 3 && stored.BallImpactBounceHorizontalRetention == 0))) _ballImpactBounceHorizontalRetention = stored.BallImpactBounceHorizontalRetention;
        if ((stored.BallImpactBounceMaxVertical > 0 || (stored.Version >= 3 && stored.BallImpactBounceMaxVertical == 0))) _ballImpactBounceMaxVertical = stored.BallImpactBounceMaxVertical;
        if (stored.BallImpactFeedbackEnabled is { } feedbackEnabled) _ballImpactFeedbackEnabled = feedbackEnabled;
        if (stored.BallImpactFeedbackMaxVisualDamage is { } maxVisualDamage && maxVisualDamage >= 1)
        {
            _ballImpactFeedbackMaxVisualDamage = maxVisualDamage;
        }
        if (stored.KickAirborneDeltaScale is { } airborneScale && airborneScale is >= 0.1f and <= 1.0f)
        {
            _kickAirborneDeltaScale = airborneScale;
        }
        if (stored.BlockedSoundHashes is { } blockedHashes)
        {
            _blockedSoundHashes = blockedHashes;
        }
        if (stored.KickSoundName is { } kickSoundName)
        {
            _kickSoundName = kickSoundName;
        }
        // Range-checked nullable (0 is a legitimate stored coordinate).
        if (stored.BallResetX is { } resetX && MathF.Abs(resetX) <= 500.0f) _ballResetX = resetX;
        if (stored.BallResetY is { } resetY && MathF.Abs(resetY) <= 500.0f) _ballResetY = resetY;

        Logger.LogInformation(
            "[SM2DIAG] ball_settings_loaded kickDelta={KickDelta:F0} model={Model} massScale={MassScale:F2}",
            _kickDeltaVelocity,
            _ballPhysicsModelKey,
            _gameplayMassScale);
    }

    private bool SaveBallSettings(string reason)
    {
        var snapshot = new BallSettingsStore
        {
            Version = 3,
            CurveStrength = _curveStrength,
            CurveDuration = _curveDuration,
            TrapWindow = _trapWindow,
            TrapRetention = _trapRetention,
            WallPopChance = _wallPopChance,
            WallPopVertical = _wallPopVertical,
            WallPopLateral = _wallPopLateral,

            KickSurfaceReach = _kickSurfaceReach,
            KickAimConeDegrees = _kickAimConeDegrees,
            KickCooldownSeconds = _kickCooldownSeconds,
            KickDeltaVelocity = _kickDeltaVelocity,
            KickMaximumBallSpeed = _kickMaximumBallSpeed,
            KickOverheadBonusMax = _kickOverheadBonusMax,
            SoftPassStartRatio = _softPassStartRatio,
            SoftPassFullRatio = _softPassFullRatio,
            SoftPassMinPowerScale = _softPassMinPowerScale,
            SoftPitchStartDegrees = _softPitchStartDegrees,
            SoftPitchFullDegrees = _softPitchFullDegrees,
            SoftPitchMinPowerScale = _softPitchMinPowerScale,
            RightClickPowerScale = _rightClickPowerScale,
            LeftClickPowerScale = _leftClickPowerScale,
            LeftClickCrouchPowerScale = _leftClickCrouchPowerScale,
            RightClickCrouchPowerScale = _rightClickCrouchPowerScale,
            BallSpinFactor = _ballSpinFactor,
            KickElevationSensitivity = _kickElevationSensitivity,
            BallPushTransferRatio = _ballPushTransferRatio,
            BallPushMaxSpeed = _ballPushMaxSpeed,
            KickMode = _kickMode == KickMode.Thruster ? "thruster" : "velocity",
            ModelKey = _ballPhysicsModelKey,
            MassScale = _gameplayMassScale,
            Friction = _gameplayFriction,
            Elasticity = _gameplayElasticity,
            GravityScale = _gameplayGravityScale,
            CollisionGroup = _ballCollisionGroup,
            WallAssistEnabled = _wallAssistEnabled,
            WallAssistConversionRatio = _wallAssistConversionRatio,
            WallAssistMaxAddedVertical = _wallAssistMaxAddedVertical,
            WallAssistMinimumNormalRetention = _wallAssistMinimumNormalRetention,
            SettleEnabled = _settleEnabled,
            SettleSpeedThreshold = _settleSpeedThreshold,
            SettleTicks = _settleTicks,
            MuteLandingEnabled = _muteLandingEnabled,
            MenuRenderMode = _menuRenderMode.ToString().ToLowerInvariant(),
            MenuUsePlainCenterText = _menuRenderMode != MenuRenderMode.Html,
            MenuRedrawPlainSeconds = _menuRedrawPlainSeconds,
            MenuRedrawHtmlSeconds = _menuRedrawHtmlSeconds,
            BallImpactEnabled = _ballImpactEnabled,
            BallImpactMinSpeed = _ballImpactMinSpeed,
            BallImpactPlayerPushRatio = _ballImpactPlayerPushRatio,
            BallImpactPlayerPushMax = _ballImpactPlayerPushMax,
            BallImpactFallSpeedThreshold = _ballImpactFallSpeedThreshold,
            BallImpactBounceRestitution = _ballImpactBounceRestitution,
            BallImpactBounceHorizontalRetention = _ballImpactBounceHorizontalRetention,
            BallImpactBounceMaxVertical = _ballImpactBounceMaxVertical,
            BallImpactFeedbackEnabled = _ballImpactFeedbackEnabled,
            BallImpactFeedbackMaxVisualDamage = _ballImpactFeedbackMaxVisualDamage,
            KickAirborneDeltaScale = _kickAirborneDeltaScale,
            BlockedSoundHashes = _blockedSoundHashes,
            KickSoundName = _kickSoundName,
            BallResetX = _ballResetX,
            BallResetY = _ballResetY,
        };

        if (SaveJsonAtomic(BallSettingsFileName, snapshot))
        {
            Logger.LogInformation("[SM2DIAG] ball_settings_saved reason={Reason}", reason);
            return true;
        }
        return false;
    }

    // 2026-09-02 user request: a "Restore defaults" option in the Ball
    // menu. "Default" here means the Default* consts above, which were
    // promoted to match whatever was live on the server at the time of
    // this request (see the comments on DefaultBallSpinFactor,
    // DefaultKickAirborneDeltaScale, DefaultRightClickPowerScale,
    // DefaultLeftClickPowerScale) - so this restores the values the user
    // had actually settled on, not the original launch tuning. Only
    // touches fields the Ball menu itself can edit; collision group and
    // the Advanced-menu-only fields aren't included because there's no
    // "undo" affordance for them in the menu to begin with.
    private void RestoreBallDefaults(string reason)
    {
        _ballSpinFactor = DefaultBallSpinFactor;
        _kickAirborneDeltaScale = DefaultKickAirborneDeltaScale;
        _leftClickPowerScale = DefaultLeftClickPowerScale;
        _rightClickPowerScale = DefaultRightClickPowerScale;
        _ballPushTransferRatio = DefaultBallPushTransferRatio;
        _ballPushMaxSpeed = DefaultBallPushMaxSpeed;
        _kickSoundName = DefaultKickSoundName;
        _ballImpactEnabled = true;
        _settleEnabled = DefaultSettleEnabled;
        _kickElevationSensitivity = DefaultKickElevationSensitivity;

        SaveBallSettings(reason);
        Logger.LogInformation("[SM2DIAG] ball_settings_restored_to_defaults reason={Reason}", reason);
    }

    private void OnBallSoftPassCommand(CounterStrikeSharp.API.Core.CCSPlayerController? player, CounterStrikeSharp.API.Modules.Commands.CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 4
            && float.TryParse(command.GetArg(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var start)
            && float.TryParse(command.GetArg(2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var full)
            && float.TryParse(command.GetArg(3), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minScale)
            && full > start
            && minScale is > 0.0f and <= 1.0f)
        {
            _softPassStartRatio = start;
            _softPassFullRatio = full;
            _softPassMinPowerScale = minScale;
            SaveBallSettings("softpass_command");
        }

        command.ReplyToCommand(
            $"[SM] soft pass: start={_softPassStartRatio:F2} full={_softPassFullRatio:F2} minScale={_softPassMinPowerScale:F2} "
            + "(aim-below-centre in ball radii; usage: css_sm2ball_softpass <start> <full> <minScale>)");
    }

    private void OnBallSoftPitchCommand(CounterStrikeSharp.API.Core.CCSPlayerController? player, CounterStrikeSharp.API.Modules.Commands.CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 4
            && float.TryParse(command.GetArg(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var start)
            && float.TryParse(command.GetArg(2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var full)
            && float.TryParse(command.GetArg(3), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minScale)
            && full > start
            && minScale is > 0.0f and <= 1.0f)
        {
            _softPitchStartDegrees = start;
            _softPitchFullDegrees = full;
            _softPitchMinPowerScale = minScale;
            SaveBallSettings("softpitch_command");
        }

        command.ReplyToCommand(
            $"[SM] soft pitch: start={_softPitchStartDegrees:F1} full={_softPitchFullDegrees:F1} minScale={_softPitchMinPowerScale:F2} "
            + "(look-down angle in degrees; usage: css_sm2ball_softpitch <startDeg> <fullDeg> <minScale>)");
    }

    private void OnBallRightClickCommand(CounterStrikeSharp.API.Core.CCSPlayerController? player, CounterStrikeSharp.API.Modules.Commands.CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 2
            && float.TryParse(command.GetArg(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale)
            && scale is >= 0.05f and <= 1.0f)
        {
            _rightClickPowerScale = scale;
            SaveBallSettings("rightclick_command");
        }

        command.ReplyToCommand(
            $"[SM] right-click kick power scale: {_rightClickPowerScale:F2} "
            + "(usage: css_sm2ball_rightclick <scale 0.05-1.0>)");
    }

    private void OnBallLeftClickCommand(CounterStrikeSharp.API.Core.CCSPlayerController? player, CounterStrikeSharp.API.Modules.Commands.CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 2
            && float.TryParse(command.GetArg(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale)
            && scale is >= 0.05f and <= 2.0f)
        {
            _leftClickPowerScale = scale;
            SaveBallSettings("leftclick_command");
        }

        command.ReplyToCommand(
            $"[SM] left-click kick power scale: {_leftClickPowerScale:F2} "
            + "(usage: css_sm2ball_leftclick <scale 0.05-2.0>)");
    }

    private void OnBallLeftClickCrouchCommand(CounterStrikeSharp.API.Core.CCSPlayerController? player, CounterStrikeSharp.API.Modules.Commands.CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 2
            && float.TryParse(command.GetArg(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale)
            && scale is >= 0.05f and <= 2.0f)
        {
            _leftClickCrouchPowerScale = scale;
            SaveBallSettings("leftclick_crouch_command");
        }

        command.ReplyToCommand(
            $"[SM] crouched left-click kick power scale: {_leftClickCrouchPowerScale:F2} "
            + "(usage: css_sm2ball_leftclick_crouch <scale 0.05-2.0>)");
    }

    private void OnBallRightClickCrouchCommand(CounterStrikeSharp.API.Core.CCSPlayerController? player, CounterStrikeSharp.API.Modules.Commands.CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 2
            && float.TryParse(command.GetArg(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale)
            && scale is >= 0.05f and <= 2.0f)
        {
            _rightClickCrouchPowerScale = scale;
            SaveBallSettings("rightclick_crouch_command");
        }

        command.ReplyToCommand(
            $"[SM] crouched right-click kick power scale: {_rightClickCrouchPowerScale:F2} "
            + "(usage: css_sm2ball_rightclick_crouch <scale 0.05-2.0>)");
    }

    private void OnBallElevationCommand(CounterStrikeSharp.API.Core.CCSPlayerController? player, CounterStrikeSharp.API.Modules.Commands.CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 2
            && float.TryParse(command.GetArg(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sensitivity)
            && sensitivity is >= 0.1f and <= 1.0f)
        {
            _kickElevationSensitivity = sensitivity;
            SaveBallSettings("elevation_command");
        }

        command.ReplyToCommand(
            $"[SM] kick elevation sensitivity: {_kickElevationSensitivity:F2} "
            + "(how much view pitch drives launch angle on ordinary kicks, 1.0=full, fades back to full near a headed ball; usage: css_sm2ball_elevation <0.1-1.0>)");
    }

    private void OnBallPushCommand(CounterStrikeSharp.API.Core.CCSPlayerController? player, CounterStrikeSharp.API.Modules.Commands.CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 3
            && float.TryParse(command.GetArg(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ratio)
            && float.TryParse(command.GetArg(2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var maxSpeed)
            && ratio > 0.0f
            && maxSpeed > 0.0f)
        {
            _ballPushTransferRatio = ratio;
            _ballPushMaxSpeed = maxSpeed;
            SaveBallSettings("push_command");
        }

        command.ReplyToCommand(
            $"[SM] body push: ratio={_ballPushTransferRatio:F2} maxSpeed={_ballPushMaxSpeed:F0} "
            + "(usage: css_sm2ball_push <ratio> <maxSpeed>)");
    }

    private void OnBallAirKickCommand(CounterStrikeSharp.API.Core.CCSPlayerController? player, CounterStrikeSharp.API.Modules.Commands.CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 2
            && float.TryParse(command.GetArg(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale)
            && scale is >= 0.1f and <= 1.0f)
        {
            _kickAirborneDeltaScale = scale;
            SaveBallSettings("airkick_command");
        }

        command.ReplyToCommand(
            $"[SM] airborne (volley) kick power scale: {_kickAirborneDeltaScale:F2} "
            + "(ground kicks unaffected; usage: css_sm2ball_airkick <0.1-1.0>, 1.0=off)");
    }

    private void OnBallKickSoundCommand(CounterStrikeSharp.API.Core.CCSPlayerController? player, CounterStrikeSharp.API.Modules.Commands.CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            var arg = command.GetArg(1);
            _kickSoundName = arg.Equals("off", StringComparison.OrdinalIgnoreCase) ? string.Empty : arg;
            SaveBallSettings("kicksound_command");
        }

        command.ReplyToCommand(
            $"[SM] kick sound: {(string.IsNullOrEmpty(_kickSoundName) ? "off" : _kickSoundName)} "
            + "(usage: css_sm2ball_kicksound <soundEventName|off>)");
    }

    // 2026-09-01: the painted centre spot is a map TEXTURE - no trace or
    // arena measurement can find it (both prior hard-coded guesses looked
    // visibly wrong to the user). "here" captures the ball's current
    // position after rolling it onto the spot by eye, once, forever.
    private void OnBallCenterCommand(CounterStrikeSharp.API.Core.CCSPlayerController? player, CounterStrikeSharp.API.Modules.Commands.CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            var arg = command.GetArg(1);
            if (arg.Equals("here", StringComparison.OrdinalIgnoreCase))
            {
                if (_ball is { IsValid: true } && _ball.AbsOrigin is { } origin
                    && MathF.Abs(origin.X) <= 500.0f && MathF.Abs(origin.Y) <= 500.0f)
                {
                    _ballResetX = origin.X;
                    _ballResetY = origin.Y;
                    SaveBallSettings("center_command");
                }
                else
                {
                    command.ReplyToCommand("[SM] ball unavailable or too far from midfield (limit 500u) - roll it onto the spot first");
                    return;
                }
            }
            else if (arg.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                _ballResetX = DefaultBallResetX;
                _ballResetY = DefaultBallResetY;
                SaveBallSettings("center_command");
            }
            else if (command.ArgCount >= 3
                && float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
                && float.TryParse(command.GetArg(2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)
                && MathF.Abs(x) <= 500.0f && MathF.Abs(y) <= 500.0f)
            {
                _ballResetX = x;
                _ballResetY = y;
                SaveBallSettings("center_command");
            }
        }

        command.ReplyToCommand(
            $"[SM] kickoff spot: ({_ballResetX:F2}, {_ballResetY:F2}) "
            + "(usage: css_sm2ball_center <here|x y|default>; 'here' captures the ball's current position)");
    }

    private void OnReloadSettingsCommand(CounterStrikeSharp.API.Core.CCSPlayerController? player, CounterStrikeSharp.API.Modules.Commands.CommandInfo command)
    {
        if (!RequireServerConsole(player, command))
        {
            return;
        }

        BallSettingsOnLoad();
        command.ReplyToCommand("[SM2DIAG] soccermod_settings.json reloaded");
    }

    // --- Match/goal/sprint tuning persistence (Phase 2, CS:S-parity plan) --
    // css_sm2goal_calib/_swap and the match period config were runtime-only
    // (lost on every restart) - flagged as a real gap while writing the
    // parity plan. Separate file from the ball settings: different concern,
    // same atomic-write pattern.
    private const string MatchSettingsFileName = "soccermod_match_settings.json";

    private sealed class MatchSettingsStore
    {
        public int Version { get; set; } = 1;
        public float GoalHalfWidthX { get; set; }
        public float GoalApertureMaxZ { get; set; }
        public bool CtDefendsNegativeY { get; set; } = true;
        public int MatchPeriods { get; set; }
        public float PeriodLengthSeconds { get; set; }
        public float BreakLengthSeconds { get; set; }
        public bool GoldenGoalEnabled { get; set; } = true;
        public string TeamNameCt { get; set; } = "Counter-Terrorists";
        public string TeamNameT { get; set; } = "Terrorists";
        public bool SprintUseButtonTrigger { get; set; } = true;
        public bool GoalPunishEnabled { get; set; } = true;
        public bool GoalRoundWinEnabled { get; set; }
        public bool HealthGodmodeEnabled { get; set; } = true;
        public int HealthAmount { get; set; }
        public string ChatPrefix { get; set; } = "Soccer Mod";
        public string ChatPrefixColor { get; set; } = "green";
        public string ChatTextColor { get; set; } = "lightgreen";
        public int DeadChatMode { get; set; }
        public bool BlockDjbEnabled { get; set; } = true;
        public float BlockDjbSeconds { get; set; }
        public bool KickoffWallEnabled { get; set; }
        public bool AfkLockEnabled { get; set; }
        public bool GkAreasEnabled { get; set; } = true;
        public float GkAreaHalfWidth { get; set; }
        public float GkAreaDepth { get; set; }
        public float GkAreaHeight { get; set; }
        public bool? TeamColorEnabled { get; set; }
        public int? TeamColorTr { get; set; }
        public int? TeamColorTg { get; set; }
        public int? TeamColorTb { get; set; }
        public int? TeamColorCtr { get; set; }
        public int? TeamColorCtg { get; set; }
        public int? TeamColorCtb { get; set; }
        public bool? TeamModelEnabled { get; set; }
        // 2026-09-01 goal-line fix (Match.cs): the goal line's Y and how far
        // past it the ball's CENTRE must travel before it counts. Nullable so
        // an older file keeps the compiled defaults.
        public float? GoalLineY { get; set; }
        public float? GoalDepthRequired { get; set; }
        // 2026-09-02: !menu narrowed to Help/Settings/Credits for everyone
        // without the "admin" flag (Menu.cs). Nullable so an older file
        // keeps the compiled default (off).
        public bool? PublicModeEnabled { get; set; }
    }

    private void MatchSettingsOnLoad()
    {
        var stored = LoadJsonOrNull<MatchSettingsStore>(MatchSettingsFileName);
        if (stored is null)
        {
            SaveMatchSettings("initial_defaults");
            return;
        }

        if (stored.GoalHalfWidthX > 0) _goalHalfWidthX = stored.GoalHalfWidthX;
        if (stored.GoalApertureMaxZ > 0) _goalApertureMaxZ = stored.GoalApertureMaxZ;
        _ctDefendsNegativeY = stored.CtDefendsNegativeY;
        if (stored.MatchPeriods > 0) _matchPeriods = stored.MatchPeriods;
        if (stored.PeriodLengthSeconds > 0) _periodLengthSeconds = stored.PeriodLengthSeconds;
        if (float.IsFinite(stored.BreakLengthSeconds) && stored.BreakLengthSeconds >= 0) _breakLengthSeconds = stored.BreakLengthSeconds;
        _goldenGoalEnabled = stored.GoldenGoalEnabled;
        if (!string.IsNullOrWhiteSpace(stored.TeamNameCt)) _teamNameCt = stored.TeamNameCt;
        if (!string.IsNullOrWhiteSpace(stored.TeamNameT)) _teamNameT = stored.TeamNameT;
        _permanentTeamNameCt = _teamNameCt;
        _permanentTeamNameT = _teamNameT;
        _sprintUseButtonTrigger = stored.SprintUseButtonTrigger;
        _goalPunishEnabled = stored.GoalPunishEnabled;
        _goalRoundWinEnabled = stored.GoalRoundWinEnabled;
        _healthGodmodeEnabled = stored.HealthGodmodeEnabled;
        if (stored.HealthAmount > 0) _healthAmount = stored.HealthAmount;
        if (!string.IsNullOrWhiteSpace(stored.ChatPrefix)) _chatPrefix = stored.ChatPrefix;
        if (!string.IsNullOrWhiteSpace(stored.ChatPrefixColor)) _chatPrefixColor = stored.ChatPrefixColor;
        if (!string.IsNullOrWhiteSpace(stored.ChatTextColor)) _chatTextColor = stored.ChatTextColor;
        _deadChatMode = stored.DeadChatMode;
        _blockDjbEnabled = stored.BlockDjbEnabled;
        if (stored.BlockDjbSeconds > 0) _blockDjbSeconds = stored.BlockDjbSeconds;
        _kickoffWallEnabled = stored.KickoffWallEnabled;
        _afkLockEnabled = stored.AfkLockEnabled;
        _gkAreasEnabled = stored.GkAreasEnabled;
        if (stored.GkAreaHalfWidth > 0) _gkAreaHalfWidth = stored.GkAreaHalfWidth;
        if (stored.GkAreaDepth > 0) _gkAreaDepth = stored.GkAreaDepth;
        if (stored.GkAreaHeight > 0) _gkAreaHeight = stored.GkAreaHeight;
        if (stored.TeamColorEnabled is { } colorEnabled) _teamColorEnabled = colorEnabled;
        if (stored.TeamColorTr is { } tr && tr is >= 0 and <= 255) _teamColorTr = tr;
        if (stored.TeamColorTg is { } tg && tg is >= 0 and <= 255) _teamColorTg = tg;
        if (stored.TeamColorTb is { } tb && tb is >= 0 and <= 255) _teamColorTb = tb;
        if (stored.TeamColorCtr is { } ctr && ctr is >= 0 and <= 255) _teamColorCtr = ctr;
        if (stored.TeamColorCtg is { } ctg && ctg is >= 0 and <= 255) _teamColorCtg = ctg;
        if (stored.TeamColorCtb is { } ctb && ctb is >= 0 and <= 255) _teamColorCtb = ctb;
        if (stored.TeamModelEnabled is { } modelEnabled) _teamModelEnabled = modelEnabled;
        if (stored.GoalLineY is { } goalLineY && goalLineY is >= 1000.0f and <= 1500.0f) _goalLineY = goalLineY;
        if (stored.GoalDepthRequired is { } goalDepth && goalDepth is >= 0.0f and <= 60.0f) _goalDepthRequired = goalDepth;
        if (stored.PublicModeEnabled is { } publicMode) _publicModeEnabled = publicMode;

        Logger.LogInformation(
            "[SM2DIAG] match_settings_loaded periods={Periods} periodLength={PeriodLength} goalHalfWidth={GoalHalfWidth:F0}",
            _matchPeriods,
            _periodLengthSeconds,
            _goalHalfWidthX);
    }

    private void SaveMatchSettings(string reason)
    {
        var snapshot = new MatchSettingsStore
        {
            GoalHalfWidthX = _goalHalfWidthX,
            GoalApertureMaxZ = _goalApertureMaxZ,
            CtDefendsNegativeY = _ctDefendsNegativeY,
            MatchPeriods = _matchPeriods,
            PeriodLengthSeconds = _periodLengthSeconds,
            BreakLengthSeconds = _breakLengthSeconds,
            GoldenGoalEnabled = _goldenGoalEnabled,
            TeamNameCt = _permanentTeamNameCt,
            TeamNameT = _permanentTeamNameT,
            SprintUseButtonTrigger = _sprintUseButtonTrigger,
            GoalPunishEnabled = _goalPunishEnabled,
            GoalRoundWinEnabled = _goalRoundWinEnabled,
            HealthGodmodeEnabled = _healthGodmodeEnabled,
            HealthAmount = _healthAmount,
            ChatPrefix = _chatPrefix,
            ChatPrefixColor = _chatPrefixColor,
            ChatTextColor = _chatTextColor,
            DeadChatMode = _deadChatMode,
            BlockDjbEnabled = _blockDjbEnabled,
            BlockDjbSeconds = _blockDjbSeconds,
            KickoffWallEnabled = _kickoffWallEnabled,
            AfkLockEnabled = _afkLockEnabled,
            GkAreasEnabled = _gkAreasEnabled,
            GkAreaHalfWidth = _gkAreaHalfWidth,
            GkAreaDepth = _gkAreaDepth,
            GkAreaHeight = _gkAreaHeight,
            TeamColorEnabled = _teamColorEnabled,
            TeamColorTr = _teamColorTr,
            TeamColorTg = _teamColorTg,
            TeamColorTb = _teamColorTb,
            TeamColorCtr = _teamColorCtr,
            TeamColorCtg = _teamColorCtg,
            TeamColorCtb = _teamColorCtb,
            TeamModelEnabled = _teamModelEnabled,
            GoalLineY = _goalLineY,
            GoalDepthRequired = _goalDepthRequired,
            PublicModeEnabled = _publicModeEnabled,
        };

        if (SaveJsonAtomic(MatchSettingsFileName, snapshot))
        {
            Logger.LogInformation("[SM2DIAG] match_settings_saved reason={Reason}", reason);
        }
    }
}

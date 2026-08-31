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
        public float? BallImpactFeedbackMaxShakeAmplitude { get; set; }
        public int? BallImpactFeedbackMaxVisualDamage { get; set; }
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

        _kickDeltaVelocity = stored.KickDeltaVelocity > 0 ? stored.KickDeltaVelocity : _kickDeltaVelocity;
        _kickMaximumBallSpeed = stored.KickMaximumBallSpeed > 0 ? stored.KickMaximumBallSpeed : _kickMaximumBallSpeed;
        _kickOverheadBonusMax = stored.KickOverheadBonusMax > 0 ? stored.KickOverheadBonusMax : _kickOverheadBonusMax;
        if (stored.SoftPassStartRatio > 0) _softPassStartRatio = stored.SoftPassStartRatio;
        if (stored.SoftPassFullRatio > 0) _softPassFullRatio = stored.SoftPassFullRatio;
        if (stored.SoftPassMinPowerScale > 0) _softPassMinPowerScale = stored.SoftPassMinPowerScale;
        if (stored.SoftPitchStartDegrees > 0) _softPitchStartDegrees = stored.SoftPitchStartDegrees;
        if (stored.SoftPitchFullDegrees > 0) _softPitchFullDegrees = stored.SoftPitchFullDegrees;
        if (stored.SoftPitchMinPowerScale > 0) _softPitchMinPowerScale = stored.SoftPitchMinPowerScale;
        if (stored.RightClickPowerScale > 0) _rightClickPowerScale = stored.RightClickPowerScale;
        if (stored.LeftClickPowerScale > 0) _leftClickPowerScale = stored.LeftClickPowerScale;
        if (stored.LeftClickCrouchPowerScale > 0) _leftClickCrouchPowerScale = stored.LeftClickCrouchPowerScale;
        if (stored.KickElevationSensitivity > 0) _kickElevationSensitivity = stored.KickElevationSensitivity;
        if (stored.BallPushTransferRatio > 0) _ballPushTransferRatio = stored.BallPushTransferRatio;
        if (stored.BallPushMaxSpeed > 0) _ballPushMaxSpeed = stored.BallPushMaxSpeed;
        _kickMode = string.Equals(stored.KickMode, "thruster", StringComparison.OrdinalIgnoreCase)
            ? KickMode.Thruster
            : KickMode.Velocity;
        if (BallPhysicsModelCandidates.TryGetValue(stored.ModelKey, out var modelPath))
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
        if (stored.WallAssistConversionRatio > 0) _wallAssistConversionRatio = stored.WallAssistConversionRatio;
        if (stored.WallAssistMaxAddedVertical > 0) _wallAssistMaxAddedVertical = stored.WallAssistMaxAddedVertical;
        if (stored.WallAssistMinimumNormalRetention is { } normalRetention && normalRetention >= 0)
        {
            _wallAssistMinimumNormalRetention = normalRetention;
        }
        _settleEnabled = stored.SettleEnabled;
        if (stored.SettleSpeedThreshold > 0) _settleSpeedThreshold = stored.SettleSpeedThreshold;
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
        if (stored.BallImpactPlayerPushRatio > 0) _ballImpactPlayerPushRatio = stored.BallImpactPlayerPushRatio;
        if (stored.BallImpactPlayerPushMax > 0) _ballImpactPlayerPushMax = stored.BallImpactPlayerPushMax;
        if (stored.BallImpactFallSpeedThreshold > 0) _ballImpactFallSpeedThreshold = stored.BallImpactFallSpeedThreshold;
        if (stored.BallImpactBounceRestitution > 0) _ballImpactBounceRestitution = stored.BallImpactBounceRestitution;
        if (stored.BallImpactBounceHorizontalRetention > 0) _ballImpactBounceHorizontalRetention = stored.BallImpactBounceHorizontalRetention;
        if (stored.BallImpactBounceMaxVertical > 0) _ballImpactBounceMaxVertical = stored.BallImpactBounceMaxVertical;
        if (stored.BallImpactFeedbackEnabled is { } feedbackEnabled) _ballImpactFeedbackEnabled = feedbackEnabled;
        if (stored.BallImpactFeedbackMaxShakeAmplitude is { } maxShake && maxShake >= 0.35f)
        {
            _ballImpactFeedbackMaxShakeAmplitude = maxShake;
        }
        if (stored.BallImpactFeedbackMaxVisualDamage is { } maxVisualDamage && maxVisualDamage >= 1)
        {
            _ballImpactFeedbackMaxVisualDamage = maxVisualDamage;
        }

        Logger.LogInformation(
            "[SM2DIAG] ball_settings_loaded kickDelta={KickDelta:F0} model={Model} massScale={MassScale:F2}",
            _kickDeltaVelocity,
            _ballPhysicsModelKey,
            _gameplayMassScale);
    }

    private void SaveBallSettings(string reason)
    {
        var snapshot = new BallSettingsStore
        {
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
            BallImpactFeedbackMaxShakeAmplitude = _ballImpactFeedbackMaxShakeAmplitude,
            BallImpactFeedbackMaxVisualDamage = _ballImpactFeedbackMaxVisualDamage,
        };

        if (SaveJsonAtomic(BallSettingsFileName, snapshot))
        {
            Logger.LogInformation("[SM2DIAG] ball_settings_saved reason={Reason}", reason);
        }
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
        if (stored.BreakLengthSeconds > 0) _breakLengthSeconds = stored.BreakLengthSeconds;
        _goldenGoalEnabled = stored.GoldenGoalEnabled;
        if (!string.IsNullOrWhiteSpace(stored.TeamNameCt)) _teamNameCt = stored.TeamNameCt;
        if (!string.IsNullOrWhiteSpace(stored.TeamNameT)) _teamNameT = stored.TeamNameT;
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
            TeamNameCt = _teamNameCt,
            TeamNameT = _teamNameT,
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
        };

        if (SaveJsonAtomic(MatchSettingsFileName, snapshot))
        {
            Logger.LogInformation("[SM2DIAG] match_settings_saved reason={Reason}", reason);
        }
    }
}

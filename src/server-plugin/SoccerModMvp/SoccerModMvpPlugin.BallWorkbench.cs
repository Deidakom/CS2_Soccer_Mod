using CounterStrikeSharp.API.Core;
using System.Globalization;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private float _kickSurfaceReach = KickSurfaceReach;
    private float _kickAimConeDegrees = 70f;
    private float _kickCooldownSeconds = (float)KickCooldownSeconds;
    private float _curveStrength = 1f;
    private float _curveDuration = 1.25f;
    private float _trapWindow = 0.35f;
    private float _trapRetention = 0.2f;
    private float _wallPopChance = WallPopTriggerChance;
    private float _wallPopVertical = WallPopVerticalSpeed;
    private float _wallPopLateral = WallPopLateralSpeed;
    private sealed record BallDial(string Key, string Group, string Label, float Min, float Max, float Step,
        Func<float> Read, Action<float> Write, bool Integer = false);
    private List<BallDial> BallDials() => new()
    {
        new("curveStrength", "Optional handling", "Curve strength", 0f, 2f, 0.05f, () => _curveStrength, v => _curveStrength = v),
        new("curveDuration", "Optional handling", "Curve duration (seconds)", 0f, 3f, 0.05f, () => _curveDuration, v => _curveDuration = v),
        new("trapWindow", "Optional handling", "First-touch window (seconds)", 0.1f, 1f, 0.05f, () => _trapWindow, v => _trapWindow = v),
        new("trapRetention", "Optional handling", "First-touch retained momentum", 0f, 1f, 0.05f, () => _trapRetention, v => _trapRetention = v),
        new("wallPopChance", "Optional handling", "Wall-pop probability", 0f, 1f, 0.05f, () => _wallPopChance, v => _wallPopChance = v),
        new("wallPopVertical", "Optional handling", "Wall-pop upward speed", 0f, 2000f, 25f, () => _wallPopVertical, v => _wallPopVertical = v),
        new("wallPopLateral", "Optional handling", "Wall-pop sideways speed", 0f, 1000f, 25f, () => _wallPopLateral, v => _wallPopLateral = v),

        new("kickSurfaceReach", "Kick power", "Surface reach (units)", 16, 160, 1, () => _kickSurfaceReach, v => _kickSurfaceReach = v),
        new("kickAimConeDegrees", "Kick power", "Aim half-cone (degrees)", 10, 90, 1, () => _kickAimConeDegrees, v => _kickAimConeDegrees = v),
        new("kickCooldownSeconds", "Kick power", "Kick cooldown (seconds)", .05f, 2, .01f, () => _kickCooldownSeconds, v => _kickCooldownSeconds = v),
        new("kickDeltaVelocity", "Kick power", "Base impulse", 100f, 6000f, 50f, () => _kickDeltaVelocity, v => _kickDeltaVelocity = v),
        new("kickMaximumBallSpeed", "Kick power", "Speed limit", 500f, 8000f, 100f, () => _kickMaximumBallSpeed, v => _kickMaximumBallSpeed = v),
        new("leftClickPowerScale", "Kick power", "Left click", .05f, 2f, .05f, () => _leftClickPowerScale, v => _leftClickPowerScale = v),
        new("rightClickPowerScale", "Kick power", "Right click", .05f, 1f, .05f, () => _rightClickPowerScale, v => _rightClickPowerScale = v),
        new("leftClickCrouchPowerScale", "Kick power", "Crouched left click", .05f, 2f, .05f, () => _leftClickCrouchPowerScale, v => _leftClickCrouchPowerScale = v),
        new("rightClickCrouchPowerScale", "Kick power", "Crouched right click", .05f, 2f, .05f, () => _rightClickCrouchPowerScale, v => _rightClickCrouchPowerScale = v),
        new("kickAirborneDeltaScale", "Kick power", "Volley scale", .1f, 1f, .05f, () => _kickAirborneDeltaScale, v => _kickAirborneDeltaScale = v),
        new("kickOverheadBonusMax", "Lift and soft passes", "Overhead bonus", 0f, 2f, .05f, () => _kickOverheadBonusMax, v => _kickOverheadBonusMax = v),
        new("kickElevationSensitivity", "Lift and soft passes", "Elevation sensitivity", .1f, 1f, .05f, () => _kickElevationSensitivity, v => _kickElevationSensitivity = v),
        new("softPassStartRatio", "Lift and soft passes", "Soft pass start", 0f, 3f, .05f, () => _softPassStartRatio, v => _softPassStartRatio = v),
        new("softPassFullRatio", "Lift and soft passes", "Soft pass full", .05f, 4f, .05f, () => _softPassFullRatio, v => _softPassFullRatio = v),
        new("softPassMinPowerScale", "Lift and soft passes", "Soft pass minimum power", .01f, 1f, .05f, () => _softPassMinPowerScale, v => _softPassMinPowerScale = v),
        new("softPitchStartDegrees", "Lift and soft passes", "Look-down start (degrees)", 0f, 89f, 1f, () => _softPitchStartDegrees, v => _softPitchStartDegrees = v),
        new("softPitchFullDegrees", "Lift and soft passes", "Look-down full (degrees)", 1f, 90f, 1f, () => _softPitchFullDegrees, v => _softPitchFullDegrees = v),
        new("softPitchMinPowerScale", "Lift and soft passes", "Look-down minimum power", .01f, 1f, .05f, () => _softPitchMinPowerScale, v => _softPitchMinPowerScale = v),
        new("ballPushTransferRatio", "Dribbling and impact", "Body push transfer", 0f, 4f, .05f, () => _ballPushTransferRatio, v => _ballPushTransferRatio = v),
        new("ballPushMaxSpeed", "Dribbling and impact", "Body push speed limit", 0f, 2000f, 20f, () => _ballPushMaxSpeed, v => _ballPushMaxSpeed = v),
        new("ballImpactMinSpeed", "Dribbling and impact", "Impact minimum speed", 1f, 4000f, 25f, () => _ballImpactMinSpeed, v => _ballImpactMinSpeed = v),
        new("ballImpactPlayerPushRatio", "Dribbling and impact", "Impact player push", 0f, 2f, .05f, () => _ballImpactPlayerPushRatio, v => _ballImpactPlayerPushRatio = v),
        new("ballImpactPlayerPushMax", "Dribbling and impact", "Impact push limit", 1f, 4000f, 50f, () => _ballImpactPlayerPushMax, v => _ballImpactPlayerPushMax = v),
        new("ballImpactFallSpeedThreshold", "Dribbling and impact", "Falling impact threshold", 1f, 2000f, 10f, () => _ballImpactFallSpeedThreshold, v => _ballImpactFallSpeedThreshold = v),
        new("ballImpactBounceRestitution", "Dribbling and impact", "Player bounce restitution", 0f, 2f, .05f, () => _ballImpactBounceRestitution, v => _ballImpactBounceRestitution = v),
        new("ballImpactBounceHorizontalRetention", "Dribbling and impact", "Player bounce horizontal retention", 0f, 1f, .05f, () => _ballImpactBounceHorizontalRetention, v => _ballImpactBounceHorizontalRetention = v),
        new("ballImpactBounceMaxVertical", "Dribbling and impact", "Player bounce vertical limit", 0f, 2000f, 25f, () => _ballImpactBounceMaxVertical, v => _ballImpactBounceMaxVertical = v),
        new("wallAssistConversionRatio", "Walls and settling", "Wall lift conversion", 0f, 2f, .01f, () => _wallAssistConversionRatio, v => _wallAssistConversionRatio = v),
        new("wallAssistMaxAddedVertical", "Walls and settling", "Wall added lift limit", 0f, 2000f, 10f, () => _wallAssistMaxAddedVertical, v => _wallAssistMaxAddedVertical = v),
        new("wallAssistMinimumNormalRetention", "Walls and settling", "Wall normal retention", 0f, 2f, .05f, () => _wallAssistMinimumNormalRetention, v => _wallAssistMinimumNormalRetention = v),
        new("settleSpeedThreshold", "Walls and settling", "Settle speed threshold", 0f, 200f, 1f, () => _settleSpeedThreshold, v => _settleSpeedThreshold = v),
        new("settleTicks", "Walls and settling", "Settle ticks", 1f, 640f, 1f, () => _settleTicks, v => _settleTicks = (int)v, true),
        new("gameplayMassScale", "Engine physics", "Mass scale", .05f, 2f, .05f, () => _gameplayMassScale, v => _gameplayMassScale = v),
        new("gameplayFriction", "Engine physics", "Friction", 0f, 2f, .05f, () => _gameplayFriction, v => _gameplayFriction = v),
        new("gameplayElasticity", "Engine physics", "Elasticity", 0f, 1.5f, .05f, () => _gameplayElasticity, v => _gameplayElasticity = v),
        new("gameplayGravityScale", "Engine physics", "Gravity scale", .1f, 2f, .05f, () => _gameplayGravityScale, v => _gameplayGravityScale = v),
        new("ballSpinFactor", "Engine physics", "Native spin factor (experimental)", 0f, 2f, .05f, () => _ballSpinFactor, v => _ballSpinFactor = v),
        new("ballResetX", "Kickoff position", "Kickoff X", -500f, 500f, 10f, () => _ballResetX, v => _ballResetX = v),
        new("ballResetY", "Kickoff position", "Kickoff Y", -500f, 500f, 10f, () => _ballResetY, v => _ballResetY = v),
    };
    private sealed class BallTuning
    {
        public Dictionary<string, float> Values { get; set; } = new();
        public bool WallAssist { get; set; }
        public bool Settle { get; set; }
        public bool Impact { get; set; }
        public bool Feedback { get; set; }
        public string Sound { get; set; } = "";
    }
    private const string BallPresetsFile = "soccermod_ball_presets.json";
    private Dictionary<string, BallTuning> _ballPresets = new();
    private readonly List<BallTuning> _ballUndo = new();
    private BallTuning CaptureBallTuning() => new()
    {
        Values = BallDials().ToDictionary(d => d.Key, d => d.Read()),
        WallAssist = _wallAssistEnabled, Settle = _settleEnabled,
        Impact = _ballImpactEnabled, Feedback = _ballImpactFeedbackEnabled, Sound = _kickSoundName
    };
    private bool ValidateBallTuning(BallTuning? tuning)
    {
        if (tuning?.Values is null || tuning.Sound is null || tuning.Sound.Length > 128
            || tuning.Sound.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '/')) return false;
        var dials = BallDials();
        if (tuning.Values.Count != dials.Count) return false;
        foreach (var d in dials)
            if (!tuning.Values.TryGetValue(d.Key, out var value) || !float.IsFinite(value)
                || value < d.Min || value > d.Max || (d.Integer && value != MathF.Truncate(value))) return false;
        return tuning.Values["softPassStartRatio"] < tuning.Values["softPassFullRatio"]
            && tuning.Values["softPitchStartDegrees"] < tuning.Values["softPitchFullDegrees"];
    }
    private void AssignBallTuning(BallTuning tuning)
    {
        foreach (var d in BallDials()) d.Write(tuning.Values[d.Key]);
        _wallAssistEnabled = tuning.WallAssist; _settleEnabled = tuning.Settle;
        _ballImpactEnabled = tuning.Impact; _ballImpactFeedbackEnabled = tuning.Feedback;
        _kickSoundName = tuning.Sound;
    }
    private bool ApplyBallTuning(BallTuning tuning, bool remember = true)
    {
        if (!ValidateBallTuning(tuning)) return false;
        var before = CaptureBallTuning();
        AssignBallTuning(tuning);
        if (!SaveBallSettings("ball_workbench")) { AssignBallTuning(before); return false; }
        if (remember)
        {
            _ballUndo.Add(before);
            if (_ballUndo.Count > 10) _ballUndo.RemoveAt(0);
        }
        ResetDerivedMotion(clearTouchHistory: false);
        foreach (var entry in PlayableBalls())
        { NewBallContact(entry.Ball); ApplyGameplayPhysicsProfile(entry.Ball, "ball_workbench"); }
        return true;
    }
    private bool BallWorkbenchAccess(CCSPlayerController player)
    {
        if (player.IsValid && HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0UL, "root")) return true;
        if (player.IsValid) player.PrintToChat(" [SM] Root permission is required to tune the ball.");
        return false;
    }
    private void BallWorkbenchOnLoad()
    {
        _ballPresets = LoadJsonOrNull<Dictionary<string, BallTuning>>(BallPresetsFile) ?? new();
        if (!_ballPresets.ContainsKey("Before workbench") && _ballPresets.Count < 24)
        {
            var initial = CaptureBallTuning();
            if (ValidateBallTuning(initial))
            {
                var copy = new Dictionary<string, BallTuning>(_ballPresets) { ["Before workbench"] = initial };
                if (SaveJsonAtomic(BallPresetsFile, copy)) _ballPresets = copy;
            }
        }
        AddCommand("css_sm2ball_tune", "Root: list tuning or set <key> <value>.", (player, command) =>
        {
            if (!RequirePermission(player, command, "root")) return;
            var dials = BallDials();
            if (command.ArgCount == 1)
            {
                foreach (var d in dials) command.ReplyToCommand($"{d.Key}={d.Read():0.###} [{d.Min}..{d.Max}]");
                return;
            }
            var dial = dials.FirstOrDefault(d => d.Key.Equals(command.GetArg(1), StringComparison.OrdinalIgnoreCase));
            if (command.ArgCount != 3 || dial is null || !float.TryParse(command.GetArg(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            { command.ReplyToCommand("[SM] Usage: css_sm2ball_tune [key value]"); return; }
            var tuning = CaptureBallTuning(); tuning.Values[dial.Key] = value;
            command.ReplyToCommand(ApplyBallTuning(tuning) ? "[SM] Ball tuning saved." : "[SM] Rejected: invalid tuning or settings could not be saved.");
        });
        AddCommand("css_sm2ball_undo", "Root: undo the last workbench tuning change.", (player, command) =>
        {
            if (!RequirePermission(player, command, "root")) return;
            command.ReplyToCommand(UndoBallTuning() ? "[SM] Ball tuning restored." : "[SM] No undo available or restore failed.");
        });
    }
    private bool UndoBallTuning()
    {
        if (_ballUndo.Count == 0 || !ApplyBallTuning(_ballUndo[^1], false)) return false;
        _ballUndo.RemoveAt(_ballUndo.Count - 1); return true;
    }
    private void OpenBallAdminMenu(CCSPlayerController player)
    {
        if (!BallWorkbenchAccess(player)) return;
        var menu = new NumberMenu { Title = "Ball workbench", OnBack = OpenAdminMenu };
        menu.Add("Live ball controls", OpenBallLiveMenu);
        foreach (var group in BallDials().Select(d => d.Group).Distinct())
            menu.Add(group, p => OpenBallDialGroup(p, group));
        menu.Add("Effects and sound", OpenBallEffectsMenu);
        menu.Add($"Handling profile: {_handling.Profile}", OpenBallProfileMenu);
        menu.Add("Saved tuning presets", OpenBallPresetsMenu);
        menu.Add("Restore established defaults...", OpenBallRestoreDefaultsMenu);
        menu.Add($"Undo last tuning change ({_ballUndo.Count})", p =>
        {
            if (!BallWorkbenchAccess(p)) return;
            p.PrintToChat(UndoBallTuning() ? " [SM] Previous tuning restored." : " [SM] No undo available or restore failed.");
            OpenBallAdminMenu(p);
        });
        OpenNumberMenu(player, menu);
    }
    private void OpenBallDialGroup(CCSPlayerController player, string group)
    {
        if (!BallWorkbenchAccess(player)) return;
        var menu = new NumberMenu { Title = "Ball - " + group, OnBack = OpenBallAdminMenu };
        if (group == "Optional handling")
        {
            menu.AddInfo("Curve/first touch: creative profile only.");
            menu.AddInfo("Wall-pop: legacy profile only; 0 chance disables it.");
            menu.AddInfo("First touch uses css_ball_trap; lower retention cushions more.");
        }
        if (group == "Engine physics")
        {
            menu.AddInfo("Engine requests: bounce/spin require live verification.");
            menu.AddInfo("Model/hull size fixed by the Workshop asset.");
        }
        foreach (var dial in BallDials().Where(d => d.Group == group))
            menu.Add($"{dial.Label}: {BallMenuNumber(dial.Read())}", p => OpenBallDial(p, dial));
        OpenNumberMenu(player, menu);
    }
    private void OpenBallDial(CCSPlayerController player, BallDial dial)
    {
        if (!BallWorkbenchAccess(player)) return;
        void Set(CCSPlayerController p, float value)
        {
            if (!BallWorkbenchAccess(p)) return;
            var tuning = CaptureBallTuning(); tuning.Values[dial.Key] = value;
            p.PrintToChat(ApplyBallTuning(tuning) ? $" [SM] {dial.Label}: {BallMenuNumber(value)} (saved)"
                : " [SM] Not changed: check range, start < full, or disk write failure.");
            OpenBallDial(p, dial);
        }
        var menu = new NumberMenu { Title = $"{dial.Label}: {BallMenuNumber(dial.Read())}", OnBack = p => OpenBallDialGroup(p, dial.Group) };
        menu.AddInfo($"Range {dial.Min} to {dial.Max}; changes apply immediately.");
        menu.Add($"Decrease by {dial.Step}", p => Set(p, Math.Clamp(MathF.Round((dial.Read() - dial.Step) * 10000) / 10000, dial.Min, dial.Max)));
        menu.Add($"Increase by {dial.Step}", p => Set(p, Math.Clamp(MathF.Round((dial.Read() + dial.Step) * 10000) / 10000, dial.Min, dial.Max)));
        menu.Add("Enter exact value in chat", p =>
        {
            if (!BallWorkbenchAccess(p)) return;
            BeginChatNumberInput(p, $"{dial.Label} ({dial.Min}..{dial.Max})", dial.Min, dial.Max, Set, q => OpenBallDial(q, dial));
        });
        OpenNumberMenu(player, menu);
    }
    private void OpenBallEffectsMenu(CCSPlayerController player)
    {
        if (!BallWorkbenchAccess(player)) return;
        var menu = new NumberMenu { Title = "Ball effects", OnBack = OpenBallAdminMenu };
        void Change(CCSPlayerController p, Action<BallTuning> edit)
        {
            if (!BallWorkbenchAccess(p)) return;
            var tuning = CaptureBallTuning(); edit(tuning);
            if (!ApplyBallTuning(tuning)) p.PrintToChat(" [SM] Settings could not be saved; no change applied.");
            OpenBallEffectsMenu(p);
        }
        menu.Add($"Wall assist: {_wallAssistEnabled}", p => Change(p, t => t.WallAssist = !t.WallAssist));
        menu.Add($"Ground settling: {_settleEnabled}", p => Change(p, t => t.Settle = !t.Settle));
        menu.Add($"Player impact: {_ballImpactEnabled}", p => Change(p, t => t.Impact = !t.Impact));
        menu.Add($"Impact feedback: {_ballImpactFeedbackEnabled}", p => Change(p, t => t.Feedback = !t.Feedback));
        menu.AddInfo($"Sound: {(_kickSoundName.Length == 0 ? "off" : _kickSoundName)}");
        foreach (var sound in new[] { "Weapon_Knife.HitWall", "Default.Land", "GrenadeBase.Bounce", "" })
            menu.Add(sound.Length == 0 ? "Sound off" : sound, p => Change(p, t => t.Sound = sound));
        menu.Add("Enter sound event name", p =>
        {
            if (!BallWorkbenchAccess(p)) return;
            BeginChatTextInput(p, "Enter an installed sound event name (cancel to abort).", (q, text) => Change(q, t => t.Sound = text), OpenBallEffectsMenu);
        });
        OpenNumberMenu(player, menu);
    }
    private void OpenBallRestoreDefaultsMenu(CCSPlayerController player)
    {
        if (!BallWorkbenchAccess(player)) return;
        var menu = new NumberMenu { Title = "Restore established defaults?", OnBack = OpenBallAdminMenu };
        menu.AddInfo("Restores the original menu's controls; other tuning stays unchanged.");
        menu.Add("Confirm restore", p =>
        {
            if (!BallWorkbenchAccess(p)) return;
            var t = CaptureBallTuning();
            t.Values["ballSpinFactor"] = DefaultBallSpinFactor;
            t.Values["kickAirborneDeltaScale"] = DefaultKickAirborneDeltaScale;
            t.Values["leftClickPowerScale"] = DefaultLeftClickPowerScale;
            t.Values["rightClickPowerScale"] = DefaultRightClickPowerScale;
            t.Values["ballPushTransferRatio"] = DefaultBallPushTransferRatio;
            t.Values["ballPushMaxSpeed"] = DefaultBallPushMaxSpeed;
            t.Values["kickElevationSensitivity"] = DefaultKickElevationSensitivity;
            t.Sound = DefaultKickSoundName; t.Impact = true; t.Settle = DefaultSettleEnabled;
            p.PrintToChat(ApplyBallTuning(t) ? " [SM] Established defaults restored; undo available." : " [SM] Restore failed; unchanged.");
            OpenBallAdminMenu(p);
        });
        OpenNumberMenu(player, menu);
    }
    private void OpenBallProfileMenu(CCSPlayerController player)
    {
        if (!BallWorkbenchAccess(player)) return;
        var menu = new NumberMenu { Title = "Ball handling profile", OnBack = OpenBallAdminMenu };
        menu.AddInfo("Profile is separate from tuning presets and undo.");
        foreach (var profile in new[] { "improved", "creative", "legacy" })
            menu.Add(profile, p => { if (BallWorkbenchAccess(p)) RunBallMenuCommand(p, $"css_sm2ball_profile {profile}", OpenBallAdminMenu); });
        OpenNumberMenu(player, menu);
    }
    private void OpenBallPresetsMenu(CCSPlayerController player)
    {
        if (!BallWorkbenchAccess(player)) return;
        var menu = new NumberMenu { Title = "Ball tuning presets", OnBack = OpenBallAdminMenu };
        menu.AddInfo("Saves tuning/effects; handling profile stays unchanged.");
        menu.Add("Save current tuning as...", p =>
        {
            if (!BallWorkbenchAccess(p)) return;
            BeginChatTextInput(p, "Preset name: 1-32 letters, numbers, spaces, _ or -; cancel to abort.", (q, name) =>
            {
                if (!BallWorkbenchAccess(q)) return;
                name = name.Trim();
                if (name.Length is < 1 or > 32 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != ' ' && c != '_' && c != '-')
                    || !ValidateBallTuning(CaptureBallTuning()) || _ballPresets.Count >= 24 || _ballPresets.Keys.Any(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    q.PrintToChat(" [SM] Use a new valid name; maximum 24 presets.");
                else
                {
                    var copy = new Dictionary<string, BallTuning>(_ballPresets) { [name] = CaptureBallTuning() };
                    if (SaveJsonAtomic(BallPresetsFile, copy)) { _ballPresets = copy; q.PrintToChat(" [SM] Preset saved."); }
                    else q.PrintToChat(" [SM] Preset save failed.");
                }
                OpenBallPresetsMenu(q);
            }, OpenBallPresetsMenu);
        });
        foreach (var name in _ballPresets.Keys.OrderBy(n => n)) menu.Add(name, p => OpenBallPreset(p, name));
        OpenNumberMenu(player, menu);
    }
    private void OpenBallPreset(CCSPlayerController player, string name)
    {
        if (!BallWorkbenchAccess(player) || !_ballPresets.TryGetValue(name, out var tuning)) return;
        var menu = new NumberMenu { Title = "Preset: " + name, OnBack = OpenBallPresetsMenu };
        menu.AddInfo("Load replaces tuning/effects; undo remains available.");
        menu.Add("Review saved values", p =>
        {
            if (!BallWorkbenchAccess(p)) return;
            if (!ValidateBallTuning(tuning)) { p.PrintToChat(" [SM] Preset is invalid or from an incompatible version."); return; }
            var review = new NumberMenu { Title = "Saved values: " + name, OnBack = q => OpenBallPreset(q, name) };
            foreach (var d in BallDials())
                review.AddInfo($"{d.Label}: {BallMenuNumber(tuning.Values[d.Key])} (now {BallMenuNumber(d.Read())})");
            review.AddInfo($"Wall {tuning.WallAssist}; settle {tuning.Settle}; impact {tuning.Impact}; feedback {tuning.Feedback}");
            review.AddInfo($"Sound: {(tuning.Sound.Length == 0 ? "off" : tuning.Sound)}");
            OpenNumberMenu(p, review);
        });
        menu.Add("Confirm load", p =>
        {
            if (!BallWorkbenchAccess(p)) return;
            p.PrintToChat(ApplyBallTuning(tuning) ? " [SM] Preset applied and saved." : " [SM] Preset invalid or save failed.");
            OpenBallPresetsMenu(p);
        });
        if (name != "Before workbench") menu.Add("Delete preset...", p =>
        {
            if (!BallWorkbenchAccess(p)) return;
            var confirm = new NumberMenu { Title = "Delete " + name + "?", OnBack = OpenBallPresetsMenu };
            confirm.Add("Confirm delete", q =>
            {
                if (!BallWorkbenchAccess(q)) return;
                var copy = new Dictionary<string, BallTuning>(_ballPresets); copy.Remove(name);
                if (SaveJsonAtomic(BallPresetsFile, copy)) _ballPresets = copy;
                else q.PrintToChat(" [SM] Preset delete failed.");
                OpenBallPresetsMenu(q);
            });
            OpenNumberMenu(p, confirm);
        });
        OpenNumberMenu(player, menu);
    }
    private void OpenBallLiveMenu(CCSPlayerController player)
    {
        if (!BallWorkbenchAccess(player)) return;
        var menu = new NumberMenu { Title = "Live ball controls", OnBack = OpenBallAdminMenu };
        menu.AddInfo("Physical controls available in warmup only.");
        void Control(CCSPlayerController p, Action action)
        {
            if (!BallWorkbenchAccess(p)) return;
            if (_matchPhase != MatchPhase.Warmup || _websiteCapStore.Active || _capFightPending || _capFightStarted || _capPicksLeft > 0)
                p.PrintToChat(" [SM] Stop the match/CAP before moving or freezing the ball.");
            else if (_ball is not { IsValid: true }) p.PrintToChat(" [SM] No active ball.");
            else action();
            OpenBallLiveMenu(p);
        }
        menu.Add(_pausedBallHandle == 0 ? "Freeze ball" : "Resume ball motion", p => Control(p, () =>
        { if (_pausedBallHandle == 0) FreezeBallForPause(); else ReleasePausedBall(true); }));
        menu.Add("Stop ball (discard momentum)", p => Control(p, () => { FreezeBallForPause(); ReleasePausedBall(false); _ball!.Teleport(velocity: new Vector(0, 0, 0)); ResetDerivedMotion(); }));
        menu.Add("Reset ball to kickoff", p => Control(p, () => { ReleasePausedBall(false); ResetBallForGoalSafety("ball_workbench"); }));
        menu.Add("Place on pitch at crosshair", p => Control(p, () =>
        {
            if (!TryGetAimHitPoint(p, out var hit) || !float.IsFinite(hit.X) || !float.IsFinite(hit.Y)
                || MathF.Abs(hit.X) > FoundationWallPlaneX - BallCollisionRadius * 2
                || MathF.Abs(hit.Y) > _goalLineY - BallCollisionRadius * 2)
            { p.PrintToChat(" [SM] Aim inside the pitch, away from walls and goals."); return; }
            ReleasePausedBall(false);
            _ball!.Teleport(position: new Vector(hit.X, hit.Y, BallResetZ), velocity: new Vector(0, 0, 0));
            ResetDerivedMotion();
        }));
        if (_ball is { IsValid: true, AbsOrigin: { } origin })
        {
            menu.AddInfo($"Position {origin.X:0}, {origin.Y:0}, {origin.Z:0}");
            menu.AddInfo($"Measured speed {_derivedBallVelocity.Length():0} u/s");
        }
        OpenNumberMenu(player, menu);
    }
}

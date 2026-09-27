using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace SoccerModMvp;

// 2026-09-27 owner: when a goal is scored the roof screens show the scorer's
// name as an animation with a gradient effect; later the same day: "write
// GOALLL!! instead of the name" (OWN GOAL!! for own goals). Needs the roof
// score text (MapScoreText.cs flag). The text pops in (overshoot scale), its colour runs
// through a team gradient (dark -> bright -> gold/ice -> bright), a darker
// glow copy sits just behind it, and it fades out; then the numbers return.
// The round restart after the goal pause deletes the texts; the tick respawns
// them so the animation runs its full length.
public sealed partial class SoccerModMvpPlugin
{
    private const float GoalFxSeconds = 3.6f;
    private const float GoalFxPopSeconds = 0.35f;
    private const float GoalFxFadeSeconds = 0.4f;
    private const float GoalFxFont = 110.0f;
    private const float GoalFxMaxWidth = 196.0f; // the black screen is ~212 wide
    private const string GoalFxNamePrefix = "sm2_roofgoal_";
    // The map has the old scoreboard colon (two white dots) baked into the
    // screen; the glow layer draws a black backing that hides them.
    private const string GoalFxBackingMaterial = "materials/dev/black_simple.vmat";

    private bool _goalFxActive;
    private Timer? _goalFxTimer;
    private double _goalFxStartedAt;
    private string _goalFxText = "";
    private CsTeam _goalFxTeam;
    private float _goalFxUnitsPerPx;
    private readonly List<(CPointWorldText Text, bool Glow)> _goalFxTexts = new();

    private static readonly Color[] GoalFxGradientT =
        { Color.FromArgb(150, 0, 0), Color.FromArgb(255, 55, 40), Color.FromArgb(255, 205, 70), Color.FromArgb(255, 55, 40) };
    private static readonly Color[] GoalFxGradientCt =
        { Color.FromArgb(0, 40, 160), Color.FromArgb(60, 140, 255), Color.FromArgb(150, 235, 255), Color.FromArgb(60, 140, 255) };

    private void MapScoreGoalFxStart(string scorerName, CsTeam scoringTeam, bool ownGoal)
    {
        if (!MapScoreTextEnabled) return;
        MapScoreGoalFxStop();

        _goalFxText = ownGoal ? "OWN GOAL!!" : "GOALLL!!";
        _goalFxTeam = scoringTeam;
        // Arial averages ~0.6 of the font size per glyph: shrink long names to fit.
        var estimatedPx = Math.Max(3, _goalFxText.Length) * 0.62f * GoalFxFont;
        _goalFxUnitsPerPx = Math.Min(_mapScoreTextUnitsPerPx, GoalFxMaxWidth / estimatedPx);
        _goalFxStartedAt = Server.CurrentTime;
        _goalFxActive = true;
        MapScoreTextEnsure("goal_fx_start");
        GoalFxTick();
        _goalFxTimer = AddTimer(0.05f, GoalFxTick, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void MapScoreGoalFxStop()
    {
        _goalFxTimer?.Kill();
        _goalFxTimer = null;
        foreach (var (text, _) in _goalFxTexts)
        {
            if (text.IsValid) text.Remove();
        }
        _goalFxTexts.Clear();
        if (_goalFxActive)
        {
            _goalFxActive = false;
            MapScoreTextEnsure("goal_fx_end");
        }
    }

    private void GoalFxTick()
    {
        var t = (float)(Server.CurrentTime - _goalFxStartedAt);
        if (t >= GoalFxSeconds || !MapScoreTextEnabled)
        {
            MapScoreGoalFxStop();
            return;
        }

        if (_goalFxTexts.Count != 4 || _goalFxTexts.Any(entry => !entry.Text.IsValid))
        {
            foreach (var (text, _) in _goalFxTexts)
            {
                if (text.IsValid) text.Remove();
            }
            _goalFxTexts.Clear();
            foreach (var side in new[] { 1, -1 })
            {
                var yaw = (side > 0 ? 0.0f : 180.0f) + _mapScoreTextYaw;
                // Glow first (slightly deeper in the screen), then the name in front.
                foreach (var glow in new[] { true, false })
                {
                    var inset = glow ? _mapScoreTextInset * 0.6f : _mapScoreTextInset;
                    var origin = new Vector(0.0f, side * (MapScoreTextScreenY - inset), MapScoreTextCenterZ);
                    var text = MapScoreTextSpawn($"fx_{(glow ? "glow" : "name")}_{side}", origin, yaw, Color.White);
                    if (text is null) continue;
                    text.Entity!.Name = GoalFxNamePrefix + (glow ? "glow_" : "name_") + side;
                    text.MessageText = _goalFxText;
                    text.FontSize = GoalFxFont;
                    if (glow)
                    {
                        text.DrawBackground = true;
                        text.BackgroundMaterialName = GoalFxBackingMaterial;
                        text.BackgroundBorderWidth = 12.0f;
                        text.BackgroundBorderHeight = 6.0f;
                        text.BackgroundWorldToUV = 0.05f;
                        Utilities.SetStateChanged(text, "CPointWorldText", "m_bDrawBackground");
                        Utilities.SetStateChanged(text, "CPointWorldText", "m_BackgroundMaterialName");
                        Utilities.SetStateChanged(text, "CPointWorldText", "m_flBackgroundBorderWidth");
                        Utilities.SetStateChanged(text, "CPointWorldText", "m_flBackgroundBorderHeight");
                    }
                    Utilities.SetStateChanged(text, "CPointWorldText", "m_messageText");
                    Utilities.SetStateChanged(text, "CPointWorldText", "m_flFontSize");
                    _goalFxTexts.Add((text, glow));
                }
            }
            // The restart also recreated the numbers: keep them hidden.
            MapScoreTextEnsure("goal_fx_respawn");
        }

        // Pop in with a small overshoot, then hold.
        float scale;
        if (t < GoalFxPopSeconds)
        {
            var p = t / GoalFxPopSeconds - 1.0f;
            const float back = 1.9f;
            scale = Math.Max(0.15f, 1.0f + p * p * ((back + 1.0f) * p + back));
        }
        else
        {
            scale = 1.0f;
        }

        var alpha = t > GoalFxSeconds - GoalFxFadeSeconds
            ? Math.Clamp((GoalFxSeconds - t) / GoalFxFadeSeconds, 0.0f, 1.0f)
            : 1.0f;
        var gradient = _goalFxTeam == CsTeam.Terrorist ? GoalFxGradientT : GoalFxGradientCt;
        var front = GoalFxSample(gradient, t * 1.1f);
        var glowColor = GoalFxSample(gradient, t * 1.1f + 0.5f);

        foreach (var (text, glow) in _goalFxTexts)
        {
            if (!text.IsValid) continue;
            var color = glow
                ? Color.FromArgb((int)(150 * alpha), glowColor.R / 2, glowColor.G / 2, glowColor.B / 2)
                : Color.FromArgb((int)(255 * alpha), front);
            text.WorldUnitsPerPx = _goalFxUnitsPerPx * scale * (glow ? 1.07f : 1.0f);
            text.Color = color;
            Utilities.SetStateChanged(text, "CPointWorldText", "m_flWorldUnitsPerPx");
            Utilities.SetStateChanged(text, "CPointWorldText", "m_Color");
        }
    }

    // Loops through the gradient stops; phase 1.0 = one full pass.
    private static Color GoalFxSample(Color[] stops, float phase)
    {
        var f = (phase % 1.0f + 1.0f) % 1.0f * stops.Length;
        var i = (int)f;
        var a = stops[i % stops.Length];
        var b = stops[(i + 1) % stops.Length];
        var k = f - i;
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * k),
            (int)(a.G + (b.G - a.G) * k),
            (int)(a.B + (b.B - a.B) * k));
    }
}

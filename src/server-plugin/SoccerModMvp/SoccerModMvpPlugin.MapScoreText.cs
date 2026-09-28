using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.Drawing;

namespace SoccerModMvp;

// 2026-09-27 owner: the roof scoreboard is not readable from a distance. The
// map's 7-segment digits are thin func_brush strips that the client culls as
// "too small" far away (fade 0 and ObjectCulling 0 were both tried, see
// MapCleanup.cs). With the flag file below the plugin writes the score as big
// point_worldtext numbers onto both roof screens instead (T red left, CT blue
// right, as seen from the pitch) and switches the segments off.
// Screens (entity lump of soccer_cssl_stadium_v8): segments at y = +-1943.5,
// x = -106..106, z = 864..912; the +y screen reads T at -x, the -y screen is
// mirrored (T at +x). Tunable live: css_sm2scoretext <font> <unitsPerPx> <inset> <x> [yawOffset].
public sealed partial class SoccerModMvpPlugin
{
    private const string MapScoreTextFlagFile = "soccermod_scoreboard_text.enabled";
    private const string MapScoreTextNamePrefix = "sm2_roofscore_";
    private const float MapScoreTextScreenY = 1943.5f;
    private const float MapScoreTextCenterZ = 888.0f;

    private float _mapScoreTextFont = 156.0f; // 2026-09-27 owner: +30% (was 120)
    private float _mapScoreTextUnitsPerPx = 0.40f;
    private float _mapScoreTextInset = 2.0f;
    private float _mapScoreTextX = 70.0f;
    private float _mapScoreTextYaw = 0.0f;
    private int _mapScoreTextT = -1;
    private int _mapScoreTextCt = -1;

    private readonly List<CPointWorldText> _mapScoreTexts = new();

    private bool MapScoreTextEnabled => FlagFileOn(MapScoreTextFlagFile);

    private void MapScoreTextOnLoad()
    {
        AddCommand("css_sm2scoretext", "Admin: roof score text <font> <unitsPerPx> <inset> <x> [yawOffset] (runtime only).", (player, command) =>
        {
            if (!RequirePermission(player, command, "admin")) return;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var num = System.Globalization.NumberStyles.Float;
            if (command.ArgCount >= 5
                && float.TryParse(command.GetArg(1), num, inv, out var font)
                && float.TryParse(command.GetArg(2), num, inv, out var upp)
                && float.TryParse(command.GetArg(3), num, inv, out var inset)
                && float.TryParse(command.GetArg(4), num, inv, out var x))
            {
                _mapScoreTextFont = font;
                _mapScoreTextUnitsPerPx = upp;
                _mapScoreTextInset = inset;
                _mapScoreTextX = x;
                if (command.ArgCount >= 6 && float.TryParse(command.GetArg(5), num, inv, out var yawOffset)) _mapScoreTextYaw = yawOffset;
                MapScoreTextRemove();
                MapScoreTextEnsure("command");
            }
            command.ReplyToCommand($"[SM] Roof score text: flag={MapScoreTextEnabled} texts={_mapScoreTexts.Count(t => t.IsValid)} font={_mapScoreTextFont:F0} upp={_mapScoreTextUnitsPerPx:F2} inset={_mapScoreTextInset:F1} x={_mapScoreTextX:F0} yaw+={_mapScoreTextYaw:F0}");
        });
        RegisterListener<Listeners.OnMapEnd>(() =>
        {
            _mapScoreTexts.Clear();
            // A map change within 3.6 s of a goal must not leave the numbers
            // hidden and the plates up on the next map (review 2026-09-28).
            _goalFxTimer = null;
            _goalFxActive = false;
        });
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            MapScoreBlockerPrecache(manifest);
        });
    }

    private void MapScoreTextRemove()
    {
        foreach (var text in _mapScoreTexts)
        {
            if (text.IsValid) text.Remove();
        }
        _mapScoreTexts.Clear();
    }

    // Called with the current score from UpdateMapScoreboard and, with the
    // last score, from the map-start / round-start / maintenance passes (a
    // round restart deletes the texts).
    private void MapScoreTextEnsure(string reason, int t = -1, int ct = -1)
    {
        if (!MapScoreTextEnabled)
        {
            if (_mapScoreTexts.Count > 0) MapScoreTextRemove();
            foreach (var prop in _mapScoreBlockers) if (prop.IsValid) prop.Remove();
            _mapScoreBlockers.Clear();
            return;
        }

        if (t >= 0) _mapScoreTextT = t;
        if (ct >= 0) _mapScoreTextCt = ct;
        var scoreT = Math.Max(0, _mapScoreTextT);
        var scoreCt = Math.Max(0, _mapScoreTextCt);

        _mapScoreTexts.RemoveAll(text => !text.IsValid);
        if (_mapScoreTexts.Count != 4)
        {
            MapScoreTextRemove();
            // side +1: screen at +y, seen from the pitch looking +y (T at -x).
            // side -1: screen at -y, seen looking -y (T at +x).
            foreach (var side in new[] { 1, -1 })
            {
                var y = side * (MapScoreTextScreenY - _mapScoreTextInset);
                var yaw = (side > 0 ? 0.0f : 180.0f) + _mapScoreTextYaw;
                var tText = MapScoreTextSpawn($"t_{side}", new Vector(-side * _mapScoreTextX, y, MapScoreTextCenterZ), yaw, Color.FromArgb(255, 230, 40, 40));
                var ctText = MapScoreTextSpawn($"ct_{side}", new Vector(side * _mapScoreTextX, y, MapScoreTextCenterZ), yaw, Color.FromArgb(255, 60, 110, 255));
                if (tText is not null) _mapScoreTexts.Add(tText);
                if (ctText is not null) _mapScoreTexts.Add(ctText);
            }
            Logger.LogInformation("[SM2DIAG] map_score_text_spawned reason={Reason} count={Count}", reason, _mapScoreTexts.Count);
        }

        foreach (var text in _mapScoreTexts)
        {
            var isT = text.Entity?.Name?.StartsWith(MapScoreTextNamePrefix + "t_", StringComparison.Ordinal) == true;
            var message = (isT ? scoreT : scoreCt).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (text.MessageText != message)
            {
                text.MessageText = message;
                Utilities.SetStateChanged(text, "CPointWorldText", "m_messageText");
            }
            // The goal-scorer animation (MapScoreGoalFx.cs) owns the screen while it runs.
            var show = !_goalFxActive;
            if (text.Enabled != show)
            {
                text.Enabled = show;
                Utilities.SetStateChanged(text, "CPointWorldText", "m_bEnabled");
            }
        }

        MapScoreBlockerEnsure();

        // The texts replace the thin segments: keep those off.
        foreach (var brush in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_brush"))
        {
            if (!brush.IsValid || brush.Entity?.Name is not { } name) continue;
            var key = name.StartsWith("[PR#]", StringComparison.Ordinal) ? name[5..] : name;
            if (key.StartsWith(MapScoreboardDigitNamePrefix, StringComparison.Ordinal)) brush.AcceptInput("Disable");
        }
    }

    private CPointWorldText? MapScoreTextSpawn(string suffix, Vector origin, float yaw, Color color,
        string message = "0", float fontSize = -1.0f)
    {
        var text = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
        if (text is null || !text.IsValid) return null;
        text.Entity!.Name = MapScoreTextNamePrefix + suffix;
        text.MessageText = message;
        text.FontName = "Arial";
        text.FontSize = fontSize > 0.0f ? fontSize : _mapScoreTextFont;
        text.WorldUnitsPerPx = _mapScoreTextUnitsPerPx;
        text.DepthOffset = 0.0f;
        text.Fullbright = true;
        text.Enabled = true;
        text.DrawBackground = false;
        text.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
        text.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_CENTER;
        text.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE;
        text.Color = color;
        try
        {
            text.DispatchSpawn();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[SM2DIAG] map_score_text_spawn_failed name={Name}", suffix);
            if (text.IsValid) text.Remove();
            return null;
        }
        text.Teleport(origin, new QAngle(0.0f, yaw, 90.0f), new Vector(0, 0, 0));
        return text;
    }
}

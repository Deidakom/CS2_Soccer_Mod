using System.Net;

namespace SoccerModMvp;
internal static class SprintBarView
{
    internal static string Text(float stamina)
    {
        var amount = float.IsFinite(stamina) ? Math.Clamp(stamina, 0, 100) : 0;
        var filled = Math.Clamp((int)MathF.Floor(amount / 5), 0, 20);
        var segments = new string('|', filled) + new string('.', 20 - filled);
        return $"[{segments[..10]} {MathF.Floor(amount):0}% {segments[10..]}]";
    }
    // Panorama bar: the fill-N class, N = 0..100 in steps of 5.
    internal static int FillStep(float stamina)
        => float.IsFinite(stamina) ? Math.Clamp((int)MathF.Round(stamina / 5f) * 5, 0, 100) : 0;

    // 2026-09-25 owner: the Panorama label sits centred above the bar and
    // carries the state plus the percentage, in the bar's 5 % steps (so the
    // text only changes with the fill class).
    internal static string HudLabel(float stamina, bool active, bool full, string cooldownLabel)
    {
        var percent = $"{FillStep(stamina)}%";
        if (active) return $"SPRINT {percent}";
        if (full) return "READY 100%";
        // Red (refilling) bar, owner 2026-09-25: "RECHARGE 40%".
        return $"RECHARGE {percent}";
    }

    // The live score on its own centre line (the Panorama bar no longer carries it).
    internal static string ScoreHtml(string score)
        => $"<font class='fontSize-sm' color='#FFFFFF'>{WebUtility.HtmlEncode(score).Replace("\n", "<br>")}</font>";

    internal static bool Visible(int mode, bool active, float stamina, bool eligible, bool menuOpen, bool suppressed)
        => eligible && !menuOpen && !suppressed && mode != 2
            && (mode == 0 || active || stamina < 99.95f);

    internal static string Html(float stamina, bool active, string score)
    {
        var parts = Text(stamina).Split(' ');
        var refilling = !active && (!float.IsFinite(stamina) || stamina < 99.95f);
        var color = refilling ? "#FF6464" : "#66EEFF";
        // Equal-length wings centre the percentage inside a wider meter. The
        // client centres the row; medium text and no blank rows reduce its box.
        var bar = $"<font class='fontSize-m' color='{color}'>{parts[0]} </font>"
            + $"<font class='fontSize-m' color='#FFFFFF'>{parts[1]}</font>"
            + $"<font class='fontSize-m' color='{color}'> {parts[2]}</font>";
        // Blank rows inflate the native HUD background; only render real content.
        if (string.IsNullOrEmpty(score)) return bar;
        var above = WebUtility.HtmlEncode(score).Replace("\n", "<br>");
        return $"<font class='fontSize-sm' color='#FFFFFF'>{above}</font><br>" + bar;
    }
}

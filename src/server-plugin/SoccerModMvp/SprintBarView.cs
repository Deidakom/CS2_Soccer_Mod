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
        // Put the meter at the bottom of the panel, toward the hands. Keep
        // two rows above it in either mode so match info cannot move it up.
        var above = string.IsNullOrEmpty(score) ? "&nbsp;<br>&nbsp;"
            : WebUtility.HtmlEncode(score).Replace("\n", "<br>");
        return $"<font class='fontSize-sm' color='#FFFFFF'>{above}</font><br>" + bar;
    }
}

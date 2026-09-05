using System.Net;

namespace SoccerModMvp;
internal static class SprintBarView
{
    internal static string Text(float stamina)
    {
        var amount = float.IsFinite(stamina) ? Math.Clamp(stamina, 0, 100) : 0;
        var filled = Math.Clamp((int)MathF.Floor(amount / 10), 0, 10);
        return $"[{new string('|', filled)}{new string('.', 10 - filled)}] {MathF.Floor(amount):0}%";
    }
    internal static bool Visible(int mode, bool active, float stamina, bool eligible, bool menuOpen, bool suppressed)
        => eligible && !menuOpen && !suppressed && mode != 2
            && (mode == 0 || active || stamina < 99.95f);

    internal static string Html(float stamina, string score)
    {
        var text = Text(stamina);
        var split = text.LastIndexOf(' ');
        var scoreRows = string.IsNullOrEmpty(score) ? "&nbsp;<br>&nbsp;" : WebUtility.HtmlEncode(score).Replace("\n", "<br>");
        // Reserve both score rows even in warmup so the bar's screen position
        // does not shift when match information appears or disappears.
        return $"<font class='fontSize-l' color='#66EEFF'>{text[..split]}</font> "
            + $"<font class='fontSize-l' color='#FFFFFF'>{text[(split + 1)..]}</font><br>"
            + $"<font class='fontSize-sm' color='#FFFFFF'>{scoreRows}</font>";
    }
}

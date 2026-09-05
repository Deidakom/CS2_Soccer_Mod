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

    // 2026-09-05: the meter used to prepend the match score/time above the
    // bar. That put the same text on the HTML centre channel that the match
    // banner writes to the PLAIN centre channel, and because the two are
    // independent client channels - the plain one lingering for seconds
    // after a single write and not revocable - a live match showed BOTH
    // panels at once (with different timestamps, the lower one stale). The
    // meter is standalone now; the match clock lives on the native HUD
    // round timer instead (SyncNativeRoundClock in Match.cs).
    internal static string Html(float stamina, bool active)
    {
        var parts = Text(stamina).Split(' ');
        var refilling = !active && (!float.IsFinite(stamina) || stamina < 99.95f);
        var color = refilling ? "#FF6464" : "#66EEFF";
        // Equal-length wings centre the percentage inside a wider meter. The
        // client centres the row; medium text and no blank rows reduce its box.
        return $"<font class='fontSize-m' color='{color}'>{parts[0]} </font>"
            + $"<font class='fontSize-m' color='#FFFFFF'>{parts[1]}</font>"
            + $"<font class='fontSize-m' color='{color}'> {parts[2]}</font>";
    }
}

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace SoccerModMvp;

// 2026-10-01 owner: "re-add the floating banner instead of the top scoreboard UI, as a 3rd
// option, with the floating text you had once but enhanced and nicer".
//
// Before the Panorama scoreboard (ScoreHud.cs, 2026-09-25) the score was a plain grey text box
// floating in the middle of the screen (Match.cs UpdateScoreboardDisplay). This is that banner
// again as a per-player choice in Settings - HUD - Top scoreboard, next to "CS2 standard",
// "SoccerMod" and "SoccerMod compact": two coloured lines without a panel around them -
//   RED  2 : 1  BLUE          (team colours, big score)
//   05:32  1ST HALF  LIVE     (clock, period, status; an event such as GOAL takes this line)
// It is drawn with the game's own centre text (PrintToCenterHtml), so it needs no file from the
// Workshop item. A player with the floating banner keeps CS2's own top bar (HudChoice.cs stamps
// the match time into its clock).
public sealed partial class SoccerModMvpPlugin
{
    private const string FloatBannerRed = "#ff6464";
    private const string FloatBannerBlue = "#64a8ff";
    private const string FloatBannerGold = "#ffd24a";
    private const string FloatBannerGrey = "#c8c8c8";
    // The centre panel fades out by itself; the menu's HTML renderer redraws every tick for the
    // same reason (Menu.cs). The text itself is rebuilt 4x a second.
    private const double FloatBannerRebuildSeconds = 0.25;

    private string _floatBannerHtml = string.Empty;
    private double _floatBannerNextBuild;
    // The running event (ShowScoreBanner): main line, sub line, style, until.
    private (string Main, string Sub, string Style, double Until) _floatBannerEvent;
    // Players who got the banner last tick (Menu.cs keeps the centre panel from pulsing for them).
    private int _floatBannerShown;

    private bool FloatTopBar(CCSPlayerController player) =>
        _hudPrefs.FloatTopBar.TryGetValue(SteamIdOf(player), out var on) && on;

    private void SetFloatTopBar(CCSPlayerController player, bool on)
    {
        var id = SteamIdOf(player);
        if (id == 0) return;
        _hudPrefs.FloatTopBar[id] = on;
        SaveJsonAtomic(HudPrefsFile, _hudPrefs);
        _nextScoreHudDraw = 0;
        // the panel fades by itself; blank it at once when the player switches away
        if (!on && player.IsValid) player.PrintToCenterHtml(" ");
    }

    // ShowScoreBanner: the event shows in the banner's second line for the same three seconds.
    private void FloatBannerEvent(string main, string sub, string style)
    {
        _floatBannerEvent = (main, sub, style, Server.TickedTime + ScoreHudBannerSeconds);
        _floatBannerNextBuild = 0;
    }

    // Called every tick from OnTick, after ScoreHudOnTick.
    private void FloatBannerOnTick()
    {
        _floatBannerShown = 0;
        if (_hudPrefs.FloatTopBar.Count == 0) return;
        var now = (double)Server.TickedTime;
        var visible = _menuParity.MatchInfo && (MatchRunning || now < _scoreHudHoldUntil);
        if (!visible) return;
        if (now >= _floatBannerNextBuild)
        {
            _floatBannerNextBuild = now + FloatBannerRebuildSeconds;
            _floatBannerHtml = BuildFloatBannerHtml(now);
        }
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot || !FloatTopBar(player)) continue;
            var slot = player.Slot;
            // the same screen region: a text menu, the text sprint bar and the aim hint go first
            if (_sprintBars.ContainsKey(slot) || _aimPicks.ContainsKey(slot)) continue;
            if (_openMenus.ContainsKey(slot) && !UsesKeyMenu(player) && !UsesClickMenu(player)) continue;
            player.PrintToCenterHtml(_floatBannerHtml, MenuPanelDurationSeconds);
            _floatBannerShown++;
        }
    }

    private string BuildFloatBannerHtml(double now)
    {
        static string Font(string size, string color, string text) =>
            $"<font class='fontSize-{size}' color='{color}'>{text}</font>";
        var (clock, period, status, statusClass) = ScoreHudState(now);
        var red = MenuText.EscapeHtml(MatchRuleMath.ScoreHudTeamName(_teamNameT, "RED"));
        var blue = MenuText.EscapeHtml(MatchRuleMath.ScoreHudTeamName(_teamNameCt, "BLUE"));
        var top = Font("l", FloatBannerRed, $"<b>{red}</b>")
            + "&nbsp;&nbsp;&nbsp;" + Font("xl", "#ffffff", $"<b>{_scoreT} : {_scoreCt}</b>") + "&nbsp;&nbsp;&nbsp;"
            + Font("l", FloatBannerBlue, $"<b>{blue}</b>");

        string bottom;
        if (now < _floatBannerEvent.Until)
        {
            var color = _floatBannerEvent.Style switch
            {
                "goal-red" => FloatBannerRed,
                "goal-blue" => FloatBannerBlue,
                _ => FloatBannerGold,
            };
            bottom = Font("l", color, $"<b>{MenuText.EscapeHtml(_floatBannerEvent.Main)}</b>");
            if (_floatBannerEvent.Sub.Length > 0)
                bottom += "&nbsp;&nbsp;" + Font("m", FloatBannerGrey, MenuText.EscapeHtml(_floatBannerEvent.Sub));
        }
        else
        {
            var statusColor = statusClass switch
            {
                "live" => "#7dff7d",
                "goal" => FloatBannerGold,
                "paused" => FloatBannerGrey,
                _ => FloatBannerGold,
            };
            bottom = Font("m", "#ffffff", $"<b>{clock}</b>")
                + "&nbsp;&nbsp;" + Font("sm", FloatBannerGrey, MenuText.EscapeHtml(period))
                + "&nbsp;&nbsp;" + Font("sm", statusColor, MenuText.EscapeHtml(status));
        }
        return top + "<br>" + bottom;
    }
}

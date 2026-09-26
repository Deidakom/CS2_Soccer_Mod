using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-26 owner: CS2's own centre banners - "The game will restart in 1
// second" (a TextMsg the server sends before every mp_restartgame, so after
// every goal) and "MATCH START" (the client's reaction to the game event
// round_announce_match_start after the restart) - play as the scoreboard
// overlay instead (ScoreHud.cs). Only while a match runs with the Panorama
// scoreboard (outside a match there is no scoreboard, so the originals stay),
// and never over an overlay that is still showing (a GOAL stays).
public sealed partial class SoccerModMvpPlugin
{
    private const int TextMsgMessageId = 124;   // UM_TextMsg (usermessages.proto)
    private const int HudPrintCenter = 4;
    private readonly HashSet<string> _nativeCenterTextsLogged = new();

    private void NativeBannersOnLoad()
    {
        HookUserMessage(TextMsgMessageId, OnNativeTextMsg, HookMode.Pre);
        RegisterEventHandler<EventRoundAnnounceMatchStart>(OnNativeMatchStartAnnounce, HookMode.Pre);
    }

    private bool NativeBannersReplaced => ScoreHudPanorama && MatchRunning;

    private HookResult OnNativeTextMsg(UserMessage message)
    {
        if (!NativeBannersReplaced) return HookResult.Continue;
        int dest;
        string text;
        try
        {
            dest = message.ReadInt("dest");
            text = message.ReadString("param", 0);
        }
        catch (Exception)
        {
            return HookResult.Continue;
        }
        if (dest != HudPrintCenter) return HookResult.Continue;
        if (text.Contains("Game_will_restart", StringComparison.OrdinalIgnoreCase))
        {
            string seconds;
            try { seconds = message.ReadString("param", 1); }
            catch (Exception) { seconds = "1"; }
            ShowNativeOverlay("RESTART", $"in {seconds} s", "break");
            return HookResult.Stop;
        }
        if (text.Contains("Match_Start", StringComparison.OrdinalIgnoreCase))
        {
            ShowKickoffOverlay();
            return HookResult.Stop;
        }
        // Anything else stays as CS2 sends it; logged once so the next
        // candidates for an overlay are visible in the journal.
        if (_nativeCenterTextsLogged.Add(text))
            Logger.LogInformation("[SM2DIAG] native_center_text kept text={Text}", text);
        return HookResult.Continue;
    }

    private HookResult OnNativeMatchStartAnnounce(EventRoundAnnounceMatchStart @event, GameEventInfo info)
    {
        if (!NativeBannersReplaced) return HookResult.Continue;
        info.DontBroadcast = true;
        ShowKickoffOverlay();
        return HookResult.Continue;
    }

    private void ShowKickoffOverlay()
    {
        var team = _kickoffTeam is CsTeam.Terrorist or CsTeam.CounterTerrorist ? $"{GoalSideLabel(_kickoffTeam)} kick off" : "Kick-off";
        ShowNativeOverlay("KICK-OFF", team, "start");
    }

    // Replaces a native banner, unless one of our own events is still up.
    private void ShowNativeOverlay(string main, string sub, string style)
    {
        if (Server.TickedTime < _scoreHudOverlayUntil) return;
        ShowScoreBanner(main, sub, style);
    }
}

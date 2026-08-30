using System;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Port of SoMoE-19's chatset.sp (prefix/colors) and deadchat.sp
// (2026-08-30 SoMoE reconstruction round).
//
// Chat prefix/colors: SoMoE's original is a whole-server setting (not
// per-player), captured via an interactive "type it in chat" flow. We use a
// plain admin command instead (css_sm2chat <key> <value>) - same "one
// command, no new capture-state machinery" style as css_teamname. This is
// a NEW formatting helper (FormatSoccerModMessage) for messages added in
// this reconstruction round (health/deadchat/duckjump toggles); the many
// existing AnnounceAll call sites across Match.cs/Cap.cs etc. keep their
// own hand-picked \x04/\x01 codes untouched - retrofitting every one of
// them to the configurable prefix is a separate, much larger change and
// was not requested.
//
// Dead chat: SoMoE hooks the SayText2 usermessage to rebroadcast dead
// players' chat to a computed recipient list per visibility mode - not
// reachable from CSSharp (no usermessage rebroadcast hook). Ported at
// convar level instead via sv_deadtalk (confirmed present on this build).
// Mode 2 (visibility "Everyone") in the original also depends on
// sv_alltalk; that convar is left as the server's own setting since this
// server already runs sv_alltalk 1 permanently.
public sealed partial class SoccerModMvpPlugin
{
    private string _chatPrefix = "Soccer Mod";
    private string _chatPrefixColor = "green";
    private string _chatTextColor = "lightgreen";

    // 0 = off, 1 = on, 2 = on only while sv_alltalk is 1 (SoMoE default 0
    // is "off"; we keep that default rather than SoMoE's, since a
    // knife-only private test server has no obvious reason to want dead
    // players heard by default).
    private int _deadChatMode;

    private static char ResolveChatColor(string name) => name.ToLowerInvariant() switch
    {
        "white" => ChatColors.White,
        "darkred" => ChatColors.DarkRed,
        "green" => ChatColors.Green,
        "lightyellow" => ChatColors.LightYellow,
        "lightblue" => ChatColors.LightBlue,
        "olive" => ChatColors.Olive,
        "lime" => ChatColors.Lime,
        "red" => ChatColors.Red,
        "lightpurple" => ChatColors.LightPurple,
        "purple" => ChatColors.Purple,
        "grey" or "gray" => ChatColors.Grey,
        "yellow" => ChatColors.Yellow,
        "gold" => ChatColors.Gold,
        "silver" => ChatColors.Silver,
        "blue" => ChatColors.Blue,
        "darkblue" => ChatColors.DarkBlue,
        "bluegrey" or "bluegray" => ChatColors.BlueGrey,
        "magenta" => ChatColors.Magenta,
        "lightred" => ChatColors.LightRed,
        "orange" => ChatColors.Orange,
        _ => ChatColors.Default,
    };

    // For new messages only - see the class header comment.
    private string FormatSoccerModMessage(string text) =>
        $" {ResolveChatColor(_chatPrefixColor)}[{_chatPrefix}] {ResolveChatColor(_chatTextColor)}{text}";

    private void ChatSettingsOnLoad()
    {
        AddCommand("css_sm2chat", "Admin: set prefix|prefixcolor|textcolor|deadchat.", OnChatSettingsCommand);
    }

    private void OnChatSettingsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        if (command.ArgCount < 3)
        {
            command.ReplyToCommand(
                $"[SM] chat: prefix=\"{_chatPrefix}\" prefixColor={_chatPrefixColor} textColor={_chatTextColor} deadChatMode={_deadChatMode} "
                + "(usage: css_sm2chat <prefix|prefixcolor|textcolor|deadchat> <value...>)");
            return;
        }

        var key = command.GetArg(1).ToLowerInvariant();
        // Prefix text may contain spaces; args 2..N joined back together.
        var valueArg = string.Join(' ', GetArgsFrom(command, 2));

        switch (key)
        {
            case "prefix":
                if (string.IsNullOrWhiteSpace(valueArg) || valueArg.Length > 32)
                {
                    command.ReplyToCommand("[SM] prefix must be 1-32 characters");
                    return;
                }
                _chatPrefix = valueArg;
                break;
            case "prefixcolor":
                _chatPrefixColor = valueArg;
                break;
            case "textcolor":
                _chatTextColor = valueArg;
                break;
            case "deadchat":
                if (!int.TryParse(valueArg, out var mode) || mode is < 0 or > 2)
                {
                    command.ReplyToCommand("[SM] deadchat mode must be 0 (off), 1 (on) or 2 (on if alltalk)");
                    return;
                }
                _deadChatMode = mode;
                ApplyDeadChatMode();
                break;
            default:
                command.ReplyToCommand("[SM] unknown key; use prefix|prefixcolor|textcolor|deadchat");
                return;
        }

        SaveMatchSettings("chat_settings_command");
        command.ReplyToCommand(
            $"[SM] chat: prefix=\"{_chatPrefix}\" prefixColor={_chatPrefixColor} textColor={_chatTextColor} deadChatMode={_deadChatMode}");
    }

    private static string[] GetArgsFrom(CommandInfo command, int startIndex)
    {
        var args = new string[command.ArgCount - startIndex];
        for (var i = 0; i < args.Length; i++)
        {
            args[i] = command.GetArg(startIndex + i);
        }
        return args;
    }

    private void ApplyDeadChatMode()
    {
        var alltalkOn = ConVar.Find("sv_alltalk")?.GetPrimitiveValue<bool>() ?? false;
        var enabled = _deadChatMode == 1 || (_deadChatMode == 2 && alltalkOn);
        ConVar.Find("sv_deadtalk")?.SetValue<bool>(enabled);
        Logger.LogInformation("[SM2DIAG] deadchat_applied mode={Mode} enabled={Enabled}", _deadChatMode, enabled);
    }
}

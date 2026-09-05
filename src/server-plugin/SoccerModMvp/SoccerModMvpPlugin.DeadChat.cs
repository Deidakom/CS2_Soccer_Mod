using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.UserMessages;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    // CUserMessageSayText2 in Valve's current usermessages.proto.
    private const int SayText2MessageId = 118;
    private readonly Dictionary<uint, (ulong SteamId, bool Team, double At)> _pendingChat = new();
    private bool _deadChatSchemaFailed;
    private bool DeadChatEnabled => _deadChatMode == 1 || _deadChatMode == 2 && (ConVar.Find("sv_alltalk")?.GetPrimitiveValue<bool>() ?? false);
    private static bool ExtendChatTo(bool teamMessage, int visibility, int authorTeam, int recipientTeam)
        => !teamMessage || visibility == 2 || visibility == 1 && authorTeam == recipientTeam;
    private void DeadChatOnLoad()
    {
        foreach (var command in new[] { "say", "say_team" })
            AddCommandListener(command, (player, info) =>
            {
                if (player is { IsValid: true } && player.AuthorizedSteamID is { } steam)
                    _pendingChat[player.Index] = (steam.SteamId64, command == "say_team", Server.TickedTime);
                return HookResult.Continue;
            }, HookMode.Pre);
        RegisterListener<Listeners.OnMapStart>(_ => _pendingChat.Clear());
        RegisterListener<Listeners.OnClientDisconnect>(slot => _pendingChat.Remove((uint)(slot + 1)));
        HookUserMessage(SayText2MessageId, ExtendDeadChatRecipients, HookMode.Pre);
        AddCommand("css_sm2chat_schema", "Server only: verify SayText2 fields without sending a message.", (p, c) =>
        {
            if (!RequireServerConsole(p, c)) return;
            using var message = UserMessage.FromId(SayText2MessageId);
            message.SetBool("chat", true); message.SetInt("entityindex", 1);
            message.SetString("messagename", "schema_probe");
            c.ReplyToCommand($"[SM] SayText2 type={message.Type} chat={message.ReadBool("chat")} entity={message.ReadInt("entityindex")} name={message.ReadString("messagename")} recipients={message.Recipients.Count}; no message sent");
        });
    }
    private HookResult ExtendDeadChatRecipients(UserMessage message)
    {
        if (!DeadChatEnabled || _deadChatSchemaFailed) return HookResult.Continue;
        try
        {
            if (!message.ReadBool("chat")) return HookResult.Continue;
            var index = message.ReadInt("entityindex");
            if (index < 1 || !_pendingChat.Remove((uint)index, out var pending) || Server.TickedTime - pending.At > 1) return HookResult.Continue;
            var author = Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && p.Index == (uint)index && p.AuthorizedSteamID?.SteamId64 == pending.SteamId);
            if (author is null) return HookResult.Continue;
            var recipients = message.Recipients.Select(p => p.Slot).ToHashSet();
            foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot))
                if (ExtendChatTo(pending.Team, _menuParity.DeadChatVisibility, (int)author.Team, (int)player.Team) && recipients.Add(player.Slot))
                    message.Recipients.Add(player);
        }
        catch (Exception ex)
        {
            _deadChatSchemaFailed = true;
            Logger.LogWarning(ex, "[SM2DIAG] deadchat_routing_disabled; original recipient list remains available");
        }
        return HookResult.Continue;
    }
}

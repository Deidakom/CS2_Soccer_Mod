using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// SoMoE-19 "Type a value between X and Y" chat capture (soccer_mod.sp
// SayCommandListener + changeSetting[client]). The NumberMenu has no free-
// text input, and the original mod never had one either: a menu entry arms
// a per-player pending request, and the NEXT chat line that player sends is
// consumed as the value instead of being broadcast. Used by the training
// cannon settings (randomness/fire rate/power), the match settings "Custom"
// period/break lengths and the custom team names.
//
// Chat reaches us as the "say"/"say_team" console commands (CommandListener
// with HookMode.Pre, exactly like the slot1..slot9 menu listeners in
// Menu.cs). GetArg(1) is the unquoted message; ArgString would carry the
// client's quotes. Returning HookResult.Handled swallows the line so the
// value never shows up in everyone's chat. A "!42" style message never
// arrives here at all - CSSharp turns it into the css_42 chat command
// before any listener runs - so the prompt tells the player to type the
// bare number. Non-numeric chat while a number is pending passes through
// untouched (the request simply keeps waiting until it times out).
public sealed partial class SoccerModMvpPlugin
{
    private const double ChatInputTimeoutSeconds = 30.0;

    private sealed class ChatInputRequest
    {
        public required double ExpiresAt;
        public float Min;
        public float Max;
        public bool IsText;
        public Action<CCSPlayerController, float>? OnNumber;
        public Action<CCSPlayerController, string>? OnText;
        public Action<CCSPlayerController>? OnCancel;
    }

    private readonly Dictionary<int, ChatInputRequest> _chatInputBySlot = new();

    private void ChatInputOnLoad()
    {
        AddCommandListener("say", OnChatInputSay, HookMode.Pre);
        AddCommandListener("say_team", OnChatInputSay, HookMode.Pre);
        RegisterListener<Listeners.OnClientDisconnect>(slot => _chatInputBySlot.Remove(slot));
    }

    // SoMoE semantics: "Type a value between %f and %f"; typing 0 cancels
    // whenever 0 is outside the accepted range (fire rate/power/period/
    // break), otherwise 0 is simply a valid value (cannon randomness).
    private void BeginChatNumberInput(
        CCSPlayerController player,
        string prompt,
        float min,
        float max,
        Action<CCSPlayerController, float> onValue,
        Action<CCSPlayerController>? onCancel = null)
    {
        _chatInputBySlot[player.Slot] = new ChatInputRequest
        {
            ExpiresAt = Server.TickedTime + ChatInputTimeoutSeconds,
            Min = min,
            Max = max,
            IsText = false,
            OnNumber = onValue,
            OnCancel = onCancel,
        };
        player.PrintToChat($" \x04[SM]\x01 {prompt}");
        var cancelHint = min > 0.0f ? ", 0 to stop" : string.Empty;
        player.PrintToChat($" \x04[SM]\x01 Type a value between {FormatChatNumber(min)} and {FormatChatNumber(max)} in chat (just the number, no !){cancelHint}.");
        Logger.LogInformation("[SM2DIAG] chat_input_armed slot={Slot} kind=number min={Min} max={Max}", player.Slot, min, max);
    }

    private void BeginChatTextInput(
        CCSPlayerController player,
        string prompt,
        Action<CCSPlayerController, string> onValue,
        Action<CCSPlayerController>? onCancel = null)
    {
        _chatInputBySlot[player.Slot] = new ChatInputRequest
        {
            ExpiresAt = Server.TickedTime + ChatInputTimeoutSeconds,
            IsText = true,
            OnText = onValue,
            OnCancel = onCancel,
        };
        player.PrintToChat($" \x04[SM]\x01 {prompt}");
        Logger.LogInformation("[SM2DIAG] chat_input_armed slot={Slot} kind=text", player.Slot);
    }

    private static string FormatChatNumber(float value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private HookResult OnChatInputSay(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !player.IsValid || !_chatInputBySlot.TryGetValue(player.Slot, out var request))
        {
            return HookResult.Continue;
        }

        if (Server.TickedTime > request.ExpiresAt)
        {
            _chatInputBySlot.Remove(player.Slot);
            return HookResult.Continue;
        }

        var text = command.ArgCount >= 2 ? command.GetArg(1).Trim() : string.Empty;
        if (text.Length == 0)
        {
            return HookResult.Continue;
        }

        if (request.IsText)
        {
            _chatInputBySlot.Remove(player.Slot);
            if (text.Equals("!cancel", StringComparison.OrdinalIgnoreCase)
                || text.Equals("cancel", StringComparison.OrdinalIgnoreCase))
            {
                player.PrintToChat(" \x04[SM]\x01 Cancelled.");
                request.OnCancel?.Invoke(player);
                return HookResult.Handled;
            }

            request.OnText!(player, text);
            return HookResult.Handled;
        }

        var numeric = text.Replace(',', '.');
        if (!float.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !float.IsFinite(value))
        {
            // Ordinary chat: let it through, keep waiting.
            return HookResult.Continue;
        }

        if (value == 0.0f && request.Min > 0.0f)
        {
            _chatInputBySlot.Remove(player.Slot);
            player.PrintToChat(" \x04[SM]\x01 Cancelled.");
            request.OnCancel?.Invoke(player);
            return HookResult.Handled;
        }

        if (value < request.Min || value > request.Max)
        {
            player.PrintToChat($" \x04[SM]\x01 Type a value between {FormatChatNumber(request.Min)} and {FormatChatNumber(request.Max)}.");
            return HookResult.Handled;
        }

        _chatInputBySlot.Remove(player.Slot);
        request.OnNumber!(player, value);
        return HookResult.Handled;
    }
}

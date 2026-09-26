using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.UserMessages;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-26 owner: knife hits on the ball still throw shards and dust.
// Emptying impact_plastic*.vpcf in the Feature Package cannot work (CS2's own
// files win over Workshop items), so the effect has to be stopped where the
// server sends it. This diagnostic logs which user messages go out while the
// ball is kicked (effect/particle/decal range), for 30 seconds, so the right
// one can be blocked. Admin: css_sm2um_log.
public sealed partial class SoccerModMvpPlugin
{
    // Temp-entity protobufs (TE_*, 400+) and the particle manager / effect range.
    private static readonly int[] UserMessageLogIds = Enumerable.Range(100, 60).Concat(Enumerable.Range(400, 40)).ToArray();
    private bool _umLogOn;
    private readonly Dictionary<int, int> _umLogCounts = new();

    private void UserMessageLogOnLoad()
    {
        AddCommand("css_sm2um_log", "Admin: log effect/particle user messages for 30 s (diagnostic).", OnUserMessageLogCommand);
    }

    private void OnUserMessageLogCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin")) return;
        if (_umLogOn) { command.ReplyToCommand("[SM] User message log already running."); return; }
        _umLogOn = true;
        _umLogCounts.Clear();
        foreach (var id in UserMessageLogIds) HookUserMessage(id, OnLoggedUserMessage, HookMode.Pre);
        command.ReplyToCommand("[SM] Logging effect user messages for 30 s - kick the ball now.");
        AddTimer(30.0f, () =>
        {
            foreach (var id in UserMessageLogIds) UnhookUserMessage(id, OnLoggedUserMessage, HookMode.Pre);
            _umLogOn = false;
            Logger.LogInformation("[SM2DIAG] um_log_done counts={Counts}",
                string.Join(",", _umLogCounts.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}")));
        });
    }

    private HookResult OnLoggedUserMessage(UserMessage um)
    {
        _umLogCounts[um.Id] = _umLogCounts.GetValueOrDefault(um.Id) + 1;
        string detail;
        try { detail = um.DebugString; } catch { detail = "?"; }
        if (detail.Length > 300) detail = detail[..300];
        Logger.LogInformation("[SM2DIAG] um id={Id} ball={Ball} {Detail}", um.Id,
            _ball?.AbsOrigin is { } b ? $"{b.X:F0},{b.Y:F0},{b.Z:F0}" : "-", detail.Replace('\n', ' '));
        return HookResult.Continue;
    }
}

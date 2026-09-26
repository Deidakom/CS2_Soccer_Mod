using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-26 owner: the stadium's own roof scoreboard (the 7-segment "00:00"
// digits) shows the match score. The map drives each digit from a
// math_counter through a logic_case (entity lump of soccer_cssl_stadium_v8):
//   T  (left, red):   Math_counter (units), Math_counter_10 (tens)
//   CT (right, blue): Math_counter_axis (units), Math_counter_10_axis (tens)
// The wall +/- buttons that used to add to them are removed (MapCleanup.cs);
// the plugin sets the counters instead, together with the CS2 team scores:
// on every goal, round restart and match start. Outside a match: 00:00.
public sealed partial class SoccerModMvpPlugin
{
    private void UpdateMapScoreboard()
    {
        var running = MatchRunning || _matchPhase == MatchPhase.Finished;
        var t = running ? _scoreT : 0;
        var ct = running ? _scoreCt : 0;
        var values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Math_counter"] = t % 10,
            ["Math_counter_10"] = t / 10 % 10,
            ["Math_counter_axis"] = ct % 10,
            ["Math_counter_10_axis"] = ct / 10 % 10,
        };
        var set = 0;
        foreach (var counter in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("math_counter"))
        {
            if (!counter.IsValid || counter.Entity?.Name is not { } name) continue;
            // Runtime names normally lack the "[PR#]" prefix seen in the dump.
            var key = name.StartsWith("[PR#]", StringComparison.Ordinal) ? name[5..] : name;
            if (!values.TryGetValue(key, out var value)) continue;
            counter.AcceptInput("SetValue", value: value.ToString());
            set++;
        }
        if (set > 0)
            Logger.LogInformation("[SM2DIAG] map_scoreboard_set counters={Set} t={T} ct={Ct}", set, t, ct);
    }
}

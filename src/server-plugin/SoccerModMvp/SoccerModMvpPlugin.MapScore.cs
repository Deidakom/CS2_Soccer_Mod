using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-26 owner: the stadium's own roof scoreboard (the 7-segment "00:00"
// digits) shows the match score. In the map (entity lump of
// soccer_cssl_stadium_v8) each digit is 7 func_brush segments
// Counter_digit_<a..g><suffix> - suffix "" = T units, "0" = T tens,
// "_axis" = CT units, "0_axis" = CT tens (T left/red, CT right/blue) - driven
// by math_counter -> logic_case -> logic_relay chains. Setting the counters
// was tried first: the live log showed them set (t=0 ct=1) but the digits
// stayed 00:00 - the chain's outputs target the dump's "[PR#]" names, which
// the runtime entities do not carry. So the plugin switches the segments
// itself (Enable/Disable), with the map's own segment table below, on every
// goal, round restart and match start. Outside a match: 00:00.
public sealed partial class SoccerModMvpPlugin
{
    // Lit segments per digit, read from the map's Counter_digit_0..9 relays
    // ("d" is the middle bar in this map, not the standard "g").
    private static readonly string[] MapDigitSegments =
        { "abcefg", "cf", "acdeg", "acdfg", "bcdf", "abdfg", "abdefg", "acf", "abcdefg", "abcdfg" };

    private void UpdateMapScoreboard()
    {
        var running = MatchRunning || _matchPhase == MatchPhase.Finished;
        var t = running ? _scoreT : 0;
        var ct = running ? _scoreCt : 0;
        var digits = new Dictionary<string, int>
        {
            [""] = t % 10,
            ["0"] = t / 10 % 10,
            ["_axis"] = ct % 10,
            ["0_axis"] = ct / 10 % 10,
        };
        var switched = 0;
        foreach (var brush in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_brush"))
        {
            if (!brush.IsValid || brush.Entity?.Name is not { } name) continue;
            // Runtime names normally lack the "[PR#]" prefix seen in the dump.
            var key = name.StartsWith("[PR#]", StringComparison.Ordinal) ? name[5..] : name;
            const string prefix = "Counter_digit_";
            if (!key.StartsWith(prefix, StringComparison.Ordinal) || key.Length < prefix.Length + 1) continue;
            var segment = key[prefix.Length];
            if (segment < 'a' || segment > 'g' || !digits.TryGetValue(key[(prefix.Length + 1)..], out var digit)) continue;
            brush.AcceptInput(MapDigitSegments[digit].Contains(segment) ? "Enable" : "Disable");
            switched++;
        }
        if (switched > 0)
            Logger.LogInformation("[SM2DIAG] map_scoreboard_set segments={Switched} t={T} ct={Ct}", switched, t, ct);
    }
}

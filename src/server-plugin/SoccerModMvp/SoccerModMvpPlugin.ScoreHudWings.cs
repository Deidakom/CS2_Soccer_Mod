using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;

// 2026-09-27 owner (option B): CS2's own top bar shows one avatar per player
// next to its clock and there is no server switch to hide it; with more
// players the avatars reach past our scoreboard. Wings left and right of the
// bar (soccermod_scorebug.xml / .css, Feature Package) grow with the team
// size and cover them. Width = base + perPlayer x (bigger team), minus the
// team-name fields in the full layout, in 20 px steps (CSS wn-1..wn-30), the
// same on both sides so the bar stays centred, and at least wide enough for
// the little players: one per member of that team (pc-<n>, owner request).
// Tunable live (the CSS itself needs a Workshop upload):
// css_sm2hud_wings <base> <perPlayer> [forcePlayers|-1], saved to
// soccermod_hud_wings.json.
public sealed partial class SoccerModMvpPlugin
{
    private const string HudWingsFile = "soccermod_hud_wings.json";
    private const int HudWingStepPx = 20;
    private const int HudWingMaxSteps = 30;
    private const int HudWingFullLayoutPx = 150; // .sm-team width, full layout only

    private sealed class HudWingSettings
    {
        public int BasePx { get; set; } = -52;
        public int PerPlayerPx { get; set; } = 36;
    }

    private HudWingSettings _hudWings = new();
    private int _hudWingForcePlayers = -1;

    private void ScoreHudWingsOnLoad()
    {
        _hudWings = LoadJsonOrNull<HudWingSettings>(HudWingsFile) ?? new HudWingSettings();
        AddCommand("css_sm2hud_wings", "Admin: scoreboard avatar wings <base> <perPlayer> [forcePlayers|-1].", OnHudWingsCommand);
    }

    private void OnHudWingsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin")) return;
        if (command.ArgCount >= 3
            && int.TryParse(command.GetArg(1), out var basePx)
            && int.TryParse(command.GetArg(2), out var perPlayer))
        {
            _hudWings = new HudWingSettings { BasePx = basePx, PerPlayerPx = perPlayer };
            SaveJsonAtomic(HudWingsFile, _hudWings);
            if (command.ArgCount >= 4 && int.TryParse(command.GetArg(3), out var force)) _hudWingForcePlayers = force;
            _nextScoreHudDraw = 0;
        }
        var (t, ct) = HudWingTeamCounts();
        command.ReplyToCommand($"[SM] HUD wings: base={_hudWings.BasePx} perPlayer={_hudWings.PerPlayerPx} force={_hudWingForcePlayers} teams T={t} CT={ct} -> compact {HudWingSteps(false) * HudWingStepPx}px, full {HudWingSteps(true) * HudWingStepPx}px");
    }

    private static (int T, int Ct) HudWingTeamCounts()
    {
        int t = 0, ct = 0;
        foreach (var p in Utilities.GetPlayers())
        {
            if (!p.IsValid) continue;
            if (p.Team == CsTeam.Terrorist) t++;
            else if (p.Team == CsTeam.CounterTerrorist) ct++;
        }
        return (t, ct);
    }

    private int HudWingSteps(bool fullLayout)
    {
        var (t, ct) = HudWingTeamCounts();
        var players = _hudWingForcePlayers >= 0 ? _hudWingForcePlayers : Math.Max(t, ct);
        var px = _hudWings.BasePx + _hudWings.PerPlayerPx * players - (fullLayout ? HudWingFullLayoutPx : 0);
        // room for the little players (16 px each, max 10) plus a margin
        var people = Math.Min(10, Math.Max(t, ct));
        if (people > 0) px = Math.Max(px, people * 16 + 12);
        if (px <= 0) return 0;
        return Math.Min(HudWingMaxSteps, (px + HudWingStepPx - 1) / HudWingStepPx);
    }

    // From ScoreHudOnTick, per player (only what changed is sent).
    private void ScoreHudWingsDraw(CCSPlayerController player, bool compact)
    {
        var (t, ct) = HudWingTeamCounts();
        var red = Math.Min(t, 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var blue = Math.Min(ct, 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Remember(player.Slot, "#people_red", red)) _scoreHudPanel!.SetVariant(player, "sm_wing_red", "pc-", red);
        if (Remember(player.Slot, "#people_blue", blue)) _scoreHudPanel!.SetVariant(player, "sm_wing_blue", "pc-", blue);
        var wings = HudWingSteps(!compact).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Remember(player.Slot, "#wings", wings)) _scoreHudPanel!.SetVariant(player, "sm_row", "wn-", wings);
    }
}

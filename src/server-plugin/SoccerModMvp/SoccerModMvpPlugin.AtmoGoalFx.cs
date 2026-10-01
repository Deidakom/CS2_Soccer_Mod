using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-29 "Arena Vision" goal celebrations (v8 only): on top of the team
// goal show the scorer's own celebration plays, unlocked by career stats
// (public + match goals, MVPs). Owner: the thresholds are set by the admin in
// Admin - Settings - Stadium effects - Goal celebrations ("10 goals is very
// unlikely"), defaults are low. Players pick theirs in !menu - Settings -
// Goal celebration; "best unlocked" is the default.
public sealed partial class SoccerModMvpPlugin
{
    private const string GoalFxPrefsFile = "soccermod_goal_fx_prefs.json";
    private static readonly int[] GoalFxThresholdSteps = { 0, 1, 2, 3, 5, 10, 15, 20, 25, 30, 50, 75, 100 };

    private enum GoalFx { Classic, Fireworks, Strobe, GoldRain, Legend }
    private static readonly string[] GoalFxNames = { "Classic", "Fireworks finale", "Stadium strobe", "Gold rain", "Legend" };

    private Dictionary<ulong, string>? _goalFxPrefs;
    private Dictionary<ulong, string> GoalFxPrefs => _goalFxPrefs ??= LoadJsonOrNull<Dictionary<ulong, string>>(GoalFxPrefsFile) ?? new();

    private int GoalFxGoalsNeeded(GoalFx fx) => fx switch
    {
        GoalFx.Fireworks => AtmoSet.UnlockFireworksGoals,
        GoalFx.Strobe => AtmoSet.UnlockStrobeGoals,
        GoalFx.GoldRain => AtmoSet.UnlockGoldGoals,
        GoalFx.Legend => AtmoSet.UnlockLegendGoals,
        _ => 0,
    };

    private (int Goals, int Mvps) GoalFxCareer(ulong steamId) =>
        _statsBySteamId.TryGetValue(steamId, out var e) ? (e.Public.Goals + e.Match.Goals, e.Public.Mvp + e.Match.Mvp) : (0, 0);

    private bool GoalFxUnlocked(GoalFx fx, ulong steamId)
    {
        var (goals, mvps) = GoalFxCareer(steamId);
        return goals >= GoalFxGoalsNeeded(fx) || fx == GoalFx.GoldRain && AtmoSet.UnlockGoldMvps > 0 && mvps >= AtmoSet.UnlockGoldMvps;
    }

    private string GoalFxLockText(GoalFx fx)
    {
        var text = $"{GoalFxGoalsNeeded(fx)} goals";
        return fx == GoalFx.GoldRain && AtmoSet.UnlockGoldMvps > 0 ? $"{text} or {AtmoSet.UnlockGoldMvps} MVPs" : text;
    }

    private GoalFx GoalFxFor(ulong steamId)
    {
        if (GoalFxPrefs.TryGetValue(steamId, out var pick) && Enum.TryParse<GoalFx>(pick, out var chosen) && GoalFxUnlocked(chosen, steamId))
            return chosen;
        for (var fx = GoalFx.Legend; fx > GoalFx.Classic; fx--)
            if (GoalFxUnlocked(fx, steamId)) return fx;
        return GoalFx.Classic;
    }

    // From AtmoGoalShow, after the team show (fanSign: the scorer's fan end).
    private void AtmoGoalCelebration(int scorerSlot, bool red, int fanSign, int goalSign)
    {
        if (!AtmoSet.GoalCelebrations || scorerSlot < 0 || Utilities.GetPlayerFromSlot(scorerSlot) is not { IsValid: true, IsBot: false } scorer) return;
        var fx = GoalFxFor(scorer.SteamID);
        if (fx == GoalFx.Classic) return;
        var colour = red ? "red" : "blue";
        // Each part obeys its own admin switch (review 2026-09-29); loop values are copied
        // before the delayed actions so every step keeps its own value.
        if ((fx is GoalFx.Fireworks or GoalFx.Legend) && AtmoSet.Fireworks)
            for (var i = 0; i < 7; i++)
            {
                var x = -1200f + i * 400f;
                var z = 1350f + (i % 2) * 250f;
                var effect = i % 2 == 0 ? "firework_gold" : $"firework_{colour}";
                AtmoLater(1.8 + i * 0.25, () => AtmoParticle(effect, new Vector(x, fanSign * 1500f, z), 5.0));
            }
        if (fx is GoalFx.Strobe or GoalFx.Legend)
        {
            if (AtmoSet.LightRing)
            {
                for (var k = 0; k < 8; k++)
                {
                    var white = k % 2 == 0;
                    AtmoLater(2.0 + k * 0.22, () => AtmoRingPlay(AtmoRingMode.Solid, white ? System.Drawing.Color.White : red ? AtmoRingRed : AtmoRingBlue, 0.22));
                }
                AtmoLater(2.0 + 8 * 0.22, () => AtmoRingPlay(AtmoRingMode.Chase, red ? AtmoRingRed : AtmoRingBlue, 3.0));
            }
            if (AtmoSet.CameraFlashes)
                foreach (var (x, y) in AtmoStandCentres) AtmoLater(1.0, () => AtmoParticle("camera_flashes", new Vector(x, y, 420f), 5.0));
        }
        if ((fx is GoalFx.GoldRain or GoalFx.Legend) && AtmoSet.Pyro)
        {
            AtmoLater(1.2, () =>
            {
                foreach (var y in new[] { goalSign * 1250f, goalSign * 400f })
                    AtmoParticle("confetti_rain_gold", new Vector(0f, y, 1100f), 14.0);
            });
            // gold flares along both side stands (front row |x| 1716, z 107): the whole stadium celebrates
            foreach (var sideX in new[] { 1716f, -1716f })
                for (var y = -600f; y <= 600f; y += 600f)
                    AtmoParticle("flare_gold", AtmoSideRowSpot(sideX, y), 11.0);
        }
        Logger.LogInformation("[SM2DIAG] atmo_goal_celebration scorer=\"{Name}\" fx={Fx}", scorer.PlayerName, fx);
    }

    // !menu - Settings (while the stadium effects run).
    private void AddGoalFxEntry(NumberMenu menu)
    {
        if (!AtmoOn || !AtmoSet.GoalCelebrations) return;
        menu.Add("Goal celebration", OpenGoalFxMenu);
    }

    private void OpenGoalFxMenu(CCSPlayerController player)
    {
        var steamId = player.SteamID;
        var (goals, mvps) = GoalFxCareer(steamId);
        var current = GoalFxFor(steamId);
        var menu = new NumberMenu { Title = $"Goal celebration ({goals} goals, {mvps} MVPs)", OnBack = OpenClientSettingsMenu };
        foreach (var fx in Enum.GetValues<GoalFx>())
        {
            var unlocked = GoalFxUnlocked(fx, steamId);
            var label = $"{GoalFxNames[(int)fx]}{(fx == current ? " [on]" : "")}{(unlocked ? "" : $" - locked: {GoalFxLockText(fx)}")}";
            menu.Add(label, p =>
            {
                if (!GoalFxUnlocked(fx, p.SteamID)) { p.PrintToChat($" [SM] {GoalFxNames[(int)fx]} unlocks at {GoalFxLockText(fx)}."); OpenGoalFxMenu(p); return; }
                GoalFxPrefs[p.SteamID] = fx.ToString();
                SaveJsonAtomic(GoalFxPrefsFile, GoalFxPrefs);
                p.PrintToChat($" [SM] Goal celebration: {GoalFxNames[(int)fx]}.");
                OpenGoalFxMenu(p);
            });
        }
        OpenNumberMenu(player, menu);
    }

    // Admin - Settings - Stadium effects - Goal celebrations: thresholds.
    private void OpenGoalFxAdminMenu(CCSPlayerController player)
    {
        var s = AtmoSet;
        var menu = new NumberMenu { Title = "Stadium effects - Goal celebrations", OnBack = OpenAtmoSettingsMenu };
        menu.Add($"Goal celebrations: {AtmoOnOff(s.GoalCelebrations)}", p => { if (!SettingsAccess(p)) return; s.GoalCelebrations = !s.GoalCelebrations; SaveJsonAtomic(AtmoSettingsFile, s); OpenGoalFxAdminMenu(p); });
        void Step(string label, Func<int> get, Action<int> set) => menu.Add($"{label}: {get()}", p =>
        {
            if (!SettingsAccess(p)) return;
            var i = Array.FindIndex(GoalFxThresholdSteps, v => v > get());
            set(i < 0 ? 0 : GoalFxThresholdSteps[i]);
            SaveJsonAtomic(AtmoSettingsFile, s);
            OpenGoalFxAdminMenu(p);
        });
        Step("Fireworks finale - goals", () => s.UnlockFireworksGoals, v => s.UnlockFireworksGoals = v);
        Step("Stadium strobe - goals", () => s.UnlockStrobeGoals, v => s.UnlockStrobeGoals = v);
        Step("Gold rain - goals", () => s.UnlockGoldGoals, v => s.UnlockGoldGoals = v);
        Step("Gold rain - or MVPs (0 = off)", () => s.UnlockGoldMvps, v => s.UnlockGoldMvps = v);
        Step("Legend - goals", () => s.UnlockLegendGoals, v => s.UnlockLegendGoals = v);
        OpenNumberMenu(player, menu);
    }
}

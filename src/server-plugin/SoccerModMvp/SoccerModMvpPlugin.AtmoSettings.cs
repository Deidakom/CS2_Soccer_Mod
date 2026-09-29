using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;

// 2026-09-29 owner: "everything you add must be toggleable for the admin in
// the menu". Admin - Settings - Stadium effects (shown while the test flag
// soccermod_atmo.enabled exists): one switch per "Arena Vision" module, saved
// in soccermod_atmo_settings.json. The flag file stays the server-level gate;
// all effects run on the v8 stadium only.
public sealed partial class SoccerModMvpPlugin
{
    private const string AtmoSettingsFile = "soccermod_atmo_settings.json";

    private sealed class AtmoSettings
    {
        public bool Director { get; set; } = true;
        public bool Pyro { get; set; } = true;
        public bool Fireworks { get; set; } = true;
        public bool CameraFlashes { get; set; } = true;
        public bool WavingBanners { get; set; } = true;
        public bool Crowd { get; set; } = true;
        public bool CrowdFillDynamic { get; set; } = false;
        public bool Dugouts { get; set; } = true;
        public bool LedBoards { get; set; } = true;
        public bool LightRing { get; set; } = true;
        public bool BallFx { get; set; } = true;
        public bool GoalCelebrations { get; set; } = true;
        public bool MatchdayShow { get; set; } = true;
        public bool Announcer { get; set; } = true;
        public bool Spidercam { get; set; } = false;   // 2026-09-29 owner: off for now
        public bool CrowdSound { get; set; } = true;
        public int UnlockFireworksGoals { get; set; } = 1;
        public int UnlockStrobeGoals { get; set; } = 3;
        public int UnlockGoldGoals { get; set; } = 5;
        public int UnlockGoldMvps { get; set; } = 2;
        public int UnlockLegendGoals { get; set; } = 0;   // 2026-09-29 owner: Legend is the default celebration for everyone
    }

    private AtmoSettings? _atmoSettingsCache;
    private AtmoSettings AtmoSet => _atmoSettingsCache ??= LoadJsonOrNull<AtmoSettings>(AtmoSettingsFile) ?? new AtmoSettings();

    private void EditAtmoSettings(CCSPlayerController player, Action<AtmoSettings> change, Action<CCSPlayerController>? reopen = null)
    {
        if (!SettingsAccess(player)) return;
        change(AtmoSet);
        SaveJsonAtomic(AtmoSettingsFile, AtmoSet);
        (reopen ?? OpenAtmoSettingsMenu)(player);
    }

    // One line in OpenServerSettingsMenu (Menu.cs).
    private void AddAtmoSettingsEntry(NumberMenu menu)
    {
        AddProximityAdminEntry(menu);
        if (!File.Exists(ConfigPath(AtmoFlagFile))) return;
        menu.Add("Stadium effects (test, v8 only)", OpenAtmoSettingsMenu);
    }

    private static string AtmoOnOff(bool on) => on ? "on" : "off";

    // Grouped (2026-09-29, 17 switches did not fit one page).
    private void OpenAtmoSettingsMenu(CCSPlayerController player)
    {
        if (!SettingsAccess(player)) return;
        var s = AtmoSet;
        var menu = new NumberMenu { Title = "Admin - Settings - Stadium effects", OnBack = OpenServerSettingsMenu };
        menu.Add($"All stadium effects: {AtmoOnOff(s.Director)}", p => EditAtmoSettings(p, x => x.Director = !x.Director, q => { AtmoRespawnAll(); OpenAtmoSettingsMenu(q); }));
        menu.Add("Stands: crowd, banners, boards, ring", OpenAtmoStandsMenu);
        menu.Add("Goal show: pyro, fireworks, flashes, sparks", OpenAtmoGoalMenu);
        menu.Add("Match & TV: show, voice, sounds, cam", OpenAtmoMatchMenu);
        menu.Add("Goal celebrations (unlock thresholds)", OpenGoalFxAdminMenu);
        menu.Add("Tests", OpenAtmoTestMenu);
        OpenNumberMenu(player, menu);
    }

    private void AtmoRespawnAll()
    {
        AtmoCrowdEnsure("menu"); AtmoBannersEnsure("menu"); AtmoBoardsEnsure("menu"); AtmoRingEnsure("menu"); AtmoSpiderEnsure("menu");
    }

    private void OpenAtmoStandsMenu(CCSPlayerController player)
    {
        if (!SettingsAccess(player)) return;
        var s = AtmoSet;
        var menu = new NumberMenu { Title = "Stadium effects - Stands", OnBack = OpenAtmoSettingsMenu };
        menu.Add($"Crowd (full stadium): {AtmoOnOff(s.Crowd)}", p => EditAtmoSettings(p, x => x.Crowd = !x.Crowd, q => { AtmoCrowdEnsure("menu"); OpenAtmoStandsMenu(q); }));
        menu.Add($"Crowd fill: {(s.CrowdFillDynamic ? "dynamic (player count)" : "full")}", p => EditAtmoSettings(p, x => x.CrowdFillDynamic = !x.CrowdFillDynamic, q => { AtmoCrowdEnsure("menu"); OpenAtmoStandsMenu(q); }));
        menu.Add($"Waving banners: {AtmoOnOff(s.WavingBanners)}", p => EditAtmoSettings(p, x => x.WavingBanners = !x.WavingBanners, q => { AtmoBannersEnsure("menu"); OpenAtmoStandsMenu(q); }));
        menu.Add($"LED boards (ads + goal takeover): {AtmoOnOff(s.LedBoards)}", p => EditAtmoSettings(p, x => x.LedBoards = !x.LedBoards, q => { AtmoBoardsEnsure("menu"); OpenAtmoStandsMenu(q); }));
        menu.Add($"Dugouts (subs + coaches): {AtmoOnOff(s.Dugouts)}", p => EditAtmoSettings(p, x => x.Dugouts = !x.Dugouts, q => { AtmoDugoutsEnsure("menu"); OpenAtmoStandsMenu(q); }));
        menu.Add($"Light ring under the roof: {AtmoOnOff(s.LightRing)}", p => EditAtmoSettings(p, x => x.LightRing = !x.LightRing, q => { AtmoRingEnsure("menu"); OpenAtmoStandsMenu(q); }));
        OpenNumberMenu(player, menu);
    }

    private void OpenAtmoGoalMenu(CCSPlayerController player)
    {
        if (!SettingsAccess(player)) return;
        var s = AtmoSet;
        var menu = new NumberMenu { Title = "Stadium effects - Goal show", OnBack = OpenAtmoSettingsMenu };
        menu.Add($"Pyro (flares, smoke, confetti): {AtmoOnOff(s.Pyro)}", p => EditAtmoSettings(p, x => x.Pyro = !x.Pyro, OpenAtmoGoalMenu));
        menu.Add($"Fireworks: {AtmoOnOff(s.Fireworks)}", p => EditAtmoSettings(p, x => x.Fireworks = !x.Fireworks, OpenAtmoGoalMenu));
        menu.Add($"Camera flashes in the stands: {AtmoOnOff(s.CameraFlashes)}", p => EditAtmoSettings(p, x => x.CameraFlashes = !x.CameraFlashes, OpenAtmoGoalMenu));
        menu.Add($"Ball effects (post sparks): {AtmoOnOff(s.BallFx)}", p => EditAtmoSettings(p, x => x.BallFx = !x.BallFx, OpenAtmoGoalMenu));
        OpenNumberMenu(player, menu);
    }

    private void OpenAtmoMatchMenu(CCSPlayerController player)
    {
        if (!SettingsAccess(player)) return;
        var s = AtmoSet;
        var menu = new NumberMenu { Title = "Stadium effects - Match & TV", OnBack = OpenAtmoSettingsMenu };
        menu.Add($"Matchday show (started matches): {AtmoOnOff(s.MatchdayShow)}", p => EditAtmoSettings(p, x => x.MatchdayShow = !x.MatchdayShow, OpenAtmoMatchMenu));
        menu.Add($"Stadium announcer (voice): {AtmoOnOff(s.Announcer)}", p => EditAtmoSettings(p, x => x.Announcer = !x.Announcer, OpenAtmoMatchMenu));
        menu.Add($"Crowd sounds (replace the boo): {AtmoOnOff(s.CrowdSound)}", p => EditAtmoSettings(p, x => x.CrowdSound = !x.CrowdSound, OpenAtmoMatchMenu));
        menu.Add($"Spidercam over the pitch: {AtmoOnOff(s.Spidercam)}", p => EditAtmoSettings(p, x => x.Spidercam = !x.Spidercam, q => { AtmoSpiderEnsure("menu"); OpenAtmoMatchMenu(q); }));
        OpenNumberMenu(player, menu);
    }

    private void OpenAtmoTestMenu(CCSPlayerController player)
    {
        if (!SettingsAccess(player)) return;
        var menu = new NumberMenu { Title = "Stadium effects - Tests", OnBack = OpenAtmoSettingsMenu };
        menu.Add("Goal show red (with your celebration)", p => { AtmoGoalShow(CsTeamRed, -1, p.Slot); OpenAtmoTestMenu(p); });
        menu.Add("Goal show blue (with your celebration)", p => { AtmoGoalShow(CsTeamBlue, 1, p.Slot); OpenAtmoTestMenu(p); });
        menu.Add("Mexican wave", p => { AtmoCrowdWave(); OpenAtmoTestMenu(p); });
        menu.Add("Matchday show: match start", p => { AtmoShowPhase(AtmoMoment.MatchStart); OpenAtmoTestMenu(p); });
        menu.Add("Announcer: goal", p => { AtmoAnnounce("GoalRed", true); OpenAtmoTestMenu(p); });
        menu.Add("Crowd: applause", p => { AtmoCrowdSound("Applause"); OpenAtmoTestMenu(p); });
        menu.Add("Crowd: ooh", p => { AtmoCrowdSound("Ooh"); OpenAtmoTestMenu(p); });
        OpenNumberMenu(player, menu);
    }
}

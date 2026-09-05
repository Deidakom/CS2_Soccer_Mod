using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private const string MenuParityFile = "soccermod_menu_parity.json";
    private sealed class MenuParitySettings
    {
        public bool SprintStamina { get; set; } = true;
        public bool KeepMenusOpen { get; set; } = true;
        public bool KickoffOutline { get; set; } = true;
        public bool ForfeitEnabled { get; set; } = true;
        public bool ForfeitPublic { get; set; } = true;
        public bool ForfeitAutoSpec { get; set; }
        public bool ForfeitCapOnly { get; set; }
        public int ForfeitGoalDifference { get; set; }
        public bool MatchLogEnabled { get; set; } = true;
        public bool MatchLogGoals { get; set; } = true;
        public bool MatchLogCards { get; set; } = true;
        public bool MatchInfo { get; set; } = true;
        public bool CelebrationWeapons { get; set; }
        public bool Killfeed { get; set; } = true;
        public bool GoalkeeperSavesOnly { get; set; }
        public bool IngameCap { get; set; } = true;
        public int DeadChatVisibility { get; set; }
        public int PublicAccess { get; set; } = 2; // Preserve the server's existing free Match/CAP controls.
        public int RankCooldown { get; set; } = 30;
        public int RankMode { get; set; }
        public int CapTeamSize { get; set; } = 6;
        public int CapFirstPlayers { get; set; }
        public int ReadyMode { get; set; } = 1;
        public bool HalfwayStoppage { get; set; } = true;
        public bool HostnameInfo { get; set; } = true;
        public bool LogScheduled { get; set; }
        public int LogDays { get; set; } = 127;
        public int LogStartMinute { get; set; }
        public int LogEndMinute { get; set; }
        public bool LogPauses { get; set; } = true;
        public bool LogPeriods { get; set; } = true;
        public bool InfoPeriod { get; set; } = true;
        public bool InfoBreak { get; set; } = true;
        public bool InfoGolden { get; set; } = true;
        public bool InfoForfeit { get; set; } = true;
        public bool InfoForfeitSettings { get; set; } = true;
        public bool InfoLog { get; set; } = true;
        public bool RoundMvp { get; set; } = true;
    }
    private MenuParitySettings _menuParity = new();
    private bool _capDraftCompleted;
    private bool _matchWasCap;
    private void MenuParityOnLoad()
    {
        _menuParity = LoadJsonOrNull<MenuParitySettings>(MenuParityFile) ?? new();
        _menuParity.PublicAccess = Math.Clamp(_menuParity.PublicAccess, 0, 2);
        _menuParity.DeadChatVisibility = Math.Clamp(_menuParity.DeadChatVisibility, 0, 2);
        _menuParity.RankMode = Math.Clamp(_menuParity.RankMode, 0, 2);
        _menuParity.RankCooldown = Math.Clamp(_menuParity.RankCooldown, 0, 300);
        _menuParity.CapTeamSize = Math.Clamp(_menuParity.CapTeamSize, 1, 11);
        _menuParity.CapFirstPlayers = Math.Clamp(_menuParity.CapFirstPlayers, 0, 2);
        _menuParity.ReadyMode = Math.Clamp(_menuParity.ReadyMode, 0, 2);
        _menuParity.LogStartMinute = Math.Clamp(_menuParity.LogStartMinute, 0, 1439);
        _menuParity.LogEndMinute = Math.Clamp(_menuParity.LogEndMinute, 0, 1439);
        AddCommand("css_sm2parity_status", "Server only: menu parity diagnostics.", (p, c) =>
        {
            if (!RequireServerConsole(p, c)) return;
            c.ReplyToCommand($"[SM] parity={ModuleVersion} history={_statsStore.Entries.Count} readyMode={_menuParity.ReadyMode} pausedBall={_pausedBallHandle != 0} stoppage={_menuParity.HalfwayStoppage} capTeamSize={CapMatchMaxPlayers} firstPlayers={_menuParity.CapFirstPlayers} trainingDevices={_trainingDevices.Count} advanced={_advancedTraining} logActive={LogActive} ingameCap={_menuParity.IngameCap} killfeed={_menuParity.Killfeed} gkOnly={_menuParity.GoalkeeperSavesOnly} chatVisibility={_menuParity.DeadChatVisibility}");
        });
        AddCommand("css_sm2training_test", "Server only, outside matches: spawn cone|can|plate for eight seconds.", (p, c) =>
        {
            if (!RequireServerConsole(p, c) || MatchRunning || IsWebsiteCapActive()) return;
            var kind = c.ArgCount > 1 ? c.GetArg(1) : "off";
            const ulong testOwner = ulong.MaxValue;
            ClearTrainingDevices(testOwner);
            if (kind == "off") return;
            if (!TrainingPropModels.ContainsKey(kind)) { c.ReplyToCommand("Use cone|can|plate|off."); return; }
            var origin = CreateBallResetOrigin();
            var success = SpawnTrainingDevice(testOwner, new TrainingPlacement { Kind = kind, X = origin.X + 256, Y = origin.Y, Z = origin.Z + 64 });
            if (success)
            {
                var device = _trainingDevices.Last();
                var prop = device.Prop!;
                c.ReplyToCommand($"[SM] training test kind={kind} index={prop.Index} solid={prop.Collision.SolidType} physics={prop.Collision.EnablePhysics} mins={prop.Collision.Mins} maxs={prop.Collision.Maxs}");
                AddTimer(8, () => RemoveTrainingDevice(device), CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
            }
            else c.ReplyToCommand("[SM] training test spawn failed");
        });
        AddCommand("css_readycheck", "Open the paused match ready check.", (p, c) => { if (p is not null) OpenReadyMenu(p); });
        AddCommand("css_unready", "Withdraw your ready state.", (p, c) => { if (p is not null) SetPlayerReady(p, false); });
        _menuParity.ForfeitGoalDifference = Math.Clamp(_menuParity.ForfeitGoalDifference, 0, 30);
        AddCommand("css_sm2kickoffwall_style", "Admin: outline (CS:S semicircle) or legacy (half only).", (player, command) =>
        {
            if (!RequirePermission(player, command, "match")) return;
            if (command.ArgCount > 1)
            {
                var value = command.GetArg(1).ToLowerInvariant();
                if (value is not ("outline" or "legacy")) { command.ReplyToCommand("Use outline or legacy."); return; }
                var before = _menuParity.KickoffOutline; _menuParity.KickoffOutline = value == "outline";
                if (!SaveJsonAtomic(MenuParityFile, _menuParity)) _menuParity.KickoffOutline = before;
                DrawKickoffOutline();
            }
            command.ReplyToCommand($"[SM] kickoff style={(_menuParity.KickoffOutline ? "outline" : "legacy")} active={_kickoffRestrictionActive} lines={_kickoffBeams.Count}");
        });
        AddCommand("css_sm2kickoffwall_test", "Server only, outside a match: preview ct|t until touch or explicit off.", (player, command) =>
        {
            if (!RequireServerConsole(player, command) || MatchRunning) return;
            var value = command.ArgCount > 1 ? command.GetArg(1) : "off";
            if (value == "off") { _kickoffRestrictionActive = false; ClearKickoffOutline(); }
            else if (value is "ct" or "t") StartKickoffRestriction(value == "ct" ? CsTeam.CounterTerrorist : CsTeam.Terrorist);
            else { command.ReplyToCommand("Use ct|t|off."); return; }
            command.ReplyToCommand($"[SM] kickoff preview active={_kickoffRestrictionActive} lines={_kickoffBeams.Count}");
        });
    }
    private bool SettingsAccess(CCSPlayerController player)
    {
        if (HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0, "admin")) return true;
        player.PrintToChat(" [SM] You do not have permission to change server settings."); return false;
    }
    private void EditParity(CCSPlayerController player, Action<MenuParitySettings> edit, Action<CCSPlayerController> reopen)
    {
        if (!SettingsAccess(player)) return;
        if (MatchRunning || _capFightPending || _capFightStarted || _capPicksLeft > 0) { player.PrintToChat(" [SM] Match settings cannot change during a match."); return; }
        var before = System.Text.Json.JsonSerializer.Serialize(_menuParity);
        edit(_menuParity);
        if (!SaveJsonAtomic(MenuParityFile, _menuParity))
        {
            _menuParity = System.Text.Json.JsonSerializer.Deserialize<MenuParitySettings>(before)!;
            player.PrintToChat(" [SM] Could not save settings; previous values restored.");
        }
        reopen(player);
    }
    private static string OnOff(bool value) => value ? "ON" : "OFF";
    private void OpenMiscSettingsMenu(CCSPlayerController player)
    {
        if (!SettingsAccess(player)) return;
        var menu = new NumberMenu { Title = "Soccer Mod - Settings - Misc", OnBack = OpenServerSettingsMenu };
        menu.Add($"Kickoff Wall: {OnOff(_kickoffWallEnabled)}", p => RunBallMenuCommand(p, $"css_sm2kickoffwall {(_kickoffWallEnabled ? "off" : "on")}", OpenMiscSettingsMenu));
        menu.Add($"Kickoff style: {(_menuParity.KickoffOutline ? "CS:S outline" : "Legacy half")}", p => RunBallMenuCommand(p, $"css_sm2kickoffwall_style {(_menuParity.KickoffOutline ? "legacy" : "outline")}", OpenMiscSettingsMenu));
        menu.Add($"Rank mode: {new[] { "Total", "Per round", "Per match" }[_menuParity.RankMode]}", p => EditParity(p, s => s.RankMode = (s.RankMode + 1) % 3, OpenMiscSettingsMenu));
        menu.Add($"Rank cooldown: {_menuParity.RankCooldown}s", p => BeginChatNumberInput(p, "Ranking cooldown (0-300 seconds).", 0, 300, (actor, value) => EditParity(actor, s => s.RankCooldown = (int)value, OpenMiscSettingsMenu), OpenMiscSettingsMenu));
        menu.Add($"Celebration weapons: {OnOff(_menuParity.CelebrationWeapons)}", p => EditParity(p, s => s.CelebrationWeapons = !s.CelebrationWeapons, OpenMiscSettingsMenu));
        menu.Add($"Killfeed: {OnOff(_menuParity.Killfeed)}", p => EditParity(p, s => s.Killfeed = !s.Killfeed, OpenMiscSettingsMenu));
        menu.Add($"GK saves only: {OnOff(_menuParity.GoalkeeperSavesOnly)}", p => EditParity(p, s => s.GoalkeeperSavesOnly = !s.GoalkeeperSavesOnly, OpenMiscSettingsMenu));
        menu.Add($"In-game CAP menu: {OnOff(_menuParity.IngameCap)}", p => EditParity(p, s => s.IngameCap = !s.IngameCap, OpenMiscSettingsMenu));
        menu.Add("Match Rules / Ready Check", OpenMatchRulesMenu);
        menu.Add($"DuckJumpBlock: {OnOff(_blockDjbEnabled)}", p => RunBallMenuCommand(p, $"css_sm2djb {(_blockDjbEnabled ? "off" : "on")}", OpenMiscSettingsMenu));
        menu.Add($"Damage feedback: {OnOff(_ballImpactFeedbackEnabled)}", p => RunBallMenuCommand(p, $"css_sm2ball_impact_feedback {(_ballImpactFeedbackEnabled ? "off" : "on")}", OpenMiscSettingsMenu));
        menu.Add($"Health protection: {OnOff(_healthGodmodeEnabled)}", p => RunBallMenuCommand(p, $"css_sm2health godmode {(_healthGodmodeEnabled ? "off" : "on")}", OpenMiscSettingsMenu));
        menu.Add($"Sprint profile: {(_menuParity.SprintStamina ? "Stamina" : "Legacy")}", p => RunBallMenuCommand(p, $"css_sm2sprint_profile {(_menuParity.SprintStamina ? "legacy" : "stamina")}", OpenMiscSettingsMenu));
        menu.Add($"Use-button sprint: {OnOff(_sprintUseButtonTrigger)}", p => RunBallMenuCommand(p, $"css_sprint_usebutton {(_sprintUseButtonTrigger ? "off" : "on")}", OpenMiscSettingsMenu));
        menu.Add($"CAP server lock: {OnOff(_afkLockEnabled)}", p => RunBallMenuCommand(p, $"css_sm2lock {(_afkLockEnabled ? "off" : "on")}", OpenMiscSettingsMenu));
        OpenNumberMenu(player, menu);
    }
    private void OpenChatSettingsMenu(CCSPlayerController player)
    {
        if (!SettingsAccess(player)) return;
        var menu = new NumberMenu { Title = "Soccer Mod - Settings - Chat", OnBack = OpenServerSettingsMenu };
        menu.Add($"Prefix: {_chatPrefix}", p => BeginChatTextInput(p, "Enter a chat prefix (1-32 characters).", (actor, value) =>
        {
            if (!SettingsAccess(p) || string.IsNullOrWhiteSpace(value) || value.Length > 32) return;
            _chatPrefix = value; SaveMatchSettings("menu_chat_prefix"); OpenChatSettingsMenu(p);
        }, OpenChatSettingsMenu));
        menu.Add($"Prefix color: {_chatPrefixColor}", p => OpenChatColorMenu(p, true));
        menu.Add($"Text color: {_chatTextColor}", p => OpenChatColorMenu(p, false));
        menu.Add($"Dead chat: {_deadChatMode}", p => RunBallMenuCommand(p, $"css_sm2chat deadchat {(_deadChatMode + 1) % 3}", OpenChatSettingsMenu));
        menu.Add($"Dead-chat visibility: {new[] { "Default", "Teammates", "Everyone (includes team chat)" }[_menuParity.DeadChatVisibility]}", p => EditParity(p, s => s.DeadChatVisibility = (s.DeadChatVisibility + 1) % 3, OpenChatSettingsMenu));
        OpenNumberMenu(player, menu);
    }
    private void OpenChatColorMenu(CCSPlayerController player, bool prefix)
    {
        if (!SettingsAccess(player)) return;
        var menu = new NumberMenu { Title = prefix ? "Prefix Color" : "Text Color", OnBack = OpenChatSettingsMenu };
        foreach (var color in new[] { "white", "green", "lightblue", "yellow", "red", "purple", "gold", "orange" })
            menu.Add(color, p => RunBallMenuCommand(p, $"css_sm2chat {(prefix ? "prefixcolor" : "textcolor")} {color}", OpenChatSettingsMenu));
        OpenNumberMenu(player, menu);
    }
    private void OpenSkinSettingsMenu(CCSPlayerController player)
    {
        if (!SettingsAccess(player)) return;
        var menu = new NumberMenu { Title = "Soccer Mod - Settings - Skins", OnBack = OpenServerSettingsMenu };
        menu.Add($"Team colors: {OnOff(_teamColorEnabled)}", p => RunBallMenuCommand(p, $"css_sm2teamcolor {(_teamColorEnabled ? "off" : "on")}", OpenSkinSettingsMenu));
        menu.Add($"Team models: {OnOff(_teamModelEnabled)}", p => RunBallMenuCommand(p, $"css_sm2teammodel {(_teamModelEnabled ? "off" : "on")}", OpenSkinSettingsMenu));
        menu.Add("Toggle my goalkeeper", p => RunBallMenuCommand(p, "css_gk", OpenSkinSettingsMenu));
        menu.Add("Toggle my first-person legs", p => RunBallMenuCommand(p, "css_legs", OpenSkinSettingsMenu));
        OpenNumberMenu(player, menu);
    }
    private void OpenSoundSettingsMenu(CCSPlayerController player)
    {
        if (!SettingsAccess(player)) return;
        var menu = new NumberMenu { Title = "Soccer Mod - Settings - Sound Control", OnBack = OpenServerSettingsMenu };
        menu.Add($"Sound diagnostics: {OnOff(_soundLogEnabled)}", p => RunBallMenuCommand(p, $"css_sm2sound_log {(_soundLogEnabled ? "off" : "on")}", OpenSoundSettingsMenu));
        menu.Add("Recent sound events", OpenRecentSoundMenu);
        menu.Add("Block a sound hash", p => BeginChatTextInput(p, "Enter a numeric sound-event hash.", (actor, value) =>
        {
            if (uint.TryParse(value, out var hash) && hash != 0) RunBallMenuCommand(actor, $"css_sm2sound_block {hash}", OpenSoundSettingsMenu);
        }, OpenSoundSettingsMenu));
        menu.Add("Blocked sounds", p => p.ExecuteClientCommandFromServer("css_sm2sound_blocklist"));
        foreach (var hash in _blockedSoundHashes.OrderBy(h => h))
            menu.Add($"Unblock {hash}", p => RunBallMenuCommand(p, $"css_sm2sound_unblock {hash}", OpenSoundSettingsMenu));
        OpenNumberMenu(player, menu);
    }
    private void OpenForfeitSettingsMenu(CCSPlayerController player)
    {
        if (!SettingsAccess(player)) return;
        var menu = new NumberMenu { Title = "Soccer Mod - Forfeit Settings", OnBack = OpenMatchSettingsMenu };
        menu.Add($"Forfeit Vote: {OnOff(_menuParity.ForfeitEnabled)}", p => EditParity(p, s => s.ForfeitEnabled = !s.ForfeitEnabled, OpenForfeitSettingsMenu));
        menu.Add($"Vote Condition: {_menuParity.ForfeitGoalDifference} goals behind", p => BeginChatNumberInput(p, "Enter goal deficit (0-30).", 0, 30, (actor, value) => EditParity(actor, s => s.ForfeitGoalDifference = (int)value, OpenForfeitSettingsMenu), OpenForfeitSettingsMenu));
        menu.Add($"Availability: {(_menuParity.ForfeitPublic ? "Everyone" : "Admins only")}", p => EditParity(p, s => s.ForfeitPublic = !s.ForfeitPublic, OpenForfeitSettingsMenu));
        menu.Add($"Auto-Spec: {OnOff(_menuParity.ForfeitAutoSpec)}", p => EditParity(p, s => s.ForfeitAutoSpec = !s.ForfeitAutoSpec, OpenForfeitSettingsMenu));
        menu.Add($"Cap only mode: {OnOff(_menuParity.ForfeitCapOnly)}", p => EditParity(p, s => s.ForfeitCapOnly = !s.ForfeitCapOnly, OpenForfeitSettingsMenu));
        OpenNumberMenu(player, menu);
    }
    private void OpenLogSettingsMenu(CCSPlayerController player)
    {
        if (!SettingsAccess(player)) return;
        var menu = new NumberMenu { Title = "Soccer Mod - Match Log Settings", OnBack = OpenMatchSettingsMenu };
        menu.Add($"Match Log: {OnOff(_menuParity.MatchLogEnabled)}", p => EditParity(p, s => s.MatchLogEnabled = !s.MatchLogEnabled, OpenLogSettingsMenu));
        menu.Add($"Goals: {OnOff(_menuParity.MatchLogGoals)}", p => EditParity(p, s => s.MatchLogGoals = !s.MatchLogGoals, OpenLogSettingsMenu));
        menu.Add("Days and times", OpenLogScheduleMenu);
        menu.Add($"Pauses: {OnOff(_menuParity.LogPauses)}", p => EditParity(p, s => s.LogPauses = !s.LogPauses, OpenLogSettingsMenu));
        menu.Add($"Periods: {OnOff(_menuParity.LogPeriods)}", p => EditParity(p, s => s.LogPeriods = !s.LogPeriods, OpenLogSettingsMenu));
        menu.Add($"Cards: {OnOff(_menuParity.MatchLogCards)}", p => EditParity(p, s => s.MatchLogCards = !s.MatchLogCards, OpenLogSettingsMenu));
        OpenNumberMenu(player, menu);
    }
    private void OpenMatchInfoSettingsMenu(CCSPlayerController player)
    {
        if (!SettingsAccess(player)) return;
        var menu = new NumberMenu { Title = "Soccer Mod - Match Info Settings", OnBack = OpenMatchSettingsMenu };
        menu.Add($"Match information: {OnOff(_menuParity.MatchInfo)}", p => EditParity(p, s => s.MatchInfo = !s.MatchInfo, OpenMatchInfoSettingsMenu));
        menu.Add($"Period announcement: {OnOff(_menuParity.InfoPeriod)}", p => EditParity(p, s => s.InfoPeriod = !s.InfoPeriod, OpenMatchInfoSettingsMenu));
        menu.Add($"Break announcement: {OnOff(_menuParity.InfoBreak)}", p => EditParity(p, s => s.InfoBreak = !s.InfoBreak, OpenMatchInfoSettingsMenu));
        menu.Add($"Golden goal announcement: {OnOff(_menuParity.InfoGolden)}", p => EditParity(p, s => s.InfoGolden = !s.InfoGolden, OpenMatchInfoSettingsMenu));
        menu.Add($"Forfeit availability: {OnOff(_menuParity.InfoForfeit)}", p => EditParity(p, s => s.InfoForfeit = !s.InfoForfeit, OpenMatchInfoSettingsMenu));
        menu.Add($"Forfeit conditions: {OnOff(_menuParity.InfoForfeitSettings)}", p => EditParity(p, s => s.InfoForfeitSettings = !s.InfoForfeitSettings, OpenMatchInfoSettingsMenu));
        menu.Add($"Log status: {OnOff(_menuParity.InfoLog)}", p => EditParity(p, s => s.InfoLog = !s.InfoLog, OpenMatchInfoSettingsMenu));

        OpenNumberMenu(player, menu);
    }
    private void OpenTrainingDrillsMenu(CCSPlayerController player)
    {
        if (!TrainingHasAccess(player) || MatchRunning) return;
        var menu = new NumberMenu { Title = "Soccer Mod - Training - Shot Drills", OnBack = OpenTrainingMenu };
        menu.Add("Target at crosshair", p => RunBallMenuCommand(p, "css_ball_target", OpenTrainingDrillsMenu));
        menu.Add("Wall-pass target at crosshair", p => RunBallMenuCommand(p, "css_ball_target wall", OpenTrainingDrillsMenu));
        menu.Add("Clear target", p => RunBallMenuCommand(p, "css_ball_target off", OpenTrainingDrillsMenu));
        menu.Add("Replay last personal-ball shot", p => RunBallMenuCommand(p, "css_ball_replay", OpenTrainingDrillsMenu));
        OpenNumberMenu(player, menu);
    }
}

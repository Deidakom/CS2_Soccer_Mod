using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private readonly Dictionary<uint, SprintStamina> _staminaByPawn = new();
    private SprintStamina StaminaFor(CCSPlayerPawn pawn)
    {
        var key = pawn.EntityHandle.Raw;
        if (!_staminaByPawn.TryGetValue(key, out var state)) _staminaByPawn[key] = state = new();
        return state;
    }
    private SprintPrefEntry SprintPreference(CCSPlayerController player)
    {
        var id = player.AuthorizedSteamID?.SteamId64 ?? 0;
        var entry = _sprintPrefsStore.Prefs.FirstOrDefault(p => p.SteamId64 == id);
        if (entry is not null) return entry;
        entry = new() { SteamId64 = id };
        if (id != 0) _sprintPrefsStore.Prefs.Add(entry);
        return entry;
    }
    private void SprintParityOnLoad()
    {
        AddCommand("css_sprint_settings", "Open Sprint 2.0 controls and HUD preferences.", (player, command) =>
        { if (player is { IsValid: true }) OpenSprintSettingsMenu(player); });
        AddCommand("css_sm2sprint_profile", "Admin: stamina or legacy burst timing.", (player, command) =>
        {
            if (!RequirePermission(player, command, "admin")) return;
            if (command.ArgCount > 1)
            {
                var mode = command.GetArg(1).ToLowerInvariant();
                if (mode is not ("stamina" or "legacy")) { command.ReplyToCommand("Use stamina|legacy."); return; }
                var before = _menuParity.SprintStamina;
                _menuParity.SprintStamina = mode == "stamina";
                if (!SaveJsonAtomic(MenuParityFile, _menuParity)) _menuParity.SprintStamina = before;
                foreach (var target in Utilities.GetPlayers()) ResetSprint(target);
                _staminaByPawn.Clear(); _sprintStateBySlot.Clear();
            }
            command.ReplyToCommand($"[SM] sprint profile={(_menuParity.SprintStamina ? "stamina" : "legacy")}");
        });
    }
    private void SprintStaminaOnTick()
    {
        var live = new HashSet<uint>();
        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsEligiblePlayer(player) || player.PlayerPawn.Value is not { IsValid: true } pawn) continue;
            live.Add(pawn.EntityHandle.Raw);
            var state = StaminaFor(pawn);
            var now = Server.TickedTime;
            var wasActive = state.Active; var wasExhausted = state.Exhausted;
            var pref = SprintPreference(player);
            state.Update(now);
            if (_sprintSuppressed || _matchPhase == MatchPhase.Paused) state.Stop(now);
            else state.Input(now, _sprintUseButtonTrigger && (player.Buttons & PlayerButtons.Use) != 0, pref.Hold);
            if (state.Active || wasActive)
            {
                pawn.VelocityModifier = state.Active ? SprintSpeedMultiplier : 1;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
            }
            if (pref.Messages && wasActive != state.Active)
                player.PrintToChat(state.Active ? " [SM] Sprint active." : state.Exhausted ? " [SM] Sprint exhausted: wait for 100%." : " [SM] Sprint stopped; recovery in 1s.");
            if (pref.Messages && wasExhausted && !state.Exhausted) player.PrintToChat(" [SM] Sprint fully recharged.");
            // During matches the existing scoreboard appends this status, so
            // two independent writers never fight over the centre panel.
            if (!MatchRunning && !_openMenus.ContainsKey(player.Slot) && Server.TickCount % 8 == 0)
            {
                var hud = SprintHud(player);
                if (hud.Length > 0) player.PrintToCenter(hud);
            }
        }
        foreach (var key in _staminaByPawn.Keys.Where(k => !live.Contains(k)).ToArray()) _staminaByPawn.Remove(key);
    }
    private string SprintHud(CCSPlayerController player)
    {
        if (!_menuParity.SprintStamina || player.PlayerPawn.Value is not { IsValid: true } pawn || !IsAlive(pawn)) return "";
        var pref = SprintPreference(player);
        if (pref.Hud == 2) return "";
        var state = StaminaFor(pawn);
        if (pref.Hud == 1 && !state.Active && state.Stamina >= 99.95f) return "";
        var status = state.Active ? "ACTIVE" : state.Exhausted ? "EXHAUSTED" : Server.TickedTime < state.RegenAt ? "RECOVERY" : "READY";
        return $"SPRINT {state.Stamina:F0}% | {status} | {(pref.Hold ? "HOLD" : "TOGGLE")}";
    }
    private void OpenSprintSettingsMenu(CCSPlayerController player)
    {
        var pref = SprintPreference(player);
        var menu = new NumberMenu { Title = "Sprint 2.0 Settings", OnBack = OpenClientSettingsMenu };
        menu.Add($"Control: {(pref.Hold ? "Hold" : "Toggle")}", p =>
        {
            var setting = SprintPreference(p); setting.Hold = !setting.Hold;
            if (p.PlayerPawn.Value is { IsValid: true } pawn)
            {
                var state = StaminaFor(pawn); state.Stop(Server.TickedTime); state.RequireRelease = true;
                pawn.VelocityModifier = 1; Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
            }
            SaveJsonAtomic(SprintPrefsFileName, _sprintPrefsStore); OpenSprintSettingsMenu(p);
        });
        var hudLabels = new[] { "Always visible", "During activity", "Disabled" };
        menu.Add($"Stamina HUD: {hudLabels[Math.Clamp(pref.Hud, 0, 2)]}", p =>
        { var setting = SprintPreference(p); setting.Hud = (setting.Hud + 1) % 3; SaveJsonAtomic(SprintPrefsFileName, _sprintPrefsStore); OpenSprintSettingsMenu(p); });
        menu.Add($"Chat messages: {OnOff(pref.Messages)}", p => RunBallMenuCommand(p, "css_sprintset", OpenSprintSettingsMenu));
        menu.Add("How Sprint 2.0 works", p =>
        { p.PrintToChat(" [SM] 1.25x speed; 3s full stamina. Stop early to save it. Recovery begins after 1s; a full recharge takes 7.5s. Exhaustion requires 100% and a release before reuse. !sprint toggles; Hold uses +use."); OpenSprintSettingsMenu(p); });
        menu.Add("Reset sprint UI settings", p =>
        { var setting = SprintPreference(p); setting.Hud = 2; setting.Hold = false; setting.Messages = true; SaveJsonAtomic(SprintPrefsFileName, _sprintPrefsStore); OpenSprintSettingsMenu(p); });
        OpenNumberMenu(player, menu);
    }
}

using System;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Legacy SoMoE-19 burst sprint, retained for rollback. SprintParity.cs supplies
// the current repository's Sprint 2.0 stamina model. Legacy constants are exact (globals.sp: fSPRINT_SPEED 1.25,
// fSPRINT_TIME 3.0, fSPRINT_COOLDOWN 7.5). Mechanism differs from CS:S by
// necessity: CS:S set m_flLaggedMovementValue; CS2's analogue is
// CCSPlayerPawn.VelocityModifier, which the engine keeps pulling back toward
// 1.0, so it has to be re-applied every tick for the sprint's duration
// rather than set once. MaxSpeed rewrites were rejected (the plan doc's
// rationale still applies: CS2 recomputes it from the active weapon on its
// own movement pass, so a one-off write there gets silently overwritten).
//
// The compact player-only bar is implemented separately in SprintBar.cs.
public sealed partial class SoccerModMvpPlugin
{
    private const float SprintSpeedMultiplier = 1.25f;
    private const float SprintDurationSeconds = 3.0f;
    private const float SprintCooldownSeconds = 7.5f;

    private enum SprintPhase
    {
        Ready,
        Sprinting,
        Cooldown,
    }

    private sealed class SprintState
    {
        public SprintPhase Phase = SprintPhase.Ready;
        public double PhaseEndTime;
    }

    private readonly Dictionary<int, SprintState> _sprintStateBySlot = new();
    private bool _sprintUseButtonTrigger = true;
    // SoMoE cap.sp "tempSprint": sprint is switched off for the duration of
    // a cap fight and restored afterwards (Cap.cs).
    private bool _sprintSuppressed;

    // Per-player equivalent of SoMoE-19's sprint/clientsettings.sp message
    // flag (the progress-bar flag was removed alongside the display).
    private const string SprintPrefsFileName = "soccermod_sprint_prefs.json";

    private sealed class SprintPrefEntry
    {
        public ulong SteamId64 { get; set; }
        public bool Messages { get; set; } = true;
        public bool Hold { get; set; }
        public int Hud { get; set; } = 1; // Compact bar shown only while draining/recharging.
    }

    private sealed class SprintPrefsStore
    {
        public int Version { get; set; } = 1;
        public List<SprintPrefEntry> Prefs { get; set; } = new();
    }

    private SprintPrefsStore _sprintPrefsStore = new();

    private bool SprintMessagesEnabled(CCSPlayerController player)
    {
        var steamId = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        return steamId == 0 || _sprintPrefsStore.Prefs.FirstOrDefault(p => p.SteamId64 == steamId)?.Messages != false;
    }

    private void SprintOnLoad()
    {
        _sprintPrefsStore = LoadJsonOrNull<SprintPrefsStore>(SprintPrefsFileName) ?? new SprintPrefsStore();
        SprintParityOnLoad();
        SprintBarOnLoad();
        AddCommand("css_sprint", "Use a burst of sprint speed (SoMoE parity: 1.25x for 3s, 7.5s cooldown).", OnSprintCommand);
        AddCommand("css_sprint_usebutton", "Admin: toggle whether holding +use auto-triggers sprint.", OnSprintUseButtonCommand);
        AddCommand("css_sprintset", "Toggle your own sprint start/end chat messages (on/off).", OnSprintSetCommand);
    }

    private void OnSprintSetCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        var steamId = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        if (steamId == 0)
        {
            command.ReplyToCommand("[SM] unable to identify your SteamID");
            return;
        }

        var pref = _sprintPrefsStore.Prefs.FirstOrDefault(p => p.SteamId64 == steamId);
        if (pref is null)
        {
            pref = new SprintPrefEntry { SteamId64 = steamId };
            _sprintPrefsStore.Prefs.Add(pref);
        }

        pref.Messages = !pref.Messages;
        SaveJsonAtomic(SprintPrefsFileName, _sprintPrefsStore);
        player.PrintToChat($" \x04[SM]\x01 Sprint messages: {(pref.Messages ? "on" : "off")}.");
    }

    private SprintState GetSprintState(int slot)
    {
        if (!_sprintStateBySlot.TryGetValue(slot, out var state))
        {
            state = new SprintState();
            _sprintStateBySlot[slot] = state;
        }

        return state;
    }

    private void SprintOnTick()
    {
        if (_menuParity.SprintStamina) { SprintStaminaOnTick(); return; }
        if (_sprintSuppressed)
        {
            return;
        }

        var now = Server.TickedTime;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
            {
                continue;
            }

            var pawn = player.PlayerPawn.Value;
            if (pawn is not { IsValid: true } || !IsAlive(pawn))
            {
                continue;
            }

            var state = GetSprintState(player.Slot);
            switch (state.Phase)
            {
                case SprintPhase.Sprinting:
                    pawn.VelocityModifier = SprintSpeedMultiplier;
                    Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
                    if (now >= state.PhaseEndTime)
                    {
                        state.Phase = SprintPhase.Cooldown;
                        state.PhaseEndTime = now + SprintCooldownSeconds;
                        pawn.VelocityModifier = 1.0f;
                        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
                        if (SprintMessagesEnabled(player))
                        {
                            player.PrintToChat(" \x04[SoccerMod]\x01 Sprint has ended.");
                        }
                    }
                    break;

                case SprintPhase.Cooldown:
                    if (now >= state.PhaseEndTime)
                    {
                        state.Phase = SprintPhase.Ready;
                        if (SprintMessagesEnabled(player))
                        {
                            player.PrintToChat(" \x04[SoccerMod]\x01 You can use sprint again (!sprint).");
                        }
                    }
                    break;

                case SprintPhase.Ready:
                    if (_sprintUseButtonTrigger && (player.Buttons & PlayerButtons.Use) != 0)
                    {
                        StartSprint(player, pawn, state, now);
                    }
                    break;
            }
        }
    }

    private void StartSprint(CCSPlayerController player, CCSPlayerPawn pawn, SprintState state, double now)
    {
        state.Phase = SprintPhase.Sprinting;
        state.PhaseEndTime = now + SprintDurationSeconds;
        pawn.VelocityModifier = SprintSpeedMultiplier;
        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
        if (SprintMessagesEnabled(player))
        {
            player.PrintToChat(" \x04[SoccerMod]\x01 You are sprinting!");
        }
        Logger.LogInformation("[SM2DIAG] sprint_start slot={Slot} name={Name}", player.Slot, player.PlayerName);
    }

    private void ResetSprint(CCSPlayerController player)
    {
        RemoveSprintBar(player.Slot);
        if (player.PlayerPawn.Value is { IsValid: true } resetPawn)
        {
            _staminaByPawn.Remove(resetPawn.EntityHandle.Raw);
            resetPawn.VelocityModifier = 1;
            Utilities.SetStateChanged(resetPawn, "CCSPlayerPawn", "m_flVelocityModifier");
        }
        if (!_sprintStateBySlot.TryGetValue(player.Slot, out var state))
        {
            return;
        }

        state.Phase = SprintPhase.Ready;
        state.PhaseEndTime = 0;
        var pawn = player.PlayerPawn.Value;
        if (pawn is { IsValid: true })
        {
            pawn.VelocityModifier = 1.0f;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
        }
    }

    private void SprintOnRoundStart()
    {
        ClearSprintBars();
        _staminaByPawn.Clear();
        _sprintStateBySlot.Clear();
    }

    private void OnSprintCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        var pawn = player.PlayerPawn.Value;
        if (pawn is not { IsValid: true } || !IsAlive(pawn))
        {
            command.ReplyToCommand("[SM] you must be alive to sprint");
            return;
        }

        if (_sprintSuppressed)
        {
            command.ReplyToCommand("[SM] sprint is disabled during the cap fight");
            return;
        }

        if (_menuParity.SprintStamina)
        {
            if (!IsEligiblePlayer(player) || _matchPhase == MatchPhase.Paused) return;
            var stamina = StaminaFor(pawn); stamina.Update(Server.TickedTime);
            if (stamina.Active) stamina.Stop(Server.TickedTime);
            else if (!stamina.TryStart(Server.TickedTime)) command.ReplyToCommand("[SM] Wait for sprint recovery; exhaustion needs 100%.");
            pawn.VelocityModifier = stamina.Active ? SprintSpeedMultiplier : 1;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
            return;
        }
        var state = GetSprintState(player.Slot);
        if (state.Phase != SprintPhase.Ready)
        {
            var remaining = state.PhaseEndTime - Server.TickedTime;
            command.ReplyToCommand(state.Phase == SprintPhase.Sprinting
                ? $"[SM] already sprinting ({remaining:F1}s left)"
                : $"[SM] sprint on cooldown ({remaining:F1}s left)");
            return;
        }

        StartSprint(player, pawn, state, Server.TickedTime);
    }

    private void OnSprintUseButtonCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            _sprintUseButtonTrigger = command.GetArg(1).Equals("on", StringComparison.OrdinalIgnoreCase);
            SaveMatchSettings("sprint_usebutton_command");
        }

        command.ReplyToCommand($"[SM] sprint +use auto-trigger: {(_sprintUseButtonTrigger ? "on" : "off")}");
    }
}

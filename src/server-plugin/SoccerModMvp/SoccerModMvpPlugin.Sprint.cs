using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// SoMoE-19 sprint, ported numerically exact (globals.sp: fSPRINT_SPEED 1.25,
// fSPRINT_TIME 3.0, fSPRINT_COOLDOWN 7.5). Mechanism differs from CS:S by
// necessity: CS:S set m_flLaggedMovementValue; CS2's analogue is
// CCSPlayerPawn.VelocityModifier, which the engine keeps pulling back toward
// 1.0, so it has to be re-applied every tick for the sprint's duration
// rather than set once. MaxSpeed rewrites were rejected (the plan doc's
// rationale still applies: CS2 recomputes it from the active weapon on its
// own movement pass, so a one-off write there gets silently overwritten).
public sealed partial class SoccerModMvpPlugin
{
    private const float SprintSpeedMultiplier = 1.25f;
    private const float SprintDurationSeconds = 3.0f;
    private const float SprintCooldownSeconds = 7.5f;
    // SoMoE fed RoundFloat(7.5) into the integer progress-bar duration.
    private const int SprintCooldownProgressBarSeconds = 8;

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
        public bool ProgressBarActive;
    }

    private readonly Dictionary<int, SprintState> _sprintStateBySlot = new();
    private bool _sprintUseButtonTrigger = true;

    // Per-player equivalents of SoMoE-19's sprint/clientsettings.sp flags.
    // CS2 retains the native defuse-style progress-bar fields on
    // CCSPlayerPawnBase, so the original cooldown bar is genuinely portable.
    // Sound and the separately positioned HUD text timer remain out of scope.
    private const string SprintPrefsFileName = "soccermod_sprint_prefs.json";

    private sealed class SprintPrefEntry
    {
        public ulong SteamId64 { get; set; }
        public bool Messages { get; set; } = true;
        public bool ProgressBar { get; set; } = true;
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

    private bool SprintProgressBarEnabled(CCSPlayerController player)
    {
        var steamId = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        return steamId == 0 || _sprintPrefsStore.Prefs.FirstOrDefault(p => p.SteamId64 == steamId)?.ProgressBar != false;
    }

    private void SprintOnLoad()
    {
        _sprintPrefsStore = LoadJsonOrNull<SprintPrefsStore>(SprintPrefsFileName) ?? new SprintPrefsStore();
        AddCommand("css_sprint", "Use a burst of sprint speed (SoMoE parity: 1.25x for 3s, 7.5s cooldown).", OnSprintCommand);
        AddCommand("css_sprint_usebutton", "Admin: toggle whether holding +use auto-triggers sprint.", OnSprintUseButtonCommand);
        AddCommand("css_sprintset", "Toggle your own sprint start/end chat messages (on/off).", OnSprintSetCommand);
        AddCommand("css_sprintbar", "Toggle your own SoMoE-style sprint cooldown progress bar (on/off).", OnSprintBarCommand);
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

    private void OnSprintBarCommand(CCSPlayerController? player, CommandInfo command)
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

        pref.ProgressBar = !pref.ProgressBar;
        SaveJsonAtomic(SprintPrefsFileName, _sprintPrefsStore);

        var pawn = player.PlayerPawn.Value;
        if (pawn is { IsValid: true }
            && _sprintStateBySlot.TryGetValue(player.Slot, out var state))
        {
            if (pref.ProgressBar && state.Phase == SprintPhase.Cooldown)
            {
                StartSprintCooldownProgressBar(pawn, state.PhaseEndTime - SprintCooldownSeconds);
                state.ProgressBarActive = true;
            }
            else if (!pref.ProgressBar && state.ProgressBarActive)
            {
                ClearSprintProgressBar(pawn);
                state.ProgressBarActive = false;
            }
        }

        player.PrintToChat($" \x04[SM]\x01 Sprint progress bar: {(pref.ProgressBar ? "on" : "off")}.");
    }

    private static void StartSprintCooldownProgressBar(CCSPlayerPawn pawn, double cooldownStartTime)
    {
        pawn.ProgressBarStartTime = (float)cooldownStartTime;
        pawn.ProgressBarDuration = SprintCooldownProgressBarSeconds;
        Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_flProgressBarStartTime");
        Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_iProgressBarDuration");
    }

    private static void ClearSprintProgressBar(CCSPlayerPawn pawn)
    {
        if (pawn.ProgressBarDuration == 0 && pawn.ProgressBarStartTime == 0.0f)
        {
            return;
        }

        pawn.ProgressBarStartTime = 0.0f;
        pawn.ProgressBarDuration = 0;
        Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_flProgressBarStartTime");
        Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_iProgressBarDuration");
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
                        if (SprintProgressBarEnabled(player))
                        {
                            StartSprintCooldownProgressBar(pawn, now);
                            state.ProgressBarActive = true;
                        }
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
                        if (state.ProgressBarActive)
                        {
                            ClearSprintProgressBar(pawn);
                            state.ProgressBarActive = false;
                        }
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
        if (state.ProgressBarActive)
        {
            ClearSprintProgressBar(pawn);
            state.ProgressBarActive = false;
        }
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
            if (state.ProgressBarActive)
            {
                ClearSprintProgressBar(pawn);
            }
        }
        state.ProgressBarActive = false;
    }

    private void SprintOnRoundStart()
    {
        foreach (var (playerSlot, state) in _sprintStateBySlot)
        {
            if (state.ProgressBarActive
                && Utilities.GetPlayerFromSlot(playerSlot)?.PlayerPawn.Value is { IsValid: true } pawn)
            {
                ClearSprintProgressBar(pawn);
            }
        }
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

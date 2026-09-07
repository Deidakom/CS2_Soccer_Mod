using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;

public sealed partial class SoccerModMvpPlugin
{
    private bool HasGoalkeeperBoxSprint(CCSPlayerController player, CCSPlayerPawn pawn) =>
        !_sprintSuppressed && !_capFightStarted && !_capFightPending
        && _matchPhase is MatchPhase.Warmup or MatchPhase.Live
        && IsEligiblePlayer(player) && IsGkSlot(player.Slot, player.Team)
        && pawn.AbsOrigin is { } feet
        && GoalkeeperSprintRules.InBox(N(feet), GkBoxFor(player.Team));

    private static float SprintMovementMultiplier(SprintStamina state) => !state.Active ? 1
        : state.Unlimited ? GoalkeeperSprintRules.SpeedMultiplier(SprintSpeedMultiplier) : SprintSpeedMultiplier;

    // Re-evaluate immediately when the skin is released or the team changes.
    // ResetSprint would incorrectly give the player a fresh stamina bar.
    private void RefreshGoalkeeperSprint(CCSPlayerController player)
    {
        if (player.PlayerPawn.Value is not { IsValid: true } pawn) return;
        if (_menuParity.SprintStamina)
        {
            var state = StaminaFor(pawn);
            state.Update(Server.TickedTime, HasGoalkeeperBoxSprint(player, pawn));
            if (!IsEligiblePlayer(player)) state.Stop(Server.TickedTime);
            pawn.VelocityModifier = SprintMovementMultiplier(state);
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
        }
        else UpdateLegacyKeeperSprint(player, pawn, GetSprintState(player.Slot), Server.TickedTime);
    }

    // Keep the legacy burst profile usable too. Its remaining normal burst or
    // cooldown is preserved while the separate, free keeper sprint is used.
    private bool UpdateLegacyKeeperSprint(CCSPlayerController player, CCSPlayerPawn pawn,
        SprintState state, double now, bool command = false)
    {
        var inside = HasGoalkeeperBoxSprint(player, pawn);
        if (!inside && double.IsNaN(state.KeeperSince)) return false;
        if (inside)
        {
            if (double.IsNaN(state.KeeperSince))
            {
                state.KeeperSince = now;
                state.KeeperSprint = new();
                state.KeeperSprint.Update(now, true);
                if (state.Phase == SprintPhase.Sprinting && now < state.PhaseEndTime)
                {
                    state.KeeperSprint.TryStart(now);
                    state.KeeperSprint.InputDown = _sprintUseButtonTrigger && (player.Buttons & PlayerButtons.Use) != 0;
                }
            }
            var sprint = state.KeeperSprint;
            sprint.Update(now, true);
            if (command)
            {
                if (sprint.Active) sprint.Stop(now); else sprint.TryStart(now);
                if (SprintMessagesEnabled(player)) player.PrintToChat(sprint.Active
                    ? " [SM] GK box sprint active (unlimited)." : " [SM] GK box sprint stopped.");
            }
            else sprint.Input(now, _sprintUseButtonTrigger && (player.Buttons & PlayerButtons.Use) != 0,
                SprintPreference(player).Hold);
            pawn.VelocityModifier = SprintMovementMultiplier(sprint);
        }
        else
        {
            var active = state.KeeperSprint.Active;
            if (state.Phase != SprintPhase.Ready) state.PhaseEndTime += now - state.KeeperSince;
            state.KeeperSince = double.NaN;
            state.KeeperSprint = new();
            if (!IsEligiblePlayer(player) || _sprintSuppressed || _matchPhase == MatchPhase.Paused) active = false;
            if (active && state.Phase == SprintPhase.Ready) StartSprint(player, pawn, state, now);
            else if (!active && state.Phase == SprintPhase.Sprinting)
            {
                state.Phase = SprintPhase.Cooldown;
                state.PhaseEndTime = now + SprintCooldownSeconds;
            }
            pawn.VelocityModifier = state.Phase == SprintPhase.Sprinting ? SprintSpeedMultiplier : 1;
        }
        Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
        return inside;
    }
}

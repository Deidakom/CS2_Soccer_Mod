using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-08-30 user request: mute the landing sound instead of removing it.
// Previously ruled impossible via the sound API (no landing-sound cvar in
// this CS2 build, and sv_footsteps 0 kills ALL footstep audio, not just
// landing). CORRECTION 2026-09-01: the "no sound/user-message listener
// exists in CSSharp 1.0.373" half of that was wrong - HookUserMessage does
// exist (see SoccerModMvpPlugin.SoundBlock.cs, added later the same
// session for a hash-based sound-event blocker). This FallVelocity
// experiment predates that discovery and is left as-is since it already
// works; SoundBlock.cs is the mechanism for the landing sound specifically.
//
// Lead pursued instead at the time: Source landing sounds are gated on fall velocity, and
// m_flFallVelocity lives on CPlayer_MovementServices_Humanoid (reachable
// from the pawn's own CPlayer_MovementServices via a raw-pointer downcast -
// every CSSharp NativeObject wrapper, including the movement-services
// classes, exposes a public ctor(IntPtr) plus a Handle property for exactly
// this). Clamping it to zero every tick while airborne should keep the
// land-sound branch from ever firing server-side.
//
// EXPERIMENTAL, told to the user up front: CS2 movement is
// client-predicted, so the client may compute its own fall velocity and
// play the sound locally regardless of what the server writes back. If the
// in-game A/B shows no difference, this is a genuine dead end (no further
// workaround exists) and the toggle's default should flip to off.
public sealed partial class SoccerModMvpPlugin
{
    private bool _muteLandingEnabled = true;

    private void LandingSoundOnLoad()
    {
        AddCommand("css_sm2_mutelanding", "Admin: toggle the landing-sound mute experiment (on/off).", OnMuteLandingCommand);
    }

    private void MuteLandingOnTick()
    {
        if (!_muteLandingEnabled)
        {
            return;
        }

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

            if (pawn.MovementServices is not { } movement)
            {
                continue;
            }

            // MovementServices comes back typed as the base
            // CPlayer_MovementServices; FallVelocity is declared further
            // down the chain on CPlayer_MovementServices_Humanoid. Every
            // CSSharp schema wrapper is just a typed view over the same
            // underlying pointer, so re-wrapping it is the standard way to
            // reach a derived class's members.
            var humanoidMovement = new CCSPlayer_MovementServices(movement.Handle);
            if (humanoidMovement.FallVelocity > 0.0f)
            {
                humanoidMovement.FallVelocity = 0.0f;
                // NO SetStateChanged here, and specifically never
                // SetStateChanged(pawn, "CPlayer_MovementServices_Humanoid", ...).
                //
                // 2026-08-30 BUG (caused player movement to break entirely):
                // SetStateChanged resolves the offset for the given
                // class+field and stamps it on the entity you pass. Passing
                // the PAWN together with a field that lives on the movement-
                // services sub-object marks a completely unrelated offset on
                // the pawn dirty, corrupting its networked state and
                // desyncing client movement prediction.
                // m_flFallVelocity is a predicted movement field the client
                // recomputes anyway, so no networking notify is needed.
            }
        }
    }

    private void OnMuteLandingCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            var arg = command.GetArg(1);
            if (string.Equals(arg, "on", System.StringComparison.OrdinalIgnoreCase))
            {
                _muteLandingEnabled = true;
                SaveBallSettings("mutelanding_command");
            }
            else if (string.Equals(arg, "off", System.StringComparison.OrdinalIgnoreCase))
            {
                _muteLandingEnabled = false;
                SaveBallSettings("mutelanding_command");
            }
        }

        command.ReplyToCommand($"[SM] landing-sound mute: {(_muteLandingEnabled ? "on" : "off")} (usage: css_sm2_mutelanding <on|off>)");
    }
}

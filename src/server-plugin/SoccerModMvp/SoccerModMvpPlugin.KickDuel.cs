using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-24 owner request: when two players jump for the same ball, the one
// who hits it first and cleaner must win, like CS:S, instead of both kicks
// stacking. Kicks ADD a delta to the ball's velocity, so a second player's kick
// a tick later blended into the first one and neither shot went where it was
// aimed (live: slot 0 volley, slot 1 kick 16 ms later). Rules:
// - after a kick, another player's kick on that ball within the duel window is
//   ignored ("duel_lost"): the faster player won;
// - two kicks in the SAME tick: the cleaner one (higher aim alignment, then
//   closer) wins; if the later-processed kick is cleaner it replaces the first
//   from the ball's pre-kick velocity, so slot order never decides.
// The same player can always follow up; 0 turns the rule off.
public sealed partial class SoccerModMvpPlugin
{
    private const float DefaultKickDuelWindowSeconds = 0.10f;
    private float _kickDuelWindowSeconds = DefaultKickDuelWindowSeconds;

    // Quality of a knife contact: aim alignment first, distance as tie-break.
    private static float KickDuelQuality(float aimDot, float distance) => aimDot - distance * 0.0001f;

    // Returns false when this kick lost the duel. May rewind the ball to the
    // velocity before a worse same-tick kick and hands back that velocity as
    // the kick's inherited velocity.
    private bool ResolveKickDuel(CCSPlayerController player, CPhysicsPropMultiplayer ball, float aimDot, float distance, ref Vector inherited)
    {
        var state = State(ball);
        var now = Server.TickedTime;
        var quality = KickDuelQuality(aimDot, distance);
        var contested = _kickDuelWindowSeconds > 0
            && state.LastKickerSlot >= 0 && state.LastKickerSlot != player.Slot
            && now - state.LastKickTime < _kickDuelWindowSeconds;
        if (contested)
        {
            var sameTick = state.LastKickTick == Server.TickCount;
            if (!sameTick || quality <= state.LastKickQuality || state.PreKickVelocity is not { } before)
            {
                Logger.LogInformation(
                    "[SM2DIAG] kick_duel_lost slot={Slot} winner={Winner} afterMs={AfterMs:F0} sameTick={SameTick} quality={Quality:F3} winnerQuality={WinnerQuality:F3}",
                    player.Slot, state.LastKickerSlot, (now - state.LastKickTime) * 1000, sameTick, quality, state.LastKickQuality);
                return false;
            }

            Logger.LogInformation(
                "[SM2DIAG] kick_duel_replaced slot={Slot} loser={Loser} quality={Quality:F3} loserQuality={LoserQuality:F3}",
                player.Slot, state.LastKickerSlot, quality, state.LastKickQuality);
            ball.Teleport(velocity: before);
            inherited = before;
        }

        state.LastKickerSlot = player.Slot;
        state.LastKickTime = now;
        state.LastKickQuality = quality;
        state.PreKickVelocity = new Vector(inherited.X, inherited.Y, inherited.Z);
        return true;
    }
}

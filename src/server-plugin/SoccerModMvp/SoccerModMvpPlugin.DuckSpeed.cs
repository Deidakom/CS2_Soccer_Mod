using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;

namespace SoccerModMvp;

// 2026-09-27 owner: "when you crouch too often the crouch gets slowed down -
// remove that, unlimited crouch like in CS:S", then "spam crouch without
// delay" and "speed up the crouch 50%".
// - sv_timebetweenducks (CS2 default 0.4 s) ignores a crouch pressed too soon
//   after the last one. It is replicated, so client prediction follows it;
//   held at 0 here so it survives map changes and cfg resets.
// - CS2 also lowers the movement services' duck speed on every crouch and lets
//   it recover over time; holding it at 12 (CS2 ideal 8, +50%) removes that
//   slowdown and makes the crouch faster. Never SetStateChanged through the
//   pawn for movement-services fields (LandingSound.cs, 2026-08-30 bug).
// Crouch-jump blocking (DuckJumpBlock.cs) is separate and unchanged.
public sealed partial class SoccerModMvpPlugin
{
    private const float FullDuckSpeed = 12.0f;
    private ConVar? _timeBetweenDucks;
    private int _duckConVarTick;

    private void DuckSpeedOnTick()
    {
        if (++_duckConVarTick >= 64)
        {
            _duckConVarTick = 0;
            _timeBetweenDucks ??= ConVar.Find("sv_timebetweenducks");
            if (_timeBetweenDucks is { } cv && cv.GetPrimitiveValue<float>() != 0f)
                Server.ExecuteCommand("sv_timebetweenducks 0");
        }
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.PlayerPawn.Value is not { IsValid: true } pawn
                || pawn.MovementServices is not { } movement) continue;
            var services = new CCSPlayer_MovementServices(movement.Handle);
            if (services.DuckSpeed != FullDuckSpeed) services.DuckSpeed = FullDuckSpeed;
        }
    }
}

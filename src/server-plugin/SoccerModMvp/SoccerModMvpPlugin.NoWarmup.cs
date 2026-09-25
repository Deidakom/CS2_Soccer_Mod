using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-25 owner: no "match starting 3..2..1" after a map (re)load. The
// config already disables warmup (mp_warmup_online_enabled false), but CS2
// still starts a short "game commencing" warmup when the first player joins
// an empty server (journal: "Warmup period ended" -> "BeginMatch"). End any
// warmup the moment it appears, so play starts directly.
public sealed partial class SoccerModMvpPlugin
{
    private double _nextNoWarmupCheck;

    private void NoWarmupOnTick()
    {
        var now = (double)Server.TickedTime;
        if (now < _nextNoWarmupCheck) return;
        _nextNoWarmupCheck = now + 0.25;
        var proxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        if (proxy is not { IsValid: true } || proxy.GameRules is not { WarmupPeriod: true }) return;
        Server.ExecuteCommand("mp_warmup_end");
        Logger.LogInformation("[SM2DIAG] warmup_ended_immediately");
    }
}

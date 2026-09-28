using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;

namespace SoccerModMvp;

// 2026-09-28 owner: CS2's own TAB scoreboard (the default, Settings - HUD)
// shows the soccer numbers of the SoccerMod TAB board. A server cannot rename
// its columns (they are the client's UI), so only the values change:
//   K = goals, A = assists, D = saves, MVP stars = MVPs, Score = points
// - the same match stats as TabBoard.cs (entry.Current) - and the clan tag in
// front of the name shows the TAB board position ([GK], [DEF], ...), in the
// cap tag style (CapRoles.cs / WebCap.cs). A player's own clan tag is left
// alone. Written once a second where a value differs, so the engine's own
// changes (a death from !kill, kill score) are put back right away.
public sealed partial class SoccerModMvpPlugin
{
    private void NativeScoreboardOnLoad() => AddTimer(1.0f, UpdateNativeScoreboard, TimerFlags.REPEAT);

    private void UpdateNativeScoreboard()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsHLTV) continue;
            var stats = SteamIdOf(player) is var id && id != 0 && _statsBySteamId.TryGetValue(id, out var entry) ? entry.Current : null;
            var goals = stats?.Goals ?? 0;
            var assists = stats?.Assists ?? 0;
            var saves = stats?.Saves ?? 0;

            if (player.ActionTrackingServices?.MatchStats is { } match
                && (match.Kills != goals || match.Assists != assists || match.Deaths != saves))
            {
                match.Kills = goals;
                match.Assists = assists;
                match.Deaths = saves;
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_pActionTrackingServices");
            }

            var points = stats?.Points ?? 0;
            if (player.Score != points)
            {
                player.Score = points;
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_iScore");
            }

            var mvps = stats?.Mvp ?? 0;
            if (player.MVPs != mvps)
            {
                player.MVPs = mvps;
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_iMVPs");
            }

            var position = TabBoardPosition(player);
            var tag = position.Length > 0 ? $"[{position}]" : string.Empty;
            var clan = player.Clan ?? string.Empty;
            if (clan != tag && (clan.Length == 0 || IsWebsiteCapPositionTag(clan)))
            {
                player.Clan = tag;
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_szClan");
            }
        }
    }
}

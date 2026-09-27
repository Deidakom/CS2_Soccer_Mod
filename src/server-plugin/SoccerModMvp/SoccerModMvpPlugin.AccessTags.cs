using CounterStrikeSharp.API.Core;

namespace SoccerModMvp;

// 2026-09-26 owner (Natsu): "I don't know what a soccermod admin and a root
// admin can do" - the main and Admin menu entries carry an access tag, shown
// to the owner only:
//   (All) everyone
//   (P)   everyone while Settings - Public access is "CAP / Match", else SM
//   (SM)  soccermod admin (root has it too)
//   (R)   root only
public sealed partial class SoccerModMvpPlugin
{
    private const ulong OwnerSteamId64 = 76561198389841964UL;

    private static string AccessTag(CCSPlayerController viewer, string tag) =>
        viewer.AuthorizedSteamID?.SteamId64 == OwnerSteamId64 ? $" ({tag})" : string.Empty;

    private static bool IsOwner(CCSPlayerController? player) =>
        player?.AuthorizedSteamID?.SteamId64 == OwnerSteamId64;

    // 2026-09-27 owner: kick reach and cone width (defaults 65 / 45) are changed
    // in game by the owner only - not by root admins. Server console/RCON still can.
    private static readonly string[] OwnerOnlyBallDials = { "kickSurfaceReach", "kickAimConeDegrees" };
    private const string OwnerOnlyBallDialMessage = " [SM] Kick reach and cone width can only be changed by the server owner.";
}

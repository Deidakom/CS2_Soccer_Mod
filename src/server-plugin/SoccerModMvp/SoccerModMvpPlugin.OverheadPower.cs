namespace SoccerModMvp;

// 2026-09-27 owner: "reduce the power when the ball lands on me and I shoot it
// far away". Balls above eye height got the overhead bonus (+14%) on top of the
// airborne scale, so they flew almost as hard as ground kicks (median 1280 u/s,
// up to ~2000). This scale blends in with overheadRatio (0 at eye height, 1 a
// full ball radius above it), so ground kicks and chest-high volleys are
// unchanged. Ball menu: Kick power - "Overhead contact power".
public sealed partial class SoccerModMvpPlugin
{
    // Owner 2026-09-27: default 1 = unchanged power (the owner only wanted a way
    // to change it himself; 0.75 was live briefly and reverted).
    private const float DefaultKickOverheadPowerScale = 1.0f;
    private float _kickOverheadPowerScale = DefaultKickOverheadPowerScale;

    private float OverheadPowerFactor(float overheadRatio) =>
        1f - (1f - _kickOverheadPowerScale) * Math.Clamp(overheadRatio, 0f, 1f);
}
